using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AIVitals.Adapters.Abstractions;
using AIVitals.Application;
using AIVitals.Domain;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace AIVitals.App;

public sealed record WidgetQuotaBandViewModel(
    string ShortLabel,
    string UsageText,
    double UsageValue,
    string ResetText,
    double RingDiameter,
    MediaBrush SignalBrush);

public sealed class WidgetConnectionViewModel : INotifyPropertyChanged
{
    private string _stateText = "SIN DATOS";
    private string _detailText = "Esperando lectura";
    private string _primaryUsageText = "—";

    public WidgetConnectionViewModel(string providerId)
    {
        ProviderId = providerId;
        DisplayName = providerId switch
        {
            "codex" => "CODEX",
            "claude-code" => "CLAUDE",
            _ => providerId.Replace('-', ' ').ToUpperInvariant()
        };
        LogoSource = new Uri(providerId == "claude-code"
            ? "pack://application:,,,/AIVitals.App;component/Assets/Providers/claude-ai.svg"
            : "pack://application:,,,/AIVitals.App;component/Assets/Providers/codex-dark.svg",
            UriKind.Absolute);
        var resourceKey = providerId == "claude-code" ? "WarmBrush" : "CodexBrush";
        AccentBrush = System.Windows.Application.Current.Resources[resourceKey] as MediaBrush
            ?? new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(
                providerId == "claude-code" ? "#FF9B2F" : "#3D8BFF"));
        var logoResourceKey = providerId == "claude-code" ? "ClaudeBrandBrush" : "CodexBrush";
        LogoBrush = System.Windows.Application.Current.Resources[logoResourceKey] as MediaBrush ?? AccentBrush;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProviderId { get; }
    public string DisplayName { get; }
    public Uri LogoSource { get; }
    public MediaBrush LogoBrush { get; }
    public MediaBrush AccentBrush { get; }
    public ObservableCollection<WidgetQuotaBandViewModel> Bands { get; } = [];
    public ObservableCollection<WidgetQuotaBandViewModel> RingBands { get; } = [];
    public string StateText { get => _stateText; private set => Set(ref _stateText, value); }
    public string DetailText { get => _detailText; private set => Set(ref _detailText, value); }
    public string PrimaryUsageText { get => _primaryUsageText; private set => Set(ref _primaryUsageText, value); }
    public int BandCount => Bands.Count;
    public double VerticalWidth => Math.Max(38, 6 + BandCount * 27);
    public string AccessibleName => Bands.Count == 0
        ? $"{DisplayName}, {StateText}"
        : $"{DisplayName}, {string.Join(", ", Bands.Select(item => $"{item.ShortLabel} {item.UsageText}"))}, {StateText}";

