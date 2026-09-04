using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AIVitals.AgentActivity;
using AIVitals.Application;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace AIVitals.App;

/// <summary>One traffic light: an agent session, or a provider that has not reported anything yet.</summary>
public sealed class ActivityTileViewModel : INotifyPropertyChanged
{
    private TrafficLightColor _color = TrafficLightColor.Unknown;
    private DateTimeOffset? _turnSince;
    private bool _turnSinceIsExact;
    private string _elapsedText = string.Empty;
    private string _displayLabel = string.Empty;
    private string _tooltipText = string.Empty;
    private string _accessibleName = string.Empty;
    private string _toolCountText = string.Empty;
    private Visibility _toolCountVisibility = Visibility.Collapsed;

    public ActivityTileViewModel(string key, AgentActivityProvider provider)
    {
        Key = key;
        Provider = provider;
        var isClaude = provider == AgentActivityProvider.ClaudeCode;
        LogoSource = new Uri(isClaude
            ? "pack://application:,,,/AIVitals.App;component/Assets/Providers/claude-ai.svg"
            : "pack://application:,,,/AIVitals.App;component/Assets/Providers/codex-dark.svg",
            UriKind.Absolute);
        LogoBrush = Resource(isClaude ? "ClaudeBrandBrush" : "CodexBrush", isClaude ? "#FF9B2F" : "#3D8BFF");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }
    public AgentActivityProvider Provider { get; }
    public Uri LogoSource { get; }
    public MediaBrush LogoBrush { get; }

    public double RedOpacity => _color == TrafficLightColor.Red ? 1 : 0.16;
    public double YellowOpacity => _color == TrafficLightColor.Yellow ? 1 : 0.16;
    public double GreenOpacity => _color == TrafficLightColor.Green ? 1 : 0.16;
    public string DisplayLabel { get => _displayLabel; private set => Set(ref _displayLabel, value); }
    public string TooltipText { get => _tooltipText; private set => Set(ref _tooltipText, value); }
    public string AccessibleName { get => _accessibleName; private set => Set(ref _accessibleName, value); }
    public string ToolCountText { get => _toolCountText; private set => Set(ref _toolCountText, value); }
    public string ElapsedText { get => _elapsedText; private set => Set(ref _elapsedText, value); }

    /// <summary>
    /// A turn is only being timed when it is running and we saw it start. A stale session keeps its
    /// last known start, but showing a ticking clock for an agent we lost contact with would lie.
    /// </summary>
    public bool HasRunningTurn => _turnSince is not null
                                  && _color is TrafficLightColor.Red or TrafficLightColor.Yellow;
    public Visibility ToolCountVisibility { get => _toolCountVisibility; private set => Set(ref _toolCountVisibility, value); }

    public void Apply(
        AgentActivitySessionSnapshot? session,
        string language,
        bool showSessionLabels,
        bool disambiguate)
    {
        _color = session?.Color ?? TrafficLightColor.Unknown;
        _turnSince = session?.TurnStartedAt ?? session?.TurnObservedFrom;
        _turnSinceIsExact = session?.TurnStartedAt is not null;
        var providerName = Provider == AgentActivityProvider.ClaudeCode ? "CLAUDE" : "CODEX";
        var workspace = showSessionLabels ? session?.WorkspaceLabel : null;

        if (!string.IsNullOrWhiteSpace(workspace))
        {
            // The label is shown in full: MarqueeText slides it when it does not fit the tile.
            DisplayLabel = workspace;
            TooltipText = $"{providerName} · {workspace} · {StatusFor(language, _color)}";
        }
        else if (disambiguate && session is not null)
        {
            var shortKey = session.SessionKey[..Math.Min(4, session.SessionKey.Length)];
            DisplayLabel = $"{providerName}·{shortKey}";
            TooltipText = $"{providerName} · {StatusFor(language, _color)}";
        }
        else
        {
            DisplayLabel = providerName;
            TooltipText = $"{providerName} · {StatusFor(language, _color)}";
        }

        // A red light already means one tool is running, so the badge starts at two: it exists to
        // report parallel tools, not to repeat the color.
        var toolCount = session?.ActiveToolCount ?? 0;
        var parallel = toolCount > 1;
        ToolCountText = parallel ? toolCount.ToString() : string.Empty;
        ToolCountVisibility = parallel ? Visibility.Visible : Visibility.Collapsed;
        AccessibleName = parallel
            ? $"{DisplayLabel}, {StatusFor(language, _color)}, {string.Format(Text(language, "ActivityTools"), toolCount)}"
            : $"{DisplayLabel}, {StatusFor(language, _color)}";

        OnPropertyChanged(nameof(RedOpacity));
        OnPropertyChanged(nameof(YellowOpacity));
        OnPropertyChanged(nameof(GreenOpacity));
        OnPropertyChanged(nameof(HasRunningTurn));
    }

