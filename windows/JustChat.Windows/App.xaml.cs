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
        _mainWindow = new MainWindow();
        this.MainWindow = _mainWindow;
        _trayIcon = new TrayIconService(ShowPanel, ExitApplication);
        SessionEnding += (_, _) => _mainWindow?.PrepareForExit();
        _mainWindow.Show();
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
        _trayIcon?.Dispose();
        _mainWindow?.PrepareForExit();
        base.OnExit(e);
    }
}
