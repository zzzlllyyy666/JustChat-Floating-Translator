using System.IO;
using System.Windows;
using Brushes = System.Windows.Media.Brushes;
using JustChat.Core;

namespace JustChat.Windows;

public partial class MainWindow : Window
{
    private readonly TranslationClient _translationClient = new();
    private readonly DeepSeekCredentialStore _credentialStore = new();
    private readonly TranslationPreferencesStore _preferencesStore = new();
    private GlobalHotKeyController? _globalHotKeyController;
    private ForegroundWindowTracker? _foregroundWindowTracker;
    private TextInserter? _textInserter;
    private TranslationPreferences _translationPreferences = TranslationPreferences.Empty;
    private bool _isTranslating;
    private bool _isWritingBack;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        TargetLanguageSelector.ItemsSource = TranslationTargetLanguage.All;
        TargetLanguageSelector.SelectedItem = TranslationTargetLanguage.English;
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        LoadSavedApiKey();
        LoadSavedPreferences();
        UpdatePreferencesSummary();
        UpdateOperationState();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            _foregroundWindowTracker = new ForegroundWindowTracker();
            _textInserter = new TextInserter(_foregroundWindowTracker);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            SetStatus("无法跟踪目标窗口，文本写回已停用", isError: true);
        }

        try
        {
            _globalHotKeyController = new GlobalHotKeyController(this, TogglePanel);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            SetStatus("Ctrl+Shift+Space 注册失败，可能被占用；请使用托盘图标打开", isError: true);
        }

        UpdateOperationState();
    }

    public void TogglePanel()
    {
        if (FloatingPanelPolicy.ShouldHideOnToggle(IsVisible, IsActive))
        {
            Hide();
        }
        else
        {
            ShowPanel();
        }
    }

    public void ShowPanel()
    {
        if (!IsActive)
        {
            _foregroundWindowTracker?.CaptureForegroundWindow();
        }

        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        DraftTextBox.Focus();
    }

    public void PrepareForExit()
    {
        _allowClose = true;
        _globalHotKeyController?.Dispose();
        _foregroundWindowTracker?.Dispose();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
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

    private void LoadSavedPreferences()
    {
        try
        {
            _translationPreferences = _preferencesStore.Load();
        }
        catch (InvalidDataException exception)
        {
            _translationPreferences = TranslationPreferences.Empty;
            SetStatus(exception.Message, isError: true);
        }
    }

    private void UpdatePreferencesSummary()
    {
        PreferencesButton.Content = _translationPreferences.Summary;
        PreferencesButton.ToolTip = _translationPreferences.Summary;
    }

    private void PreferencesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isTranslating || _isWritingBack)
        {
            return;
        }

        var preferencesWindow = new PreferencesWindow(_translationPreferences)
        {
            Owner = this
        };
        if (preferencesWindow.ShowDialog() != true)
        {
            return;
        }

        _translationPreferences = preferencesWindow.Preferences;
        UpdatePreferencesSummary();
        SetStatus(_translationPreferences.IsEmpty
            ? "已清除默认翻译要求"
            : "已保存，以后翻译自动使用");
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
        if (_isTranslating || _isWritingBack)
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
        var preferences = _translationPreferences;
        _isTranslating = true;
        TranslateButton.IsEnabled = false;
        TranslateButton.Content = "正在翻译…";
        TranslationTextBox.Clear();
        UpdateOperationState();
        SetStatus("正在请求 DeepSeek，中文草稿已保留…");

        try
        {
            var translation = await _translationClient.TranslateAsync(
                draft,
                targetLanguage,
                preferences,
                apiKey);

            if (!string.Equals(draft, DraftTextBox.Text, StringComparison.Ordinal) ||
                !ReferenceEquals(targetLanguage, TargetLanguageSelector.SelectedItem) ||
                !string.Equals(preferences.Industry, _translationPreferences.Industry, StringComparison.Ordinal) ||
                !string.Equals(preferences.Tone, _translationPreferences.Tone, StringComparison.Ordinal))
            {
                SetStatus("草稿、目标语言或翻译偏好已变化，本次译文未回填", isError: true);
                return;
            }

            TranslationTextBox.Text = translation;
            SetStatus(preferences.IsEmpty
                ? "翻译完成，译文可以编辑"
                : "已使用默认行业与语气，译文可以编辑");
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
            UpdateOperationState();
        }
    }

    private async void WriteBackButton_Click(object sender, RoutedEventArgs e)
    {
        await WriteBackAsync();
    }

    private async void MainWindow_PreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.S ||
            System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Control)
        {
            return;
        }

        e.Handled = true;
        await WriteBackAsync();
    }

    private async Task WriteBackAsync()
    {
        if (_isTranslating || _isWritingBack)
        {
            return;
        }

        if (_textInserter is null)
        {
            SetStatus("目标窗口跟踪不可用，无法写回；请复制译文后手动粘贴", isError: true);
            return;
        }

        var text = TranslationTextBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("没有可写回的译文", isError: true);
            return;
        }

        _isWritingBack = true;
        TranslateButton.IsEnabled = false;
        DraftTextBox.IsReadOnly = true;
        TranslationTextBox.IsReadOnly = true;
        UpdateOperationState();
        SetStatus("正在写回最近使用的输入框…");

        try
        {
            var result = await _textInserter.InsertAsync(text);
            DraftTextBox.Clear();
            TranslationTextBox.Clear();
            var message = result.ClipboardRestore switch
            {
                ClipboardRestoreResult.Restored => "已写回，原剪贴板已恢复",
                ClipboardRestoreResult.ChangedByUser => "已写回；你已更新剪贴板，未恢复旧内容",
                ClipboardRestoreResult.Failed => "已写回，但原剪贴板恢复失败",
                _ => "已写回"
            };
            SetStatus(message, isError: result.ClipboardRestore == ClipboardRestoreResult.Failed);
        }
        catch (TextInsertionException exception)
        {
            SetStatus(exception.Message, isError: true);
            ShowPanel();
        }
        catch (Exception)
        {
            SetStatus("写回失败，草稿和译文仍保留，请重试或手动粘贴", isError: true);
            ShowPanel();
        }
        finally
        {
            _isWritingBack = false;
            DraftTextBox.IsReadOnly = false;
            TranslationTextBox.IsReadOnly = false;
            TranslateButton.IsEnabled = true;
            UpdateOperationState();
        }
    }

    private void TranslationTextBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateWriteBackState();
    }

    private void UpdateOperationState()
    {
        PreferencesButton.IsEnabled = !_isTranslating && !_isWritingBack;
        UpdateWriteBackState();
    }

    private void UpdateWriteBackState()
    {
        WriteBackButton.IsEnabled =
            !_isTranslating &&
            !_isWritingBack &&
            _textInserter is not null &&
            !string.IsNullOrWhiteSpace(TranslationTextBox.Text);
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusTextBlock.Text = message;
        StatusTextBlock.Foreground = isError ? Brushes.Firebrick : Brushes.DimGray;
    }
}
