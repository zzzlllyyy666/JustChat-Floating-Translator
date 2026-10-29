import XCTest
@testable import JustChatWriter

final class WriterTargetTrackingTests: XCTestCase {
    func testVisiblePanelFollowsLatestExternalApplication() {
        let ownPID = ProcessInfo.processInfo.processIdentifier
        XCTAssertTrue(WriterTargetTracking.shouldCapture(
            isPanelVisible: true,
            isWriting: false,
            frontmostPID: 100,
            ownPID: ownPID
        ))
        XCTAssertTrue(WriterTargetTracking.shouldCapture(
            isPanelVisible: true,
            isWriting: false,
            frontmostPID: 200,
            ownPID: ownPID
        ))
    }

    func testJustChatAndMissingFrontmostApplicationDoNotReplaceTarget() {
        let ownPID = ProcessInfo.processInfo.processIdentifier
        XCTAssertFalse(WriterTargetTracking.shouldCapture(
            isPanelVisible: true,
            isWriting: false,
            frontmostPID: ownPID,
            ownPID: ownPID
        ))
        XCTAssertFalse(WriterTargetTracking.shouldCapture(
            isPanelVisible: true,
            isWriting: false,
            frontmostPID: nil,
            ownPID: ownPID
        ))
    }

    func testHiddenPanelAndActiveInsertionKeepExistingTarget() {
        let ownPID = ProcessInfo.processInfo.processIdentifier
        XCTAssertFalse(WriterTargetTracking.shouldCapture(
            isPanelVisible: false,
            isWriting: false,
            frontmostPID: 200,
            ownPID: ownPID
        ))
        XCTAssertFalse(WriterTargetTracking.shouldCapture(
            isPanelVisible: true,
            isWriting: true,
            frontmostPID: 200,
            ownPID: ownPID
        ))
    }
}
