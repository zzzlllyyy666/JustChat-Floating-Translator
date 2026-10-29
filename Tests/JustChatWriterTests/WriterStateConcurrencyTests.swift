import XCTest
@testable import JustChatWriter

final class WriterStateConcurrencyTests: XCTestCase {
    @MainActor
    func testReopeningRejectsOldTranslationEvenWhenNewDraftIsIdentical() async {
        let requestStarted = expectation(description: "Translation started")
        DelayedTranslationURLProtocol.reset { requestStarted.fulfill() }
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [DelayedTranslationURLProtocol.self]
        let client = TranslationClient(
            endpoint: URL(string: "https://example.com/api/translate"),
            session: URLSession(configuration: configuration),
            apiKey: "test-key"
        )
        let suiteName = "JustChatWriterTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suiteName)!
        defer { defaults.removePersistentDomain(forName: suiteName) }
        let state = WriterState(translationClient: client, defaults: defaults)
        state.updateDraftText("你好")
        let oldRequest = Task { await state.makePreview() }
        await fulfillment(of: [requestStarted], timeout: 1)

        state.startNewDraft()
        state.updateDraftText("你好")
        await oldRequest.value

        XCTAssertEqual(state.phase, .drafting)
        XCTAssertEqual(state.draft, "你好")
        XCTAssertEqual(state.preview, "")
        XCTAssertTrue(state.canPreview)
        XCTAssertFalse(state.canWrite)
        XCTAssertFalse(state.isTranslating)
    }

    @MainActor
    func testLanguageSwitchInvalidatesEarlierTranslationRequest() async {
        let firstRequestStarted = expectation(description: "First translation request started")
        DelayedTranslationURLProtocol.reset {
            firstRequestStarted.fulfill()
        }

        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [DelayedTranslationURLProtocol.self]
        let client = TranslationClient(
            endpoint: URL(string: "https://example.com/api/translate"),
            session: URLSession(configuration: configuration),
            apiKey: "test-key"
        )
        let suiteName = "JustChatWriterTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suiteName)!
        defer { defaults.removePersistentDomain(forName: suiteName) }
        let state = WriterState(translationClient: client, defaults: defaults)
        state.draft = "你好"

        let englishRequest = Task { await state.makePreview() }
        await fulfillment(of: [firstRequestStarted], timeout: 1)

        state.selectTargetLanguage(.japanese)
        let japaneseRequest = Task { await state.makePreview() }
        await japaneseRequest.value

        XCTAssertEqual(state.preview, "こんにちは")
        XCTAssertEqual(state.phase, .preview)
        XCTAssertFalse(state.isTranslating)

        await englishRequest.value

        XCTAssertEqual(state.targetLanguage, .japanese)
        XCTAssertEqual(state.preview, "こんにちは")
        XCTAssertEqual(state.phase, .preview)
        XCTAssertFalse(state.isTranslating)
        XCTAssertEqual(state.statusMessage, "日语译文可以直接修改；确认后写入")
    }
}

private final class DelayedTranslationURLProtocol: URLProtocol {
    private static let lock = NSLock()
    private static var requestCount = 0
    private static var onFirstRequest: (() -> Void)?

    static func reset(onFirstRequest: @escaping () -> Void) {
        lock.lock()
        requestCount = 0
        self.onFirstRequest = onFirstRequest
        lock.unlock()
    }

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        Self.lock.lock()
        Self.requestCount += 1
        let requestNumber = Self.requestCount
        let firstRequestCallback = Self.onFirstRequest
        Self.lock.unlock()

        if requestNumber == 1 {
            firstRequestCallback?()
        }

        let delay = requestNumber == 1 ? 0.2 : 0.01
        let translation = requestNumber == 1 ? "Hello" : "こんにちは"
        let content = "{\"translation\":\"\(translation)\"}"
        let data = try! JSONSerialization.data(withJSONObject: [
            "choices": [["message": ["content": content]]]
        ])

        DispatchQueue.global().asyncAfter(deadline: .now() + delay) { [self] in
            let response = HTTPURLResponse(
                url: request.url!,
                statusCode: 200,
                httpVersion: nil,
                headerFields: ["Content-Type": "application/json"]
            )!
            client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
            client?.urlProtocol(self, didLoad: data)
            client?.urlProtocolDidFinishLoading(self)
        }
    }

    override func stopLoading() {}
}
