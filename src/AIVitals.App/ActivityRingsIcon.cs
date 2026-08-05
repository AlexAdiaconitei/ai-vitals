using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace AIVitals.App;

public sealed class ActivityRingsIcon : FrameworkElement
{
    public static readonly DependencyProperty OuterBrushProperty = BrushProperty(nameof(OuterBrush));
    public static readonly DependencyProperty MiddleBrushProperty = BrushProperty(nameof(MiddleBrush));
    public static readonly DependencyProperty InnerBrushProperty = BrushProperty(nameof(InnerBrush));
    public static readonly DependencyProperty TrackBrushProperty = BrushProperty(nameof(TrackBrush));

    public Brush OuterBrush { get => (Brush)GetValue(OuterBrushProperty); set => SetValue(OuterBrushProperty, value); }
    public Brush MiddleBrush { get => (Brush)GetValue(MiddleBrushProperty); set => SetValue(MiddleBrushProperty, value); }
    public Brush InnerBrush { get => (Brush)GetValue(InnerBrushProperty); set => SetValue(InnerBrushProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 32 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 32 : availableSize.Height);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var diameter = Math.Min(ActualWidth, ActualHeight);
        if (diameter <= 0) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var width = Math.Max(1.6, diameter * .105);
        DrawRing(drawingContext, center, diameter * .405, width, 304, OuterBrush, TrackBrush);
        DrawRing(drawingContext, center, diameter * .285, width, 266, MiddleBrush, TrackBrush);
        DrawRing(drawingContext, center, diameter * .165, width, 226, InnerBrush, TrackBrush);
    }

    private static void DrawRing(DrawingContext context, Point center, double radius, double width, double sweep, Brush fill, Brush track)
    {
        var trackPen = new Pen(track, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var fillPen = new Pen(fill, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        context.DrawEllipse(null, trackPen, center, radius, radius);
        context.DrawGeometry(null, fillPen, Arc(center, radius, -90, sweep));
    }

    private static Geometry Arc(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        static Point At(Point center, double radius, double degrees)
        {
            var radians = degrees * Math.PI / 180;
            return new Point(center.X + Math.Cos(radians) * radius, center.Y + Math.Sin(radians) * radius);
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(At(center, radius, startDegrees), false, false);
            context.ArcTo(
                At(center, radius, startDegrees + sweepDegrees),
                new Size(radius, radius),
                0,
                sweepDegrees > 180,
                SweepDirection.Clockwise,
                true,
                false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static DependencyProperty BrushProperty(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(ActivityRingsIcon),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
}
