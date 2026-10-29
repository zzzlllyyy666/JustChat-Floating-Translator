using System.Windows;
using JustChat.Core;

namespace JustChat.Windows;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TargetLanguageSelector.ItemsSource = TranslationTargetLanguage.All;
        TargetLanguageSelector.SelectedItem = TranslationTargetLanguage.English;
    }
}
