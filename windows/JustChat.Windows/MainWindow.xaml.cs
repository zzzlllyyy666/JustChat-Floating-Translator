using System.IO;
using System.Windows;
using System.Windows.Media;
using JustChat.Core;

namespace JustChat.Windows;

public partial class MainWindow : Window
{
    private readonly TranslationClient _translationClient = new();
    private readonly DeepSeekCredentialStore _credentialStore = new();
    private bool _isTranslating;

    public MainWindow()
    {
        InitializeComponent();
        TargetLanguageSelector.ItemsSource = TranslationTargetLanguage.All;
        TargetLanguageSelector.SelectedItem = TranslationTargetLanguage.English;
        LoadSavedApiKey();
    }

    private void LoadSavedApiKey()
    {
        try
        {
            var apiKey = _credentialStore.Load();
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                ApiKeyPasswordBox.Password = apiKey;
                SetStatus("已加载保存的 API Key，可以直接翻译");
            }
        }
        catch (InvalidDataException exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private void SaveKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = ApiKeyPasswordBox.Password.Trim();
        if (apiKey.Length == 0)
        {
            SetStatus("请先填写 DeepSeek API Key", isError: true);
            return;
        }

        try
        {
            _credentialStore.Save(apiKey);
            ApiKeyPasswordBox.Password = apiKey;
            SetStatus("API Key 已保存，仅当前 Windows 用户可读取");
        }
        catch (IOException exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private async void TranslateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isTranslating)
        {
            return;
        }

        var apiKey = ApiKeyPasswordBox.Password.Trim();
        if (apiKey.Length == 0)
        {
            SetStatus("请先填写 DeepSeek API Key", isError: true);
            return;
        }

        var draft = DraftTextBox.Text;
        if (string.IsNullOrWhiteSpace(draft))
        {
            SetStatus("请输入需要翻译的内容", isError: true);
            return;
        }

        var targetLanguage = TargetLanguageSelector.SelectedItem as TranslationTargetLanguage
            ?? TranslationTargetLanguage.English;
        _isTranslating = true;
        TranslateButton.IsEnabled = false;
        TranslateButton.Content = "正在翻译…";
        TranslationTextBox.Clear();
        SetStatus("正在请求 DeepSeek，中文草稿已保留…");

        try
        {
            var translation = await _translationClient.TranslateAsync(
                draft,
                targetLanguage,
                TranslationPreferences.Empty,
                apiKey);

            if (!string.Equals(draft, DraftTextBox.Text, StringComparison.Ordinal) ||
                !ReferenceEquals(targetLanguage, TargetLanguageSelector.SelectedItem))
            {
                SetStatus("草稿或目标语言已变化，本次译文未回填", isError: true);
                return;
            }

            TranslationTextBox.Text = translation;
            SetStatus("翻译完成");
        }
        catch (TranslationClientException exception)
        {
            SetStatus(exception.Message, isError: true);
        }
        catch (Exception)
        {
            SetStatus("翻译失败，中文草稿已保留，请重试", isError: true);
        }
        finally
        {
            _isTranslating = false;
            TranslateButton.Content = "翻译";
            TranslateButton.IsEnabled = true;
        }
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusTextBlock.Text = message;
        StatusTextBlock.Foreground = isError ? Brushes.Firebrick : Brushes.DimGray;
    }
}
