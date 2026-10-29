import XCTest
@testable import JustChatWriter

final class WriterStateWorkflowTests: XCTestCase {
    @MainActor
    func testInlineAPISettingsPreserveDraftAndResumeWithoutInsertion() {
        let state = WriterState(translationClient: TranslationClient())
        state.updateDraftText("未完成的中文")
        let session = state.editorSessionID
        state.openAPISettings()
        XCTAssertTrue(state.showingAPISettings)
        XCTAssertFalse(state.canPreview)
        XCTAssertFalse(state.canWrite)
        XCTAssertEqual(state.draft, "未完成的中文")
        state.closeAPISettings(connected: true)
        XCTAssertEqual(state.editorSessionID, session)
        XCTAssertEqual(state.draft, "未完成的中文")
        XCTAssertTrue(state.canPreview)
        XCTAssertFalse(state.canWrite)
    }

    @MainActor
    func testInlineAPISettingsPreserveEditedPreview() {
        let state = WriterState(translationClient: TranslationClient())
        state.draft = "原中文"
        state.preview = "My edited translation"
        state.phase = .preview
        state.openAPISettings()
        XCTAssertFalse(state.canWrite)
        state.closeAPISettings()
        XCTAssertTrue(state.canWrite)
        XCTAssertEqual(state.preview, "My edited translation")
        XCTAssertEqual(state.draft, "原中文")
    }

    @MainActor
    func testOpeningAPISettingsRejectsPendingPreparationResult() async {
        var resume: CheckedContinuation<Void, Never>?
        let state = WriterState(translationClient: TranslationClient(endpoint: nil, session: .shared),
            prepareTranslation: { await withCheckedContinuation { resume = $0 } })
        state.updateDraftText("原中文")
        let request = Task { await state.makePreview() }
        while resume == nil { await Task.yield() }
        state.openAPISettings()
        resume?.resume()
        await request.value
        XCTAssertTrue(state.showingAPISettings)
        XCTAssertFalse(state.isTranslating)
        XCTAssertNil(state.errorMessage)
        XCTAssertEqual(state.preview, "")
        state.closeAPISettings()
        XCTAssertTrue(state.canPreview)
    }

    @MainActor
    func testUnavailableServiceKeepsDraftAndAllowsRetry() async {
        let state = WriterState(
            translationClient: TranslationClient(endpoint: nil, session: .shared),
            prepareTranslation: { throw TranslationClientError.server(message: "翻译服务不可用") }
        )
        state.updateDraftText("不能丢失的中文")
        await state.makePreview()
        XCTAssertEqual(state.draft, "不能丢失的中文")
        XCTAssertEqual(state.errorMessage, "翻译服务不可用")
        XCTAssertEqual(state.phase, .drafting)
        XCTAssertFalse(state.isTranslating)
        XCTAssertTrue(state.canPreview)
        XCTAssertFalse(state.canWrite)
    }

    @MainActor
    func testOpeningStartsBlankDraftAndPreservesTranslationSettings() {
        let suiteName = "JustChatWriterTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suiteName)!
        defer { defaults.removePersistentDomain(forName: suiteName) }
        let state = WriterState(translationClient: TranslationClient(), defaults: defaults)
        state.selectTargetLanguage(.english)
        state.updatePreferences(industry: "建筑设计", tone: "自然")
        state.draft = "旧中文"
        state.preview = "Old translation"
        state.phase = .preview
        state.errorMessage = "旧错误"
        state.insertionStarted()

        state.startNewDraft()

        XCTAssertEqual(state.phase, .drafting)
        XCTAssertEqual(state.draft, "")
        XCTAssertEqual(state.preview, "")
        XCTAssertNil(state.errorMessage)
        XCTAssertFalse(state.isWriting)
        XCTAssertFalse(state.canPreview)
        XCTAssertFalse(state.canWrite)
        XCTAssertEqual(state.targetLanguage, .english)
        XCTAssertEqual(state.preferences.industry, "建筑设计")
        XCTAssertEqual(state.preferences.tone, "自然")

        state.updateDraftText("新中文")
        XCTAssertTrue(state.canPreview)
        XCTAssertFalse(state.canWrite)
    }

    @MainActor
    func testClearingPreviewRequiresTranslationOfNewChinese() {
        let state = WriterState(translationClient: TranslationClient())
        state.draft = "旧中文"
        state.preview = "Old translation"
        state.phase = .preview
        let oldSessionID = state.editorSessionID

        state.updatePreviewText(" \n", sessionID: oldSessionID)
        state.updateDraftText("新的中文")
        state.updatePreviewText("旧编辑器延迟回写", sessionID: oldSessionID)

        XCTAssertEqual(state.phase, .drafting)
        XCTAssertEqual(state.draft, "新的中文")
        XCTAssertEqual(state.preview, "")
        XCTAssertTrue(state.canPreview)
        XCTAssertFalse(state.canWrite)
    }

    @MainActor
    func testNewSessionRejectsOldDraftEditorWriteBack() {
        let state = WriterState(translationClient: TranslationClient())
        let oldSessionID = state.editorSessionID
        state.updateDraftText("旧中文", sessionID: oldSessionID)
        state.startNewDraft()
        state.updateDraftText("新中文", sessionID: state.editorSessionID)

        state.updateDraftText("旧中文", sessionID: oldSessionID)

        XCTAssertEqual(state.draft, "新中文")
        XCTAssertTrue(state.canPreview)
        XCTAssertFalse(state.canWrite)
    }

    @MainActor
    func testReturningToChineseDiscardsPreviousPreview() {
        let state = WriterState(translationClient: TranslationClient())
        state.draft = "原中文"
        state.preview = "Old translation"
        state.phase = .preview

        state.returnToChinese()

        XCTAssertEqual(state.draft, "原中文")
        XCTAssertEqual(state.preview, "")
        XCTAssertTrue(state.canPreview)
        XCTAssertFalse(state.canWrite)
        state.insertionStarted()
        XCTAssertFalse(state.isWriting)
    }
}
