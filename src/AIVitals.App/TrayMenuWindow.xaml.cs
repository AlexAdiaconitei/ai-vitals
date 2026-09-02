using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AIVitals.AgentActivity;
using AIVitals.Application;
using Forms = System.Windows.Forms;
using InputKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfButton = System.Windows.Controls.Button;

namespace AIVitals.App;

public partial class TrayMenuWindow : Window
{
    private readonly Action _openDashboard;
    private readonly Action _openSettings;
    private readonly Func<Task> _toggleWidget;
    private readonly Func<WidgetVisualMode, Task> _setMode;
    private readonly Func<Task> _toggleLock;
    private readonly Func<Task> _toggleClickThrough;
    private readonly Func<Task> _recover;
    private readonly Func<Task> _moveHere;
    private readonly Func<Task> _toggleActivityWidget;
    private readonly Func<Task> _toggleActivityLock;
    private readonly Func<Task> _toggleActivityClickThrough;
    private readonly Func<Task> _recoverActivity;
    private readonly Func<AgentActivitySnapshot?> _activitySnapshot;
    private readonly Func<string, Task> _setTheme;
    private readonly Func<Task> _applyUpdate;
    private readonly Func<Task> _exit;
    private WidgetPreferences _widget = new();
    private ActivityWidgetPreferences _activityWidget = new();
    private bool _activityAvailable;
    private bool _activityTabSelected;
    private string _theme = "System";
    private AppUpdateStatus? _pendingUpdate;

    private const double CollapsedHeight = 458;
    private const double HeightWithUpdateBanner = 552;

    /// <summary>The tab strip only exists once a second widget does, so its height is conditional too.</summary>
    private const double TabStripHeight = 44;

    public TrayMenuWindow(
        Action openDashboard,
        Action openSettings,
        Func<Task> toggleWidget,
        Func<WidgetVisualMode, Task> setMode,
        Func<Task> toggleLock,
        Func<Task> toggleClickThrough,
        Func<Task> recover,
        Func<Task> moveHere,
        Func<Task> toggleActivityWidget,
        Func<Task> toggleActivityLock,
        Func<Task> toggleActivityClickThrough,
        Func<Task> recoverActivity,
        Func<AgentActivitySnapshot?> activitySnapshot,
        Func<string, Task> setTheme,
        Func<Task> applyUpdate,
        Func<Task> exit)
    {
        _openDashboard = openDashboard;
        _openSettings = openSettings;
        _toggleWidget = toggleWidget;
        _setMode = setMode;
        _toggleLock = toggleLock;
        _toggleClickThrough = toggleClickThrough;
        _recover = recover;
        _moveHere = moveHere;
        _toggleActivityWidget = toggleActivityWidget;
        _toggleActivityLock = toggleActivityLock;
        _toggleActivityClickThrough = toggleActivityClickThrough;
        _recoverActivity = recoverActivity;
        _activitySnapshot = activitySnapshot;
        _setTheme = setTheme;
        _applyUpdate = applyUpdate;
        _exit = exit;
        InitializeComponent();
        Deactivated += (_, _) => Hide();
    }

    /// <param name="activityAvailable">
    /// Whether the traffic light is switched on in settings. Its tab is hidden otherwise, because
    /// with no provider integration enabled the widget has nothing to report.
    /// </param>
    public void UpdateState(
        WidgetPreferences widget,
        string theme,
        ActivityWidgetPreferences activityWidget,
        bool activityAvailable)
    {
        _widget = WidgetPreferenceRules.Normalize(widget);
        _activityWidget = ActivityWidgetPreferenceRules.Normalize(activityWidget);
        _activityAvailable = activityAvailable;
        if (!activityAvailable) _activityTabSelected = false;
        _theme = theme;
        ApplySelectionStates();
        // The banner text is formatted, not bound, so a language change has to reformat it here.
        ApplyPendingUpdate();
    }

    /// <summary>
    /// The tray only advertises an update once one is downloaded and waiting, so this entry never
    /// competes with the "Refresh" action that reloads provider data.
    /// </summary>
    public void UpdatePendingUpdate(AppUpdateStatus? status)
    {
        _pendingUpdate = status;
        ApplyPendingUpdate();
    }

