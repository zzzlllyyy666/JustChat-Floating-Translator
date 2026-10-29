using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using FormsClipboard = System.Windows.Forms.Clipboard;

namespace JustChat.UiSmokeTests;

internal static class Program
{
    private const string FirstTranslation = "First paragraph.\r\nSecond paragraph.";
    private const string SecondTranslation = "Third paragraph.\r\nFourth paragraph.";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: JustChat.UiSmokeTests <JustChat.exe path> <results directory>");
            return 2;
        }

        var appPath = Path.GetFullPath(args[0]);
        var resultsDirectory = Path.GetFullPath(args[1]);
        var appDataDirectory = Path.Combine(resultsDirectory, "AppData");
        Directory.CreateDirectory(appDataDirectory);
        if (!File.Exists(appPath))
        {
            Console.Error.WriteLine($"Application not found: {appPath}");
            return 2;
        }

        var checks = new List<string>();
        string? error = null;
        var exitCode = 0;
        var reservedHotKey = false;
        Process? app = null;
        AutomationElement? mainWindow = null;
        using var server = new MockTranslationServer();
        using var target = new TargetWindowHost();
        Log($"Application: {appPath}");
        Log($"Mock endpoint: {server.Endpoint}");
        Log($"Windows interactive session: {Environment.UserInteractive}");

        try
        {
            UiDriver.ReservePrimaryHotKey(target.Handle);
            reservedHotKey = true;
            app = LaunchApp(appPath, appDataDirectory, server.Endpoint);
            mainWindow = UiDriver.WaitForWindow(app.Id, "JustChat", TimeSpan.FromSeconds(30));
            var mainHandle = mainWindow.Current.NativeWindowHandle;
            Pass(checks, "Published JustChat.exe starts and exposes its main window");

            CompleteFirstRunOnboarding(mainWindow, mainHandle, resultsDirectory);
            if (server.RequestCount != 1)
            {
                throw new InvalidOperationException($"Expected one onboarding request, got {server.RequestCount}.");
            }
            Pass(checks, "First-run guide shows all three steps, saves the key and verifies connection");

            var hotKeyHint = UiDriver.FindById(mainWindow, "HotKeyHintTextBlock");
            UiDriver.WaitUntil(
                () => hotKeyHint.Current.Name.StartsWith("Ctrl+Alt+Space", StringComparison.Ordinal),
                "The fallback hotkey is active while the primary hotkey is occupied");
            Pass(checks, "An occupied primary hotkey automatically switches to Ctrl+Alt+Space");

            Translate(mainWindow, "第一段\r\n第二段", FirstTranslation);
            if (server.RequestCount != 2)
            {
                throw new InvalidOperationException($"Expected two requests after the first translation, got {server.RequestCount}.");
            }
            Pass(checks, "UI translation sends the segmented draft to the mock HTTP service");
            UiDriver.CaptureWindow(mainHandle, Path.Combine(resultsDirectory, "01-translation.png"));

            SetClipboardText("original-clipboard");
            WriteBack(mainWindow, target, editorIndex: 0, FirstTranslation, mainHandle, useFallback: true);
            WaitForClipboard("original-clipboard");
            Pass(checks, "Fallback hotkey and Ctrl+S write back to target A and restore the clipboard");

            UiDriver.ReleasePrimaryHotKey(target.Handle);
            reservedHotKey = false;
            UiDriver.Invoke(UiDriver.FindById(mainWindow, "HotKeyRetryButton"));
            UiDriver.WaitUntil(
                () => hotKeyHint.Current.Name.StartsWith("Ctrl+Shift+Space", StringComparison.Ordinal),
                "The primary hotkey is restored after the conflict is removed");
            Pass(checks, "The primary hotkey can be restored without restarting the application");

            UiDriver.Activate(mainWindow);
            UiDriver.SendControlShiftSpace();
            UiDriver.WaitUntil(
                () => NativeMethods.GetForegroundWindow() != mainHandle,
                "The panel hides when its global hotkey is pressed while active");
            target.FocusEditor(0);
            UiDriver.CaptureWindow(target.Handle, Path.Combine(resultsDirectory, "02-write-back.png"));
            Pass(checks, "Global hotkey hides the active floating panel");

            target.FocusEditor(0);
            UiDriver.WaitForForeground(target.Handle, "Target A is active before the second translation");
            UiDriver.SendControlShiftSpace();
            UiDriver.WaitForForeground(mainHandle, "The global hotkey reopens the floating panel");
            Translate(mainWindow, "第三段\r\n第四段", SecondTranslation);
            if (server.RequestCount != 3)
            {
                throw new InvalidOperationException($"Expected three requests after the second translation, got {server.RequestCount}.");
            }

            SetClipboardText("second-original-clipboard");
            WriteBack(mainWindow, target, editorIndex: 1, SecondTranslation, mainHandle);
            WaitForClipboard("second-original-clipboard");
            UiDriver.WaitForValue(
                UiDriver.FindById(AutomationElement.FromHandle(target.Handle), "TargetA"),
                FirstTranslation,
                "Target A remains unchanged after switching to target B");
            Pass(checks, "Switching from target A to B writes only to B and preserves the clipboard");

            UiDriver.Activate(mainWindow);
            UiDriver.Invoke(UiDriver.FindById(mainWindow, "PreferencesButton"));
            var preferencesWindow = UiDriver.WaitForWindow(app.Id, "默认翻译习惯");
            UiDriver.SetValue(UiDriver.FindById(preferencesWindow, "IndustryTextBox"), "Cloud QA");
            UiDriver.SetValue(UiDriver.FindById(preferencesWindow, "ToneTextBox"), "Direct");
            UiDriver.Invoke(UiDriver.FindButtonByName(preferencesWindow, "设为默认"));
            var preferencesPath = Path.Combine(appDataDirectory, "JustChat", "preferences.json");
            UiDriver.WaitUntil(
                () => PreferencesContain(preferencesPath, "Cloud QA", "Direct"),
                "Preferences are saved to the isolated test profile");
            UiDriver.CaptureWindow(mainHandle, Path.Combine(resultsDirectory, "03-preferences.png"));
            Pass(checks, "Preferences can be edited and saved through the real dialog");

            StopApp(app);
            app = null;
            app = LaunchApp(appPath, appDataDirectory, server.Endpoint, theme: "dark");
            mainWindow = UiDriver.WaitForWindow(app.Id, "JustChat", TimeSpan.FromSeconds(30));
            var preferencesButton = UiDriver.FindById(mainWindow, "PreferencesButton");
            UiDriver.WaitUntil(
                () => preferencesButton.Current.Name == "Cloud QA · Direct",
                "Saved preferences are loaded after restart");
            UiDriver.CaptureWindow(
                mainWindow.Current.NativeWindowHandle,
                Path.Combine(resultsDirectory, "04-dark-restart.png"));
            Pass(checks, "Dark theme starts cleanly and saved preferences survive restart");

            UiDriver.Invoke(UiDriver.FindById(mainWindow, "GuideButton"));
            UiDriver.Invoke(UiDriver.FindById(mainWindow, "OnboardingSkipButton"));
            UiDriver.Invoke(UiDriver.FindById(mainWindow, "OnboardingContinueButton"));
            UiDriver.Invoke(UiDriver.FindById(mainWindow, "OnboardingFinishButton"));
            Pass(checks, "The complete guide can be reopened from the main window");
            Log("All Windows UI acceptance checks passed.");
        }
        catch (Exception exception)
        {
            exitCode = 1;
            error = exception.ToString();
            Console.Error.WriteLine(error);
            TryCaptureFailure(mainWindow, target, resultsDirectory);
        }
        finally
        {
            if (reservedHotKey)
            {
                UiDriver.ReleasePrimaryHotKey(target.Handle);
            }
            if (app is not null)
            {
                StopApp(app);
            }

            var result = new
            {
                success = exitCode == 0,
                platform = Environment.OSVersion.ToString(),
                checks,
                mockRequests = server.RequestCount,
                error
            };
            File.WriteAllText(
                Path.Combine(resultsDirectory, "results.json"),
                JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }

        return exitCode;
    }

    private static Process LaunchApp(string appPath, string appDataDirectory, Uri endpoint, string theme = "light")
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = appPath,
            WorkingDirectory = Path.GetDirectoryName(appPath)!,
            UseShellExecute = false
        };
        startInfo.Environment["APPDATA"] = appDataDirectory;
        startInfo.Environment["JUSTCHAT_CONFIG_DIRECTORY"] = Path.Combine(appDataDirectory, "JustChat");
        startInfo.Environment["JUSTCHAT_TRANSLATION_ENDPOINT"] = endpoint.AbsoluteUri;
        startInfo.Environment["JUSTCHAT_THEME"] = theme;
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start JustChat.");
    }

    private static void CompleteFirstRunOnboarding(
        AutomationElement mainWindow,
        nint mainHandle,
        string resultsDirectory)
    {
        UiDriver.Activate(mainWindow);
        UiDriver.CaptureWindow(mainHandle, Path.Combine(resultsDirectory, "00-onboarding.png"));
        UiDriver.FindById(mainWindow, "OnboardingApiKeyPasswordBox").SetFocus();
        Thread.Sleep(200);
        UiDriver.TypeAscii("ci-smoke-key");
        UiDriver.Invoke(UiDriver.FindById(mainWindow, "OnboardingConnectButton"));
        UiDriver.Invoke(UiDriver.FindById(mainWindow, "OnboardingContinueButton"));
        UiDriver.Invoke(UiDriver.FindById(mainWindow, "OnboardingFinishButton"));
    }

    private static void Translate(AutomationElement mainWindow, string draft, string expected)
    {
        UiDriver.SetValue(UiDriver.FindById(mainWindow, "DraftTextBox"), draft);
        UiDriver.Invoke(UiDriver.FindById(mainWindow, "TranslateButton"));
        UiDriver.WaitForValue(
            UiDriver.FindById(mainWindow, "TranslationTextBox"),
            expected,
            $"Translation '{expected.Replace("\r\n", " / ", StringComparison.Ordinal)}'");
    }

    private static void WriteBack(
        AutomationElement mainWindow,
        TargetWindowHost target,
        int editorIndex,
        string expected,
        nint mainHandle,
        bool useFallback = false)
    {
        target.FocusEditor(editorIndex);
        UiDriver.WaitForForeground(target.Handle, "The intended target window is active");
        if (useFallback)
        {
            UiDriver.SendControlAltSpace();
        }
        else
        {
            UiDriver.SendControlShiftSpace();
        }
        UiDriver.WaitForForeground(mainHandle, "The global hotkey captures the intended target and opens JustChat");
        UiDriver.SendControlS();
        var editor = UiDriver.FindById(
            AutomationElement.FromHandle(target.Handle),
            editorIndex == 0 ? "TargetA" : "TargetB");
        UiDriver.WaitForValue(editor, expected, $"Translation is pasted into target {(editorIndex == 0 ? 'A' : 'B')}");
    }

    private static bool PreferencesContain(string path, string industry, string tone)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.GetProperty("Industry").GetString() == industry &&
                document.RootElement.GetProperty("Tone").GetString() == tone;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void SetClipboardText(string text)
    {
        UiDriver.WaitUntil(
            () =>
            {
                try
                {
                    FormsClipboard.SetText(text);
                    return true;
                }
                catch (ExternalException)
                {
                    return false;
                }
            },
            "The test clipboard can be initialized");
    }

    private static void WaitForClipboard(string expected)
    {
        UiDriver.WaitUntil(
            () =>
            {
                try
                {
                    return FormsClipboard.ContainsText() && FormsClipboard.GetText() == expected;
                }
                catch (ExternalException)
                {
                    return false;
                }
            },
            "The original clipboard text is restored",
            TimeSpan.FromSeconds(15));
    }

    private static void TryCaptureFailure(
        AutomationElement? mainWindow,
        TargetWindowHost target,
        string resultsDirectory)
    {
        try
        {
            if (mainWindow is not null)
            {
                UiDriver.CaptureWindow(
                    mainWindow.Current.NativeWindowHandle,
                    Path.Combine(resultsDirectory, "failure-application.png"));
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Could not capture application failure screenshot: {exception.Message}");
        }

        try
        {
            UiDriver.CaptureWindow(target.Handle, Path.Combine(resultsDirectory, "failure-target.png"));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Could not capture target failure screenshot: {exception.Message}");
        }
    }

    private static void StopApp(Process app)
    {
        if (!app.HasExited)
        {
            app.Kill(entireProcessTree: true);
            app.WaitForExit(10_000);
        }

        app.Dispose();
    }

    private static void Pass(List<string> checks, string description)
    {
        checks.Add(description);
        Log($"PASS: {description}");
    }

    private static void Log(string message) => Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}");
}

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();
}
