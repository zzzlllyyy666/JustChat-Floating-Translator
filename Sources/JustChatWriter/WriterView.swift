import SwiftUI

struct WriterView: View {
    @ObservedObject var state: WriterState
    let onWrite: () -> Void
    let configuration: ServiceConfiguration
    let onOpenAPISettings: () -> Void

    @Environment(\.colorScheme) private var colorScheme
    @FocusState private var editorFocused: Bool
    @State private var showingPreferences = false
    @State private var editingIndustry = ""
    @State private var editingTone = ""

    private var isDark: Bool { colorScheme == .dark }

    private var background: Color {
        isDark
            ? Color(red: 0.13, green: 0.14, blue: 0.14)
            : Color(red: 0.79, green: 0.80, blue: 0.80)
    }

    private var panelSurface: Color {
        isDark
            ? Color(red: 0.18, green: 0.19, blue: 0.19)
            : Color(red: 0.91, green: 0.91, blue: 0.90)
    }

    private var fieldSurface: Color {
        isDark
            ? Color(red: 0.23, green: 0.24, blue: 0.24)
            : Color(red: 0.96, green: 0.96, blue: 0.95)
    }

    private var graphite: Color {
        isDark
            ? Color(red: 0.90, green: 0.91, blue: 0.90)
            : Color(red: 0.12, green: 0.14, blue: 0.14)
    }

    private var secondary: Color {
        isDark
            ? Color(red: 0.69, green: 0.71, blue: 0.70)
            : Color(red: 0.27, green: 0.29, blue: 0.29)
    }

    private var errorColor: Color {
        isDark
            ? Color(red: 0.96, green: 0.47, blue: 0.42)
            : Color(red: 0.64, green: 0.16, blue: 0.13)
    }

    var body: some View {
        VStack(spacing: 0) {
            header
            if state.showingOnboarding {
                OnboardingView(state: state, configuration: configuration)
                    .background(panelSurface)
            } else if state.showingAPISettings {
                ServiceSettingsView(configuration: configuration, onConnected: { state.closeAPISettings(connected: true) })
                    .background(panelSurface)
            } else {
                preferenceSummary
                editor
                controls
            }
        }
        .frame(width: 340, height: 360)
        .background(background)
        .onAppear { editorFocused = !state.showingOnboarding }
        .onChange(of: state.focusRequest) { _, _ in
            if !state.showingAPISettings && !state.showingOnboarding { editorFocused = true }
        }
        .onChange(of: state.showingAPISettings) { _, showing in
            if showing { showingPreferences = false; editorFocused = false }
        }
        .onChange(of: state.showingOnboarding) { _, showing in
            if showing { showingPreferences = false; editorFocused = false }
        }
    }

    private var preferenceSummary: some View {
        Button {
            editingIndustry = state.preferences.industry
            editingTone = state.preferences.tone
            showingPreferences = true
        } label: {
            HStack(spacing: 7) {
                Image(systemName: "briefcase")
                    .font(.system(size: 11, weight: .medium))
                Text(state.preferences.summary)
                    .font(.system(size: 11.5, weight: .medium))
                    .lineLimit(1)
                    .truncationMode(.tail)
                Spacer(minLength: 4)
                Image(systemName: "pencil")
                    .font(.system(size: 10.5, weight: .medium))
            }
            .foregroundStyle(graphite)
            .padding(.horizontal, 9)
            .frame(height: 32)
            .background(fieldSurface.opacity(0.78), in: RoundedRectangle(cornerRadius: 8))
            .overlay {
                RoundedRectangle(cornerRadius: 8)
                    .stroke(graphite.opacity(0.14), lineWidth: 1)
            }
        }
        .buttonStyle(.plain)
        .padding(.horizontal, 10)
        .padding(.top, 7)
        .popover(isPresented: $showingPreferences, arrowEdge: .top) {
            preferencesPopover
        }
    }

