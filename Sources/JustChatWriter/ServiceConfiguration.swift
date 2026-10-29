import Foundation

struct ServiceConfiguration {
    static let automaticModel = "deepseek-flash"
    let directory: URL
    var fileURL: URL { directory.appendingPathComponent(".env.local") }

    init(directory: URL = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Application Support/JustChat")) {
        self.directory = directory
    }

    var apiKey: String? {
        guard let key = values()["DEEPSEEK_API_KEY"]?.trimmingCharacters(in: .whitespacesAndNewlines),
              !key.isEmpty,
              key != "replace_with_your_deepseek_api_key" else { return nil }
        return key
    }

    var isConfigured: Bool { apiKey != nil }

    func save(apiKey: String) throws {
        var settings = values()
        let key = apiKey.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !key.contains(where: { $0.isWhitespace }) else {
            throw ConfigurationError.invalidValue
        }
        if !key.isEmpty { settings["DEEPSEEK_API_KEY"] = key }
        guard let savedKey = settings["DEEPSEEK_API_KEY"],
              !savedKey.isEmpty,
              savedKey != "replace_with_your_deepseek_api_key" else {
            throw ConfigurationError.missingKey
        }
        for obsoleteKey in [
            "DEEPSEEK_TRANSLATION_MODEL",
            "DEEPSEEK_TRANSLATION_FALLBACK_MODEL",
            "PROTOTYPE_HOST",
            "PROTOTYPE_PORT"
        ] {
            settings.removeValue(forKey: obsoleteKey)
        }
        try persist(settings)
    }

    private func persist(_ settings: [String: String]) throws {
        let contents = settings.keys.sorted().map { "\($0)=\(settings[$0]!)" }.joined(separator: "\n") + "\n"
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true,
                                                attributes: [.posixPermissions: 0o700])
        try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: directory.path)
        try Data(contents.utf8).write(to: fileURL, options: .atomic)
        try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: fileURL.path)
    }

    private func values() -> [String: String] {
        guard let text = try? String(contentsOf: fileURL, encoding: .utf8) else { return [:] }
        var settings: [String: String] = [:]
        for line in text.components(separatedBy: .newlines) {
            let line = line.trimmingCharacters(in: .whitespaces)
            guard !line.hasPrefix("#"), let separator = line.firstIndex(of: "=") else { continue }
            let key = String(line[..<separator]).trimmingCharacters(in: .whitespaces)
            var value = String(line[line.index(after: separator)...]).trimmingCharacters(in: .whitespaces)
            if value.count >= 2 && ((value.hasPrefix("\"") && value.hasSuffix("\"")) || (value.hasPrefix("'") && value.hasSuffix("'"))) {
                value = String(value.dropFirst().dropLast())
            }
            if !key.isEmpty { settings[key] = value }
        }
        return settings
    }

    private enum ConfigurationError: LocalizedError {
        case invalidValue, missingKey
        var errorDescription: String? {
            switch self {
            case .invalidValue: "请填写有效的单行 API Key"
            case .missingKey: "请填写你自己的 DeepSeek API Key"
            }
        }
    }
}
