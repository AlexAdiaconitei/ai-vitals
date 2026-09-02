using System.Windows;
using System.Windows.Media.Animation;

namespace AIVitals.App;

public partial class ActivityWidgetView : System.Windows.Controls.UserControl
{
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(200));

    private ActivityWidgetViewModel? _viewModel;

    public ActivityWidgetView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        MouseEnter += (_, _) => _viewModel?.SetPaused(true);
        MouseLeave += (_, _) => _viewModel?.SetPaused(false);
        Unloaded += (_, _) => Detach();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs eventArgs)
    {
        Detach();
        _viewModel = eventArgs.NewValue as ActivityWidgetViewModel;
        if (_viewModel is not null) _viewModel.PageChanged += OnPageChanged;
    }

    private void Detach()
    {
        if (_viewModel is null) return;
        _viewModel.PageChanged -= OnPageChanged;
        _viewModel = null;
    }

    /// <summary>
    /// Softens a carousel turn. Honours the system animation and high-contrast settings, so a user
    /// who asked for no motion gets an instant swap instead.
    /// </summary>
    private void OnPageChanged(object? sender, EventArgs eventArgs)
    {
        if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
        {
            TileHost.Opacity = 1;
            return;
        }

        TileHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, FadeDuration));
    }
}