    private void ApplyPendingUpdate()
    {
        var pending = _pendingUpdate is { IsPending: true };
        UpdateBanner.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        Height = (pending ? HeightWithUpdateBanner : CollapsedHeight)
                 + (_activityAvailable ? TabStripHeight : 0);
        if (!pending) return;

        var format = System.Windows.Application.Current?.TryFindResource("UpdateReadyBanner") as string;
        UpdateBannerText.Text = string.Format(format ?? "{0}", _pendingUpdate!.AvailableVersion);
    }

    public void ShowNearCursor()
    {
        ApplySelectionStates();
        ApplyPendingUpdate();
        if (!IsVisible) Show();
        var dpi = VisualTreeHelper.GetDpi(this);
        var cursor = Forms.Cursor.Position;
        var area = Forms.Screen.FromPoint(cursor).WorkingArea;
        Left = Math.Clamp(cursor.X / dpi.DpiScaleX - Width + 24, area.Left / dpi.DpiScaleX, area.Right / dpi.DpiScaleX - Width);
        Top = Math.Clamp(cursor.Y / dpi.DpiScaleY - Height + 20, area.Top / dpi.DpiScaleY, area.Bottom / dpi.DpiScaleY - Height);
        Activate();
        Focus();
    }

    private void ApplySelectionStates()
    {
        ApplyTabSelection();
        ApplyActivityState();
        WidgetVisibilityGlyph.Kind = _widget.IsVisible ? WidgetGlyphKind.Visible : WidgetGlyphKind.Hidden;
        WidgetLockGlyph.Kind = _widget.IsLocked ? WidgetGlyphKind.Locked : WidgetGlyphKind.Unlocked;
        WidgetClickThroughGlyph.Kind = _widget.IsClickThrough ? WidgetGlyphKind.ClickThroughOn : WidgetGlyphKind.ClickThroughOff;
        Select(ToggleWidgetButton, _widget.IsVisible, "SignalBrush");
        Select(LockButton, _widget.IsLocked, "SignalBrush");
        Select(ClickThroughButton, _widget.IsClickThrough, "SignalBrush");
        Select(RingsButton, _widget.Mode == WidgetVisualMode.Rings, "CodexBrush");
        Select(HorizontalButton, _widget.Mode == WidgetVisualMode.HorizontalBars, "CodexBrush");
        Select(VerticalButton, _widget.Mode == WidgetVisualMode.VerticalBars, "CodexBrush");
        Select(LightButton, _theme.Equals("Light", StringComparison.OrdinalIgnoreCase), "WarmBrush");
        Select(DarkButton, _theme.Equals("Dark", StringComparison.OrdinalIgnoreCase), "WarmBrush");
        Select(SystemButton, _theme.Equals("System", StringComparison.OrdinalIgnoreCase), "WarmBrush");
    }

    private void ApplyTabSelection()
    {
        WidgetTabStrip.Visibility = _activityAvailable ? Visibility.Visible : Visibility.Collapsed;
        var activity = _activityAvailable && _activityTabSelected;
        UsageTabPanel.Visibility = activity ? Visibility.Collapsed : Visibility.Visible;
        ActivityTabPanel.Visibility = activity ? Visibility.Visible : Visibility.Collapsed;
        Select(UsageTabButton, !activity, "SignalBrush");
        Select(ActivityTabButton, activity, "SignalBrush");
    }

