import XCTest
@testable import JustChatWriter

final class TranslationPreferencesTests: XCTestCase {
    func testFreeFormFieldsNormalizeWhitespaceAndLength() {
        let preferences = TranslationPreferences(
            industry: "  跨境   电商 ",
            tone: "自然、专业，\n不要太客套" + String(repeating: "。", count: 200)
        )

        XCTAssertEqual(preferences.industry, "跨境 电商")
        XCTAssertTrue(preferences.tone.hasPrefix("自然、专业， 不要太客套"))
        XCTAssertEqual(preferences.tone.count, TranslationPreferences.maximumFieldLength)
    }

    @MainActor
    func testWriterStatePersistsPreferencesAsDefaults() {
        let suiteName = "JustChatWriterTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suiteName)!
        defer { defaults.removePersistentDomain(forName: suiteName) }

        let state = WriterState(
            translationClient: TranslationClient(),
            defaults: defaults
        )
        state.updatePreferences(
            industry: "建筑设计",
            tone: "直接、清楚，但保持礼貌"
        )

        let recreated = WriterState(
            translationClient: TranslationClient(),
            defaults: defaults
        )
        XCTAssertEqual(recreated.preferences.industry, "建筑设计")
        XCTAssertEqual(recreated.preferences.tone, "直接、清楚，但保持礼貌")
    }

    @MainActor
    func testWriterStatePersistsTargetLanguage() {
        let suiteName = "JustChatWriterTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suiteName)!
        defer { defaults.removePersistentDomain(forName: suiteName) }

        let state = WriterState(
            translationClient: TranslationClient(),
            defaults: defaults
        )
        state.selectTargetLanguage(.german)

        let recreated = WriterState(
            translationClient: TranslationClient(),
            defaults: defaults
        )
        XCTAssertEqual(recreated.targetLanguage, .german)
    }

    @MainActor
    func testLateDraftUpdateCannotOverwritePreview() {
        let state = WriterState(translationClient: TranslationClient())
        state.draft = "原始中文"
        state.preview = "Translated text"
        state.phase = .preview

        state.updateDraftText("延迟回写的中文")

        XCTAssertEqual(state.draft, "原始中文")
        XCTAssertEqual(state.preview, "Translated text")
    }

    @MainActor
    func testInsertionClearRejectsLatePreviewWriteBack() {
        let state = WriterState(translationClient: TranslationClient())
        state.draft = "原始中文"
        state.preview = "Translated text"
        state.phase = .preview

        state.insertionSucceeded()
        state.updatePreviewText("Translated text")

        XCTAssertEqual(state.phase, .drafting)
        XCTAssertTrue(state.draft.isEmpty)
        XCTAssertTrue(state.preview.isEmpty)
    }

    @MainActor
    func testInsertionDisablesRepeatedWriteUntilPasteIsPosted() {
        let state = WriterState(translationClient: TranslationClient())
        state.preview = "Translated text"
        state.phase = .preview

        state.insertionStarted()

        XCTAssertTrue(state.isWriting)
        XCTAssertFalse(state.canWrite)

        state.insertionSucceeded()
        XCTAssertFalse(state.isWriting)
    }
}
