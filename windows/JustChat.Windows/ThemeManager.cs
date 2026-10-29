using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Application = System.Windows.Application;

namespace JustChat.Windows;

internal static class ThemeManager
{
    private const int DwmUseImmersiveDarkMode = 20;
    private static bool _isInitialized;

    public static bool IsDark { get; private set; }

    public static void Initialize()
    {
        _isInitialized = true;
        Refresh();
    }

    public static void Refresh()
    {
        if (!_isInitialized || Application.Current is null)
        {
            return;
        }

        IsDark = ReadDarkPreference();
        var themeName = IsDark ? "Dark" : "Light";
        var theme = new ResourceDictionary
        {
            Source = new Uri($"/JustChat;component/Themes/{themeName}.xaml", UriKind.Absolute)
        };
        if (Application.Current.Resources.MergedDictionaries.Count == 0)
        {
            Application.Current.Resources.MergedDictionaries.Add(theme);
        }
        else
        {
            Application.Current.Resources.MergedDictionaries[0] = theme;
        }

        foreach (Window window in Application.Current.Windows)
        {
            ApplyToWindow(window);
        }
    }

    public static void ApplyToWindow(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        var useDarkMode = IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(
            handle,
            DwmUseImmersiveDarkMode,
            ref useDarkMode,
            Marshal.SizeOf<int>());
    }

    private static bool ReadDarkPreference()
    {
        var configuredTheme = Environment.GetEnvironmentVariable("JUSTCHAT_THEME");
        if (string.Equals(configuredTheme, "dark", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (string.Equals(configuredTheme, "light", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
