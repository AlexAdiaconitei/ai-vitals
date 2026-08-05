using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace AIVitals.App;

public sealed class UsageActivityChart : FrameworkElement
{
    private INotifyCollectionChanged? _observableSource;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(UsageActivityChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    public static readonly DependencyProperty CodexBrushProperty = DependencyProperty.Register(
        nameof(CodexBrush), typeof(Brush), typeof(UsageActivityChart),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ClaudeBrushProperty = DependencyProperty.Register(
        nameof(ClaudeBrush), typeof(Brush), typeof(UsageActivityChart),
        new FrameworkPropertyMetadata(Brushes.DarkOrange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(UsageActivityChart),
        new FrameworkPropertyMetadata(Brushes.SlateGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush), typeof(Brush), typeof(UsageActivityChart),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
        nameof(EmptyText), typeof(string), typeof(UsageActivityChart),
        new FrameworkPropertyMetadata("No hay datos en este intervalo", FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public Brush CodexBrush { get => (Brush)GetValue(CodexBrushProperty); set => SetValue(CodexBrushProperty, value); }
    public Brush ClaudeBrush { get => (Brush)GetValue(ClaudeBrushProperty); set => SetValue(ClaudeBrushProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public Brush LabelBrush { get => (Brush)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public string EmptyText { get => (string)GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 640 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 190 : availableSize.Height);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var points = ItemsSource?.Cast<object>().OfType<ActivityPointViewModel>().ToArray() ?? [];
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var plot = new Rect(34, 10, Math.Max(0, ActualWidth - 44), Math.Max(0, ActualHeight - 38));
        if (plot.Width <= 0 || plot.Height <= 0) return;

        var gridPen = new Pen(GridBrush, 1) { DashStyle = new DashStyle([2, 4], 0) };
        gridPen.Freeze();
        for (var level = 0; level <= 4; level++)
        {
            var value = level * 25;
            var y = plot.Bottom - plot.Height * value / 100d;
            drawingContext.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(drawingContext, $"{value}%", typeface, 9, LabelBrush, 0, y - 7, dpi);
        }

        if (points.Length == 0)
        {
            var text = FormatText(EmptyText, typeface, 12, LabelBrush, dpi);
            drawingContext.DrawText(text, new Point(plot.Left + (plot.Width - text.Width) / 2, plot.Top + (plot.Height - text.Height) / 2));
            return;
        }

        var minimum = points.Min(item => item.ObservedAt).ToUniversalTime();
        var maximum = points.Max(item => item.ObservedAt).ToUniversalTime();
        if (maximum <= minimum)
        {
            minimum = minimum.AddMinutes(-15);
            maximum = maximum.AddMinutes(15);
        }

        DrawSeries(drawingContext, points, "codex", CodexBrush, plot, minimum, maximum);
        DrawSeries(drawingContext, points, "claude-code", ClaudeBrush, plot, minimum, maximum);

        for (var index = 0; index < 5; index++)
        {
            var ratio = index / 4d;
            var instant = minimum + TimeSpan.FromTicks((long)((maximum - minimum).Ticks * ratio));
            var label = maximum - minimum <= TimeSpan.FromDays(2)
                ? instant.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)
                : instant.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture);
            var text = FormatText(label, typeface, 9, LabelBrush, dpi);
            var center = plot.Left + plot.Width * ratio;
            drawingContext.DrawText(text, new Point(Math.Clamp(center - text.Width / 2, plot.Left, plot.Right - text.Width), plot.Bottom + 7));
        }
    }

    private static void DrawSeries(
        DrawingContext context,
        IEnumerable<ActivityPointViewModel> points,
        string providerId,
        Brush brush,
        Rect plot,
        DateTimeOffset minimum,
        DateTimeOffset maximum)
    {
        var series = points
            .Where(point => point.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(point => point.ObservedAt)
            .ToArray();
        if (series.Length == 0) return;

        var spanTicks = Math.Max(1, (maximum - minimum).Ticks);
        Point Translate(ActivityPointViewModel point)
        {
            var x = plot.Left + plot.Width * (point.ObservedAt.ToUniversalTime() - minimum).Ticks / spanTicks;
            var y = plot.Bottom - plot.Height * Math.Clamp(point.Value, 0, 100) / 100d;
            return new Point(x, y);
        }

        var line = new StreamGeometry();
        using (var geometry = line.Open())
        {
            geometry.BeginFigure(Translate(series[0]), false, false);
            if (series.Length > 1)
                geometry.PolyLineTo(series.Skip(1).Select(Translate).ToArray(), true, false);
        }
        line.Freeze();

        var pen = new Pen(brush, 2) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        context.DrawGeometry(null, pen, line);

        foreach (var point in series.TakeLast(Math.Min(24, series.Length)))
            context.DrawEllipse(brush, null, Translate(point), 2.3, 2.3);
    }

    private static FormattedText FormatText(string value, Typeface typeface, double size, Brush brush, double dpi) =>
        new(value, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, typeface, size, brush, dpi);

    private static void DrawText(DrawingContext context, string value, Typeface typeface, double size, Brush brush, double x, double y, double dpi) =>
        context.DrawText(FormatText(value, typeface, size, brush, dpi), new Point(x, y));

    private static void OnItemsSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var chart = (UsageActivityChart)dependencyObject;
        if (chart._observableSource is not null) chart._observableSource.CollectionChanged -= chart.OnCollectionChanged;
        chart._observableSource = eventArgs.NewValue as INotifyCollectionChanged;
        if (chart._observableSource is not null) chart._observableSource.CollectionChanged += chart.OnCollectionChanged;
        chart.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs) => InvalidateVisual();
}
