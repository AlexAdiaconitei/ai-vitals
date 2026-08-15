using System.Text;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using AIVitals.Application;
using Microsoft.Win32;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace AIVitals.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly WidgetViewModel _previewWidgetViewModel;
    private readonly Func<WidgetPreferences, Task> _applyWidget;
    private readonly Func<Task> _recoverWidget;
    private readonly Func<Task> _moveWidgetToCurrentMonitor;
    private readonly IAppUpdateService _updateService;
    private readonly Func<Task> _checkForUpdates;
    private readonly Func<Task> _applyUpdate;
    private readonly Func<bool, bool, Task> _saveUpdatePreferences;
    private readonly List<string> _widgetProviderOrder = ["codex", "claude-code"];

    public MainWindow(
        UsageMonitorService monitor,
        MainViewModel viewModel,
        Func<WidgetPreferences, Task> applyWidget,
        Func<Task> recoverWidget,
        Func<Task> moveWidgetToCurrentMonitor,
        IAppUpdateService updateService,
        Func<Task> checkForUpdates,
        Func<Task> applyUpdate,
        Func<bool, bool, Task> saveUpdatePreferences)
    {
        _viewModel = viewModel;
        _applyWidget = applyWidget;
        _recoverWidget = recoverWidget;
        _moveWidgetToCurrentMonitor = moveWidgetToCurrentMonitor;
        _updateService = updateService;
        _checkForUpdates = checkForUpdates;
        _applyUpdate = applyUpdate;
        _saveUpdatePreferences = saveUpdatePreferences;
        InitializeComponent();
        DataContext = viewModel;
        _previewWidgetViewModel = new WidgetViewModel(monitor, viewModel.WidgetPreferences);
        WidgetPreview.DataContext = _previewWidgetViewModel;
        _updateService.StatusChanged += OnUpdateStatusChanged;
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _updateService.StatusChanged -= OnUpdateStatusChanged;
            _previewWidgetViewModel.Dispose();
        };
    }

    public void ShowSection(int index) => DashboardTabs.SelectedIndex = Math.Clamp(index, 0, DashboardTabs.Items.Count - 1);

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        SyncWidgetControls();
        SyncAppearanceControls();
        SyncUpdateControls();
        await RefreshHistoryAsync();
    }

    private void OnUpdateStatusChanged(object? sender, AppUpdateStatus status)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnUpdateStatusChanged(sender, status));
            return;
        }

        SyncUpdateControls();
    }

    private async void OnCheckForUpdates(object sender, RoutedEventArgs eventArgs) => await _checkForUpdates();

    private async void OnInstallUpdate(object sender, RoutedEventArgs eventArgs) => await _applyUpdate();

    private void OnOpenReleases(object sender, RoutedEventArgs eventArgs) =>
        OpenExternal("https://github.com/AlexAdiaconitei/ai-vitals/releases");

    private async void OnSaveUpdatePreferences(object sender, RoutedEventArgs eventArgs)
    {
        await _saveUpdatePreferences(AutomaticUpdateCheck.IsChecked == true, StartWithWindows.IsChecked == true);
        SyncUpdateControls();
        ActionStatus.Text = T("UpdatePreferencesSaved");
    }

    private void SyncUpdateControls()
    {
        var status = _updateService.Status;
        var supported = status.State != AppUpdateState.Unsupported;
        InstalledVersionValue.Text = status.InstalledVersion;
        UpdateChannelValue.Text = status.Channel;
        AutomaticUpdateCheck.IsChecked = _viewModel.Preferences.AutomaticUpdateCheckEnabled;
        StartWithWindows.IsChecked = _viewModel.Preferences.StartWithWindows;
        AutomaticUpdateCheck.IsEnabled = supported;
        CheckForUpdatesButton.IsEnabled = supported && !status.IsBusy;

        UpdateReadyCard.Visibility = status.IsPending ? Visibility.Visible : Visibility.Collapsed;
        AboutUpdateDot.Visibility = status.IsPending ? Visibility.Visible : Visibility.Collapsed;
        if (status.IsPending)
            UpdateReadyText.Text = string.Format(T("UpdateReadyBanner"), status.AvailableVersion);

        UpdateStatusText.Text = status.State switch
        {
            AppUpdateState.Unsupported => T("UpdateStatusUnsupported"),
            AppUpdateState.Checking => T("UpdateStatusChecking"),
            AppUpdateState.Downloading => string.Format(T("UpdateStatusDownloading"), status.AvailableVersion),
            AppUpdateState.ReadyToApply => string.Format(T("UpdateStatusReady"), status.AvailableVersion),
            AppUpdateState.Failed => string.Format(T("UpdateStatusFailed"), status.FailureDetail),
            _ => T("UpdateStatusIdle")
        };
        UpdateLastCheckedText.Text = status.LastCheckedUtc is null
            ? T("UpdateNeverChecked")
            : string.Format(T("UpdateLastChecked"), status.LastCheckedUtc.Value.ToLocalTime().ToString("g"));
    }

    private async void OnRefreshHistory(object sender, RoutedEventArgs eventArgs)
    {
        await RefreshHistoryAsync();
        ActionStatus.Text = T("RefreshCompleted");
    }
    private void OnOpenWidgetEditor(object sender, RoutedEventArgs eventArgs) => DashboardTabs.SelectedIndex = 3;
    private void OnWidgetModeChanged(object sender, SelectionChangedEventArgs eventArgs) => UpdateWidgetPreview();
    private void OnDashboardSectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (!ReferenceEquals(eventArgs.Source, DashboardTabs)) return;
        if (DashboardTabs.SelectedIndex == 3 && WidgetMode is not null) SyncWidgetControls();
        if (DashboardTabs.SelectedIndex == 5 && LanguageSelector is not null) SyncAppearanceControls();
        if (DashboardTabs.SelectedIndex == 6 && AutomaticUpdateCheck is not null) SyncUpdateControls();
    }
    private void OnMinimizeWindow(object sender, RoutedEventArgs eventArgs) => WindowState = WindowState.Minimized;
    private void OnMaximizeWindow(object sender, RoutedEventArgs eventArgs) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnCloseWindow(object sender, RoutedEventArgs eventArgs) => Close();
    private async void OnExportCsv(object sender, RoutedEventArgs eventArgs) => await ExportAsync(json: false);
    private async void OnExportJson(object sender, RoutedEventArgs eventArgs) => await ExportAsync(json: true);
    private void OnOpenGitHub(object sender, RoutedEventArgs eventArgs) => OpenExternal("https://github.com/AlexAdiaconitei/ai-vitals");
    private void OnOpenKoFi(object sender, RoutedEventArgs eventArgs) => OpenExternal("https://ko-fi.com/K3Q5236GOO");

    private async void OnDeleteFiltered(object sender, RoutedEventArgs eventArgs) => await DeleteAsync(allData: false);
    private async void OnDeleteAll(object sender, RoutedEventArgs eventArgs) => await DeleteAsync(allData: true);

    private async void OnSaveAppearance(object sender, RoutedEventArgs eventArgs)
    {
        var language = (LanguageSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "en";
        var theme = (ThemeSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "System";
        await _viewModel.SaveAppearanceAsync(language, theme);
        WindowsAppearance.Apply(_viewModel.Preferences);
        // Update copy is formatted in code, so the new language only reaches it by re-running this.
        SyncUpdateControls();
        await RefreshHistoryAsync();
        ActionStatus.Text = T("AppearanceApplied");
    }

    private async void OnSaveWidget(object sender, RoutedEventArgs eventArgs)
    {
        var providers = SelectedWidgetProviders();
        if (providers.Count == 0)
        {
            ActionStatus.Text = T("PinOneConnection");
            PinCodex.Focus();
            return;
        }

        var modeName = (WidgetMode.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        _ = Enum.TryParse<WidgetVisualMode>(modeName, out var mode);
        var current = _viewModel.WidgetPreferences;
        var preferences = current with
        {
            IsVisible = WidgetVisible.IsChecked == true,
            Mode = mode,
            IsLocked = WidgetLocked.IsChecked == true,
            IsClickThrough = WidgetClickThrough.IsChecked == true,
            PinnedProviderIds = providers.ToArray()
        };
        await _applyWidget(preferences);
        ActionStatus.Text = T("WidgetSaved");
    }

    private async void OnRecoverWidget(object sender, RoutedEventArgs eventArgs)
    {
        await _recoverWidget();
        SyncWidgetControls();
        ActionStatus.Text = T("WidgetRecovered");
    }

    private async void OnMoveWidgetHere(object sender, RoutedEventArgs eventArgs)
    {
        await _moveWidgetToCurrentMonitor();
        SyncWidgetControls();
        ActionStatus.Text = T("MoveToCurrentDisplay");
    }

    private async Task RefreshHistoryAsync()
    {
        var provider = (ProviderFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var rangeText = (RangeFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var days = int.TryParse(rangeText, out var parsed) && parsed > 0 ? parsed : (int?)null;
        await _viewModel.RefreshHistoryAsync(provider, days);
    }

    private async Task ExportAsync(bool json)
    {
        var dialog = new WpfSaveFileDialog
        {
            Title = T(json ? "ExportJsonTitle" : "ExportCsvTitle"),
            Filter = json ? "JSON (*.json)|*.json" : "CSV (*.csv)|*.csv",
            DefaultExt = json ? ".json" : ".csv",
            AddExtension = true,
            FileName = $"ai-usage-{DateTime.Now:yyyyMMdd-HHmm}.{(json ? "json" : "csv")}"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var content = await _viewModel.ExportCurrentAsync(json);
            await File.WriteAllTextAsync(dialog.FileName, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            ActionStatus.Text = string.Format(T("ExportCreated"), Path.GetFileName(dialog.FileName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(exception.Message, T("ExportFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task DeleteAsync(bool allData)
    {
        var description = T(allData ? "DeleteAllDescription" : "DeleteFilterDescription");
        var result = System.Windows.MessageBox.Show(
            string.Format(T("DeleteConfirmation"), description),
            T("DeleteTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var deleted = await _viewModel.DeleteCurrentAsync(allData);
        ActionStatus.Text = string.Format(T("DeletedCount"), deleted);
    }

    private void SyncWidgetControls()
    {
        var preferences = _viewModel.WidgetPreferences;
        WidgetVisible.IsChecked = preferences.IsVisible;
        WidgetLocked.IsChecked = preferences.IsLocked;
        WidgetClickThrough.IsChecked = preferences.IsClickThrough;
        PinCodex.IsChecked = preferences.PinnedProviderIds!.Contains("codex", StringComparer.OrdinalIgnoreCase);
        PinClaude.IsChecked = preferences.PinnedProviderIds!.Contains("claude-code", StringComparer.OrdinalIgnoreCase);
        _widgetProviderOrder.Clear();
        _widgetProviderOrder.AddRange(preferences.PinnedProviderIds!);
        if (!_widgetProviderOrder.Contains("codex", StringComparer.OrdinalIgnoreCase)) _widgetProviderOrder.Add("codex");
        if (!_widgetProviderOrder.Contains("claude-code", StringComparer.OrdinalIgnoreCase)) _widgetProviderOrder.Add("claude-code");
        ApplyProviderOrder();
        WidgetMode.SelectedIndex = preferences.Mode switch
        {
            WidgetVisualMode.Rings => 0,
            WidgetVisualMode.HorizontalBars => 1,
            WidgetVisualMode.VerticalBars => 2,
            _ => 0
        };
        UpdateWidgetPreview();
    }

    private void UpdateWidgetPreview()
    {
        if (WidgetPreview is null || _previewWidgetViewModel is null || WidgetMode is null) return;
        var modeName = (WidgetMode.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        _ = Enum.TryParse<WidgetVisualMode>(modeName, out var mode);
        var providers = SelectedWidgetProviders();
        if (providers.Count == 0) providers.Add(_widgetProviderOrder[0]);
        var previewPreferences = _viewModel.WidgetPreferences with
        {
            IsVisible = WidgetVisible.IsChecked == true,
            Mode = mode,
            IsLocked = WidgetLocked.IsChecked == true,
            IsClickThrough = WidgetClickThrough.IsChecked == true,
            PinnedProviderIds = providers.ToArray()
        };
        previewPreferences = WidgetPreferenceRules.Normalize(previewPreferences);
        _previewWidgetViewModel.ApplyPreferences(previewPreferences);
        (WidgetPreview.Width, WidgetPreview.Height) = WidgetGeometry.Calculate(_previewWidgetViewModel, previewPreferences);
    }

    private void OnWidgetPreviewInputChanged(object sender, RoutedEventArgs eventArgs) => UpdateWidgetPreview();

    private void OnMoveProviderUp(object sender, RoutedEventArgs eventArgs) => MoveProvider(sender, -1);

    private void OnMoveProviderDown(object sender, RoutedEventArgs eventArgs) => MoveProvider(sender, 1);

    private void MoveProvider(object sender, int offset)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string provider }) return;
        var index = _widgetProviderOrder.FindIndex(item => item.Equals(provider, StringComparison.OrdinalIgnoreCase));
        var destination = index + offset;
        if (index < 0 || destination < 0 || destination >= _widgetProviderOrder.Count) return;
        (_widgetProviderOrder[index], _widgetProviderOrder[destination]) = (_widgetProviderOrder[destination], _widgetProviderOrder[index]);
        ApplyProviderOrder();
        UpdateWidgetPreview();
    }

    private void ApplyProviderOrder()
    {
        if (CodexProviderRow is null || ClaudeProviderRow is null) return;
        Grid.SetRow(CodexProviderRow, _widgetProviderOrder.FindIndex(item => item.Equals("codex", StringComparison.OrdinalIgnoreCase)));
        Grid.SetRow(ClaudeProviderRow, _widgetProviderOrder.FindIndex(item => item.Equals("claude-code", StringComparison.OrdinalIgnoreCase)));
    }

    private List<string> SelectedWidgetProviders() => _widgetProviderOrder
        .Where(provider => provider.Equals("codex", StringComparison.OrdinalIgnoreCase)
            ? PinCodex?.IsChecked == true
            : provider.Equals("claude-code", StringComparison.OrdinalIgnoreCase) && PinClaude?.IsChecked == true)
        .ToList();

    private void SyncAppearanceControls()
    {
        LanguageSelector.SelectedIndex = _viewModel.Preferences.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        ThemeSelector.SelectedIndex = _viewModel.Preferences.Theme switch
        {
            var value when value.Equals("Dark", StringComparison.OrdinalIgnoreCase) => 1,
            var value when value.Equals("Light", StringComparison.OrdinalIgnoreCase) => 2,
            _ => 0
        };
    }

    private string T(string key) => UiLanguageCatalog.Get(_viewModel.Preferences.Language, key);

    private static void OpenExternal(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
