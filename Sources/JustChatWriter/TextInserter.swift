import AppKit
import ApplicationServices

@MainActor
final class TextInserter {
    private struct PasteboardSnapshot {
        let items: [[NSPasteboard.PasteboardType: Data]]

        init(pasteboard: NSPasteboard) {
            items = (pasteboard.pasteboardItems ?? []).map { item in
                Dictionary(uniqueKeysWithValues: item.types.compactMap { type in
                    item.data(forType: type).map { (type, $0) }
                })
            }
        }

        func restore(to pasteboard: NSPasteboard) {
            pasteboard.clearContents()
            let restoredItems = items.map { values -> NSPasteboardItem in
                let item = NSPasteboardItem()
                for (type, data) in values {
                    item.setData(data, forType: type)
                }
                return item
            }
            if !restoredItems.isEmpty {
                pasteboard.writeObjects(restoredItems)
            }
        }
    }

    enum InsertionError: LocalizedError {
        case targetUnavailable
        case accessibilityPermissionRequired
        case pasteEventUnavailable

        var errorDescription: String? {
            switch self {
            case .targetUnavailable:
                "找不到刚才的目标 APP，请回到目标输入框后重新呼出面板"
            case .accessibilityPermissionRequired:
                "需要辅助功能权限才能把译文写入当前光标。请在系统设置中允许 JustChat"
            case .pasteEventUnavailable:
                "无法发送粘贴指令，译文仍保留在面板中"
            }
        }
    }

    private var targetApplication: NSRunningApplication?

    var isTargetFrontmost: Bool {
        guard let targetApplication, !targetApplication.isTerminated else { return false }
        return NSWorkspace.shared.frontmostApplication?.processIdentifier == targetApplication.processIdentifier
    }

    func captureFrontmostTarget() {
        guard let application = NSWorkspace.shared.frontmostApplication,
              application.processIdentifier != ProcessInfo.processInfo.processIdentifier else {
            return
        }
        targetApplication = application
    }

    static var isAccessibilityTrusted: Bool {
        AXIsProcessTrusted()
    }

    static func requestAccessibilityPermission() {
        let key = kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String
        AXIsProcessTrustedWithOptions([key: true] as CFDictionary)
    }

    static func openAccessibilitySettings() {
        guard let url = URL(
            string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility"
        ) else { return }
        NSWorkspace.shared.open(url)
    }

    func insert(
        _ text: String,
        onPastePosted: () -> Void = {}
    ) async throws {
        guard let targetApplication, !targetApplication.isTerminated else {
            throw InsertionError.targetUnavailable
        }
        guard AXIsProcessTrusted() else {
            Self.requestAccessibilityPermission()
            throw InsertionError.accessibilityPermissionRequired
        }

        let pasteboard = NSPasteboard.general
        let snapshot = PasteboardSnapshot(pasteboard: pasteboard)
        pasteboard.clearContents()
        guard pasteboard.setString(text, forType: .string) else {
            snapshot.restore(to: pasteboard)
            throw InsertionError.pasteEventUnavailable
        }
        let temporaryChangeCount = pasteboard.changeCount
        defer {
            if pasteboard.changeCount == temporaryChangeCount,
               pasteboard.string(forType: .string) == text {
                snapshot.restore(to: pasteboard)
            }
        }

        targetApplication.activate(options: [.activateAllWindows])
        for _ in 0..<25 {
            if NSWorkspace.shared.frontmostApplication?.processIdentifier
                == targetApplication.processIdentifier {
                break
            }
            try await Task.sleep(nanoseconds: 40_000_000)
        }
        guard NSWorkspace.shared.frontmostApplication?.processIdentifier
                == targetApplication.processIdentifier else {
            throw InsertionError.targetUnavailable
        }

        // Resolve focus after activating the latest target application. A saved
        // AXUIElement can point to a field the user has already left.
        if let targetElement = focusedElement(in: targetApplication) {
            AXUIElementSetAttributeValue(
                targetElement,
                kAXFocusedAttribute as CFString,
                kCFBooleanTrue
            )
        }
        try await Task.sleep(nanoseconds: 160_000_000)

        guard let source = CGEventSource(stateID: .hidSystemState),
              let keyDown = CGEvent(keyboardEventSource: source, virtualKey: 0x09, keyDown: true),
              let keyUp = CGEvent(keyboardEventSource: source, virtualKey: 0x09, keyDown: false) else {
            throw InsertionError.pasteEventUnavailable
        }

        keyDown.flags = .maskCommand
        keyUp.flags = .maskCommand
        keyDown.post(tap: .cghidEventTap)
        keyUp.post(tap: .cghidEventTap)
        onPastePosted()

        // Office and HTML mail editors may consume the clipboard lazily.
        try await Task.sleep(nanoseconds: 1_100_000_000)
    }

    private func focusedElement(
        in application: NSRunningApplication
    ) -> AXUIElement? {
        let applicationElement = AXUIElementCreateApplication(
            application.processIdentifier
        )
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(
            applicationElement,
            kAXFocusedUIElementAttribute as CFString,
            &value
        ) == .success,
        let value,
        CFGetTypeID(value) == AXUIElementGetTypeID() else {
            return nil
        }
        return unsafeBitCast(value, to: AXUIElement.self)
    }
}
