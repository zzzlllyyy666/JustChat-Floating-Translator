using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace JustChat.Windows;

internal sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly Icon? _ownedIcon;
    private bool _isDisposed;

    public TrayIconService(Action showPanel, Action exitApplication)
    {
        _menu = new ContextMenuStrip();
        _menu.Items.Add("打开 JustChat（Ctrl+Shift+Space）", null, (_, _) => showPanel());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("退出 JustChat", null, (_, _) => exitApplication());

        _notifyIcon = new NotifyIcon
        {
            Text = "JustChat\nCtrl+Shift+Space 呼出／收起",
            ContextMenuStrip = _menu,
            Visible = false
        };
        _ownedIcon = TryLoadApplicationIcon();
        _notifyIcon.Icon = _ownedIcon ?? SystemIcons.Application;
        _notifyIcon.DoubleClick += (_, _) => showPanel();
        _notifyIcon.Visible = true;
    }

    private static Icon? TryLoadApplicationIcon()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            return string.IsNullOrWhiteSpace(processPath)
                ? null
                : Icon.ExtractAssociatedIcon(processPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _ownedIcon?.Dispose();
        _isDisposed = true;
    }
}
