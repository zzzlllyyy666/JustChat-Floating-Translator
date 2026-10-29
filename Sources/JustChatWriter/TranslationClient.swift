import Foundation

struct TranslationClient {
    private struct RequestBody: Encodable {
        struct Thinking: Encodable {
            let type = "disabled"
        }

        struct ResponseFormat: Encodable {
            let type = "json_object"
        }

        let model: String
        let messages: [DeepSeekMessage]
        let thinking = Thinking()
        let responseFormat = ResponseFormat()
        let maxTokens = 4096
        let stream = false

        enum CodingKeys: String, CodingKey {
            case model, messages, thinking, stream
            case responseFormat = "response_format"
            case maxTokens = "max_tokens"
        }
    }

    private struct ChatResponse: Decodable {
        struct Choice: Decodable {
            struct Message: Decodable {
                let content: String?
            }
            let message: Message
        }
        let choices: [Choice]
    }

    private struct SingleTranslation: Decodable {
        let translation: String
    }

    private struct SegmentedTranslation: Decodable {
        let translations: [TranslationSegment]
    }

    private struct APIErrorResponse: Decodable {
        struct APIError: Decodable {
            let message: String?
            let code: String?
        }
        let error: APIError
    }

    private let endpoint: URL?
    private let session: URLSession
    private let model: String
    private let apiKeyProvider: () -> String?

    init(configuration: ServiceConfiguration = ServiceConfiguration(), session: URLSession = .shared) {
        endpoint = URL(string: "https://api.deepseek.com/chat/completions")
        self.session = session
        model = ServiceConfiguration.automaticModel
        apiKeyProvider = { configuration.apiKey }
    }

    init(
        endpoint: URL?,
        session: URLSession,
        apiKey: String? = nil,
        model: String = ServiceConfiguration.automaticModel
    ) {
        self.endpoint = endpoint
        self.session = session
        self.model = model
        apiKeyProvider = { apiKey }
    }

    func translate(
        _ text: String,
        to targetLanguage: TranslationTargetLanguage,
        preferences: TranslationPreferences = .empty
    ) async throws -> String {
        guard endpoint != nil else { throw TranslationClientError.invalidEndpoint }
        guard let apiKey = apiKeyProvider()?.trimmingCharacters(in: .whitespacesAndNewlines),
              !apiKey.isEmpty else { throw TranslationClientError.missingAPIKey }
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw TranslationClientError.emptyDraft
        }
        guard (text as NSString).length <= 30_000 else {
            throw TranslationClientError.draftTooLong
        }

        let boundary = TranslationBoundary.resolveTranslationBoundary(
            text,
            targetLanguage: targetLanguage
        )
        if !TranslationBoundary.hasChineseCharacters(boundary.translatableText) {
            return TranslationBoundary.joinLockedPrefix(
                boundary.lockedPrefix,
                TranslationBoundary.translateChinesePunctuation(
                    boundary.translatableText,
                    targetLanguage: targetLanguage
                ),
                targetLanguage: targetLanguage
            )
        }

        let repeatedDraft = TranslationBoundary.splitExactRepeatedDraft(boundary.translatableText)
        let sourceForModel = repeatedDraft?.unit ?? boundary.translatableText
        let segmentedPlan = sourceForModel.contains("\n")
            ? TranslationBoundary.makeSegmentedTranslationPlan(sourceForModel)
            : nil
        var normalizedTranslation = ""

        for attempt in 0..<2 {
            let messages: [DeepSeekMessage]
            if let segmentedPlan {
                messages = TranslationPromptBuilder.segmentedMessages(
                    lockedPrefix: boundary.lockedPrefix,
                    plan: segmentedPlan,
                    targetLanguage: targetLanguage,
                    preferences: preferences,
                    isRetry: attempt > 0
                )
            } else {
                messages = TranslationPromptBuilder.messages(
                    lockedPrefix: boundary.lockedPrefix,
                    translatableText: sourceForModel,
                    targetLanguage: targetLanguage,
                    preferences: preferences,
                    isRetry: attempt > 0
                )
            }

            guard let content = try await send(messages: messages, apiKey: apiKey) else { continue }
            if let segmentedPlan {
                guard let payload = try? JSONDecoder().decode(
                    SegmentedTranslation.self,
                    from: Data(content.utf8)
                ) else { continue }
                normalizedTranslation = TranslationBoundary.assembleSegmentedTranslation(
                    segmentedPlan,
                    payload.translations,
                    lockedPrefix: boundary.lockedPrefix,
                    targetLanguage: targetLanguage
                ) ?? ""
            } else {
                guard let payload = try? JSONDecoder().decode(
                    SingleTranslation.self,
                    from: Data(content.utf8)
                ) else { continue }
                normalizedTranslation = TranslationBoundary.normalizeTargetTranslation(
                    payload.translation,
                    sourceText: sourceForModel,
                    lockedPrefix: boundary.lockedPrefix,
                    targetLanguage: targetLanguage
                )
            }

            if TranslationBoundary.isCompleteTargetTranslation(
                sourceForModel,
                normalizedTranslation,
                targetLanguage: targetLanguage
            ) {
                break
            }
            normalizedTranslation = ""
        }

