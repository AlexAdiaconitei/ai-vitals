using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AIVitals.Application;
using AIVitals.Platform.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace AIVitals.App;

public partial class WidgetWindow : Window
{
    private readonly WidgetViewModel _viewModel;
    private readonly Func<WidgetPreferences, Task> _savePreferences;
    private readonly Action _showQuickView;
    private readonly Action _showDashboard;
    private WidgetPreferences _preferences;
    private IntPtr _windowHandle;
    private bool _placementReady;

    public WidgetWindow(
        UsageMonitorService monitor,
        WidgetPreferences preferences,
        Func<WidgetPreferences, Task> savePreferences,
        Action showQuickView,
        Action showDashboard)
    {
        _preferences = WidgetPreferenceRules.Normalize(preferences);
        _savePreferences = savePreferences;
        _showQuickView = showQuickView;
        _showDashboard = showDashboard;
        _viewModel = new WidgetViewModel(monitor, _preferences);
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.LayoutChanged += OnWidgetLayoutChanged;
        ConfigureGeometry();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(SnapToVisibleWorkArea);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Closed += (_, _) =>
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _viewModel.LayoutChanged -= OnWidgetLayoutChanged;
        };
    }

    public WidgetPreferences Preferences => _preferences;

    public void ApplyPreferences(WidgetPreferences preferences, bool resetPlacement = false)
    {
        _preferences = WidgetPreferenceRules.Normalize(preferences);
        _viewModel.ApplyPreferences(_preferences);
        ConfigureGeometry();
        ApplyClickThrough();

        if (_preferences.IsVisible)
        {
            if (!IsVisible) Show();
            Dispatcher.BeginInvoke(() => RestorePlacement(resetPlacement));
        }
        else
        {
            Hide();
        }
    }

    public Task RecoverAsync() => MoveToCurrentMonitorAsync();

    public async Task MoveToCurrentMonitorAsync()
    {
        var recovered = _preferences with
        {
            IsVisible = true,
            IsLocked = false,
            IsClickThrough = false
        };
        ApplyPreferences(recovered);

        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var pointer = transform.Transform(new WpfPoint(Forms.Cursor.Position.X, Forms.Cursor.Position.Y));
        var placement = WidgetPlacement.MoveToMonitor(pointer.X, pointer.Y, Width, Height, GetWorkAreas());
        Left = placement.Left;
        Top = placement.Top;
        _preferences = recovered with { Left = Left, Top = Top };
        await _savePreferences(_preferences);
        Activate();
    }

    public void DisposeViewModel() => _viewModel.Dispose();

    private void OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        ApplyClickThrough();
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (_placementReady) return;
        RestorePlacement(resetPlacement: false);
        _placementReady = true;
    }

    private async void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left) return;
        if (eventArgs.ClickCount == 2 && !_preferences.IsClickThrough)
        {
            _showDashboard();
            return;
        }
        if (_preferences.IsLocked)
        {
            _showQuickView();
            return;
        }

        try
        {
            var originalLeft = Left;
            var originalTop = Top;
            DragMove();
            SnapToVisibleWorkArea();
            _preferences = _preferences with { Left = Left, Top = Top };
            await _savePreferences(_preferences);
            if (Math.Abs(Left - originalLeft) < 2 && Math.Abs(Top - originalTop) < 2)
                _showQuickView();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ConfigureGeometry()
    {
        (Width, Height) = WidgetGeometry.Calculate(_viewModel, _preferences);
    }

    private void OnWidgetLayoutChanged(object? sender, EventArgs eventArgs) =>
        Dispatcher.BeginInvoke(() =>
        {
            ConfigureGeometry();
            SnapToVisibleWorkArea();
        });

    private void RestorePlacement(bool resetPlacement)
    {
        var placement = WidgetPlacement.Normalize(
            resetPlacement ? null : _preferences.Left,
            resetPlacement ? null : _preferences.Top,
            Width,
            Height,
            GetWorkAreas());
        Left = placement.Left;
        Top = placement.Top;
    }

    private void SnapToVisibleWorkArea()
    {
        if (!IsLoaded) return;
        var placement = WidgetPlacement.Normalize(Left, Top, Width, Height, GetWorkAreas());
        Left = placement.Left;
        Top = placement.Top;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs eventArgs) =>
        Dispatcher.BeginInvoke(async () =>
        {
            SnapToVisibleWorkArea();
            _preferences = _preferences with { Left = Left, Top = Top };
            await _savePreferences(_preferences);
        });

    private IReadOnlyList<WidgetBounds> GetWorkAreas()
    {
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return Forms.Screen.AllScreens
            .Select(screen => TransformBounds(
                transform,
                new WpfRect(
                    screen.WorkingArea.Left,
                    screen.WorkingArea.Top,
                    screen.WorkingArea.Width,
                    screen.WorkingArea.Height)))
            .Select(bounds => new WidgetBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height))
            .ToArray();
    }

    private void ApplyClickThrough() => WindowInteraction.SetClickThrough(_windowHandle, _preferences.IsClickThrough);

    private static WpfRect TransformBounds(Matrix transform, WpfRect bounds)
    {
        var topLeft = transform.Transform(new WpfPoint(bounds.Left, bounds.Top));
        var bottomRight = transform.Transform(new WpfPoint(bounds.Right, bounds.Bottom));
        return new WpfRect(topLeft, bottomRight);
    }
}
