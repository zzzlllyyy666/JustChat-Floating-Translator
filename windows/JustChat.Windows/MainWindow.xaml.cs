using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using JustChat.Core;

namespace JustChat.Windows;

public partial class MainWindow : Window
{
    private enum StatusLevel
    {
        Neutral,
        Success,
        Warning,
        Error
    }

    private readonly TranslationClient _translationClient = AppTranslationClient.Create();
    private readonly DeepSeekCredentialStore _credentialStore = new();
    private readonly TranslationPreferencesStore _preferencesStore = new();
    private GlobalHotKeyController? _globalHotKeyController;
    private ForegroundWindowTracker? _foregroundWindowTracker;
    private TextInserter? _textInserter;
    private TranslationPreferences _translationPreferences = TranslationPreferences.Empty;
    private string _currentShortcut = GlobalHotKeyController.PrimaryShortcut;
    private bool _hasSavedApiKey;
    private bool _apiCardForced;
    private bool _isOnboardingConnecting;
    private bool _isTranslating;
    private bool _isWritingBack;
    private bool _allowClose;
    private int _onboardingStep = 1;

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
        UpdateDraftCount();
        UpdateApiCardVisibility();
        UpdateHotKeyPresentation();
        UpdateOperationState();
        if (!OnboardingStore.IsComplete)
        {
            ShowOnboarding(1);
        }
    }

    public Action<string?>? HotKeyChanged { get; set; }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        ThemeManager.ApplyToWindow(this);
        try
        {
            _foregroundWindowTracker = new ForegroundWindowTracker();
            _textInserter = new TextInserter(_foregroundWindowTracker);
        }
        catch (Win32Exception)
        {
            SetStatus("无法跟踪目标窗口，文本写回已停用", isError: true);
        }

        ConfigureGlobalHotKey(announce: false);
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
        if (OnboardingOverlay.Visibility == Visibility.Visible)
        {
            if (_onboardingStep == 1)
            {
                OnboardingApiKeyPasswordBox.Focus();
            }
            else if (_onboardingStep == 2)
            {
                OnboardingContinueButton.Focus();
            }
            else
            {
                OnboardingFinishButton.Focus();
            }
        }
        else
        {
            DraftTextBox.Focus();
        }
    }

    public void PrepareForExit()
    {
        _allowClose = true;
        _globalHotKeyController?.Dispose();
        _foregroundWindowTracker?.Dispose();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void ConfigureGlobalHotKey(bool announce)
    {
        _globalHotKeyController?.Dispose();
        _globalHotKeyController = null;

        try
        {
            _globalHotKeyController = new GlobalHotKeyController(this, TogglePanel);
            _currentShortcut = GlobalHotKeyController.PrimaryShortcut;
            if (announce)
            {
                SetStatusLevel("已恢复 Ctrl+Shift+Space 全局快捷键", StatusLevel.Success);
            }
        }
        catch (Win32Exception)
        {
            try
            {
                _globalHotKeyController = new GlobalHotKeyController(this, TogglePanel, useFallback: true);
                _currentShortcut = GlobalHotKeyController.FallbackShortcut;
                SetStatusLevel(
                    "Ctrl+Shift+Space 被占用，已自动改用 Ctrl+Alt+Space",
                    StatusLevel.Warning);
            }
            catch (Win32Exception)
            {
                _currentShortcut = string.Empty;
                SetStatus("全局快捷键暂不可用，请使用托盘图标打开；关闭冲突软件后可重试", isError: true);
            }
        }

        UpdateHotKeyPresentation();
    }

    private void UpdateHotKeyPresentation()
    {
        var hasShortcut = _currentShortcut.Length > 0;
        HotKeyHintTextBlock.Text = hasShortcut
            ? $"{_currentShortcut} 呼出／收起 · 关闭后驻留托盘"
            : "快捷键暂不可用 · 请使用托盘图标打开";
        OnboardingHotKeyTextBlock.Text = hasShortcut
            ? $"使用 {_currentShortcut} 随时呼出或收起面板。"
            : "全局快捷键暂不可用，可从托盘图标打开面板。";
        OnboardingPracticeHotKeyTextBlock.Text = hasShortcut
            ? $"2  按 {_currentShortcut} 呼出面板"
            : "2  从托盘图标打开 JustChat 面板";
        HotKeyRetryButton.Visibility = _currentShortcut == GlobalHotKeyController.PrimaryShortcut
            ? Visibility.Collapsed
            : Visibility.Visible;
        HotKeyRetryButton.Content = hasShortcut ? "恢复首选快捷键" : "重试快捷键";
        HotKeyChanged?.Invoke(hasShortcut ? _currentShortcut : null);
    }

    private void HotKeyRetryButton_Click(object sender, RoutedEventArgs e)
    {
        ConfigureGlobalHotKey(announce: true);
    }

    private void LoadSavedApiKey()
    {
        try
        {
            var apiKey = _credentialStore.Load();
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                ApiKeyPasswordBox.Password = apiKey;
                OnboardingApiKeyPasswordBox.Password = apiKey;
                _hasSavedApiKey = true;
                ApiKeyStateTextBlock.Text = "已保存";
                SetStatus("已加载保存的 API Key，可以直接翻译");
            }
        }
        catch (InvalidDataException exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private void StoreApiKey(string apiKey)
    {
        _credentialStore.Save(apiKey);
        ApiKeyPasswordBox.Password = apiKey;
        OnboardingApiKeyPasswordBox.Password = apiKey;
        _hasSavedApiKey = true;
        _apiCardForced = false;
        ApiKeyStateTextBlock.Text = "已保存";
        UpdateApiCardVisibility();
    }

    private void UpdateApiCardVisibility()
    {
        ApiCard.Visibility = _apiCardForced || !_hasSavedApiKey
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApiSettingsButton.Content = ApiCard.Visibility == Visibility.Visible ? "收起" : "API";
    }

    private void ApiSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _apiCardForced = ApiCard.Visibility != Visibility.Visible;
        UpdateApiCardVisibility();
        if (ApiCard.Visibility == Visibility.Visible)
        {
            ApiKeyPasswordBox.Focus();
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
        SetStatusLevel(
            _translationPreferences.IsEmpty ? "已清除默认翻译要求" : "已保存，以后翻译自动使用",
            StatusLevel.Success);
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
            StoreApiKey(apiKey);
            SetStatusLevel("API Key 已保存，仅当前 Windows 用户可读取", StatusLevel.Success);
            DraftTextBox.Focus();
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
            _apiCardForced = true;
            UpdateApiCardVisibility();
            SetStatus("请先填写 DeepSeek API Key", isError: true);
            ApiKeyPasswordBox.Focus();
            return;
        }

        var draft = DraftTextBox.Text;
        if (string.IsNullOrWhiteSpace(draft))
        {
            SetStatus("请输入需要翻译的内容", isError: true);
            DraftTextBox.Focus();
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
            SetStatusLevel(
                preferences.IsEmpty ? "翻译完成，译文可以编辑" : "已使用默认行业与语气，译文可以编辑",
                StatusLevel.Success);
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
            TranslateButton.Content = "翻译  Ctrl+T";
            TranslateButton.IsEnabled = true;
            UpdateOperationState();
        }
    }

    private async void WriteBackButton_Click(object sender, RoutedEventArgs e)
    {
        await WriteBackAsync();
    }

    private async void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Control)
        {
            return;
        }

        if (e.Key == System.Windows.Input.Key.T)
        {
            e.Handled = true;
            TranslateButton_Click(sender, new RoutedEventArgs());
        }
        else if (e.Key == System.Windows.Input.Key.S)
        {
            e.Handled = true;
            await WriteBackAsync();
        }
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
            SetStatusLevel(
                message,
                result.ClipboardRestore == ClipboardRestoreResult.Restored
                    ? StatusLevel.Success
                    : StatusLevel.Warning);
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

    private void DraftTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateDraftCount();
    }

    private void UpdateDraftCount()
    {
        DraftCountTextBlock.Text = $"{DraftTextBox.Text.Length} 字";
    }

    private void TranslationTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateWriteBackState();
    }

    private void UpdateOperationState()
    {
        PreferencesButton.IsEnabled = !_isTranslating && !_isWritingBack;
        SaveKeyButton.IsEnabled = !_isTranslating && !_isWritingBack;
        ApiSettingsButton.IsEnabled = !_isTranslating && !_isWritingBack;
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
        SetStatusLevel(message, isError ? StatusLevel.Error : StatusLevel.Neutral);
    }

    private void SetStatusLevel(string message, StatusLevel level)
    {
        var brushKey = level switch
        {
            StatusLevel.Success => "SuccessBrush",
            StatusLevel.Warning => "WarningBrush",
            StatusLevel.Error => "ErrorBrush",
            _ => "SecondaryTextBrush"
        };
        StatusTextBlock.Text = message;
        StatusTextBlock.Foreground = (Brush)FindResource(brushKey);
        StatusIndicator.Fill = (Brush)FindResource(brushKey);
    }

    private void GuideButton_Click(object sender, RoutedEventArgs e)
    {
        ShowOnboarding(1);
    }

    private void ShowOnboarding(int step)
    {
        _onboardingStep = Math.Clamp(step, 1, 3);
        OnboardingApiKeyPasswordBox.Password = ApiKeyPasswordBox.Password;
        OnboardingOverlay.Visibility = Visibility.Visible;
        UpdateOnboardingStep();
    }

    private void UpdateOnboardingStep()
    {
        OnboardingStepText.Text = $"{_onboardingStep} / 3";
        OnboardingStepOne.Visibility = _onboardingStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        OnboardingStepTwo.Visibility = _onboardingStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        OnboardingStepThree.Visibility = _onboardingStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        OnboardingBackButton.Visibility = _onboardingStep > 1 ? Visibility.Visible : Visibility.Collapsed;
        OnboardingSkipButton.Visibility = _onboardingStep < 3 ? Visibility.Visible : Visibility.Collapsed;
        OnboardingConnectButton.Visibility = _onboardingStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        OnboardingContinueButton.Visibility = _onboardingStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        OnboardingFinishButton.Visibility = _onboardingStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        OnboardingStatusTextBlock.Text = string.Empty;
    }

    private void OnboardingSkipButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isOnboardingConnecting && _onboardingStep < 3)
        {
            ShowOnboarding(_onboardingStep + 1);
        }
    }

    private void OnboardingBackButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isOnboardingConnecting && _onboardingStep > 1)
        {
            ShowOnboarding(_onboardingStep - 1);
        }
    }

    private void OnboardingContinueButton_Click(object sender, RoutedEventArgs e)
    {
        ShowOnboarding(3);
    }

    private void OnboardingFinishButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OnboardingStore.Complete();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus("使用指南已完成，但完成状态未能保存，下次可能再次显示", isError: true);
        }

        OnboardingOverlay.Visibility = Visibility.Collapsed;
        UpdateApiCardVisibility();
        DraftTextBox.Focus();
    }

    private async void OnboardingConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isOnboardingConnecting)
        {
            return;
        }

        var apiKey = OnboardingApiKeyPasswordBox.Password.Trim();
        if (apiKey.Length == 0)
        {
            SetOnboardingStatus("请先填写 API Key，或选择“稍后再说”。", isError: true);
            OnboardingApiKeyPasswordBox.Focus();
            return;
        }

        _isOnboardingConnecting = true;
        OnboardingConnectButton.IsEnabled = false;
        OnboardingSkipButton.IsEnabled = false;
        OnboardingBackButton.IsEnabled = false;
        OnboardingConnectButton.Content = "正在连接…";
        SetOnboardingStatus("Key 已保存，正在验证连接…", isError: false);

        try
        {
            StoreApiKey(apiKey);
            _ = await _translationClient.TranslateAsync(
                "你好",
                TranslationTargetLanguage.English,
                TranslationPreferences.Empty,
                apiKey);
            ShowOnboarding(2);
        }
        catch (IOException exception)
        {
            SetOnboardingStatus(exception.Message, isError: true);
        }
        catch (TranslationClientException exception)
        {
            SetOnboardingStatus($"Key 已保存；{exception.Message}", isError: true);
        }
        catch (Exception)
        {
            SetOnboardingStatus("Key 已保存，但连接验证失败，请稍后重试或先继续指南。", isError: true);
        }
        finally
        {
            _isOnboardingConnecting = false;
            OnboardingConnectButton.IsEnabled = true;
            OnboardingSkipButton.IsEnabled = true;
            OnboardingBackButton.IsEnabled = true;
            OnboardingConnectButton.Content = "保存并连接";
        }
    }

    private void SetOnboardingStatus(string message, bool isError)
    {
        OnboardingStatusTextBlock.Text = message;
        OnboardingStatusTextBlock.Foreground = (Brush)FindResource(isError ? "ErrorBrush" : "SecondaryTextBrush");
    }
}
