import SwiftUI

struct OnboardingView: View {
    @ObservedObject var state: WriterState
    let configuration: ServiceConfiguration
    @State private var permissionGranted = false

    var body: some View {
        VStack(spacing: 0) {
            progress
            Group {
                switch state.onboardingStep {
                case .connection:
                    ServiceSettingsView(
                        configuration: configuration,
                        onConnected: { state.advanceOnboarding() }
                    )
                case .permission:
                    OnboardingPermissionView(granted: $permissionGranted)
                case .practice:
                    OnboardingPracticeView()
                }
            }
            .frame(maxHeight: .infinity)
            footer
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private var progress: some View {
        HStack(spacing: 10) {
            Text("初次设置")
                .font(.system(size: 12, weight: .semibold))
            HStack(spacing: 4) {
                ForEach(1...3, id: \.self) { index in
                    Capsule()
                        .fill(index <= state.onboardingStep.rawValue ? Color.primary : Color.primary.opacity(0.16))
                        .frame(width: 18, height: 3)
                }
            }
            Spacer()
            Text("\(state.onboardingStep.rawValue) / 3")
                .font(.system(size: 10.5, weight: .medium, design: .rounded))
                .foregroundStyle(.secondary)
        }
        .padding(.horizontal, 12)
        .frame(height: 34)
    }

    @ViewBuilder
    private var footer: some View {
        HStack {
            if state.onboardingStep.rawValue > 1 {
                Button("上一步") { state.onboardingBack() }
                    .buttonStyle(.plain)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            if state.onboardingStep == .practice {
                Button("开始使用") { state.completeOnboarding() }
                    .buttonStyle(.borderedProminent)
                    .tint(.accentColor)
            } else if state.onboardingStep == .permission && permissionGranted {
                Button("继续") { state.advanceOnboarding() }
                    .buttonStyle(.borderedProminent)
                    .tint(.accentColor)
            } else {
                Button("稍后再说") { state.postponeOnboardingStep() }
                    .buttonStyle(.plain)
                    .foregroundStyle(.secondary)
            }
        }
        .font(.system(size: 11.5, weight: .medium))
        .padding(.horizontal, 12)
        .frame(height: 38)
    }
}

private struct OnboardingPermissionView: View {
    @Binding var granted: Bool
    private let timer = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Image(systemName: "hand.raised.fill")
                .font(.system(size: 24, weight: .medium))
                .foregroundStyle(.secondary)
            Text("允许写回你选择的输入框")
                .font(.system(size: 17, weight: .semibold))
            Text("JustChat 只在写入时模拟一次粘贴，不读取目标输入框的已有内容。你可以随时在系统设置中撤销权限。")
                .font(.system(size: 11.5))
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)

            Label(granted ? "辅助功能权限已开启" : "尚未开启辅助功能权限",
                  systemImage: granted ? "checkmark.circle.fill" : "exclamationmark.circle")
                .font(.system(size: 11.5, weight: .medium))
                .foregroundStyle(granted ? .green : .secondary)

            if !granted {
                Button("检查并授权") {
                    TextInserter.requestAccessibilityPermission()
                    TextInserter.openAccessibilitySettings()
                }
                .buttonStyle(.borderedProminent)
                .tint(.accentColor)
            }

            Text(granted ? "权限已开启，点击右下角继续。" : "在系统设置中打开 JustChat 后，这里会自动更新。")
                .font(.system(size: 10.5))
                .foregroundStyle(.secondary)
        }
        .padding(.horizontal, 16)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .onAppear { granted = TextInserter.isAccessibilityTrusted }
        .onReceive(timer) { _ in granted = TextInserter.isAccessibilityTrusted }
    }
}

private struct OnboardingPracticeView: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("30 秒完成第一次翻译")
                .font(.system(size: 17, weight: .semibold))
            Text("先在有输入框的软件里呼出面板。之后切换软件，写入目标也会跟随最新选择。")
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)

            VStack(spacing: 8) {
                practiceRow(number: 1, title: "呼出写作面板", shortcut: "⌘⇧ Space")
                practiceRow(number: 2, title: "输入中文并生成译文", shortcut: "⌘T")
                practiceRow(number: 3, title: "切到目标软件，点击输入框", shortcut: "")
                practiceRow(number: 4, title: "回到面板，写回译文", shortcut: "⌘S")
            }

            Text("写入前仍可修改译文；目标输入框不会被读取。")
                .font(.system(size: 10.5))
                .foregroundStyle(.secondary)
        }
        .padding(.horizontal, 16)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    private func practiceRow(number: Int, title: String, shortcut: String) -> some View {
        HStack(spacing: 9) {
            Text("\(number)")
                .font(.system(size: 10, weight: .semibold, design: .rounded))
                .frame(width: 19, height: 19)
                .background(Color.primary.opacity(0.09), in: Circle())
            Text(title)
                .font(.system(size: 11.5, weight: .medium))
                .lineLimit(2)
            Spacer(minLength: 3)
            if !shortcut.isEmpty {
                Text(shortcut)
                    .font(.system(size: 10, weight: .semibold, design: .rounded))
                    .foregroundStyle(.secondary)
                    .padding(.horizontal, 6)
                    .padding(.vertical, 3)
                    .background(Color.primary.opacity(0.07), in: RoundedRectangle(cornerRadius: 4))
            }
        }
        .frame(minHeight: 31)
    }
}
