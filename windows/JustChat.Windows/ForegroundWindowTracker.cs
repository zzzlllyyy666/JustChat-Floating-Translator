using System.ComponentModel;
using System.Runtime.InteropServices;
using JustChat.Core;

namespace JustChat.Windows;

internal sealed class ForegroundWindowTracker : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;

    private delegate void WinEventProc(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime);

    private readonly WinEventProc _callback;
    private readonly nint _hook;
    private bool _isDisposed;

    public ForegroundWindowTracker()
    {
        _callback = OnForegroundWindowChanged;
        _hook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            nint.Zero,
            _callback,
            0,
            0,
            WinEventOutOfContext);
        if (_hook == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        CaptureForegroundWindow();
    }

    public nint TargetWindow { get; private set; }

    public void CaptureForegroundWindow()
    {
        Capture(GetForegroundWindow());
    }

    private void OnForegroundWindowChanged(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime)
    {
        Capture(hwnd);
    }

    private void Capture(nint windowHandle)
    {
        if (_isDisposed || !IsWindow(windowHandle))
        {
            return;
        }

        GetWindowThreadProcessId(windowHandle, out var foregroundProcessId);
        if (!WriteBackPolicy.ShouldCaptureTarget(
                windowHandle,
                foregroundProcessId,
                (uint)Environment.ProcessId))
        {
            return;
        }

        TargetWindow = windowHandle;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        UnhookWinEvent(_hook);
        GC.KeepAlive(_callback);
        GC.SuppressFinalize(this);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint hmodWinEventProc,
        WinEventProc lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hWinEventHook);
}
