using System.Windows;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using AIVitals.Adapters.Abstractions;
using AIVitals.Adapters.ClaudeCode;
using AIVitals.Adapters.Codex;
using AIVitals.Application;
using AIVitals.Infrastructure;
using AIVitals.Platform.Windows;
using Velopack;

namespace AIVitals.App;

public partial class App : System.Windows.Application
{
    private UsageMonitorService? _monitor;
    private IAppUpdateService? _updateService;
    private IStartupRegistration? _startupRegistration;
    private ClaudeCodeBridgeStaging? _bridgeStaging;
    private DispatcherTimer? _updateTimer;
    private TrayIconHost? _trayIcon;
    private TrayMenuWindow? _trayMenu;
    private MainWindow? _mainWindow;
    private MainViewModel? _mainViewModel;
    private QuickPopupWindow? _quickPopup;
    private WidgetWindow? _widgetWindow;
    private GlobalHotkeyHost? _widgetHotkey;
    private ClaudeCodeStatusLineInstaller? _claudeInstaller;
    private DispatcherTimer? _freshnessTimer;
    private string? _appliedAppearance;
    private string? _trayPreferenceSignature;
    private bool _isExiting;

    [STAThread]
    private static void Main(string[] args)
    {
        // Velopack has to run before anything else: it may take over the process to finish an
        // install, update or uninstall, and it is what makes the install locator available later.
        VelopackApp.Build()
            .SetArgs(args)
            .SetAutoApplyOnStartup(false)
            .OnBeforeUninstallFastCallback(_ => RevertLocalIntegrations())
            .Run();

        var application = new App();
        application.InitializeComponent();
        application.Run();
    }

