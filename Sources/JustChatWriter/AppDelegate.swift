import AppKit
import SwiftUI
import ServiceManagement

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem?
    private var hotKeyController: GlobalHotKeyController?
    private var panelController: WriterPanelController?
    private let configuration = ServiceConfiguration()

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        NSApp.mainMenu = ApplicationMenu.make()

        let state = WriterState(translationClient: TranslationClient(configuration: configuration))
        let inserter = TextInserter()
        let panelController = WriterPanelController(
            state: state,
            inserter: inserter,
            configuration: configuration
        )
        self.panelController = panelController

        hotKeyController = GlobalHotKeyController {
            Task { @MainActor [weak panelController] in
                panelController?.toggle()
            }
        }

        configureStatusItem()
        panelController.show()
        if !state.showingOnboarding && !configuration.isConfigured { openServiceSettings() }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        panelController?.show()
        return true
    }

    private func configureStatusItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        item.button?.image = NSImage(
            systemSymbolName: "character.bubble",
            accessibilityDescription: "JustChat"
        )

        let menu = NSMenu()
        let openItem = NSMenuItem(
            title: "打开写作面板    ⌘⇧ Space",
            action: #selector(openPanel),
            keyEquivalent: ""
        )
        openItem.target = self
        menu.addItem(openItem)

        let guideItem = NSMenuItem(title: "使用指南…", action: #selector(openOnboarding), keyEquivalent: "")
        guideItem.target = self
        menu.addItem(guideItem)

        let settingsItem = NSMenuItem(title: "翻译设置…", action: #selector(openServiceSettings), keyEquivalent: ",")
        settingsItem.target = self
        menu.addItem(settingsItem)

        let loginItem = NSMenuItem(title: "登录时打开 JustChat", action: #selector(toggleLaunchAtLogin(_:)), keyEquivalent: "")
        loginItem.target = self
        loginItem.state = SMAppService.mainApp.status == .enabled ? .on : .off
        menu.addItem(loginItem)

        menu.addItem(.separator())

        let permissionItem = NSMenuItem(
            title: "检查辅助功能权限",
            action: #selector(requestAccessibilityPermission),
            keyEquivalent: ""
        )
        permissionItem.target = self
        menu.addItem(permissionItem)
        menu.addItem(.separator())

        let quitItem = NSMenuItem(
            title: "退出 JustChat",
            action: #selector(quit),
            keyEquivalent: "q"
        )
        quitItem.target = self
        menu.addItem(quitItem)

        item.menu = menu
        statusItem = item
    }

    @objc private func openPanel() {
        panelController?.show()
    }

    @objc private func openOnboarding() {
        panelController?.showOnboarding()
    }

    @objc private func openServiceSettings() {
        panelController?.showAPISettings()
    }

    @objc private func requestAccessibilityPermission() {
        TextInserter.requestAccessibilityPermission()
    }

    @objc private func toggleLaunchAtLogin(_ item: NSMenuItem) {
        do {
            if SMAppService.mainApp.status == .enabled {
                try SMAppService.mainApp.unregister()
            } else {
                try SMAppService.mainApp.register()
                if SMAppService.mainApp.status == .requiresApproval {
                    SMAppService.openSystemSettingsLoginItems()
                }
            }
            item.state = SMAppService.mainApp.status == .enabled ? .on : .off
        } catch { NSAlert(error: error).runModal() }
    }

    @objc private func quit() {
        NSApp.terminate(nil)
    }
}
