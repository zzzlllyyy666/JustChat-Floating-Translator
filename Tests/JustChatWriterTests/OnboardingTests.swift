import XCTest
@testable import JustChatWriter

@MainActor
final class OnboardingTests: XCTestCase {
    func testFreshInstallationShowsConnectionStepAndCompletionPersists() {
        let (state, defaults, suiteName) = makeState()
        defer { defaults.removePersistentDomain(forName: suiteName) }
        XCTAssertTrue(state.showingOnboarding)
        XCTAssertEqual(state.onboardingStep, .connection)

        state.completeOnboarding()

        XCTAssertFalse(state.showingOnboarding)
        let recreated = WriterState(translationClient: TranslationClient(), defaults: defaults)
        XCTAssertFalse(recreated.showingOnboarding)
    }

    func testGuideAdvancesAndMovesBackWithoutChangingDraft() {
        let (state, defaults, suiteName) = makeState()
        defer { defaults.removePersistentDomain(forName: suiteName) }
        state.updateDraftText("保留这段中文")

        state.advanceOnboarding()
        XCTAssertEqual(state.onboardingStep, .permission)
        state.advanceOnboarding()
        XCTAssertEqual(state.onboardingStep, .practice)
        state.onboardingBack()
        XCTAssertEqual(state.onboardingStep, .permission)
        XCTAssertEqual(state.draft, "保留这段中文")
        XCTAssertTrue(state.showingOnboarding)
    }

    func testGuideCanBeReopenedAndCompleted() {
        let (state, defaults, suiteName) = makeState()
        defer { defaults.removePersistentDomain(forName: suiteName) }
        state.completeOnboarding()

        state.showOnboarding()
        XCTAssertTrue(state.showingOnboarding)
        XCTAssertEqual(state.onboardingStep, .connection)
        state.completeOnboarding()
        XCTAssertFalse(state.showingOnboarding)
        XCTAssertTrue(defaults.bool(forKey: "onboarding.completed"))
    }

    func testPostponingEarlierStepsStillShowsEveryStep() {
        let (state, defaults, suiteName) = makeState()
        defer { defaults.removePersistentDomain(forName: suiteName) }

        state.postponeOnboardingStep()
        XCTAssertTrue(state.showingOnboarding)
        XCTAssertEqual(state.onboardingStep, .permission)
        XCTAssertFalse(defaults.bool(forKey: "onboarding.completed"))

        state.postponeOnboardingStep()
        XCTAssertTrue(state.showingOnboarding)
        XCTAssertEqual(state.onboardingStep, .practice)
        XCTAssertFalse(defaults.bool(forKey: "onboarding.completed"))

        state.completeOnboarding()
        XCTAssertFalse(state.showingOnboarding)
        XCTAssertTrue(defaults.bool(forKey: "onboarding.completed"))
    }

    func testAPISettingsTemporarilyLeaveGuideWithoutMarkingItComplete() {
        let (state, defaults, suiteName) = makeState()
        defer { defaults.removePersistentDomain(forName: suiteName) }

        state.openAPISettings()

        XCTAssertFalse(state.showingOnboarding)
        XCTAssertTrue(state.showingAPISettings)
        XCTAssertFalse(defaults.bool(forKey: "onboarding.completed"))
    }

    private func makeState() -> (WriterState, UserDefaults, String) {
        let suiteName = "JustChatWriterTests.Onboarding.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suiteName)!
        let state = WriterState(translationClient: TranslationClient(), defaults: defaults)
        return (state, defaults, suiteName)
    }
}
