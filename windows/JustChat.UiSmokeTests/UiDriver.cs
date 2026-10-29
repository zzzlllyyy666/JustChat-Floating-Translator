using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace JustChat.UiSmokeTests;

internal static class UiDriver
{
    private const int SwRestore = 9;
    private const int TestHotKeyId = 0x4A99;
    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFUnicode = 0x0004;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const ushort VkControl = 0x11;
    private const ushort VkShift = 0x10;
    private const ushort VkMenu = 0x12;
    private const ushort VkSpace = 0x20;
    private const ushort VkS = 0x53;

    public static AutomationElement WaitForWindow(int processId, string title, TimeSpan? timeout = null)
    {
        var processCondition = new PropertyCondition(AutomationElement.ProcessIdProperty, processId);
        var windowCondition = new AndCondition(
            processCondition,
            new PropertyCondition(AutomationElement.NameProperty, title));
        return WaitForElement(
            () => AutomationElement.RootElement.FindFirst(TreeScope.Descendants, windowCondition),
            $"Window '{title}' for process {processId}",
            timeout);
    }

    public static AutomationElement FindById(AutomationElement root, string automationId) =>
        WaitForElement(
            () => root.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, automationId)),
            $"Automation element '{automationId}'");

    public static AutomationElement FindButtonByName(AutomationElement root, string name) =>
        WaitForElement(
            () => root.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.NameProperty, name))),
            $"Button '{name}'");

    public static void SetValue(AutomationElement element, string value)
    {
        if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
        {
            throw new InvalidOperationException($"Element '{element.Current.AutomationId}' has no value pattern.");
        }

        ((ValuePattern)pattern).SetValue(value);
    }

    public static string GetValue(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
        {
            throw new InvalidOperationException($"Element '{element.Current.AutomationId}' has no value pattern.");
        }

        return ((ValuePattern)pattern).Current.Value;
    }

    public static void WaitForValue(AutomationElement element, string expected, string description)
    {
        WaitUntil(
            () => string.Equals(
                NormalizeLines(GetValue(element)),
                NormalizeLines(expected),
                StringComparison.Ordinal),
            description,
            TimeSpan.FromSeconds(20));
    }

    public static void Invoke(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
        {
            throw new InvalidOperationException($"Element '{element.Current.AutomationId}' cannot be invoked.");
        }

        ((InvokePattern)pattern).Invoke();
    }

    public static void Activate(AutomationElement window)
    {
        var handle = window.Current.NativeWindowHandle;
        ShowWindow(handle, SwRestore);
        SetForegroundWindow(handle);
        WaitUntil(
            () => GetForegroundWindow() == handle,
            $"Foreground window '{window.Current.Name}'");
    }

    public static void WaitForForeground(nint handle, string description) =>
        WaitUntil(() => GetForegroundWindow() == handle, description);

    public static void SendControlShiftSpace() => SendChord([VkControl, VkShift], VkSpace);

    public static void SendControlAltSpace() => SendChord([VkControl, VkMenu], VkSpace);

    public static void SendControlS() => SendChord([VkControl], VkS);

    public static void ReservePrimaryHotKey(nint handle)
    {
        if (!RegisterHotKey(handle, TestHotKeyId, ModControl | ModShift | ModNoRepeat, VkSpace))
        {
            throw new InvalidOperationException($"Could not reserve the test hotkey: {Marshal.GetLastWin32Error()}.");
        }
    }

    public static void ReleasePrimaryHotKey(nint handle)
    {
        _ = UnregisterHotKey(handle, TestHotKeyId);
    }

    public static void TypeAscii(string text)
    {
        var inputs = new List<Input>();
        foreach (var character in text)
        {
            inputs.Add(CreateUnicodeInput(character, KeyEventFUnicode));
            inputs.Add(CreateUnicodeInput(character, KeyEventFUnicode | KeyEventFKeyUp));
        }

        Send(inputs.ToArray());
    }

    public static void CaptureWindow(nint handle, string path)
    {
        if (!GetWindowRect(handle, out var rectangle) || rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            throw new InvalidOperationException("Cannot capture an empty or missing window.");
        }

        using var bitmap = new Bitmap(rectangle.Width, rectangle.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(rectangle.Left, rectangle.Top, 0, 0, bitmap.Size);
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    public static void WaitUntil(Func<bool> condition, string description, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(12));
        Exception? lastException = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (condition())
                {
                    return;
                }
            }
            catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException)
            {
                lastException = exception;
            }

            Thread.Sleep(120);
        }

        throw new TimeoutException($"Timed out waiting for: {description}", lastException);
    }

    private static AutomationElement WaitForElement(
        Func<AutomationElement?> finder,
        string description,
        TimeSpan? timeout = null)
    {
        AutomationElement? result = null;
        WaitUntil(
            () =>
            {
                result = finder();
                return result is not null;
            },
            description,
            timeout);
        return result!;
    }

    private static string NormalizeLines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static void SendChord(ushort[] modifiers, ushort key)
    {
        var inputs = new List<Input>();
        inputs.AddRange(modifiers.Select(modifier => CreateKeyboardInput(modifier, 0)));
        inputs.Add(CreateKeyboardInput(key, 0));
        inputs.Add(CreateKeyboardInput(key, KeyEventFKeyUp));
        inputs.AddRange(modifiers.Reverse().Select(modifier => CreateKeyboardInput(modifier, KeyEventFKeyUp)));
        Send(inputs.ToArray());
    }

    private static void Send(Input[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException($"SendInput delivered {sent} of {inputs.Length} events.");
        }
    }

    private static Input CreateKeyboardInput(ushort virtualKey, uint flags) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput { VirtualKey = virtualKey, Flags = flags }
        }
    };

    private static Input CreateUnicodeInput(char character, uint flags) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput { ScanCode = character, Flags = flags }
        }
    };

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out Rectangle rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParamL;
        public ushort ParamH;
    }
}
