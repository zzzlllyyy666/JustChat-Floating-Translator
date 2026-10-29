import SwiftUI

struct ServiceSettingsView: View {
    let configuration: ServiceConfiguration
    let onConnected: () -> Void
    @State private var apiKey = ""
    @State private var isSaving = false
    @State private var message = ""
    @State private var saveTask: Task<Void, Never>?
    @FocusState private var keyFocused: Bool

    var body: some View {
        ScrollView {
        VStack(alignment: .leading, spacing: 10) {
            Text("API 设置").font(.headline)
            Text("使用你自己的 DeepSeek API Key。输入的正文会发送到 DeepSeek 进行翻译。")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            VStack(alignment: .leading, spacing: 5) {
                Text("API Key").font(.callout.weight(.medium))
                SecureField(configuration.isConfigured ? "已配置；留空保留现有 Key" : "填写 DeepSeek API Key", text: $apiKey)
                    .focused($keyFocused)
                    .disabled(isSaving)
            }
            Text("Key 仅保存在当前 Mac 的用户专属配置目录，不会包含在安装包中。")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Text(message.isEmpty ? "点击连接会发送一句简短测试，验证是否可用。" : message)
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
                .accessibilityIdentifier("api-connection-status")
            HStack {
                Spacer()
                Button(isSaving ? "正在连接…" : "连接") {
                    isSaving = true
                    message = "正在验证 DeepSeek 连接…"
                    saveTask = Task { @MainActor in
                        defer { isSaving = false; saveTask = nil }
                        do {
                            try configuration.save(apiKey: apiKey)
                            apiKey = ""
                            try Task.checkCancellation()
                            _ = try await TranslationClient(configuration: configuration)
                                .translate("你好", to: .english)
                            try Task.checkCancellation()
                            onConnected()
                        } catch is CancellationError { }
                        catch { if !Task.isCancelled { message = error.localizedDescription } }
                    }
                }
                .disabled(isSaving || (apiKey.isEmpty && !configuration.isConfigured))
                .buttonStyle(.borderedProminent)
            }
        }
        .textFieldStyle(.roundedBorder)
        .padding(12)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .onAppear { keyFocused = true }
        .onDisappear { apiKey = ""; saveTask?.cancel() }
    }
}
