using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace JustChat.Windows;

internal sealed class GlobalHotKeyController : IDisposable
{
    private const int HotKeyId = 0x4A43;
    private const int WmHotKey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkSpace = 0x20;

    private readonly nint _windowHandle;
    private readonly HwndSource _source;
    private readonly Action _action;
    private bool _isRegistered;
    private bool _isDisposed;

    public GlobalHotKeyController(Window window, Action action)
    {
        _action = action;
        _windowHandle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_windowHandle)
            ?? throw new InvalidOperationException("无法取得窗口句柄，全局快捷键未注册");
        _source.AddHook(WindowProc);

        try
        {
            _isRegistered = RegisterHotKey(
                _windowHandle,
                HotKeyId,
                ModControl | ModShift | ModNoRepeat,
                VkSpace);
            if (!_isRegistered)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        catch
        {
            _source.RemoveHook(WindowProc);
            throw;
        }
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            handled = true;
            _action();
        }

        return nint.Zero;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        if (_isRegistered)
        {
            UnregisterHotKey(_windowHandle, HotKeyId);
            _isRegistered = false;
        }

        _source.RemoveHook(WindowProc);
        _isDisposed = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