    /// <summary>
    /// Uninstall hook. Local usage data is deliberately preserved; what must go is everything this
    /// application wrote outside its own install directory, because those entries would otherwise
    /// point at an executable that no longer exists.
    /// </summary>
    private static void RevertLocalIntegrations()
    {
        var staging = new ClaudeCodeBridgeStaging(
            Path.Combine(AppContext.BaseDirectory, "statusline"),
            AppDataPaths.ForCurrentUser().BridgeDirectory);
        try
        {
            new ClaudeCodeStatusLineInstaller(staging.HelperPath).UninstallAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // Uninstalling continues even when Claude Code's settings cannot be rewritten.
        }

        staging.Remove();
        WindowsStartupRegistration.RemoveAny();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher));

        try
        {
            var paths = AppDataPaths.ForCurrentUser();
            paths.EnsureCreated();

            _monitor = new UsageMonitorService(
                new IUsageAdapter[] { new CodexUsageAdapter(), new ClaudeCodeUsageAdapter() },
                new SqliteObservationRepository(paths.DatabasePath),
                new JsonPreferencesStore(paths.PreferencesPath));

            _updateService = new VelopackUpdateService();
            _startupRegistration = new WindowsStartupRegistration(VelopackInstallation.LauncherPath);

            var bridgeSourceDirectory = Path.Combine(AppContext.BaseDirectory, "statusline");
            _bridgeStaging = new ClaudeCodeBridgeStaging(bridgeSourceDirectory, paths.BridgeDirectory);
            _bridgeStaging.TryStage(_updateService.Status.InstalledVersion);
            var helperPath = _bridgeStaging.IsStaged
                ? _bridgeStaging.HelperPath
                : Path.Combine(bridgeSourceDirectory, ClaudeCodeBridgeStaging.HelperFileName);
            _claudeInstaller = new ClaudeCodeStatusLineInstaller(helperPath);
            var skipClaudeInstaller = string.Equals(
                Environment.GetEnvironmentVariable("AI_VITALS_SKIP_CLAUDE_INSTALLER"),
                "1",
                StringComparison.Ordinal);
            if (!skipClaudeInstaller)
            {
                if (await _claudeInstaller.IsInstalledAsync())
                    await _claudeInstaller.InstallAsync();
                else
                    await MoveBridgeToStagedLocationAsync(bridgeSourceDirectory);
            }
            _monitor.StateChanged += OnMonitorStateChanged;
            await _monitor.StartAsync();
            _freshnessTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _freshnessTimer.Tick += (_, _) => OnMonitorStateChanged(_monitor, _monitor.State);
            _freshnessTimer.Start();
            WindowsAppearance.Apply(_monitor.State.Preferences);
            _appliedAppearance = AppearanceSignature(_monitor.State.Preferences);
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;

            ApplyStartupRegistration(_monitor.State.Preferences.StartWithWindows);
            _updateService.StatusChanged += OnUpdateStatusChanged;

            _mainViewModel = new MainViewModel(_monitor);
            _mainWindow = new MainWindow(
                _monitor,
                _mainViewModel,
                ApplyWidgetFromDashboardAsync,
                RecoverWidgetAsync,
                MoveWidgetToCurrentMonitorAsync,
                _updateService,
                CheckForUpdatesAsync,
                ApplyUpdateAsync,
                SaveUpdatePreferencesAsync);
            _mainWindow.Closing += (_, args) =>
            {
                if (_isExiting) return;
                args.Cancel = true;
                _mainWindow.Hide();
            };

            var widgetPreferences = _monitor.State.Preferences.EffectiveWidget;
            _quickPopup = new QuickPopupWindow(
                _mainViewModel,
                ShowDashboard,
                ToggleWidgetAsync,
                SetWidgetModeAsync,
                ToggleWidgetLockAsync,
                ToggleWidgetClickThroughAsync,
                RecoverWidgetAsync);
            _widgetWindow = new WidgetWindow(
                _monitor,
                widgetPreferences,
                SaveWidgetPreferencesAsync,
                ShowQuickView,
                ShowDashboard);
            _widgetWindow.ApplyPreferences(widgetPreferences);
            try
            {
                _widgetHotkey = new GlobalHotkeyHost(
                    HotkeyModifiers.Control | HotkeyModifiers.Shift,
                    System.Windows.Forms.Keys.U,
                    () => _ = RecoverWidgetAsync());
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The tray recovery action remains available if another app owns the shortcut.
            }

            CreateTrayIcon(_monitor.State.Preferences.Language);
            if (string.Equals(
                    Environment.GetEnvironmentVariable("AI_VITALS_SHOW_TRAY_MENU"),
                    "1",
                    StringComparison.Ordinal))
                _ = Dispatcher.BeginInvoke(ShowTrayMenu);

            StartUpdateChecks();

            if (!_monitor.State.Preferences.OnboardingCompleted)
            {
                var onboarding = new OnboardingWindow(_mainViewModel, CompleteOnboardingAsync);
                onboarding.ShowDialog();
            }

            if (!_monitor.State.Preferences.StartMinimized) ShowDashboard();
        }
        catch (Exception exception)
        {
            var language = _monitor?.State.Preferences.Language ?? "en";
            System.Windows.MessageBox.Show(
                string.Format(UiLanguageCatalog.Get(language, "AppStartFailed"), exception.Message),
                UiLanguageCatalog.Get(language, "AppStartFailedTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnMonitorStateChanged(object? sender, UsageMonitorState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnMonitorStateChanged(sender, state));
            return;
        }

        var appearance = AppearanceSignature(state.Preferences);
        if (_appliedAppearance != appearance)
        {
            WindowsAppearance.Apply(state.Preferences);
            _appliedAppearance = appearance;
        }
        var trayPreferences = TrayPreferenceSignature(state.Preferences);
        if (_trayPreferenceSignature != trayPreferences)
        {
            _trayMenu?.UpdateState(state.Preferences.EffectiveWidget, state.Preferences.Theme);
            _trayPreferenceSignature = trayPreferences;
        }

        var now = DateTimeOffset.UtcNow;
        var statusParts = state.LatestQuotaByProvider
            .OrderBy(item => item.Key)
            .Select(item =>
            {
                var bands = QuotaBandProjection.Project(item.Value, now);
                var values = bands.Where(band => band.IsCurrent).Select(band =>
                    $"{QuotaLabel(band)} {band.UsedPercentage:0.#}%");
                var valueText = string.Join(" / ", values);
                return string.IsNullOrEmpty(valueText)
                    ? $"{ProviderLabel(item.Key)} {Text("StaleDataStatus").ToLowerInvariant()}"
                    : $"{ProviderLabel(item.Key)} {valueText}";
            })
            .Where(item => !item.EndsWith(' '))
            .ToArray();
        var status = statusParts.Length == 0
            ? $"AI Vitals · {Text("AppWaitingData")}"
            : "AI Vitals · " + string.Join(" · ", statusParts);
        _trayIcon?.SetStatus(status);
    }

    private void CreateTrayIcon(string language)
    {
        _ = language;
        var widget = _monitor?.State.Preferences.EffectiveWidget ?? new WidgetPreferences();
        var theme = _monitor?.State.Preferences.Theme ?? "System";
        _trayMenu ??= new TrayMenuWindow(
            ShowDashboard,
            OpenSettings,
            ToggleWidgetAsync,
            SetWidgetModeAsync,
            ToggleWidgetLockAsync,
            ToggleWidgetClickThroughAsync,
            RecoverWidgetAsync,
            MoveWidgetToCurrentMonitorAsync,
            SetThemeAsync,
            ApplyUpdateAsync,
            ExitAsync);
        _trayMenu.UpdateState(widget, theme);
        _trayMenu.UpdatePendingUpdate(_updateService?.Status);
        _trayIcon ??= new TrayIconHost(ShowQuickView, ShowDashboard, ShowTrayMenu);
        if (_monitor is not null) _trayPreferenceSignature = TrayPreferenceSignature(_monitor.State.Preferences);
    }

    private void ShowTrayMenu() => _trayMenu?.ShowNearCursor();

    private void StartUpdateChecks()
    {
        if (_updateService is null || _updateService.Status.State == AppUpdateState.Unsupported) return;

        _updateTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = UpdateCheckSchedule.Interval
        };
        _updateTimer.Tick += (_, _) => _ = CheckForUpdatesAutomaticallyAsync();
        _updateTimer.Start();
        _ = CheckForUpdatesAutomaticallyAsync();
    }

    private Task CheckForUpdatesAutomaticallyAsync() =>
        _monitor?.State.Preferences.AutomaticUpdateCheckEnabled == true && _updateService is not null
            ? _updateService.CheckAsync(userInitiated: false)
            : Task.CompletedTask;

    private Task CheckForUpdatesAsync() =>
        _updateService?.CheckAsync(userInitiated: true) ?? Task.CompletedTask;

    private async Task ApplyUpdateAsync()
    {
        if (_updateService is null || !_updateService.Status.IsPending) return;

        // Close everything down first: the updater replaces files this process still holds open.
        await ReleaseResourcesAsync();
        await _updateService.ApplyAndRestartAsync();
    }

    private async Task SaveUpdatePreferencesAsync(bool automaticCheckEnabled, bool startWithWindows)
    {
        if (_monitor is null) return;
        ApplyStartupRegistration(startWithWindows);
        await _monitor.SavePreferencesAsync(_monitor.State.Preferences with
        {
            AutomaticUpdateCheckEnabled = automaticCheckEnabled,
            StartWithWindows = startWithWindows
        });
    }

    private void ApplyStartupRegistration(bool enabled)
    {
        if (_startupRegistration is not { IsSupported: true }) return;
        try
        {
            if (_startupRegistration.IsEnabled() != enabled) _startupRegistration.SetEnabled(enabled);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // A locked-down Run key only means the preference cannot be honoured on this machine.
        }
    }

    private void OnUpdateStatusChanged(object? sender, AppUpdateStatus status)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnUpdateStatusChanged(sender, status));
            return;
        }

        _trayMenu?.UpdatePendingUpdate(status);
    }

    /// <summary>
    /// Migrates an installation that still points at the helper inside the application directory.
    /// The round trip restores the user's own status line first, so nothing is silently rewritten.
    /// </summary>
    private async Task MoveBridgeToStagedLocationAsync(string bridgeSourceDirectory)
    {
        if (_claudeInstaller is null || _bridgeStaging is not { IsStaged: true }) return;

        var legacyInstaller = new ClaudeCodeStatusLineInstaller(
            Path.Combine(bridgeSourceDirectory, ClaudeCodeBridgeStaging.HelperFileName));
        if (!await legacyInstaller.IsInstalledAsync()) return;
        if (await legacyInstaller.UninstallAsync() != ClaudeCodeIntegrationResult.Restored) return;
        await _claudeInstaller.InstallAsync();
    }

    private async Task SetThemeAsync(string theme)
    {
        if (_monitor is null) return;
        await _monitor.SavePreferencesAsync(_monitor.State.Preferences with { Theme = theme });
    }

    private void OpenSettings()
    {
        ShowDashboard();
        _mainWindow?.ShowSection(5);
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (_monitor is null || eventArgs.PropertyName is not (nameof(SystemParameters.HighContrast) or nameof(SystemParameters.ClientAreaAnimation)))
            return;
        WindowsAppearance.Apply(_monitor.State.Preferences);
        _appliedAppearance = AppearanceSignature(_monitor.State.Preferences);
    }

    private void ShowDashboard()
    {
        if (_mainWindow is null) return;
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void ShowQuickView() => _quickPopup?.ShowNearCursor();

    private async Task ToggleWidgetAsync()
    {
        if (_widgetWindow is null) return;
        var preferences = _widgetWindow.Preferences with { IsVisible = !_widgetWindow.Preferences.IsVisible };
        _widgetWindow.ApplyPreferences(preferences);
        await SaveWidgetPreferencesAsync(preferences);
    }

    private async Task SetWidgetModeAsync(WidgetVisualMode mode)
    {
        if (_widgetWindow is null) return;
        var preferences = _widgetWindow.Preferences with { Mode = mode, IsVisible = true };
        _widgetWindow.ApplyPreferences(preferences);
        await SaveWidgetPreferencesAsync(preferences);
    }

    private async Task ToggleWidgetLockAsync()
    {
        if (_widgetWindow is null) return;
        var unlock = _widgetWindow.Preferences.IsLocked;
        var preferences = _widgetWindow.Preferences with
        {
            IsLocked = !unlock,
            IsClickThrough = unlock ? false : _widgetWindow.Preferences.IsClickThrough
        };
        _widgetWindow.ApplyPreferences(preferences);
        await SaveWidgetPreferencesAsync(preferences);
    }

    private async Task ToggleWidgetClickThroughAsync()
    {
        if (_widgetWindow is null) return;
        var enabled = !_widgetWindow.Preferences.IsClickThrough;
        var preferences = _widgetWindow.Preferences with
        {
            IsVisible = true,
            IsLocked = enabled || _widgetWindow.Preferences.IsLocked,
            IsClickThrough = enabled
        };
        _widgetWindow.ApplyPreferences(preferences);
        await SaveWidgetPreferencesAsync(preferences);
    }

    private Task RecoverWidgetAsync() => _widgetWindow?.RecoverAsync() ?? Task.CompletedTask;

    private Task MoveWidgetToCurrentMonitorAsync() =>
        _widgetWindow?.MoveToCurrentMonitorAsync() ?? Task.CompletedTask;

    private async Task ApplyWidgetFromDashboardAsync(WidgetPreferences widgetPreferences)
    {
        if (_widgetWindow is null) return;
        _widgetWindow.ApplyPreferences(widgetPreferences);
        await SaveWidgetPreferencesAsync(widgetPreferences);
    }

    private async Task CompleteOnboardingAsync()
    {
        if (_monitor is null) return;
        await _monitor.SavePreferencesAsync(_monitor.State.Preferences with { OnboardingCompleted = true });
        ShowDashboard();
    }

    private async Task SaveWidgetPreferencesAsync(WidgetPreferences widgetPreferences)
    {
        if (_monitor is null) return;
        await _monitor.SavePreferencesAsync(_monitor.State.Preferences with
        {
            Widget = WidgetPreferenceRules.Normalize(widgetPreferences)
        });
    }

    private async Task ExitAsync()
    {
        await ReleaseResourcesAsync();
        _mainWindow?.Close();
        Shutdown();
    }

    private async Task ReleaseResourcesAsync()
    {
        if (_isExiting) return;
        _isExiting = true;

        _updateTimer?.Stop();
        _updateTimer = null;
        if (_updateService is not null) _updateService.StatusChanged -= OnUpdateStatusChanged;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _trayMenu?.Close();
        _trayMenu = null;
        _widgetHotkey?.Dispose();
        _widgetHotkey = null;
        _widgetWindow?.DisposeViewModel();
        _widgetWindow?.Close();
        _widgetWindow = null;
        _quickPopup?.Close();
        _quickPopup = null;
        _mainViewModel?.Dispose();
        _mainViewModel = null;
        _freshnessTimer?.Stop();
        _freshnessTimer = null;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        if (_monitor is not null)
        {
            _monitor.StateChanged -= OnMonitorStateChanged;
            await _monitor.DisposeAsync();
        }
    }

    private static string ProviderLabel(string providerId) => providerId == "claude-code" ? "Claude" : "Codex";

    private static string QuotaLabel(QuotaBandSnapshot band) => band.Period switch
    {
        QuotaPeriod.FiveHours => "5H",
        QuotaPeriod.Daily => "D",
        QuotaPeriod.Weekly => QuotaSourceMetadata.Variant(band.Observation.Source) switch
        {
            "apps" => "W·A",
            "sonnet" => "W·S",
            "opus" => "W·O",
            _ => "W"
        },
        QuotaPeriod.Monthly => "M",
        _ => "Q"
    };

    private string Text(string key) => UiLanguageCatalog.Get(_monitor?.State.Preferences.Language, key);

    private static string AppearanceSignature(AppPreferences preferences) =>
        $"{preferences.Language}|{preferences.Theme}|{SystemParameters.HighContrast}|{SystemParameters.ClientAreaAnimation}";

    private static string TrayPreferenceSignature(AppPreferences preferences)
    {
        var widget = preferences.EffectiveWidget;
        return $"{preferences.Language}|{preferences.Theme}|{widget.IsVisible}|{widget.Mode}|{widget.IsLocked}|{widget.IsClickThrough}";
    }

}
