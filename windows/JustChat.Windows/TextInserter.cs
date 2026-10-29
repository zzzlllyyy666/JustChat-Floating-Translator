using System.ComponentModel;
using System.Runtime.InteropServices;
using JustChat.Core;
using Clipboard = System.Windows.Clipboard;

namespace JustChat.Windows;

internal enum ClipboardRestoreResult
{
    Restored,
    ChangedByUser,
    Failed
}

internal readonly record struct TextInsertionResult(ClipboardRestoreResult ClipboardRestore);

internal sealed class TextInsertionException : Exception
{
    public TextInsertionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

internal sealed class TextInserter
{
    private const int SwRestore = 9;
    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;

    private readonly ForegroundWindowTracker _targetTracker;

    public TextInserter(ForegroundWindowTracker targetTracker)
    {
        _targetTracker = targetTracker;
    }

    public async Task<TextInsertionResult> InsertAsync(string text)
    {
        var targetWindow = _targetTracker.TargetWindow;
        if (targetWindow == nint.Zero || !IsWindow(targetWindow))
        {
            throw new TextInsertionException(
                "找不到刚才的目标窗口，请回到目标输入框后重新呼出面板；译文仍保留");
        }

        ClipboardSnapshot? snapshot = null;
        var clipboardWritten = false;
        var pasteSent = false;
        uint temporarySequence = 0;

        try
        {
            snapshot = ClipboardSnapshot.Capture();
            Clipboard.SetText(text);
            clipboardWritten = true;
            temporarySequence = ClipboardSnapshot.GetSequenceNumber();

            await ActivateTargetAsync(targetWindow);
            SendPaste();
            pasteSent = true;
        }
        catch (Exception exception) when (
            exception is TextInsertionException or Win32Exception or ExternalException)
        {
            var restoreResult = clipboardWritten && !pasteSent && snapshot is not null
                ? RestoreClipboard(snapshot, temporarySequence)
                : ClipboardRestoreResult.Restored;
            var message = exception switch
            {
                TextInsertionException => exception.Message,
                Win32Exception => "无法发送粘贴指令，译文仍保留",
                _ => "无法安全使用 Windows 剪贴板，译文仍保留"
            };
            if (restoreResult == ClipboardRestoreResult.Failed)
            {
                message += "；原剪贴板也未能恢复";
            }

            throw new TextInsertionException(message, exception);
        }

        // Office and HTML editors may consume the clipboard lazily.
        await Task.Delay(1100);
        return new TextInsertionResult(RestoreClipboard(snapshot!, temporarySequence));
    }

    private static ClipboardRestoreResult RestoreClipboard(
        ClipboardSnapshot snapshot,
        uint temporarySequence)
    {
        var currentSequence = ClipboardSnapshot.GetSequenceNumber();
        if (!WriteBackPolicy.ShouldRestoreClipboard(temporarySequence, currentSequence))
        {
            return ClipboardRestoreResult.ChangedByUser;
        }

        try
        {
            snapshot.Restore();
            return ClipboardRestoreResult.Restored;
        }
        catch (Exception)
        {
            return ClipboardRestoreResult.Failed;
        }
    }

    private static async Task ActivateTargetAsync(nint targetWindow)
    {
        if (IsIconic(targetWindow))
        {
            ShowWindow(targetWindow, SwRestore);
        }

        SetForegroundWindow(targetWindow);
        for (var attempt = 0; attempt < 25; attempt++)
        {
            if (GetForegroundWindow() == targetWindow)
            {
                break;
            }

            await Task.Delay(40);
        }

        if (GetForegroundWindow() != targetWindow)
        {
            throw new TextInsertionException(
                "无法激活刚才的目标窗口，请重新选择输入框；译文仍保留");
        }

        await Task.Delay(160);
        if (GetForegroundWindow() != targetWindow)
        {
            throw new TextInsertionException(
                "目标窗口已切换，未发送粘贴指令；译文仍保留");
        }
    }

    private static void SendPaste()
    {
        var inputs = new[]
        {
            CreateKeyboardInput(VkControl, 0),
            CreateKeyboardInput(VkV, 0),
            CreateKeyboardInput(VkV, KeyEventFKeyUp),
            CreateKeyboardInput(VkControl, KeyEventFKeyUp)
        };
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static Input CreateKeyboardInput(ushort virtualKey, uint flags) =>
        new()
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = flags
                }
            }
        };

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

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
