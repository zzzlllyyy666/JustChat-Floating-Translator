using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using FormsClipboard = System.Windows.Forms.Clipboard;

namespace JustChat.UiSmokeTests;

internal static class Program
{
    private const string FirstTranslation = "First paragraph\r\nSecond paragraph";
    private const string SecondTranslation = "Third paragraph\r\nFourth paragraph";

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
        Process? app = null;
        AutomationElement? mainWindow = null;
        using var server = new MockTranslationServer();
        using var target = new TargetWindowHost();
        Log($"Application: {appPath}");
        Log($"Mock endpoint: {server.Endpoint}");
        Log($"Windows interactive session: {Environment.UserInteractive}");

        try
        {
            app = LaunchApp(appPath, appDataDirectory, server.Endpoint);
            mainWindow = UiDriver.WaitForWindow(app.Id, "JustChat", TimeSpan.FromSeconds(30));
            var mainHandle = mainWindow.Current.NativeWindowHandle;
            Pass(checks, "Published JustChat.exe starts and exposes its main window");

            UiDriver.Activate(mainWindow);
            UiDriver.FindById(mainWindow, "ApiKeyPasswordBox").SetFocus();
            Thread.Sleep(200);
            UiDriver.TypeAscii("ci-smoke-key");
            Translate(mainWindow, "第一段\r\n第二段", FirstTranslation);
            Pass(checks, "UI translation sends the segmented draft to the mock HTTP service");
            if (server.RequestCount != 1)
            {
                throw new InvalidOperationException($"Expected one translation request, got {server.RequestCount}.");
            }

            UiDriver.CaptureWindow(mainHandle, Path.Combine(resultsDirectory, "01-translation.png"));
            SetClipboardText("original-clipboard");
            WriteBack(mainWindow, target, editorIndex: 0, FirstTranslation, mainHandle);
            WaitForClipboard("original-clipboard");
            Pass(checks, "Global hotkey and Ctrl+S write back to target A and restore the clipboard");

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
            if (server.RequestCount != 2)
            {
                throw new InvalidOperationException($"Expected two translation requests, got {server.RequestCount}.");
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
            app = LaunchApp(appPath, appDataDirectory, server.Endpoint);
            mainWindow = UiDriver.WaitForWindow(app.Id, "JustChat", TimeSpan.FromSeconds(30));
            var preferencesButton = UiDriver.FindById(mainWindow, "PreferencesButton");
            UiDriver.WaitUntil(
                () => preferencesButton.Current.Name == "Cloud QA · Direct",
                "Saved preferences are loaded after restart");
            Pass(checks, "Saved preferences survive an application restart");
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

    private static Process LaunchApp(string appPath, string appDataDirectory, Uri endpoint)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = appPath,
            WorkingDirectory = Path.GetDirectoryName(appPath)!,
            UseShellExecute = false
        };
        startInfo.Environment["APPDATA"] = appDataDirectory;
        startInfo.Environment["JUSTCHAT_TRANSLATION_ENDPOINT"] = endpoint.AbsoluteUri;
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start JustChat.");
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
        nint mainHandle)
    {
        target.FocusEditor(editorIndex);
        UiDriver.WaitForForeground(target.Handle, "The intended target window is active");
        UiDriver.SendControlShiftSpace();
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
