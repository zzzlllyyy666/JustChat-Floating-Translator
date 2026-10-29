using System.IO;
using System.Windows;
using System.Windows.Media;
using JustChat.Core;

namespace JustChat.Windows;

public partial class PreferencesWindow : Window
{
    private readonly TranslationPreferencesStore _preferencesStore = new();

    public PreferencesWindow(TranslationPreferences preferences)
    {
        Preferences = preferences;
        InitializeComponent();
        IndustryTextBox.Text = preferences.Industry;
        ToneTextBox.Text = preferences.Tone;
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);
    }

    public TranslationPreferences Preferences { get; private set; }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        IndustryTextBox.Clear();
        ToneTextBox.Clear();
        PreferencesStatusTextBlock.Text = "点击“设为默认”后清除保存的翻译偏好";
        PreferencesStatusTextBlock.Foreground = (Brush)FindResource("SecondaryTextBrush");
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var updated = new TranslationPreferences(
            IndustryTextBox.Text,
            ToneTextBox.Text);

        try
        {
            _preferencesStore.Save(updated);
            Preferences = updated;
            DialogResult = true;
        }
        catch (IOException exception)
        {
            SetError(exception.Message);
        }
        catch (Exception)
        {
            SetError("翻译偏好保存失败，请重试");
        }
    }

    private void SetError(string message)
    {
        PreferencesStatusTextBlock.Text = message;
        PreferencesStatusTextBlock.Foreground = (Brush)FindResource("ErrorBrush");
    }
}
