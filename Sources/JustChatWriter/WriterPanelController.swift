import AppKit
import SwiftUI

@MainActor
final class WriterPanelController: NSObject, NSWindowDelegate {
    private let state: WriterState
    private let inserter: TextInserter
    private let panel: NSPanel
    private var targetReturnMonitor: Any?
    private var targetReturnTimeoutTask: Task<Void, Never>?
    private var targetActivationObserver: NSObjectProtocol?

    init(state: WriterState, inserter: TextInserter, configuration: ServiceConfiguration) {
        self.state = state
        self.inserter = inserter

        panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 340, height: 360),
            styleMask: [.titled, .closable, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )

        super.init()

        panel.title = "JustChat"
        panel.titleVisibility = .hidden
        panel.titlebarAppearsTransparent = true
        panel.isMovableByWindowBackground = true
        panel.isReleasedWhenClosed = false
        panel.hidesOnDeactivate = false
        panel.level = .floating
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.backgroundColor = NSColor(name: nil) { appearance in
            let match = appearance.bestMatch(from: [.darkAqua, .aqua])
            if match == .darkAqua {
                return NSColor(red: 0.13, green: 0.14, blue: 0.14, alpha: 1)
            }
            return NSColor(red: 0.79, green: 0.80, blue: 0.80, alpha: 1)
        }
        panel.delegate = self

        let view = WriterView(
            state: state,
            onWrite: { [weak self] in self?.writePreview() },
            configuration: configuration,
            onOpenAPISettings: { [weak self] in self?.showAPISettings() }
        )
        panel.contentView = NSHostingView(rootView: view)
        startTargetTracking()
        positionPanel()
    }

    func toggle() {
        if panel.isVisible, panel.isKeyWindow {
            hide()
        } else {
            show()
        }
    }

    func show() {
        stopWaitingForTargetReturn()
        state.startNewDraft()
        inserter.captureFrontmostTarget()
        positionPanel()
        panel.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        state.requestFocus()
    }

    func hide() {
        stopWaitingForTargetReturn()
        state.closeAPISettings()
        panel.orderOut(nil)
    }

    func showAPISettings() {
        guard !state.isWriting else { return }
        stopWaitingForTargetReturn()
        if !panel.isVisible { inserter.captureFrontmostTarget() }
        state.openAPISettings()
        showWithoutChangingTarget()
    }

    func showOnboarding() {
        guard !state.isWriting else { return }
        stopWaitingForTargetReturn()
        if !panel.isVisible { inserter.captureFrontmostTarget() }
        state.showOnboarding()
        showWithoutChangingTarget()
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        hide()
        return false
    }

    private func positionPanel() {
        guard let screen = NSScreen.main else {
            panel.center()
            return
        }
        let frame = screen.visibleFrame
        let origin = NSPoint(
            x: frame.maxX - panel.frame.width - 28,
            y: frame.maxY - panel.frame.height - 28
        )
        panel.setFrameOrigin(origin)
    }

    private func writePreview() {
        guard state.canWrite else { return }
        let text = state.preview
        let sessionID = state.editorSessionID
        state.insertionStarted()

        Task { @MainActor [weak self] in
            guard let self else { return }
            var didPostPaste = false
            do {
                try await inserter.insert(text) { [weak self] in
                    guard let self else { return }
                    didPostPaste = true
                    guard state.editorSessionID == sessionID else { return }
                    state.insertionSucceeded()
                    showWithoutTakingFocus()
                    waitForTargetReturn()
                }
            } catch {
                NSLog("JustChat write failed: %@", error.localizedDescription)
                guard state.editorSessionID == sessionID, !didPostPaste else { return }
                state.insertionFailed(error)
                showWithoutChangingTarget()
            }
        }
    }

    private func showWithoutChangingTarget() {
        stopWaitingForTargetReturn()
        positionPanel()
        panel.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        state.requestFocus()
    }

    private func showWithoutTakingFocus() {
        positionPanel()
        panel.orderFrontRegardless()
    }

    private func waitForTargetReturn() {
        stopWaitingForTargetReturn()

        targetReturnMonitor = NSEvent.addGlobalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard event.keyCode == 36 || event.keyCode == 76 else { return }
            let newlineModifiers: NSEvent.ModifierFlags = [.shift, .option]
            guard event.modifierFlags.intersection(newlineModifiers).isEmpty else { return }

            Task { @MainActor [weak self] in
                self?.targetReturnPressed()
            }
        }

        targetReturnTimeoutTask = Task { @MainActor [weak self] in
            try? await Task.sleep(nanoseconds: 60_000_000_000)
            guard !Task.isCancelled else { return }
            self?.stopWaitingForTargetReturn()
        }
    }

    private func targetReturnPressed() {
        guard inserter.isTargetFrontmost else { return }
        stopWaitingForTargetReturn()

        targetReturnTimeoutTask = Task { @MainActor [weak self] in
            try? await Task.sleep(nanoseconds: 80_000_000)
            guard let self, !Task.isCancelled, inserter.isTargetFrontmost else { return }
            state.targetReturnCompleted()
            showWithoutChangingTarget()
        }
    }

    private func startTargetTracking() {
        guard targetActivationObserver == nil else { return }
        targetActivationObserver = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didActivateApplicationNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            Task { @MainActor in
                self?.captureCurrentTarget()
            }
        }
    }

    private func captureCurrentTarget() {
        guard WriterTargetTracking.shouldCapture(
            isPanelVisible: panel.isVisible,
            isWriting: state.isWriting,
            frontmostPID: NSWorkspace.shared.frontmostApplication?.processIdentifier,
            ownPID: ProcessInfo.processInfo.processIdentifier
        ) else { return }
        inserter.captureFrontmostTarget()
    }

    private func stopWaitingForTargetReturn() {
        if let targetReturnMonitor {
            NSEvent.removeMonitor(targetReturnMonitor)
            self.targetReturnMonitor = nil
        }
        targetReturnTimeoutTask?.cancel()
        targetReturnTimeoutTask = nil
    }

    deinit {
        if let targetActivationObserver {
            NSWorkspace.shared.notificationCenter.removeObserver(targetActivationObserver)
        }
    }
}

enum WriterTargetTracking {
    static func shouldCapture(
        isPanelVisible: Bool,
        isWriting: Bool,
        frontmostPID: pid_t?,
        ownPID: pid_t
    ) -> Bool {
        guard isPanelVisible, !isWriting, let frontmostPID else { return false }
        return frontmostPID != ownPID
    }
}
