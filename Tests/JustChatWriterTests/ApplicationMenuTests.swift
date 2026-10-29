import AppKit
import XCTest
@testable import JustChatWriter

final class ApplicationMenuTests: XCTestCase {
    @MainActor
    func testStandardEditingShortcutsUseFocusedResponder() throws {
        let menu = ApplicationMenu.make()
        let edit = try XCTUnwrap(menu.item(withTitle: "编辑")?.submenu)
        XCTAssertTrue(edit.autoenablesItems)
        let expected = [
            ("撤销", "undo:", "z"), ("剪切", "cut:", "x"),
            ("复制", "copy:", "c"), ("粘贴", "paste:", "v"),
            ("全选", "selectAll:", "a")
        ]
        for (title, action, key) in expected {
            let item = try XCTUnwrap(edit.item(withTitle: title))
            XCTAssertEqual(item.action, Selector(action))
            XCTAssertEqual(item.keyEquivalent, key)
            XCTAssertEqual(item.keyEquivalentModifierMask, .command)
            XCTAssertNil(item.target, "Editing must follow focus, not be sent to a fixed view")
        }
        let redo = try XCTUnwrap(edit.item(withTitle: "重做"))
        XCTAssertEqual(redo.action, Selector(("redo:")))
        XCTAssertEqual(redo.keyEquivalent, "z")
        XCTAssertEqual(redo.keyEquivalentModifierMask, [.command, .shift])
        XCTAssertNil(redo.target)
    }
}
