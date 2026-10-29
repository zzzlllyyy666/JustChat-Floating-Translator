using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace JustChat.Windows;

internal sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _openMenuItem;
    private readonly Icon? _ownedIcon;
    private bool _isDisposed;

    public TrayIconService(Action showPanel, Action exitApplication)
    {
        _menu = new ContextMenuStrip();
        _openMenuItem = new ToolStripMenuItem("打开 JustChat");
        _openMenuItem.Click += (_, _) => showPanel();
        _menu.Items.Add(_openMenuItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("退出 JustChat", null, (_, _) => exitApplication());

        _notifyIcon = new NotifyIcon
        {
            Text = "JustChat",
            ContextMenuStrip = _menu,
            Visible = false
        };
        _ownedIcon = TryLoadApplicationIcon();
        _notifyIcon.Icon = _ownedIcon ?? SystemIcons.Application;
        _notifyIcon.DoubleClick += (_, _) => showPanel();
        _notifyIcon.Visible = true;
        SetShortcut(GlobalHotKeyController.PrimaryShortcut);
    }

    public void SetShortcut(string? shortcut)
    {
        _openMenuItem.Text = string.IsNullOrWhiteSpace(shortcut)
            ? "打开 JustChat"
            : $"打开 JustChat（{shortcut}）";
        _notifyIcon.Text = string.IsNullOrWhiteSpace(shortcut)
            ? "JustChat"
            : $"JustChat\n{shortcut} 呼出／收起";
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