        guard !normalizedTranslation.isEmpty else {
            throw TranslationClientError.incompleteTranslation
        }

        let completeTranslation: String
        if let repeatedDraft {
            completeTranslation = Array(repeating: normalizedTranslation, count: repeatedDraft.count)
                .joined(separator: repeatedDraft.separator)
        } else {
            completeTranslation = normalizedTranslation
        }

        guard TranslationBoundary.isCompleteTargetTranslation(
            boundary.translatableText,
            completeTranslation,
            targetLanguage: targetLanguage
        ) else {
            throw TranslationClientError.incompleteTranslation
        }

        let translation = TranslationBoundary.joinLockedPrefix(
            boundary.lockedPrefix,
            completeTranslation,
            targetLanguage: targetLanguage
        ).trimmingCharacters(in: .whitespacesAndNewlines)
        guard !translation.isEmpty else { throw TranslationClientError.emptyTranslation }
        return translation
    }

    private func send(messages: [DeepSeekMessage], apiKey: String) async throws -> String? {
        guard let endpoint else { throw TranslationClientError.invalidEndpoint }
        var request = URLRequest(url: endpoint)
        request.httpMethod = "POST"
        request.timeoutInterval = 30
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("Bearer \(apiKey)", forHTTPHeaderField: "Authorization")
        request.httpBody = try JSONEncoder().encode(
            RequestBody(model: model, messages: messages)
        )

        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await session.data(for: request)
        } catch let error as URLError {
            switch error.code {
            case .timedOut:
                throw TranslationClientError.server(message: "翻译超时，中文草稿已保留，请重试")
            case .cannotConnectToHost, .cannotFindHost:
                throw TranslationClientError.server(message: "无法连接 DeepSeek，请检查网络后重试")
            case .notConnectedToInternet, .networkConnectionLost:
                throw TranslationClientError.server(message: "网络连接中断，请联网后重试")
            default:
                throw error
            }
        }

        guard let httpResponse = response as? HTTPURLResponse else {
            throw TranslationClientError.invalidResponse
        }
        guard (200..<300).contains(httpResponse.statusCode) else {
            let payload = try? JSONDecoder().decode(APIErrorResponse.self, from: data)
            let message: String
            switch httpResponse.statusCode {
            case 401, 403:
                message = "API Key 无效或没有使用权限，请检查翻译设置"
            case 402:
                message = "翻译账户余额不足，请在 DeepSeek 账户中查看"
            case 429:
                message = "翻译请求过多，请稍后重试"
            case 504:
                message = "翻译超时，中文草稿已保留，请重试"
            case 502, 503:
                message = "翻译引擎暂时不可用，请稍后重试"
            default:
                message = payload?.error.message ?? "DeepSeek 返回错误（\(httpResponse.statusCode)）"
            }
            throw TranslationClientError.server(message: message)
        }

        guard let result = try? JSONDecoder().decode(ChatResponse.self, from: data) else {
            throw TranslationClientError.invalidResponse
        }
        return result.choices.first?.message.content
    }
}

enum TranslationClientError: LocalizedError, Equatable {
    case missingAPIKey
    case invalidEndpoint
    case invalidResponse
    case emptyDraft
    case draftTooLong
    case emptyTranslation
    case incompleteTranslation
    case server(message: String)

    var errorDescription: String? {
        switch self {
        case .missingAPIKey:
            "请先在“翻译设置”中配置 API Key"
        case .invalidEndpoint:
            "翻译接口地址无效"
        case .invalidResponse:
            "没有收到有效的翻译响应"
        case .emptyDraft:
            "请输入需要翻译的内容"
        case .draftTooLong:
            "中文草稿过长，请分段后再翻译"
        case .emptyTranslation:
            "翻译结果为空，请重试"
        case .incompleteTranslation:
            "翻译结果不完整，请重试"
        case .server(let message):
            message
        }
    }
}