    private var preferencesPopover: some View {
        VStack(alignment: .leading, spacing: 12) {
            VStack(alignment: .leading, spacing: 3) {
                Text("默认翻译习惯")
                    .font(.system(size: 15, weight: .semibold))
                Text("直接用自己的话描述，不需要选择分类")
                    .font(.system(size: 10.5))
                    .foregroundStyle(secondary)
            }

            VStack(alignment: .leading, spacing: 6) {
                Text("你所在的行业")
                    .font(.system(size: 12, weight: .semibold))
                TextField("例如：跨境电商、建筑设计、SaaS 销售", text: $editingIndustry)
                    .textFieldStyle(.roundedBorder)
                Text("帮助模型理解常用表达和业务语境")
                    .font(.system(size: 10.5))
                    .foregroundStyle(secondary)
            }

            VStack(alignment: .leading, spacing: 6) {
                Text("你希望的语气")
                    .font(.system(size: 12, weight: .semibold))
                TextField("例如：自然、专业，不要太客套", text: $editingTone)
                    .textFieldStyle(.roundedBorder)
                Text("以后每次翻译自动使用，仍可随时修改")
                    .font(.system(size: 10.5))
                    .foregroundStyle(secondary)
            }

            HStack(spacing: 9) {
                Button("清除默认") {
                    editingIndustry = ""
                    editingTone = ""
                }
                .buttonStyle(.plain)
                .font(.system(size: 12))
                .foregroundStyle(secondary)

                Spacer()

                Button("取消") {
                    showingPreferences = false
                }

                Button("设为默认") {
                    state.updatePreferences(
                        industry: editingIndustry,
                        tone: editingTone
                    )
                    showingPreferences = false
                }
                .buttonStyle(.borderedProminent)
                .tint(graphite)
            }
        }
        .padding(14)
        .frame(width: 320)
        .background(panelSurface)
    }

    private var header: some View {
        HStack(spacing: 8) {
            Image(systemName: "character.bubble")
                .font(.system(size: 12, weight: .semibold))
                .foregroundStyle(fieldSurface)
                .frame(width: 24, height: 24)
                .background(graphite, in: RoundedRectangle(cornerRadius: 6))

            Text("JustChat")
                .font(.system(size: 14, weight: .semibold))

            Spacer()

            Menu {
                ForEach(TranslationTargetLanguage.allCases) { language in
                    Button {
                        state.selectTargetLanguage(language)
                    } label: {
                        if language == state.targetLanguage {
                            Label("\(language.chineseName) · \(language.nativeName)", systemImage: "checkmark")
                        } else {
                            Text("\(language.chineseName) · \(language.nativeName)")
                        }
                    }
                }
            } label: {
                Text("中文 → \(state.targetLanguage.shortCode)")
                .font(.system(size: 10.5, weight: .semibold))
                .foregroundStyle(graphite)
                .padding(.horizontal, 8)
                .frame(height: 26)
                .background(fieldSurface.opacity(0.68), in: RoundedRectangle(cornerRadius: 7))
                .overlay {
                    RoundedRectangle(cornerRadius: 7)
                        .stroke(graphite.opacity(0.12), lineWidth: 1)
                }
            }
            .menuStyle(.borderlessButton)
            .fixedSize()
            .disabled(state.showingAPISettings || state.showingOnboarding)

            Button {
                if state.showingAPISettings { state.closeAPISettings() }
                else { onOpenAPISettings() }
            } label: {
                Label(state.showingAPISettings ? "返回" : "API", systemImage: state.showingAPISettings ? "arrow.uturn.backward" : "key")
                    .font(.system(size: 10.5, weight: .medium))
                    .padding(.horizontal, 6)
                    .frame(height: 26)
                    .background(fieldSurface.opacity(0.68), in: RoundedRectangle(cornerRadius: 7))
            }
            .buttonStyle(.plain)
            .help(state.showingAPISettings ? "返回翻译，保留当前草稿" : "API 设置：填写或更换 DeepSeek Key")
            .accessibilityLabel(state.showingAPISettings ? "返回翻译" : "API 设置，填写或更换 DeepSeek Key")
            .keyboardShortcut(",", modifiers: .command)
            .disabled(state.isWriting || state.showingOnboarding)
        }
        .padding(.horizontal, 10)
        .frame(height: 40)
        .background(panelSurface)
        .overlay(alignment: .bottom) {
            Rectangle().fill(graphite.opacity(0.18)).frame(height: 1)
        }
    }

