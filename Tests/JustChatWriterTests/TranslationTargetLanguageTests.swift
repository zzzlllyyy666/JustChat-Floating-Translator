import XCTest
@testable import JustChatWriter

final class TranslationTargetLanguageTests: XCTestCase {
    func testServerLanguageIdentifiersStayAlignedWithIOS() {
        XCTAssertEqual(
            TranslationTargetLanguage.allCases.map(\.rawValue),
            ["en", "ja", "ko", "es", "fr", "de"]
        )
    }
}
