using System.Windows;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace AIVitals.App;

public sealed class UsageRing : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(UsageRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SignalBrushProperty = DependencyProperty.Register(
        nameof(SignalBrush),
        typeof(MediaBrush),
        typeof(UsageRing),
        new FrameworkPropertyMetadata(MediaBrushes.Teal, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(MediaBrush),
        typeof(UsageRing),
        new FrameworkPropertyMetadata(MediaBrushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public MediaBrush SignalBrush { get => (MediaBrush)GetValue(SignalBrushProperty); set => SetValue(SignalBrushProperty, value); }
    public MediaBrush TrackBrush { get => (MediaBrush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        const double thickness = 7;
        var size = Math.Max(0, Math.Min(ActualWidth, ActualHeight) - thickness);
        var radius = size / 2;
        var center = new WpfPoint(ActualWidth / 2, ActualHeight / 2);
        var trackPen = new MediaPen(TrackBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var signalPen = new MediaPen(SignalBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        drawingContext.DrawEllipse(null, trackPen, center, radius, radius);

        var value = Math.Clamp(Value, 0, 100);
        if (value <= 0) return;
        if (value >= 99.999)
        {
            drawingContext.DrawEllipse(null, signalPen, center, radius, radius);
            return;
        }

        var sweep = value / 100 * 360;
        var start = PointOnCircle(center, radius, -90);
        var end = PointOnCircle(center, radius, -90 + sweep);
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(
            end,
            new WpfSize(radius, radius),
            0,
            sweep > 180,
            SweepDirection.Clockwise,
            true));
        var geometry = new PathGeometry([figure]);
        drawingContext.DrawGeometry(null, signalPen, geometry);
    }

    private static WpfPoint PointOnCircle(WpfPoint center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180;
        return new WpfPoint(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }
}
