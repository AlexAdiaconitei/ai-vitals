using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfRect = System.Windows.Rect;

namespace AIVitals.App;

public sealed class UsageMeter : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(UsageMeter),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(WpfOrientation), typeof(UsageMeter),
        new FrameworkPropertyMetadata(WpfOrientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SignalBrushProperty = DependencyProperty.Register(
        nameof(SignalBrush), typeof(MediaBrush), typeof(UsageMeter),
        new FrameworkPropertyMetadata(MediaBrushes.Teal, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(MediaBrush), typeof(UsageMeter),
        new FrameworkPropertyMetadata(MediaBrushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public WpfOrientation Orientation { get => (WpfOrientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public MediaBrush SignalBrush { get => (MediaBrush)GetValue(SignalBrushProperty); set => SetValue(SignalBrushProperty, value); }
    public MediaBrush TrackBrush { get => (MediaBrush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var radius = Math.Min(ActualWidth, ActualHeight) / 2;
        drawingContext.DrawRoundedRectangle(TrackBrush, null, new WpfRect(0, 0, ActualWidth, ActualHeight), radius, radius);
        var fraction = Math.Clamp(Value, 0, 100) / 100;
        if (fraction <= 0) return;
        var signalRect = Orientation == WpfOrientation.Horizontal
            ? new WpfRect(0, 0, ActualWidth * fraction, ActualHeight)
            : new WpfRect(0, ActualHeight * (1 - fraction), ActualWidth, ActualHeight * fraction);
        drawingContext.DrawRoundedRectangle(SignalBrush, null, signalRect, radius, radius);
    }
}
