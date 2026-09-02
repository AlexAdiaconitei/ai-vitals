using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AIVitals.AgentActivity;
using AIVitals.Application;
using AIVitals.Platform.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace AIVitals.App;

public partial class ActivityWidgetWindow : Window
{
    private readonly ActivityWidgetViewModel _viewModel;
    private readonly Func<ActivityWidgetPreferences, Task> _savePreferences;
    private readonly Action _showDashboard;
    private ActivityWidgetPreferences _preferences;
    private string _language;
    private IntPtr _windowHandle;
    private bool _placementReady;

    public ActivityWidgetWindow(
        IAgentActivitySource activitySource,
        ActivityWidgetPreferences preferences,
        string language,
        Func<ActivityWidgetPreferences, Task> savePreferences,
        Action showDashboard)
    {
        _preferences = ActivityWidgetPreferenceRules.Normalize(preferences);
        _language = language;
        _savePreferences = savePreferences;
        _showDashboard = showDashboard;
        InitializeComponent();
        _viewModel = new ActivityWidgetViewModel(activitySource, _preferences, language, Dispatcher);
        _viewModel.LayoutChanged += OnLayoutChanged;
        DataContext = _viewModel;
        ConfigureGeometry();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(SnapToVisibleWorkArea);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Closed += (_, _) => SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }

    public ActivityWidgetPreferences Preferences => _preferences;

    public void ApplyPreferences(ActivityWidgetPreferences preferences, string language, bool resetPlacement = false)
    {
        _preferences = ActivityWidgetPreferenceRules.Normalize(preferences);
        _language = language;
        _viewModel.ApplyPreferences(_preferences, language);
        ConfigureGeometry();
        ApplyClickThrough();
        if (_preferences.IsVisible)
        {
            if (!IsVisible) Show();
            _viewModel.SetActive(true);
            Dispatcher.BeginInvoke(() => RestorePlacement(resetPlacement));
        }
        else
        {
            _viewModel.SetActive(false);
            Hide();
        }
    }

    public async Task RecoverAsync()
    {
        var recovered = _preferences with { IsVisible = true, IsLocked = false, IsClickThrough = false };
        ApplyPreferences(recovered, _language);
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var pointer = transform.Transform(new WpfPoint(Forms.Cursor.Position.X, Forms.Cursor.Position.Y));
        var placement = WidgetPlacement.MoveToMonitor(pointer.X, pointer.Y, Width, Height, GetWorkAreas());
        Left = placement.Left;
        Top = placement.Top;
        _preferences = recovered with { Left = Left, Top = Top };
        await _savePreferences(_preferences);
        Activate();
    }

    public void DisposeViewModel()
    {
        _viewModel.LayoutChanged -= OnLayoutChanged;
        _viewModel.Dispose();
    }

    /// <summary>
    /// The widget grows with the number of live sessions, so a new tile can push it off screen.
    /// Resizing is always followed by a snap back into a visible work area.
    /// </summary>
    private void OnLayoutChanged(object? sender, EventArgs eventArgs)
    {
        ConfigureGeometry();
        SnapToVisibleWorkArea();
    }

    private void ConfigureGeometry()
    {
        var layout = ActivityWidgetGeometry.Calculate(_viewModel.TileCount);
        Width = layout.Width;
        Height = layout.Height;
    }

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
            _showDashboard();
            return;
        }

        try
        {
            DragMove();
            SnapToVisibleWorkArea();
            _preferences = _preferences with { Left = Left, Top = Top };
            await _savePreferences(_preferences);
        }
        catch (InvalidOperationException)
        {
        }
    }

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

    private void OnDisplaySettingsChanged(object? sender, EventArgs eventArgs) => Dispatcher.BeginInvoke(async () =>
    {
        SnapToVisibleWorkArea();
        _preferences = _preferences with { Left = Left, Top = Top };
        await _savePreferences(_preferences);
    });

    private IReadOnlyList<WidgetBounds> GetWorkAreas()
    {
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return Forms.Screen.AllScreens
            .Select(screen => TransformBounds(transform, new WpfRect(
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
