using System.Windows;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;
using WpfSize = System.Windows.Size;

namespace AIVitals.App;

public enum WidgetGlyphKind
{
    Visible,
    Hidden,
    Locked,
    Unlocked,
    ClickThroughOn,
    ClickThroughOff,
    Rings,
    HorizontalBars,
    VerticalBars
}

public sealed class WidgetGlyph : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(WidgetGlyphKind), typeof(WidgetGlyph),
        new FrameworkPropertyMetadata(WidgetGlyphKind.Visible, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeBrushProperty = DependencyProperty.Register(
        nameof(StrokeBrush), typeof(MediaBrush), typeof(WidgetGlyph),
        new FrameworkPropertyMetadata(MediaBrushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public WidgetGlyphKind Kind { get => (WidgetGlyphKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public MediaBrush StrokeBrush { get => (MediaBrush)GetValue(StrokeBrushProperty); set => SetValue(StrokeBrushProperty, value); }

    protected override WpfSize MeasureOverride(WpfSize availableSize) => new(
        double.IsInfinity(availableSize.Width) ? 20 : availableSize.Width,
        double.IsInfinity(availableSize.Height) ? 20 : availableSize.Height);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        var scale = Math.Min(ActualWidth, ActualHeight) / 20;
        drawingContext.PushTransform(new MatrixTransform(
            scale, 0, 0, scale,
            (ActualWidth - 20 * scale) / 2,
            (ActualHeight - 20 * scale) / 2));
        var pen = new MediaPen(StrokeBrush, 1.7)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        switch (Kind)
        {
            case WidgetGlyphKind.Visible:
            case WidgetGlyphKind.Hidden:
                DrawEye(drawingContext, pen, Kind == WidgetGlyphKind.Hidden);
                break;
            case WidgetGlyphKind.Locked:
            case WidgetGlyphKind.Unlocked:
                DrawLock(drawingContext, pen, Kind == WidgetGlyphKind.Locked);
                break;
            case WidgetGlyphKind.ClickThroughOn:
            case WidgetGlyphKind.ClickThroughOff:
                DrawPointer(drawingContext, pen, Kind == WidgetGlyphKind.ClickThroughOn);
                break;
            case WidgetGlyphKind.Rings:
                drawingContext.DrawEllipse(null, pen, new WpfPoint(10, 10), 7, 7);
                drawingContext.DrawEllipse(null, pen, new WpfPoint(10, 10), 3.8, 3.8);
                break;
            case WidgetGlyphKind.HorizontalBars:
                DrawHorizontalBars(drawingContext, pen);
                break;
            case WidgetGlyphKind.VerticalBars:
                DrawVerticalBars(drawingContext, pen);
                break;
        }
        drawingContext.Pop();
    }

    private static void DrawEye(DrawingContext context, MediaPen pen, bool hidden)
    {
        var eye = new StreamGeometry();
        using (var geometry = eye.Open())
        {
            geometry.BeginFigure(new WpfPoint(2.2, 10), false, false);
            geometry.BezierTo(new WpfPoint(6.1, 4.7), new WpfPoint(13.9, 4.7), new WpfPoint(17.8, 10), true, false);
            geometry.BezierTo(new WpfPoint(13.9, 15.3), new WpfPoint(6.1, 15.3), new WpfPoint(2.2, 10), true, false);
        }
        context.DrawGeometry(null, pen, eye);
        context.DrawEllipse(null, pen, new WpfPoint(10, 10), 2.4, 2.4);
        if (hidden) context.DrawLine(pen, new WpfPoint(3.5, 3.5), new WpfPoint(16.5, 16.5));
    }

    private static void DrawLock(DrawingContext context, MediaPen pen, bool locked)
    {
        context.DrawRoundedRectangle(null, pen, new WpfRect(4, 8.5, 12, 9), 2, 2);
        var shackle = new StreamGeometry();
        using (var geometry = shackle.Open())
        {
            geometry.BeginFigure(locked ? new WpfPoint(6.5, 8.5) : new WpfPoint(11.5, 8.5), false, false);
            if (locked)
                geometry.BezierTo(new WpfPoint(6.5, 2.8), new WpfPoint(13.5, 2.8), new WpfPoint(13.5, 8.5), true, false);
            else
                geometry.BezierTo(new WpfPoint(11.5, 3.2), new WpfPoint(6.5, 3.2), new WpfPoint(6.5, 6), true, false);
        }
        context.DrawGeometry(null, pen, shackle);
    }

    private static void DrawPointer(DrawingContext context, MediaPen pen, bool enabled)
    {
        var pointer = new StreamGeometry();
        using (var geometry = pointer.Open())
        {
            geometry.BeginFigure(new WpfPoint(3.5, 2.5), false, true);
            geometry.LineTo(new WpfPoint(3.5, 15.5), true, false);
            geometry.LineTo(new WpfPoint(7.3, 12), true, false);
            geometry.LineTo(new WpfPoint(10.3, 17.5), true, false);
            geometry.LineTo(new WpfPoint(12.7, 16.2), true, false);
            geometry.LineTo(new WpfPoint(9.8, 10.8), true, false);
            geometry.LineTo(new WpfPoint(15, 10.8), true, false);
        }
        context.DrawGeometry(null, pen, pointer);
        if (enabled)
        {
            context.DrawLine(pen, new WpfPoint(13.2, 5.5), new WpfPoint(18, 5.5));
            context.DrawLine(pen, new WpfPoint(16, 3.5), new WpfPoint(18, 5.5));
            context.DrawLine(pen, new WpfPoint(16, 7.5), new WpfPoint(18, 5.5));
        }
        else
        {
            context.DrawLine(pen, new WpfPoint(12.5, 3.5), new WpfPoint(17.5, 8.5));
        }
    }

    private static void DrawHorizontalBars(DrawingContext context, MediaPen pen)
    {
        var barPen = pen.Clone();
        barPen.Thickness = 2.8;
        context.DrawLine(barPen, new WpfPoint(3, 5), new WpfPoint(17, 5));
        context.DrawLine(barPen, new WpfPoint(3, 10), new WpfPoint(13.5, 10));
        context.DrawLine(barPen, new WpfPoint(3, 15), new WpfPoint(9.5, 15));
    }

    private static void DrawVerticalBars(DrawingContext context, MediaPen pen)
    {
        var barPen = pen.Clone();
        barPen.Thickness = 2.8;
        context.DrawLine(barPen, new WpfPoint(5, 17), new WpfPoint(5, 3));
        context.DrawLine(barPen, new WpfPoint(10, 17), new WpfPoint(10, 6.5));
        context.DrawLine(barPen, new WpfPoint(15, 17), new WpfPoint(15, 10.5));
    }
}