    private void ApplyActivityState()
    {
        ActivityVisibilityGlyph.Kind = _activityWidget.IsVisible ? WidgetGlyphKind.Visible : WidgetGlyphKind.Hidden;
        ActivityLockGlyph.Kind = _activityWidget.IsLocked ? WidgetGlyphKind.Locked : WidgetGlyphKind.Unlocked;
        ActivityClickThroughGlyph.Kind = _activityWidget.IsClickThrough
            ? WidgetGlyphKind.ClickThroughOn
            : WidgetGlyphKind.ClickThroughOff;
        Select(ToggleActivityButton, _activityWidget.IsVisible, "SignalBrush");
        Select(ActivityLockButton, _activityWidget.IsLocked, "SignalBrush");
        Select(ActivityClickThroughButton, _activityWidget.IsClickThrough, "SignalBrush");

        var snapshot = _activitySnapshot();
        var color = snapshot?.Color ?? TrafficLightColor.Unknown;
        ActivityStateDot.SetResourceReference(Shape.FillProperty, color switch
        {
            TrafficLightColor.Red => "ActivityRedBrush",
            TrafficLightColor.Yellow => "ActivityYellowBrush",
            TrafficLightColor.Green => "ActivityGreenBrush",
            _ => "ActivityGreyBrush"
        });
        ActivityStateText.Text = Text(color switch
        {
            TrafficLightColor.Red => "ActivityToolRunning",
            TrafficLightColor.Yellow => "ActivityThinking",
            TrafficLightColor.Green => "ActivityIdle",
            _ => "ActivityUnknown"
        });

        var sessions = snapshot?.Sessions.Count ?? 0;
        ActivitySessionText.Text = sessions switch
        {
            0 => string.Empty,
            1 => Text("TrayActivitySession"),
            _ => string.Format(Text("TrayActivitySessions"), sessions)
        };
    }

    private static string Text(string key) =>
        System.Windows.Application.Current?.TryFindResource(key) as string ?? string.Empty;

    private static void Select(WpfButton button, bool selected, string accent)
    {
        button.SetResourceReference(BackgroundProperty, selected ? "SelectionBrush" : "WidgetSurfaceBrush");
        button.SetResourceReference(BorderBrushProperty, selected ? accent : "LineBrush");
    }

    private void OnOpenDashboard(object sender, RoutedEventArgs e) { Hide(); _openDashboard(); }
    private void OnSettings(object sender, RoutedEventArgs e) { Hide(); _openSettings(); }
    private async void OnToggleWidget(object sender, RoutedEventArgs e) { await _toggleWidget(); }
    private async void OnToggleLock(object sender, RoutedEventArgs e) { await _toggleLock(); }
    private async void OnToggleClickThrough(object sender, RoutedEventArgs e) { await _toggleClickThrough(); }
    private async void OnRecover(object sender, RoutedEventArgs e) { await _recover(); }
    private async void OnMoveHere(object sender, RoutedEventArgs e) { await _moveHere(); }
    private async void OnToggleActivityWidget(object sender, RoutedEventArgs e) { await _toggleActivityWidget(); }
    private async void OnToggleActivityLock(object sender, RoutedEventArgs e) { await _toggleActivityLock(); }
    private async void OnToggleActivityClickThrough(object sender, RoutedEventArgs e) { await _toggleActivityClickThrough(); }
    private async void OnRecoverActivity(object sender, RoutedEventArgs e) { await _recoverActivity(); }
    private void OnUsageTab(object sender, RoutedEventArgs e) { _activityTabSelected = false; ApplySelectionStates(); }
    private void OnActivityTab(object sender, RoutedEventArgs e) { _activityTabSelected = true; ApplySelectionStates(); }
    private async void OnRings(object sender, RoutedEventArgs e) { await _setMode(WidgetVisualMode.Rings); }
    private async void OnHorizontal(object sender, RoutedEventArgs e) { await _setMode(WidgetVisualMode.HorizontalBars); }
    private async void OnVertical(object sender, RoutedEventArgs e) { await _setMode(WidgetVisualMode.VerticalBars); }
    private async void OnLight(object sender, RoutedEventArgs e) { await _setTheme("Light"); }
    private async void OnDark(object sender, RoutedEventArgs e) { await _setTheme("Dark"); }
    private async void OnSystem(object sender, RoutedEventArgs e) { await _setTheme("System"); }
    private async void OnApplyUpdate(object sender, RoutedEventArgs e) { Hide(); await _applyUpdate(); }
    private async void OnExit(object sender, RoutedEventArgs e) { Hide(); await _exit(); }

    private void OnPreviewKeyDown(object sender, InputKeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Hide();
        e.Handled = true;
    }
}
