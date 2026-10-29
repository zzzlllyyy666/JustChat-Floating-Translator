import XCTest
@testable import JustChatWriter

final class ServiceConfigurationTests: XCTestCase {
    func testFreshInstallationSavesOnlyPrivateKeyAndRetainsItOnUpdate() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let config = ServiceConfiguration(directory: directory)
        XCTAssertFalse(config.isConfigured)
        XCTAssertFalse(FileManager.default.fileExists(atPath: config.fileURL.path))

        try config.save(apiKey: "unit-test-key")
        XCTAssertTrue(config.isConfigured)
        XCTAssertEqual(config.apiKey, "unit-test-key")
        let first = try String(contentsOf: config.fileURL, encoding: .utf8)
        XCTAssertEqual(first, "DEEPSEEK_API_KEY=unit-test-key\n")
        let attributes = try FileManager.default.attributesOfItem(atPath: config.fileURL.path)
        XCTAssertEqual((attributes[.posixPermissions] as? NSNumber)?.intValue, 0o600)
        let directoryAttributes = try FileManager.default.attributesOfItem(atPath: directory.path)
        XCTAssertEqual((directoryAttributes[.posixPermissions] as? NSNumber)?.intValue, 0o700)

        try config.save(apiKey: "")
        XCTAssertEqual(try String(contentsOf: config.fileURL, encoding: .utf8), first)
        try config.save(apiKey: "replacement-test-key")
        let replaced = try String(contentsOf: config.fileURL, encoding: .utf8)
        XCTAssertEqual(replaced, "DEEPSEEK_API_KEY=replacement-test-key\n")
    }

    func testRejectsInvalidCredentialsAndPreservesExistingConfiguration() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let config = ServiceConfiguration(directory: directory)
        try config.save(apiKey: "unit-test-key")
        let original = try Data(contentsOf: config.fileURL)

        XCTAssertThrowsError(try config.save(apiKey: "bad\nDEEPSEEK_API_KEY=other-key"))
        XCTAssertThrowsError(try config.save(apiKey: "bad key"))
        XCTAssertEqual(try Data(contentsOf: config.fileURL), original)
    }

    func testSavingRemovesLegacyServiceSettingsAndPreservesUnrelatedSettings() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let config = ServiceConfiguration(directory: directory)
        try Data("DEEPSEEK_API_KEY='unit-test-key'\nDEEPSEEK_TRANSLATION_MODEL=old-manual-model\nDEEPSEEK_TRANSLATION_FALLBACK_MODEL=old-fallback\nPROTOTYPE_HOST=192.168.1.2\nPROTOTYPE_PORT=4173\nCUSTOM_SETTING=keep\n".utf8)
            .write(to: config.fileURL)

        try config.save(apiKey: "")

        let updated = try String(contentsOf: config.fileURL, encoding: .utf8)
        XCTAssertTrue(updated.contains("DEEPSEEK_API_KEY=unit-test-key\n"))
        XCTAssertTrue(updated.contains("CUSTOM_SETTING=keep\n"))
        XCTAssertFalse(updated.contains("DEEPSEEK_TRANSLATION_MODEL="))
        XCTAssertFalse(updated.contains("DEEPSEEK_TRANSLATION_FALLBACK_MODEL="))
        XCTAssertFalse(updated.contains("PROTOTYPE_HOST="))
        XCTAssertFalse(updated.contains("PROTOTYPE_PORT="))
    }

    func testPlaceholderAndWhitespaceOnlyKeysAreNotConfigured() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let config = ServiceConfiguration(directory: directory)

        try Data("DEEPSEEK_API_KEY=replace_with_your_deepseek_api_key\n".utf8)
            .write(to: config.fileURL)
        XCTAssertFalse(config.isConfigured)
        XCTAssertNil(config.apiKey)
        XCTAssertThrowsError(try config.save(apiKey: ""))

        try Data("DEEPSEEK_API_KEY='   '\n".utf8).write(to: config.fileURL)
        XCTAssertFalse(config.isConfigured)
        XCTAssertNil(config.apiKey)
    }
}
