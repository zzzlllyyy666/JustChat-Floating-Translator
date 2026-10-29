import AppKit
import SwiftUI

// Only renders our own views with isolated configuration; no service, Key, hotkey or paste.
@MainActor
enum UiChecks {
    static func run(output: URL) throws {
        try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
        let configuration = ServiceConfiguration(directory: output.appendingPathComponent("isolated-config"))
        let defaults = UserDefaults(suiteName: "JustChat.UI.Check.\(UUID().uuidString)")!
        let state = WriterState(translationClient: TranslationClient(endpoint: nil, session: .shared), defaults: defaults)
        let view = WriterView(state: state, onWrite: {}, configuration: configuration, onOpenAPISettings: { state.openAPISettings() })
        let host = NSHostingView(rootView: view)
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 340, height: 360),
                              styleMask: [.titled], backing: .buffered, defer: false)
        window.contentView = host
        window.isReleasedWhenClosed = false
        window.orderFront(nil)
        defer { window.orderOut(nil) }

        for (name, appearance) in [("light", NSAppearance.Name.aqua), ("dark", .darkAqua)] {
            window.appearance = NSAppearance(named: appearance)
            state.showOnboarding()
            try capture(host, to: output.appendingPathComponent("onboarding-connection-\(name).png"))
            state.advanceOnboarding()
            try capture(host, to: output.appendingPathComponent("onboarding-permission-\(name).png"))
            state.advanceOnboarding()
            try capture(host, to: output.appendingPathComponent("onboarding-practice-\(name).png"))
            state.completeOnboarding()
            state.startNewDraft()
            state.updateDraftText("您好，我们可以安排下周发货。")
            try capture(host, to: output.appendingPathComponent("draft-\(name).png"))
            state.openAPISettings()
            try capture(host, to: output.appendingPathComponent("api-settings-\(name).png"))
            state.closeAPISettings()
            guard state.draft == "您好，我们可以安排下周发货。" else {
                throw CocoaError(.coderInvalidValue)
            }
        }
    }

    private static func capture(_ view: NSView, to url: URL) throws {
        RunLoop.main.run(until: Date().addingTimeInterval(0.2))
        view.layoutSubtreeIfNeeded()
        guard let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else {
            throw CocoaError(.fileWriteUnknown)
        }
        view.cacheDisplay(in: view.bounds, to: bitmap)
        guard let data = bitmap.representation(using: .png, properties: [:]) else {
            throw CocoaError(.fileWriteUnknown)
        }
        try data.write(to: url)
    }
}
