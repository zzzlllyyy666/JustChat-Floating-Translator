import AppKit

@main
struct JustChatWriterApp {
    @MainActor static func main() {
        let application = NSApplication.shared
        if CommandLine.arguments.count == 3, CommandLine.arguments[1] == "--ui-check" {
            application.setActivationPolicy(.accessory)
            do { try UiChecks.run(output: URL(fileURLWithPath: CommandLine.arguments[2])) }
            catch { fputs("JustChat UI check failed: \(error.localizedDescription)\n", stderr); exit(1) }
            return
        }
        let delegate = AppDelegate()
        application.delegate = delegate
        withExtendedLifetime(delegate) { application.run() }
    }
}