    private var editor: some View {
        let sessionID = state.editorSessionID
        return VStack(alignment: .leading, spacing: 7) {
            HStack {
                Text(state.phase == .drafting ? "中文草稿" : state.targetLanguage.previewTitle)
                    .font(.system(size: 12.5, weight: .semibold))
                Spacer()
                Text(state.phase == .drafting ? "\(state.draft.count) 字" : "可直接修改")
                    .font(.system(size: 10.5))
                    .foregroundStyle(secondary)
            }

            if state.phase == .drafting {
                editorField(
                    text: Binding(
                        get: { state.draft },
                        set: { state.updateDraftText($0, sessionID: sessionID) }
                    ),
                    identity: "draft-editor-\(sessionID)"
                )
            } else {
                editorField(
                    text: Binding(
                        get: { state.preview },
                        set: { state.updatePreviewText($0, sessionID: sessionID) }
                    ),
                    identity: "preview-editor-\(sessionID)"
                )
            }

            if let errorMessage = state.errorMessage {
                Label(errorMessage, systemImage: "exclamationmark.triangle")
                    .font(.system(size: 10.5))
                    .foregroundStyle(errorColor)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(.horizontal, 10)
        .padding(.top, 10)
        .padding(.bottom, 7)
        .frame(maxHeight: .infinity)
    }

    private func editorField(text: Binding<String>, identity: String) -> some View {
        TextEditor(text: text)
            .font(.system(size: 15.5))
            .lineSpacing(4)
            .scrollContentBackground(.hidden)
            .padding(7)
            .background(fieldSurface, in: RoundedRectangle(cornerRadius: 11))
            .overlay {
                RoundedRectangle(cornerRadius: 11)
                    .stroke(graphite.opacity(0.16), lineWidth: 1)
            }
            .focused($editorFocused)
            .id(identity)
            .disabled(state.isWriting)
    }

    private var controls: some View {
        VStack(spacing: 8) {
            HStack(spacing: 8) {
                Button {
                    state.returnToChinese()
                } label: {
                    Label("返回中文", systemImage: "arrow.uturn.backward")
                        .font(.system(size: 12, weight: .medium))
                        .frame(width: 100, height: 38)
                        .background(panelSurface, in: RoundedRectangle(cornerRadius: 8))
                        .overlay {
                            RoundedRectangle(cornerRadius: 8)
                                .stroke(graphite.opacity(0.15), lineWidth: 1)
                        }
                }
                .buttonStyle(.plain)
                .disabled(state.phase == .drafting || state.isTranslating || state.isWriting)
                .opacity(state.phase == .drafting ? 0.42 : 1)

                primaryActionButton
            }

            Text(state.statusMessage)
                .font(.system(size: 10.5))
                .foregroundStyle(secondary)
                .lineLimit(1)
                .multilineTextAlignment(.center)
                .frame(maxWidth: .infinity)
        }
        .padding(.horizontal, 10)
        .padding(.top, 9)
        .padding(.bottom, 9)
        .background(panelSurface)
        .overlay(alignment: .top) {
            Rectangle().fill(graphite.opacity(0.18)).frame(height: 1)
        }
    }

    @ViewBuilder
    private var primaryActionButton: some View {
        if state.phase == .drafting {
            Button {
                Task { await state.makePreview() }
            } label: {
                primaryActionLabel(
                    title: state.targetLanguage.previewActionTitle,
                    shortcut: "⌘T",
                    systemImage: "sparkles"
                )
            }
            .buttonStyle(.plain)
            .keyboardShortcut("t", modifiers: .command)
            .disabled(!state.canPreview)
            .opacity(state.canPreview ? 1 : 0.48)
        } else {
            Button {
                onWrite()
            } label: {
                primaryActionLabel(
                    title: "写入当前位置",
                    shortcut: "⌘S",
                    systemImage: "arrow.turn.down.left"
                )
            }
            .buttonStyle(.plain)
            .keyboardShortcut("s", modifiers: .command)
            .disabled(!state.canWrite)
            .opacity(state.canWrite ? 1 : 0.48)
        }
    }

    private func primaryActionLabel(
        title: String,
        shortcut: String,
        systemImage: String
    ) -> some View {
        HStack(spacing: 7) {
            if state.isTranslating || state.isWriting {
                ProgressView().controlSize(.small).tint(fieldSurface)
            } else {
                Image(systemName: systemImage)
            }
            Text(title)
            Spacer(minLength: 0)
            Text(shortcut)
                .font(.system(size: 10.5, weight: .semibold, design: .rounded))
                .foregroundStyle(fieldSurface.opacity(0.82))
                .padding(.horizontal, 6)
                .padding(.vertical, 3)
                .background(fieldSurface.opacity(0.11), in: RoundedRectangle(cornerRadius: 4))
        }
        .font(.system(size: 13, weight: .semibold))
        .foregroundStyle(fieldSurface)
        .padding(.horizontal, 11)
        .frame(maxWidth: .infinity)
        .frame(height: 38)
        .background(graphite, in: RoundedRectangle(cornerRadius: 8))
    }
}
