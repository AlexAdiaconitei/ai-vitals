using System.Windows;
using RadioButton = System.Windows.Controls.RadioButton;

namespace AIVitals.App;

public partial class OnboardingWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Func<Task> _finish;
    private bool _isSyncingLanguage;

    public OnboardingWindow(MainViewModel viewModel, Func<Task> finish)
    {
        _viewModel = viewModel;
        _finish = finish;
        InitializeComponent();
        DataContext = viewModel;
        SyncLanguageChoice();
    }

    private async void OnFinish(object sender, RoutedEventArgs eventArgs)
    {
        await _finish();
        Close();
    }

    private async void OnLanguageChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (_isSyncingLanguage) return;
        if (sender is not RadioButton { Tag: string language }) return;
        if (string.Equals(_viewModel.Preferences.Language, language, StringComparison.OrdinalIgnoreCase)) return;

        await _viewModel.SaveAppearanceAsync(language, _viewModel.Preferences.Theme);
        WindowsAppearance.Apply(_viewModel.Preferences);
    }

    private void SyncLanguageChoice()
    {
        _isSyncingLanguage = true;
        var english = _viewModel.Preferences.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        SpanishOption.IsChecked = !english;
        EnglishOption.IsChecked = english;
        _isSyncingLanguage = false;
    }
}
