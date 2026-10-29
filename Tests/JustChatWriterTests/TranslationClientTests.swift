import XCTest
@testable import JustChatWriter

final class TranslationClientTests: XCTestCase {
    func testAccountAndNetworkServiceFailuresHaveActionableMessages() async {
        let client = makeClient(apiKey: "test-key")
        for (status, expected) in [(401, "API Key"), (402, "余额不足"), (429, "请求过多"), (504, "超时")] {
            StubURLProtocol.requestHandler = { request in
                let response = HTTPURLResponse(url: request.url!, statusCode: status,
                                               httpVersion: nil, headerFields: nil)!
                return (response, Data(#"{"error":{"message":"engine unavailable"}}"#.utf8))
            }
            do {
                _ = try await client.translate("你好", to: .english)
                XCTFail("Expected HTTP error")
            } catch {
                XCTAssertTrue(error.localizedDescription.contains(expected))
            }
        }
    }

    func testInvalidEndpointProducesClearError() async {
        let client = TranslationClient(endpoint: nil, session: .shared)

        do {
            _ = try await client.translate("你好", to: .english)
            XCTFail("Expected invalid endpoint error")
        } catch let error as TranslationClientError {
            XCTAssertEqual(error.errorDescription, "翻译接口地址无效")
        } catch {
            XCTFail("Unexpected error: \(error)")
        }
    }

    func testMissingAPIKeyDoesNotSendRequest() async {
        let client = makeClient(apiKey: nil)
        StubURLProtocol.requestHandler = { _ in
            XCTFail("A request must not be sent without an API Key")
            throw URLError(.badServerResponse)
        }

        do {
            _ = try await client.translate("你好", to: .english)
            XCTFail("Expected missing API Key error")
        } catch {
            XCTAssertEqual(error as? TranslationClientError, .missingAPIKey)
        }
    }

    func testPunctuationOnlyTranslationDoesNotSendRequest() async throws {
        let client = makeClient(apiKey: "test-key")
        StubURLProtocol.requestHandler = { _ in
            XCTFail("Punctuation-only conversion must remain local")
            throw URLError(.badServerResponse)
        }

        let result = try await client.translate("Hello，world。", to: .english)

        XCTAssertEqual(result, "Hello,world.")
    }

    func testRequestIncludesDeepSeekFieldsAndFreeFormPreferences() async throws {
        let client = makeClient(apiKey: "test-key")

        StubURLProtocol.requestHandler = { request in
            XCTAssertEqual(request.httpMethod, "POST")
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer test-key")
            XCTAssertEqual(request.value(forHTTPHeaderField: "Content-Type"), "application/json")
            XCTAssertEqual(request.timeoutInterval, 30)

            let body = try requestBodyData(request)
            let json = try XCTUnwrap(
                JSONSerialization.jsonObject(with: body) as? [String: Any]
            )
            XCTAssertEqual(json["model"] as? String, "deepseek-flash")
            XCTAssertEqual(json["stream"] as? Bool, false)
            XCTAssertEqual(json["max_tokens"] as? Int, 4_096)
            XCTAssertNil(json["maxTokens"])
            XCTAssertEqual((json["response_format"] as? [String: String])?["type"], "json_object")
            XCTAssertEqual((json["thinking"] as? [String: String])?["type"], "disabled")

            let messages = try XCTUnwrap(json["messages"] as? [[String: String]])
            XCTAssertEqual(messages.count, 2)
            XCTAssertEqual(messages[0]["role"], "system")
            XCTAssertTrue(messages[0]["content"]?.contains("Japanese") == true)
            XCTAssertEqual(messages[1]["role"], "user")
            let userJSON = try XCTUnwrap(messages[1]["content"]?.data(using: .utf8))
            let user = try XCTUnwrap(
                JSONSerialization.jsonObject(with: userJSON) as? [String: Any]
            )
            XCTAssertEqual(user["text_to_translate"] as? String, "请确认交期")
            XCTAssertEqual(user["locked_prefix"] as? String, "")
            let preferences = try XCTUnwrap(user["translation_preferences"] as? [String: String])
            XCTAssertEqual(preferences["industry"], "跨境电商")
            XCTAssertEqual(preferences["tone"], "自然、专业，不要太客套")

            return (try httpResponse(for: request), translationResponse([
                "translation": "納期をご確認ください。"
            ]))
        }

        let result = try await client.translate(
            "请确认交期",
            to: .japanese,
            preferences: TranslationPreferences(
                industry: "跨境电商",
                tone: "自然、专业，不要太客套"
            )
        )

        XCTAssertEqual(result, "納期をご確認ください。")
    }

    func testSegmentedResponsePreservesNonChineseLinesAndSegmentOrder() async throws {
        let client = makeClient(apiKey: "test-key")
        StubURLProtocol.requestHandler = { request in
            let body = try requestBodyData(request)
            let json = try XCTUnwrap(JSONSerialization.jsonObject(with: body) as? [String: Any])
            let messages = try XCTUnwrap(json["messages"] as? [[String: String]])
            let userData = try XCTUnwrap(messages[1]["content"]?.data(using: .utf8))
            let user = try XCTUnwrap(JSONSerialization.jsonObject(with: userData) as? [String: Any])
            let segments = try XCTUnwrap(user["segments"] as? [[String: Any]])
            XCTAssertEqual(segments.count, 2)
            XCTAssertEqual(segments[0]["id"] as? Int, 0)
            XCTAssertEqual(segments[1]["id"] as? Int, 2)

            return (try httpResponse(for: request), translationResponse([
                "translations": [
                    ["id": 2, "translation": "Please arrange payment"],
                    ["id": 0, "translation": "Please confirm the delivery date"]
                ]
            ]))
        }

        let result = try await client.translate(
            "请确认交期\nAlready shipped\n请安排付款",
            to: .english
        )

        XCTAssertEqual(
            result,
            "Please confirm the delivery date.\nAlready shipped\nPlease arrange payment."
        )
    }

    func testIncompleteFirstResponseRetriesWithCompleteInput() async throws {
        let client = makeClient(apiKey: "test-key")
        var requestCount = 0
        StubURLProtocol.requestHandler = { request in
            requestCount += 1
            let body = try requestBodyData(request)
            let json = try XCTUnwrap(JSONSerialization.jsonObject(with: body) as? [String: Any])
            let messages = try XCTUnwrap(json["messages"] as? [[String: String]])
            if requestCount == 2 {
                XCTAssertTrue(messages[0]["content"]?.contains("previous attempt was rejected") == true)
            }
            let translation = requestCount == 1 ? "你好" : "Hello"
            return (try httpResponse(for: request), translationResponse(["translation": translation]))
        }

        let result = try await client.translate("你好", to: .english)

        XCTAssertEqual(result, "Hello.")
        XCTAssertEqual(requestCount, 2)
    }

    func testRepeatedIncompleteResponsesFailAfterTwoRequests() async {
        let client = makeClient(apiKey: "test-key")
        var requestCount = 0
        StubURLProtocol.requestHandler = { request in
            requestCount += 1
            return (try httpResponse(for: request), translationResponse(["translation": "你好"]))
        }

        do {
            _ = try await client.translate("你好", to: .english)
            XCTFail("Expected incomplete translation error")
        } catch {
            XCTAssertEqual(error as? TranslationClientError, .incompleteTranslation)
            XCTAssertEqual(requestCount, 2)
        }
    }

    func testInvalidOuterJSONProducesInvalidResponse() async {
        let client = makeClient(apiKey: "test-key")
        var requestCount = 0
        StubURLProtocol.requestHandler = { request in
            requestCount += 1
            return (try httpResponse(for: request), Data("not-json".utf8))
        }

        do {
            _ = try await client.translate("你好", to: .english)
            XCTFail("Expected invalid response error")
        } catch {
            XCTAssertEqual(error as? TranslationClientError, .invalidResponse)
            XCTAssertEqual(requestCount, 1)
        }
    }

    func testInvalidTranslationJSONRetriesThenFails() async {
        let client = makeClient(apiKey: "test-key")
        var requestCount = 0
        StubURLProtocol.requestHandler = { request in
            requestCount += 1
            return (try httpResponse(for: request), chatResponse(content: "not-json"))
        }

        do {
            _ = try await client.translate("你好", to: .english)
            XCTFail("Expected incomplete translation error")
        } catch {
            XCTAssertEqual(error as? TranslationClientError, .incompleteTranslation)
            XCTAssertEqual(requestCount, 2)
        }
    }

    func testExactRepeatedDraftIsTranslatedOnceThenExpanded() async throws {
        let client = makeClient(apiKey: "test-key")
        let unit = "请确认交期并安排发货"
        var requestCount = 0
        StubURLProtocol.requestHandler = { request in
            requestCount += 1
            let body = try requestBodyData(request)
            let json = try XCTUnwrap(JSONSerialization.jsonObject(with: body) as? [String: Any])
            let messages = try XCTUnwrap(json["messages"] as? [[String: String]])
            let userData = try XCTUnwrap(messages[1]["content"]?.data(using: .utf8))
            let user = try XCTUnwrap(JSONSerialization.jsonObject(with: userData) as? [String: Any])
            XCTAssertEqual(user["text_to_translate"] as? String, unit)
            return (try httpResponse(for: request), translationResponse([
                "translation": "Please confirm the delivery date and arrange shipment"
            ]))
        }

        let result = try await client.translate("\(unit) \(unit)", to: .english)

        XCTAssertEqual(requestCount, 1)
        XCTAssertEqual(
            result,
            "Please confirm the delivery date and arrange shipment. Please confirm the delivery date and arrange shipment."
        )
    }

    private func makeClient(apiKey: String?) -> TranslationClient {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [StubURLProtocol.self]
        return TranslationClient(
            endpoint: URL(string: "https://example.com/chat/completions"),
            session: URLSession(configuration: configuration),
            apiKey: apiKey
        )
    }
}

private func httpResponse(for request: URLRequest, status: Int = 200) throws -> HTTPURLResponse {
    try XCTUnwrap(HTTPURLResponse(
        url: request.url!,
        statusCode: status,
        httpVersion: nil,
        headerFields: ["Content-Type": "application/json"]
    ))
}

private func translationResponse(_ object: Any) -> Data {
    let contentData = try! JSONSerialization.data(withJSONObject: object)
    return chatResponse(content: String(data: contentData, encoding: .utf8)!)
}

private func chatResponse(content: String) -> Data {
    try! JSONSerialization.data(withJSONObject: [
        "choices": [["message": ["content": content]]]
    ])
}

private func requestBodyData(_ request: URLRequest) throws -> Data {
    if let body = request.httpBody {
        return body
    }

    let stream = try XCTUnwrap(request.httpBodyStream)
    stream.open()
    defer { stream.close() }

    var data = Data()
    var buffer = [UInt8](repeating: 0, count: 1_024)
    while stream.hasBytesAvailable {
        let count = stream.read(&buffer, maxLength: buffer.count)
        if count < 0 {
            throw stream.streamError ?? URLError(.cannotDecodeContentData)
        }
        if count == 0 { break }
        data.append(buffer, count: count)
    }
    return data
}

private final class StubURLProtocol: URLProtocol {
    static var requestHandler: ((URLRequest) throws -> (HTTPURLResponse, Data))?

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        do {
            let handler = try XCTUnwrap(Self.requestHandler)
            let (response, data) = try handler(request)
            client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
            client?.urlProtocol(self, didLoad: data)
            client?.urlProtocolDidFinishLoading(self)
        } catch {
            client?.urlProtocol(self, didFailWithError: error)
        }
    }

    override func stopLoading() {}
}
