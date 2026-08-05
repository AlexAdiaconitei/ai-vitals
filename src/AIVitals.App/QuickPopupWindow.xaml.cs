using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AIVitals.Application;
using Forms = System.Windows.Forms;
using InputKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKey = System.Windows.Input.Key;

namespace AIVitals.App;

public partial class QuickPopupWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Action _showDashboard;
    private readonly Func<Task> _toggleWidget;
    private readonly Func<WidgetVisualMode, Task> _setWidgetMode;
    private readonly Func<Task> _toggleWidgetLock;
    private readonly Func<Task> _toggleClickThrough;
    private readonly Func<Task> _recoverWidget;

    public QuickPopupWindow(
        MainViewModel viewModel,
        Action showDashboard,
        Func<Task> toggleWidget,
        Func<WidgetVisualMode, Task> setWidgetMode,
        Func<Task> toggleWidgetLock,
        Func<Task> toggleClickThrough,
        Func<Task> recoverWidget)
    {
        _viewModel = viewModel;
        _showDashboard = showDashboard;
        _toggleWidget = toggleWidget;
        _setWidgetMode = setWidgetMode;
        _toggleWidgetLock = toggleWidgetLock;
        _toggleClickThrough = toggleClickThrough;
        _recoverWidget = recoverWidget;
        InitializeComponent();
        DataContext = viewModel;
        RefreshWidgetControls();
        Deactivated += (_, _) => Hide();
    }

    public void ShowNearCursor()
    {
        RefreshWidgetControls();
        if (!IsVisible) Show();
        PositionNearCursor();
        Activate();
        Focus();
    }

    private void PositionNearCursor()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var cursor = Forms.Cursor.Position;
        var workArea = Forms.Screen.FromPoint(cursor).WorkingArea;
        var left = cursor.X / dpi.DpiScaleX - Width + 28;
        var top = cursor.Y / dpi.DpiScaleY - Height + 18;
        Left = Math.Clamp(left, workArea.Left / dpi.DpiScaleX, workArea.Right / dpi.DpiScaleX - Width);
        Top = Math.Clamp(top, workArea.Top / dpi.DpiScaleY, workArea.Bottom / dpi.DpiScaleY - Height);
    }

    private void OnClose(object sender, RoutedEventArgs eventArgs) => Hide();

    private void OnOpenDashboard(object sender, RoutedEventArgs eventArgs)
    {
        Hide();
        _showDashboard();
    }

    private async void OnToggleWidget(object sender, RoutedEventArgs eventArgs) { await _toggleWidget(); RefreshWidgetControls(); }
    private async void OnWidgetRings(object sender, RoutedEventArgs eventArgs) { await _setWidgetMode(WidgetVisualMode.Rings); RefreshWidgetControls(); }
    private async void OnWidgetHorizontal(object sender, RoutedEventArgs eventArgs) { await _setWidgetMode(WidgetVisualMode.HorizontalBars); RefreshWidgetControls(); }
    private async void OnWidgetVertical(object sender, RoutedEventArgs eventArgs) { await _setWidgetMode(WidgetVisualMode.VerticalBars); RefreshWidgetControls(); }
    private async void OnToggleWidgetLock(object sender, RoutedEventArgs eventArgs) { await _toggleWidgetLock(); RefreshWidgetControls(); }
    private async void OnToggleClickThrough(object sender, RoutedEventArgs eventArgs) { await _toggleClickThrough(); RefreshWidgetControls(); }
    private async void OnRecoverWidget(object sender, RoutedEventArgs eventArgs) { await _recoverWidget(); RefreshWidgetControls(); }

    private void RefreshWidgetControls()
    {
        var widget = _viewModel.WidgetPreferences;
        WidgetVisibilityGlyph.Kind = widget.IsVisible ? WidgetGlyphKind.Visible : WidgetGlyphKind.Hidden;
        WidgetLockGlyph.Kind = widget.IsLocked ? WidgetGlyphKind.Locked : WidgetGlyphKind.Unlocked;
        WidgetClickThroughGlyph.Kind = widget.IsClickThrough ? WidgetGlyphKind.ClickThroughOn : WidgetGlyphKind.ClickThroughOff;
        Select(ToggleWidgetButton, widget.IsVisible);
        Select(LockWidgetButton, widget.IsLocked);
        Select(ClickThroughButton, widget.IsClickThrough);
        Select(RingsButton, widget.Mode == WidgetVisualMode.Rings);
        Select(HorizontalButton, widget.Mode == WidgetVisualMode.HorizontalBars);
        Select(VerticalButton, widget.Mode == WidgetVisualMode.VerticalBars);
    }

    private static void Select(System.Windows.Controls.Button button, bool selected)
    {
        button.SetResourceReference(BackgroundProperty, selected ? "SelectionBrush" : "WidgetSurfaceBrush");
        button.SetResourceReference(BorderBrushProperty, selected ? "CodexBrush" : "LineBrush");
    }

    private void OnPreviewKeyDown(object sender, InputKeyEventArgs eventArgs)
    {
        if (eventArgs.Key != WpfKey.Escape) return;
        Hide();
        eventArgs.Handled = true;
    }
}
