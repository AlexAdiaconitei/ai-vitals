using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly Func<string, Task> _setTheme;
    private readonly Func<Task> _applyUpdate;
    private readonly Func<Task> _exit;
    private WidgetPreferences _widget = new();
    private string _theme = "System";

    private const double CollapsedHeight = 458;
    private const double HeightWithUpdateBanner = 552;

    public TrayMenuWindow(
        Action openDashboard,
        Action openSettings,
        Func<Task> toggleWidget,
        Func<WidgetVisualMode, Task> setMode,
        Func<Task> toggleLock,
        Func<Task> toggleClickThrough,
        Func<Task> recover,
        Func<Task> moveHere,
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
        _setTheme = setTheme;
        _applyUpdate = applyUpdate;
        _exit = exit;
        InitializeComponent();
        Deactivated += (_, _) => Hide();
    }

    public void UpdateState(WidgetPreferences widget, string theme)
    {
        _widget = WidgetPreferenceRules.Normalize(widget);
        _theme = theme;
        ApplySelectionStates();
    }

    /// <summary>
    /// The tray only advertises an update once one is downloaded and waiting, so this entry never
    /// competes with the "Refresh" action that reloads provider data.
    /// </summary>
    public void UpdatePendingUpdate(AppUpdateStatus? status)
    {
        var pending = status is { IsPending: true };
        UpdateBanner.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        Height = pending ? HeightWithUpdateBanner : CollapsedHeight;
        if (!pending) return;

        var format = System.Windows.Application.Current?.TryFindResource("UpdateReadyBanner") as string;
        UpdateBannerText.Text = string.Format(format ?? "{0}", status!.AvailableVersion);
    }

    public void ShowNearCursor()
    {
        ApplySelectionStates();
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