    public void Apply(
        IReadOnlyList<UsageObservation> observations,
        AdapterHealth? health,
        string? healthDetail,
        DateTimeOffset now,
        string language)
    {
        var snapshots = QuotaBandProjection.Project(observations, now)
            .OrderBy(item => item.Duration ?? TimeSpan.MaxValue)
            .ThenBy(item => item.Observation.Source, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var bands = snapshots.Select((band, index) => CreateBand(
            band,
            language,
            BandBrush(AccentBrush, index, snapshots.Length),
            ringDiameter: 0)).ToArray();

        Replace(Bands, bands);
        Replace(RingBands, bands
            .Reverse()
            .Select((band, index) => band with { RingDiameter = Math.Max(29, 74 - index * 15) }));
        PrimaryUsageText = bands.FirstOrDefault()?.UsageText ?? "—";

        var staleBand = snapshots.FirstOrDefault(band => band.IsActive && !band.IsCurrent);
        StateText = staleBand is not null
            ? UiLanguageCatalog.Get(language, "StatusStale")
            : health switch
            {
                AdapterHealth.Available => UiLanguageCatalog.Get(language, "StatusAvailable"),
                AdapterHealth.Degraded => UiLanguageCatalog.Get(language, "StatusDegraded"),
                AdapterHealth.Unavailable => UiLanguageCatalog.Get(language, "StatusWaiting"),
                _ => UiLanguageCatalog.Get(language, "StatusNoData")
            };
        var readings = snapshots.Length == 0
            ? UiLanguageCatalog.Get(language, "NoQuotaObserved")
            : string.Join(" · ", bands.Select(item => $"{item.ShortLabel} {item.UsageText}, {item.ResetText}"));
        // The reason leads: it is what explains a percentage that has stopped moving.
        var reason = UiLanguageCatalog.HealthDetail(language, healthDetail);
        DetailText = reason is null ? readings : $"{reason} · {readings}";
        OnPropertyChanged(nameof(BandCount));
        OnPropertyChanged(nameof(VerticalWidth));
        OnPropertyChanged(nameof(AccessibleName));
    }

    private static WidgetQuotaBandViewModel CreateBand(
        QuotaBandSnapshot band,
        string language,
        MediaBrush signalBrush,
        double ringDiameter) => new(
        ShortLabel(band),
        band.IsActive ? $"{band.UsedPercentage:0.#}%" : "0%",
        band.IsActive ? Math.Clamp((double)band.UsedPercentage, 0, 100) : 0,
        ResetLabel(band, language),
        ringDiameter,
        // A reading we cannot confirm as current is dimmed, never emptied. An empty bar
        // next to a high percentage reads as "no usage left to worry about", which is the
        // opposite of what the last known value says.
        band.IsCurrent ? signalBrush : Dim(signalBrush));

    private static MediaBrush Dim(MediaBrush brush)
    {
        if (brush is not SolidColorBrush solid) return brush;

        var color = solid.Color;
        var dimmed = new SolidColorBrush(MediaColor.FromArgb((byte)(color.A * 0.45), color.R, color.G, color.B));
        dimmed.Freeze();
        return dimmed;
    }

    private static string ShortLabel(QuotaBandSnapshot band)
    {
        switch (QuotaSourceMetadata.Variant(band.Observation.Source))
        {
            case "apps": return "W·A";
            case "sonnet": return "W·S";
            case "opus": return "W·O";
        }

        return band.Period switch
        {
            QuotaPeriod.FiveHours => "5H",
            QuotaPeriod.Daily => "D",
            QuotaPeriod.Weekly => "W",
            QuotaPeriod.Monthly => "M",
            QuotaPeriod.Custom when band.Duration is { TotalDays: >= 1 } duration => $"{duration.TotalDays:0}D",
            QuotaPeriod.Custom when band.Duration is { } duration => $"{duration.TotalHours:0}H",
            _ => "Q"
        };
    }

    private static string ResetLabel(QuotaBandSnapshot band, string language) => band switch
    {
        { IsActive: false } => UiLanguageCatalog.Get(language, "StartsOnUseShort"),
        { ResetsAtUtc: { } reset } => string.Format(
            UiLanguageCatalog.Get(language, "ResetShort"),
            reset.ToLocalTime().ToString(
                "ddd HH:mm",
                CultureInfo.GetCultureInfo(language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en-US" : "es-ES"))),
        _ => UiLanguageCatalog.Get(language, "ResetNotPublished")
    };

    private static MediaBrush BandBrush(MediaBrush baseBrush, int index, int count)
    {
        if (baseBrush is not SolidColorBrush solid || count <= 1 || index == 0) return baseBrush;

        // Keep every band in the provider's hue, but make secondary windows
        // deliberately quieter so adjacent bars/rings never read as one metric.
        var fade = Math.Max(0.38, 0.66 - (index - 1) * 0.14);
        var lift = 0.12 + (index - 1) * 0.04;
        var color = solid.Color;
        byte Mix(byte channel) => (byte)Math.Clamp(channel + (255 - channel) * lift, 0, 255);
        var brush = new SolidColorBrush(MediaColor.FromArgb((byte)(255 * fade), Mix(color.R), Mix(color.G), Mix(color.B)));
        brush.Freeze();
        return brush;
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class WidgetViewModel : INotifyPropertyChanged
{
    private readonly UsageMonitorService _monitor;
    private readonly DispatcherTimer _freshnessTimer;
    private WidgetVisualMode _mode;
    private string _interactionText = string.Empty;
    private string _interactionGlyph = "\uE785";

    public WidgetViewModel(UsageMonitorService monitor, WidgetPreferences preferences)
    {
        _monitor = monitor;
        _freshnessTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        _freshnessTimer.Tick += OnFreshnessTick;
        _freshnessTimer.Start();
        Connections = new ObservableCollection<WidgetConnectionViewModel>(
            preferences.PinnedProviderIds!.Select(provider => new WidgetConnectionViewModel(provider)));
        ApplyPreferences(preferences);
        _monitor.StateChanged += OnStateChanged;
        ApplyState(_monitor.State);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LayoutChanged;

    public ObservableCollection<WidgetConnectionViewModel> Connections { get; }
    public WidgetVisualMode Mode { get => _mode; private set => Set(ref _mode, value); }
    public string InteractionText { get => _interactionText; private set => Set(ref _interactionText, value); }
    public string InteractionGlyph { get => _interactionGlyph; private set => Set(ref _interactionGlyph, value); }
    public Visibility RingsVisibility => Mode == WidgetVisualMode.Rings ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HorizontalVisibility => Mode == WidgetVisualMode.HorizontalBars ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VerticalVisibility => Mode == WidgetVisualMode.VerticalBars ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FullHeaderVisibility => Mode == WidgetVisualMode.VerticalBars ? Visibility.Collapsed : Visibility.Visible;
    public int TotalBandCount => Connections.Sum(item => Math.Max(1, item.BandCount));
    public double VerticalContentWidth => Connections.Sum(item => item.VerticalWidth + 6);

    public void ApplyPreferences(WidgetPreferences preferences)
    {
        var providers = preferences.PinnedProviderIds!;
        if (!Connections.Select(connection => connection.ProviderId).SequenceEqual(providers, StringComparer.OrdinalIgnoreCase))
        {
            Connections.Clear();
            foreach (var provider in providers) Connections.Add(new WidgetConnectionViewModel(provider));
            ApplyState(_monitor.State);
        }

        Mode = preferences.Mode;
        var language = _monitor.State.Preferences.Language;
        InteractionText = UiLanguageCatalog.Get(language, preferences.IsClickThrough
            ? "WidgetClickThrough"
            : preferences.IsLocked ? "WidgetLocked" : "WidgetUnlocked");
        InteractionGlyph = preferences.IsClickThrough
            ? "\uE718"
            : preferences.IsLocked ? "\uE72E" : "\uE785";
        OnPropertyChanged(nameof(RingsVisibility));
        OnPropertyChanged(nameof(HorizontalVisibility));
        OnPropertyChanged(nameof(VerticalVisibility));
        OnPropertyChanged(nameof(FullHeaderVisibility));
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _freshnessTimer.Stop();
        _freshnessTimer.Tick -= OnFreshnessTick;
        _monitor.StateChanged -= OnStateChanged;
    }

    private void OnFreshnessTick(object? sender, EventArgs eventArgs) => ApplyState(_monitor.State);
    private void OnStateChanged(object? sender, UsageMonitorState state) =>
        System.Windows.Application.Current.Dispatcher.Invoke(() => ApplyState(state));

    private void ApplyState(UsageMonitorState state)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var connection in Connections)
        {
            var observations = state.LatestQuotaByProvider.TryGetValue(connection.ProviderId, out var latest) ? latest : [];
            state.AdapterHealth.TryGetValue(connection.ProviderId, out var health);
            state.AdapterHealthDetail.TryGetValue(connection.ProviderId, out var healthDetail);
            connection.Apply(
                observations,
                state.AdapterHealth.ContainsKey(connection.ProviderId) ? health : null,
                healthDetail,
                now,
                state.Preferences.Language);
        }
        OnPropertyChanged(nameof(TotalBandCount));
        OnPropertyChanged(nameof(VerticalContentWidth));
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
