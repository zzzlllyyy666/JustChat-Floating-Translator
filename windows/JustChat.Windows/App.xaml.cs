using Microsoft.Win32;
using System.Windows;
using Application = System.Windows.Application;

namespace JustChat.Windows;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private TrayIconService? _trayIcon;
    private bool _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeManager.Initialize();
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;
        _trayIcon = new TrayIconService(ShowPanel, ExitApplication);
        _mainWindow.HotKeyChanged = _trayIcon.SetShortcut;
        SessionEnding += (_, _) => _mainWindow?.PrepareForExit();
        _mainWindow.Show();
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(ThemeManager.Refresh);
    }

    private void ShowPanel()
    {
        _mainWindow?.ShowPanel();
    }

    private void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _mainWindow?.PrepareForExit();
        _trayIcon?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _trayIcon?.Dispose();
        _mainWindow?.PrepareForExit();
        base.OnExit(e);
    }
}
