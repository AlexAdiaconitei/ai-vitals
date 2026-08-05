using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using AIVitals.Adapters.Abstractions;
using AIVitals.Application;
using AIVitals.Domain;

namespace AIVitals.App;

public sealed record HistoryRowViewModel(
    string Provider,
    string Capability,
    string Value,
    string Observed,
    string Context,
    string Source)
{
    public string Summary => $"{Capability} · {Value}";
}

public sealed record ActivityPointViewModel(string ProviderId, DateTimeOffset ObservedAt, double Value);

public sealed record ProviderMetricViewModel(
    string ProviderId,
    string Provider,
    double Average,
    double Latest,
    double Peak,
    double Volatility,
    double Share,
    int SnapshotCount);

public sealed record QuotaBandViewModel(
    QuotaBandKind Kind,
    QuotaPeriod Period,
    string RoleText,
    string PeriodText,
    string UsageText,
    double UsageValue,
    string ResetText,
    string FreshnessText,
    bool IsActive,
    bool IsCurrent)
{
    public string AccessibleName => $"{RoleText}, {PeriodText}, {UsageText}. {ResetText}. {FreshnessText}.";
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly UsageMonitorService _monitor;
    private readonly TimeProvider _timeProvider;
    private readonly DispatcherTimer _freshnessTimer;
    private ObservationQuery _activeHistoryQuery = new(Limit: 10_000);
    private string _codexUsageText = "—";
    private double _codexUsageValue;
    private string _codexObservedText = string.Empty;
    private string _codexHealthText = string.Empty;
    private string _claudeUsageText = "—";
    private double _claudeUsageValue;
    private string _claudeObservedText = string.Empty;
    private string _claudeHealthText = string.Empty;
    private string _historyStatus = string.Empty;
    private string _historyProviderCount = "0";
    private string _historySnapshotCount = string.Empty;
    private string _historyAverage = "0%";
    private string _historyPeakProvider = "—";
    private string _historyPeakValue = string.Empty;
    private string _historyCurrentProvider = "—";
    private string _historyCurrentValue = string.Empty;
    private string _historyVolatility = "0%";
    private string _historyTrendTitle = string.Empty;

    public MainViewModel(UsageMonitorService monitor, TimeProvider? timeProvider = null)
    {
        _monitor = monitor;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _monitor.StateChanged += OnStateChanged;
        _freshnessTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _freshnessTimer.Tick += OnFreshnessTick;
        _freshnessTimer.Start();
        Apply(_monitor.State);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<HistoryRowViewModel> History { get; } = [];
    public ObservableCollection<ActivityPointViewModel> Activity { get; } = [];
    public ObservableCollection<ProviderMetricViewModel> ProviderMetrics { get; } = [];
    public ObservableCollection<QuotaBandViewModel> CodexBands { get; } = [];
    public ObservableCollection<QuotaBandViewModel> ClaudeBands { get; } = [];
    public string CodexUsageText { get => _codexUsageText; private set => Set(ref _codexUsageText, value); }
    public double CodexUsageValue { get => _codexUsageValue; private set => Set(ref _codexUsageValue, value); }
    public string CodexObservedText { get => _codexObservedText; private set => Set(ref _codexObservedText, value); }
    public string CodexHealthText { get => _codexHealthText; private set => Set(ref _codexHealthText, value); }
    public string ClaudeUsageText { get => _claudeUsageText; private set => Set(ref _claudeUsageText, value); }
    public double ClaudeUsageValue { get => _claudeUsageValue; private set => Set(ref _claudeUsageValue, value); }
    public string ClaudeObservedText { get => _claudeObservedText; private set => Set(ref _claudeObservedText, value); }
    public string ClaudeHealthText { get => _claudeHealthText; private set => Set(ref _claudeHealthText, value); }
    public string HistoryStatus { get => _historyStatus; private set => Set(ref _historyStatus, value); }
    public string HistoryProviderCount { get => _historyProviderCount; private set => Set(ref _historyProviderCount, value); }
    public string HistorySnapshotCount { get => _historySnapshotCount; private set => Set(ref _historySnapshotCount, value); }
    public string HistoryAverage { get => _historyAverage; private set => Set(ref _historyAverage, value); }
    public string HistoryPeakProvider { get => _historyPeakProvider; private set => Set(ref _historyPeakProvider, value); }
    public string HistoryPeakValue { get => _historyPeakValue; private set => Set(ref _historyPeakValue, value); }
    public string HistoryCurrentProvider { get => _historyCurrentProvider; private set => Set(ref _historyCurrentProvider, value); }
    public string HistoryCurrentValue { get => _historyCurrentValue; private set => Set(ref _historyCurrentValue, value); }
    public string HistoryVolatility { get => _historyVolatility; private set => Set(ref _historyVolatility, value); }
    public string HistoryTrendTitle { get => _historyTrendTitle; private set => Set(ref _historyTrendTitle, value); }
    public WidgetPreferences WidgetPreferences => _monitor.State.Preferences.EffectiveWidget;
    public AppPreferences Preferences => _monitor.State.Preferences;

    public async Task RefreshHistoryAsync(string? providerId, int? lastDays)
    {
        Apply(_monitor.State);
        _activeHistoryQuery = new ObservationQuery(
            FromUtc: lastDays is > 0 ? DateTimeOffset.UtcNow.AddDays(-lastDays.Value) : null,
            ProviderId: providerId,
            Limit: 10_000);
        var observations = await _monitor.QueryObservationsAsync(_activeHistoryQuery);
        History.Clear();
        Activity.Clear();
        ProviderMetrics.Clear();
        foreach (var observation in observations)
            History.Add(ToHistoryRow(observation, _monitor.State.Preferences.Language));
        var language = _monitor.State.Preferences.Language;
        var bucket = BucketSize(lastDays);
        var analytics = HistoryAnalytics.Build(observations, bucket);
        foreach (var point in analytics.Points)
            Activity.Add(new ActivityPointViewModel(point.ProviderId, point.ObservedAtUtc, point.Value));
        foreach (var provider in analytics.Providers)
            ProviderMetrics.Add(new ProviderMetricViewModel(
                provider.ProviderId,
                ProviderName(provider.ProviderId),
                provider.Average,
                provider.Latest,
                provider.Peak,
                provider.Volatility,
                provider.Share,
                provider.SnapshotCount));

        HistoryProviderCount = analytics.ProviderCount.ToString("N0", Culture(language));
        HistorySnapshotCount = string.Format(Culture(language), T(language, "HistorySnapshots"), analytics.SnapshotCount);
        HistoryAverage = $"{analytics.AverageUsage:0.#}%";
        HistoryPeakProvider = ProviderName(analytics.PeakProviderId);
        HistoryPeakValue = analytics.PeakProviderId is null
            ? T(language, "HistoryNoData")
            : string.Format(Culture(language), T(language, "HistoryPeakValue"), analytics.PeakUsage);
        HistoryCurrentProvider = ProviderName(analytics.CurrentProviderId);
        HistoryCurrentValue = analytics.CurrentProviderId is null
            ? T(language, "HistoryNoData")
            : string.Format(Culture(language), T(language, "HistoryCurrentValue"), analytics.CurrentUsage);
        HistoryVolatility = string.Format(Culture(language), T(language, "HistoryVolatilityValue"), analytics.AverageVolatility);
        HistoryTrendTitle = string.Format(
            Culture(language),
            T(language, "HistoryTrendTitle"),
            RangeLabel(lastDays, language),
            BucketLabel(bucket));
        HistoryStatus = observations.Count == 0
            ? T(language, "HistoryEmpty")
            : string.Format(Culture(language), T(language, "HistoryCount"), observations.Count);
    }

    public async Task<string> ExportCurrentAsync(bool json)
    {
        var observations = await _monitor.QueryObservationsAsync(_activeHistoryQuery);
        return json ? ObservationExporter.ToJson(observations) : ObservationExporter.ToCsv(observations);
    }

    public async Task<int> DeleteCurrentAsync(bool allData)
    {
        var query = allData ? new ObservationQuery(Limit: 10_000) : _activeHistoryQuery;
        var deleted = await _monitor.DeleteObservationsAsync(query);
        await RefreshHistoryAsync(_activeHistoryQuery.ProviderId, DaysFromQuery(_activeHistoryQuery));
        return deleted;
    }

    public Task SaveAppearanceAsync(string language, string theme) =>
        _monitor.SavePreferencesAsync(_monitor.State.Preferences with
        {
            Language = language,
            Theme = theme
        });

    public void Dispose()
    {
        _freshnessTimer.Stop();
        _freshnessTimer.Tick -= OnFreshnessTick;
        _monitor.StateChanged -= OnStateChanged;
    }

    private void OnFreshnessTick(object? sender, EventArgs eventArgs) => Apply(_monitor.State);

    private void OnStateChanged(object? sender, UsageMonitorState state) =>
        System.Windows.Application.Current.Dispatcher.Invoke(() => Apply(state));

    private void Apply(UsageMonitorState state)
    {
        var now = _timeProvider.GetUtcNow();
        var language = state.Preferences.Language;
        var codexBands = ProjectBands(state, "codex", now, language);
        var claudeBands = ProjectBands(state, "claude-code", now, language);
        Replace(CodexBands, codexBands);
        Replace(ClaudeBands, claudeBands);

        state.LatestByProvider.TryGetValue("codex", out var codex);
        var codexPrimary = SelectPrimary(codexBands);
        CodexUsageText = codexPrimary?.UsageText ?? "—";
        CodexUsageValue = codexPrimary?.UsageValue ?? 0;
        CodexObservedText = FormatObserved(
            codexPrimary,
            codex,
            T(language, "WaitingReading"),
            T(language, "WaitingReading"),
            language);
        CodexHealthText = FormatHealth(state.AdapterHealth, "codex", codexPrimary, T(language, "StatusStarting"), language);

        state.LatestByProvider.TryGetValue("claude-code", out var claude);
        var claudePrimary = SelectPrimary(claudeBands);
        ClaudeUsageText = claudePrimary?.UsageText ?? "—";
        ClaudeUsageValue = claudePrimary?.UsageValue ?? 0;
        ClaudeObservedText = FormatObserved(
            claudePrimary,
            claude,
            T(language, "WaitingReading"),
            T(language, "WaitingReading"),
            language);
        ClaudeHealthText = FormatHealth(state.AdapterHealth, "claude-code", claudePrimary, T(language, "StatusNoActivity"), language);
        if (string.IsNullOrEmpty(HistoryStatus)) HistoryStatus = T(language, "HistoryPrompt");
        OnPropertyChanged(nameof(WidgetPreferences));
    }

    private static HistoryRowViewModel ToHistoryRow(UsageObservation observation, string language) => new(
        observation.ProviderId == "claude-code" ? "Claude Code" : "Codex",
        observation.Capability switch
        {
            UsageCapability.QuotaWindow => T(language, "CapabilityQuota"),
            UsageCapability.TokenActivity when observation.Unit.Equals("percent", StringComparison.OrdinalIgnoreCase) => T(language, "CapabilityContext"),
            UsageCapability.TokenActivity => T(language, "CapabilityTokens"),
            UsageCapability.SessionActivity => T(language, "CapabilitySession"),
            UsageCapability.Cost => T(language, "CapabilityCost"),
            _ => observation.Capability.ToString()
        },
        ObservationValueFormatter.Format(observation.Value, observation.Unit, language),
        observation.ObservedAtUtc.ToLocalTime().ToString("dd MMM yyyy · HH:mm:ss", Culture(language)),
        FormatContext(observation, language),
        observation.Source);

    private static string FormatContext(UsageObservation observation, string language)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(observation.Model)) parts.Add(FormatModel(observation.Model));
        if (observation.AnonymousSessionId is { Length: > 0 } session)
        {
            var suffix = session.StartsWith("session-", StringComparison.OrdinalIgnoreCase)
                ? session["session-".Length..]
                : session;
            parts.Add(string.Format(T(language, "HistorySession"), suffix[..Math.Min(8, suffix.Length)]));
        }
        else if (observation.Capability == UsageCapability.QuotaWindow)
        {
            parts.Add(T(language, "HistoryAccount"));
        }
        return parts.Count == 0 ? "—" : string.Join(" · ", parts);
    }

    private static string FormatModel(string model)
    {
        var parts = model.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Select(part => part.Equals("gpt", StringComparison.OrdinalIgnoreCase)
            ? "GPT"
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(part.ToLowerInvariant())));
    }

    private static int? DaysFromQuery(ObservationQuery query)
    {
        if (query.FromUtc is null) return null;
        return Math.Max(1, (int)Math.Round((DateTimeOffset.UtcNow - query.FromUtc.Value).TotalDays));
    }

    private static TimeSpan BucketSize(int? days) => days switch
    {
        <= 7 => TimeSpan.FromMinutes(5),
        <= 14 => TimeSpan.FromMinutes(10),
        <= 30 => TimeSpan.FromMinutes(15),
        <= 60 => TimeSpan.FromMinutes(20),
        <= 90 => TimeSpan.FromMinutes(30),
        _ => TimeSpan.FromHours(1)
    };

    private static string BucketLabel(TimeSpan bucket) => bucket.TotalHours >= 1
        ? $"{bucket.TotalHours:0}h"
        : $"{bucket.TotalMinutes:0}m";

    private static string RangeLabel(int? days, string language) => days switch
    {
        1 => T(language, "Last24Hours"),
        7 => T(language, "Last7Days"),
        30 => T(language, "Last30Days"),
        _ => T(language, "AllTime")
    };

    private static string ProviderName(string? providerId) => providerId switch
    {
        "codex" => "Codex",
        "claude-code" => "Claude Code",
        null => "—",
        _ => providerId
    };

    private static string FormatObserved(
        QuotaBandViewModel? band,
        UsageObservation? observation,
        string fallback,
        string? staleFallback,
        string language) =>
        band is { IsActive: false }
            ? T(language, "NextWindowOnUse")
            : band is { IsCurrent: false }
                ? staleFallback ?? fallback
            : observation?.ObservedAtUtc.ToLocalTime().ToString("dd MMM · HH:mm:ss", Culture(language)) ?? fallback;

    private static IReadOnlyList<QuotaBandViewModel> ProjectBands(
        UsageMonitorState state,
        string providerId,
        DateTimeOffset now,
        string language)
    {
        if (!state.LatestQuotaByProvider.TryGetValue(providerId, out var observations)) return [];
        return QuotaBandProjection.Project(observations, now)
            .Select(band => new QuotaBandViewModel(
                band.Kind,
                band.Period,
                band.Kind == QuotaBandKind.Immediate ? T(language, "RoleImmediate") : T(language, "RoleTotal"),
                PeriodText(band, language),
                band.IsActive ? $"{band.UsedPercentage:0.#}%" : "0%",
                // The meter mirrors the percentage instead of collapsing to zero when the
                // reading is stale; FreshnessText is what says how old the value is.
                band.IsActive ? Math.Clamp((double)band.UsedPercentage, 0, 100) : 0,
                ResetText(band, now, language),
                !band.IsActive
                    ? T(language, "InactiveWindow")
                    : band.IsCurrent
                    ? FreshnessText(band.Observation, now, language)
                    : string.Format(
                        T(language, "LastKnownUsage"),
                        band.UsedPercentage,
                        band.Observation.ObservedAtUtc.ToLocalTime().ToString("HH:mm")),
                band.IsActive,
                band.IsCurrent))
            .ToArray();
    }

    private static QuotaBandViewModel? SelectPrimary(IReadOnlyList<QuotaBandViewModel> bands) =>
        bands.FirstOrDefault(item => item.Kind == QuotaBandKind.Immediate) ?? bands.FirstOrDefault();

    private static string PeriodText(QuotaPeriod period, TimeSpan? duration, string language) => period switch
    {
        QuotaPeriod.FiveHours => T(language, "PeriodFiveHours"),
        QuotaPeriod.Daily => T(language, "PeriodDay"),
        QuotaPeriod.Weekly => T(language, "PeriodWeek"),
        QuotaPeriod.Monthly => T(language, "PeriodMonth"),
        QuotaPeriod.Custom when duration is { } value && value.TotalDays >= 1 => string.Format(Culture(language), T(language, "CustomDays"), value.TotalDays),
        QuotaPeriod.Custom when duration is { } value => string.Format(Culture(language), T(language, "CustomHours"), value.TotalHours),
        _ => T(language, "PeriodQuota")
    };

    private static string PeriodText(QuotaBandSnapshot band, string language)
    {
        var period = PeriodText(band.Period, band.Duration, language);
        var source = band.Observation.Source;
        if (source.Contains("oauth-apps", StringComparison.OrdinalIgnoreCase)) return $"{period} · Apps";
        if (source.Contains("sonnet", StringComparison.OrdinalIgnoreCase)) return $"{period} · Sonnet";
        if (source.Contains("opus", StringComparison.OrdinalIgnoreCase)) return $"{period} · Opus";
        return period;
    }

    private static string ResetText(QuotaBandSnapshot band, DateTimeOffset now, string language)
    {
        if (!band.IsActive || band.ResetsAtUtc is null) return T(language, "StartsOnNextUse");
        var localReset = band.ResetsAtUtc.Value.ToLocalTime();
        var localToday = now.ToLocalTime().Date;
        if (localReset.Date == localToday)
            return string.Format(T(language, "ResetToday"), localReset.ToString("HH:mm"));
        if (localReset.Date == localToday.AddDays(1))
            return string.Format(T(language, "ResetTomorrow"), localReset.ToString("HH:mm"));
        return string.Format(T(language, "ResetOn"), localReset.ToString("ddd d MMM", Culture(language)), localReset.ToString("HH:mm"));
    }

    private static string FreshnessText(UsageObservation observation, DateTimeOffset now, string language)
    {
        var age = now - observation.ObservedAtUtc;
        if (age <= TimeSpan.FromMinutes(2)) return T(language, "FreshNow");
        if (age <= TimeSpan.FromMinutes(5)) return string.Format(T(language, "MinutesAgo"), Math.Max(1, (int)age.TotalMinutes));
        return string.Format(T(language, "StaleData"), observation.ObservedAtUtc.ToLocalTime().ToString("dd MMM HH:mm", Culture(language)));
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    private static string FormatHealth(
        IReadOnlyDictionary<string, AdapterHealth> healthByAdapter,
        string adapterId,
        QuotaBandViewModel? primaryBand,
        string fallback,
        string language) =>
        primaryBand is { IsActive: true, IsCurrent: false }
            ? "◆ " + T(language, "StaleDataStatus")
            : healthByAdapter.TryGetValue(adapterId, out var health)
            ? health switch
            {
                AdapterHealth.Available => "● " + T(language, "StatusAvailable"),
                AdapterHealth.Degraded => "◆ " + T(language, "StatusDegraded"),
                _ => "○ " + T(language, "StatusWaiting")
            }
            : "● " + fallback;

    private static string T(string language, string key) => UiLanguageCatalog.Get(language, key);

    private static CultureInfo Culture(string language) =>
        CultureInfo.GetCultureInfo(language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en-US" : "es-ES");

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
