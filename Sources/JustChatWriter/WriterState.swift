import Foundation

@MainActor
final class WriterState: ObservableObject {
    enum Phase {
        case drafting
        case preview
    }

    enum OnboardingStep: Int {
        case connection = 1
        case permission = 2
        case practice = 3
    }

    @Published var draft = ""
    @Published var preview = ""
    @Published var phase: Phase = .drafting
    @Published var isTranslating = false
    @Published var isWriting = false
    @Published var statusMessage = "中文只保存在写作面板，预览后再写入目标位置"
    @Published var errorMessage: String?
    @Published var focusRequest = 0
    @Published private(set) var showingAPISettings = false
    @Published private(set) var showingOnboarding = false
    @Published private(set) var onboardingStep: OnboardingStep = .connection
    @Published private(set) var editorSessionID = 0
    @Published private(set) var preferences: TranslationPreferences
    @Published private(set) var targetLanguage: TranslationTargetLanguage

    private let translationClient: TranslationClient
    private let defaults: UserDefaults
    private let prepareTranslation: () async throws -> Void
    private var translationRequestID = 0
    private static let industryDefaultsKey = "translationPreference.industry"
    private static let toneDefaultsKey = "translationPreference.tone"
    private static let targetLanguageDefaultsKey = "translation.targetLanguage"

    init(
        translationClient: TranslationClient,
        defaults: UserDefaults = .standard,
        prepareTranslation: @escaping () async throws -> Void = {}
    ) {
        self.translationClient = translationClient
        self.defaults = defaults
        self.prepareTranslation = prepareTranslation
        preferences = TranslationPreferences(
            industry: defaults.string(forKey: Self.industryDefaultsKey) ?? "",
            tone: defaults.string(forKey: Self.toneDefaultsKey) ?? ""
        )
        targetLanguage = TranslationTargetLanguage(
            rawValue: defaults.string(forKey: Self.targetLanguageDefaultsKey) ?? ""
        ) ?? .english
        showingOnboarding = !defaults.bool(forKey: "onboarding.completed")
    }

    func updateDraftText(_ text: String, sessionID: Int? = nil) {
        guard phase == .drafting,
              sessionID == nil || sessionID == editorSessionID else { return }
        draft = text
    }

    func updatePreviewText(_ text: String, sessionID: Int? = nil) {
        guard phase == .preview,
              sessionID == nil || sessionID == editorSessionID else { return }
        if text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            startNewDraft()
            requestFocus()
            return
        }
        preview = text
    }

    var canPreview: Bool {
        !showingAPISettings && phase == .drafting
            && !draft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            && !isTranslating
            && !isWriting
    }

    var canWrite: Bool {
        !showingAPISettings && phase == .preview
            && !preview.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            && !isTranslating
            && !isWriting
    }

    func requestFocus() {
        focusRequest += 1
    }

    func showOnboarding() {
        guard !isWriting else { return }
        translationRequestID += 1
        isTranslating = false
        showingAPISettings = false
        onboardingStep = .connection
        showingOnboarding = true
    }

    func advanceOnboarding() {
        guard showingOnboarding,
              let nextStep = OnboardingStep(rawValue: onboardingStep.rawValue + 1) else { return }
        onboardingStep = nextStep
    }

    func onboardingBack() {
        guard showingOnboarding,
              let previousStep = OnboardingStep(rawValue: onboardingStep.rawValue - 1) else { return }
        onboardingStep = previousStep
    }

    func postponeOnboardingStep() {
        guard onboardingStep != .practice else { return }
        advanceOnboarding()
    }

    func completeOnboarding() {
        defaults.set(true, forKey: "onboarding.completed")
        showingOnboarding = false
        requestFocus()
    }

    func openAPISettings() {
        guard !isWriting else { return }
        translationRequestID += 1
        isTranslating = false
        showingOnboarding = false
        showingAPISettings = true
    }

    func closeAPISettings(connected: Bool = false) {
        showingAPISettings = false
        if connected {
            errorMessage = nil
            statusMessage = "DeepSeek 已连接，可以继续翻译"
        }
        requestFocus()
    }

    func startNewDraft() {
        translationRequestID += 1
        editorSessionID += 1
        phase = .drafting
        draft = ""
        preview = ""
        isTranslating = false
        showingAPISettings = false
        isWriting = false
        errorMessage = nil
        statusMessage = "输入中文，翻译成\(targetLanguage.chineseName)后再写入"
    }

    func makePreview() async {
        let source = draft.trimmingCharacters(in: .whitespacesAndNewlines)
        guard canPreview else { return }

        translationRequestID += 1
        let requestID = translationRequestID
        isTranslating = true
        errorMessage = nil
        statusMessage = "正在翻译…"

        do {
            try await prepareTranslation()
            guard requestID == translationRequestID else { return }
            let requestedLanguage = targetLanguage
            let requestedDraft = source
            let result = try await translationClient.translate(
                source,
                to: requestedLanguage,
                preferences: preferences
            )
            guard requestID == translationRequestID else { return }
            guard targetLanguage == requestedLanguage,
                  draft.trimmingCharacters(in: .whitespacesAndNewlines) == requestedDraft else {
                statusMessage = "草稿或目标语言已改变，请重新预览"
                isTranslating = false
                return
            }
            preview = result
            phase = .preview
            statusMessage = preferences.isEmpty
                ? "\(targetLanguage.chineseName)译文可以直接修改；确认后写入"
                : "已使用默认行业与语气；\(targetLanguage.chineseName)译文可修改后写入"
        } catch {
            guard requestID == translationRequestID else { return }
            errorMessage = error.localizedDescription
            statusMessage = "中文草稿仍然保留，可以重试"
        }

        isTranslating = false
        requestFocus()
    }

    func returnToChinese() {
        phase = .drafting
        preview = ""
        errorMessage = nil
        statusMessage = "已恢复中文，可修改后重新预览"
        requestFocus()
    }

    func selectTargetLanguage(_ language: TranslationTargetLanguage) {
        guard language != targetLanguage else { return }
        translationRequestID += 1
        targetLanguage = language
        defaults.set(language.rawValue, forKey: Self.targetLanguageDefaultsKey)
        phase = .drafting
        preview = ""
        isTranslating = false
        errorMessage = nil
        statusMessage = "已切换为中文 → \(language.shortCode)"
        requestFocus()
    }

    func updatePreferences(industry: String, tone: String) {
        let updated = TranslationPreferences(industry: industry, tone: tone)
        preferences = updated
        defaults.set(updated.industry, forKey: Self.industryDefaultsKey)
        defaults.set(updated.tone, forKey: Self.toneDefaultsKey)
        statusMessage = updated.isEmpty
            ? "已清除默认翻译要求"
            : "已保存，以后翻译自动使用"
    }

    func insertionSucceeded() {
        startNewDraft()
        statusMessage = "已写入；目标输入框仍可直接按回车"
    }

    func targetReturnCompleted() {
        guard phase == .drafting else { return }
        statusMessage = "继续输入下一段中文"
    }

    func insertionFailed(_ error: Error) {
        isWriting = false
        errorMessage = error.localizedDescription
        statusMessage = "译文仍保留在面板中，没有丢失"
        requestFocus()
    }

    func insertionStarted() {
        guard canWrite else { return }
        isWriting = true
        errorMessage = nil
        statusMessage = "正在写入…"
    }
}