    public void Tick(DateTimeOffset now)
    {
        if (!HasRunningTurn)
        {
            ElapsedText = string.Empty;
            return;
        }

        var elapsed = now - _turnSince!.Value;
        // A hook timestamp can sit a hair ahead of the app's own clock; clamp instead of showing
        // a negative age.
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        var clock = elapsed < TimeSpan.FromHours(1)
            ? $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}"
            : $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        // A turn already under way when AI Vitals started has no known beginning, so the reading is
        // a lower bound and says so rather than passing for an exact duration.
        ElapsedText = _turnSinceIsExact ? clock : "~" + clock;
    }

    public static string StatusFor(string language, TrafficLightColor color) => Text(language, color switch
    {
        TrafficLightColor.Red => "ActivityToolRunning",
        TrafficLightColor.Yellow => "ActivityThinking",
        TrafficLightColor.Green => "ActivityIdle",
        _ => "ActivityUnknown"
    });

    private static string Text(string language, string key) => UiLanguageCatalog.Get(language, key);

    private static MediaBrush Resource(string resourceKey, string fallback) =>
        System.Windows.Application.Current?.Resources[resourceKey] as MediaBrush
        ?? new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(fallback));

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ActivityWidgetViewModel : INotifyPropertyChanged, IDisposable
{
    /// <summary>Fixed by design: a slower rotation hides sessions, a faster one is unreadable.</summary>
    public static readonly TimeSpan CarouselInterval = TimeSpan.FromSeconds(5);

    /// <summary>Bursts of parallel tool events would otherwise repaint the widget dozens of times a second.</summary>
    private static readonly TimeSpan CoalesceInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>One shared clock drives every turn timer, and only while a turn is actually running.</summary>
    private static readonly TimeSpan ClockInterval = TimeSpan.FromSeconds(1);

    private readonly IAgentActivitySource _source;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _coalesceTimer;
    private readonly DispatcherTimer _carouselTimer;
    private readonly DispatcherTimer _clockTimer;
    private readonly Dictionary<string, ActivityTileViewModel> _tiles = [];
    private IReadOnlyList<ActivityTileViewModel> _orderedTiles = [];
    private AgentActivitySnapshot _snapshot;
    private AgentActivitySnapshot? _pendingSnapshot;
    private ActivityWidgetPreferences _preferences;
    private string _language;
    private int _page;
    private bool _isPaused;
    private bool _isActive;

    public ActivityWidgetViewModel(
        IAgentActivitySource source,
        ActivityWidgetPreferences preferences,
        string language,
        Dispatcher dispatcher)
    {
        _source = source;
        _dispatcher = dispatcher;
        _preferences = ActivityWidgetPreferenceRules.Normalize(preferences);
        _language = language;
        _snapshot = source.Current;
        _coalesceTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = CoalesceInterval };
        _coalesceTimer.Tick += OnCoalesceTick;
        _carouselTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = CarouselInterval };
        _carouselTimer.Tick += OnCarouselTick;
        _clockTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = ClockInterval };
        _clockTimer.Tick += OnClockTick;
        _source.SnapshotChanged += OnSnapshotChanged;
        Rebuild();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the tile count changes the window size the widget needs.</summary>
    public event EventHandler? LayoutChanged;

    /// <summary>Raised when the carousel turns a page, so the view can cross-fade.</summary>
    public event EventHandler? PageChanged;

    public ObservableCollection<ActivityTileViewModel> TopRow { get; } = [];
    public ObservableCollection<ActivityTileViewModel> BottomRow { get; } = [];
    public ObservableCollection<bool> PageDots { get; } = [];

    public int TileCount => _orderedTiles.Count;
    public double TileWidth => ActivityWidgetGeometry.TileWidth;
    public double TileHeight => ActivityWidgetGeometry.TileHeight;
    public Visibility BottomRowVisibility => BottomRow.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CarouselVisibility => PageCount > 1 ? Visibility.Visible : Visibility.Collapsed;
    public int PageCount => Math.Max(1, (int)Math.Ceiling(_orderedTiles.Count / (double)ActivityWidgetGeometry.MaximumVisibleTiles));

    public string StatusText => ActivityTileViewModel.StatusFor(_language, _snapshot.Color);
    public string InteractionText => Text(_preferences.IsClickThrough
        ? "WidgetClickThrough"
        : _preferences.IsLocked ? "WidgetLocked" : "WidgetUnlocked");

    public string InteractionGlyph => _preferences.IsClickThrough
        ? ""
        : _preferences.IsLocked ? "" : "";

    public string PageText => PageCount > 1
        ? string.Format(Text("ActivityPage"), _page + 1, PageCount)
        : string.Empty;

    public string AccessibleStatus
    {
        get
        {
            var tiles = string.Join(". ", _orderedTiles.Select(tile => tile.AccessibleName));
            return PageCount > 1 ? $"{StatusText}. {PageText}. {tiles}" : $"{StatusText}. {tiles}";
        }
    }

    /// <summary>Suspends the carousel while the widget is hidden, so nothing ticks off-screen.</summary>
    public void SetActive(bool isActive)
    {
        _isActive = isActive;
        UpdateCarouselTimer();
        UpdateClockTimer();
    }

    /// <summary>Holds the current page while the pointer rests on the widget.</summary>
    public void SetPaused(bool isPaused)
    {
        _isPaused = isPaused;
        UpdateCarouselTimer();
    }

    public void ApplyPreferences(ActivityWidgetPreferences preferences, string language)
    {
        _preferences = ActivityWidgetPreferenceRules.Normalize(preferences);
        _language = language;
        Rebuild();
    }

    public void Dispose()
    {
        _source.SnapshotChanged -= OnSnapshotChanged;
        _coalesceTimer.Stop();
        _coalesceTimer.Tick -= OnCoalesceTick;
        _carouselTimer.Stop();
        _carouselTimer.Tick -= OnCarouselTick;
        _clockTimer.Stop();
        _clockTimer.Tick -= OnClockTick;
    }

    private void OnSnapshotChanged(AgentActivitySnapshot snapshot)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => OnSnapshotChanged(snapshot));
            return;
        }

        _pendingSnapshot = snapshot;
        if (!_coalesceTimer.IsEnabled) _coalesceTimer.Start();
    }

    private void OnCoalesceTick(object? sender, EventArgs eventArgs)
    {
        _coalesceTimer.Stop();
        if (_pendingSnapshot is null) return;
        _snapshot = _pendingSnapshot;
        _pendingSnapshot = null;
        Rebuild();
    }

    private void OnClockTick(object? sender, EventArgs eventArgs) => TickTimers();

    private void TickTimers()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var tile in _orderedTiles) tile.Tick(now);
    }

    private void UpdateClockTimer()
    {
        var shouldRun = _isActive && _orderedTiles.Any(tile => tile.HasRunningTurn);
        if (shouldRun && !_clockTimer.IsEnabled) _clockTimer.Start();
        else if (!shouldRun && _clockTimer.IsEnabled) _clockTimer.Stop();
    }

    private void OnCarouselTick(object? sender, EventArgs eventArgs)
    {
        if (PageCount <= 1) return;
        _page = (_page + 1) % PageCount;
        RenderPage();
        PageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Rebuild()
    {
        var previousCount = _orderedTiles.Count;
        var providers = ToProviders(_preferences.IncludedProviderIds!).ToArray();
        var sessions = _snapshot.Sessions
            .Where(session => providers.Contains(session.Provider))
            .ToArray();

        var ordered = new List<ActivityTileViewModel>();
        var live = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            var providerSessions = sessions.Where(session => session.Provider == provider).ToArray();
            if (providerSessions.Length == 0)
            {
                // A provider the user enabled but that has not reported yet still deserves a light.
                ordered.Add(Tile($"{provider}:", provider, null, disambiguate: false));
                live.Add($"{provider}:");
                continue;
            }

            foreach (var session in providerSessions)
            {
                var key = $"{provider}:{session.SessionKey}";
                ordered.Add(Tile(key, provider, session, providerSessions.Length > 1));
                live.Add(key);
            }
        }

        foreach (var stale in _tiles.Keys.Where(key => !live.Contains(key)).ToArray()) _tiles.Remove(stale);
        _orderedTiles = ordered;

        if (_page >= PageCount) _page = 0;
        // Tick before rendering so a tile never appears with a blank or stale duration.
        TickTimers();
        RenderPage();
        UpdateCarouselTimer();
        UpdateClockTimer();

        OnPropertyChanged(nameof(TileCount));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(InteractionText));
        OnPropertyChanged(nameof(InteractionGlyph));
        OnPropertyChanged(nameof(AccessibleStatus));
        if (previousCount != ordered.Count) LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private ActivityTileViewModel Tile(
        string key,
        AgentActivityProvider provider,
        AgentActivitySessionSnapshot? session,
        bool disambiguate)
    {
        if (!_tiles.TryGetValue(key, out var tile))
        {
            tile = new ActivityTileViewModel(key, provider);
            _tiles.Add(key, tile);
        }

        tile.Apply(session, _language, _preferences.ShowSessionLabels, disambiguate);
        return tile;
    }

    private void RenderPage()
    {
        var page = _orderedTiles
            .Skip(_page * ActivityWidgetGeometry.MaximumVisibleTiles)
            .Take(ActivityWidgetGeometry.MaximumVisibleTiles)
            .ToArray();
        var topCount = Math.Min(2, page.Length);

        Replace(TopRow, page.Take(topCount));
        Replace(BottomRow, page.Skip(topCount));

        PageDots.Clear();
        if (PageCount > 1)
            for (var index = 0; index < PageCount; index++) PageDots.Add(index == _page);

        OnPropertyChanged(nameof(BottomRowVisibility));
        OnPropertyChanged(nameof(CarouselVisibility));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(AccessibleStatus));
    }

    private void UpdateCarouselTimer()
    {
        var shouldRun = _isActive && !_isPaused && PageCount > 1;
        if (shouldRun && !_carouselTimer.IsEnabled) _carouselTimer.Start();
        else if (!shouldRun && _carouselTimer.IsEnabled) _carouselTimer.Stop();
    }

    private static void Replace(ObservableCollection<ActivityTileViewModel> target, IEnumerable<ActivityTileViewModel> tiles)
    {
        var items = tiles.ToArray();
        if (target.SequenceEqual(items)) return;
        target.Clear();
        foreach (var tile in items) target.Add(tile);
    }

    private static IEnumerable<AgentActivityProvider> ToProviders(IEnumerable<string> providerIds) =>
        providerIds.Select(providerId => providerId.Equals("claude-code", StringComparison.OrdinalIgnoreCase)
            ? AgentActivityProvider.ClaudeCode
            : AgentActivityProvider.Codex);

    private string Text(string key) => UiLanguageCatalog.Get(_language, key);

    private static MediaBrush Resource(string resourceKey) =>
        System.Windows.Application.Current?.Resources[resourceKey] as MediaBrush ?? System.Windows.Media.Brushes.Gray;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
