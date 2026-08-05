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

public abstract class HistoryMetricsChart : FrameworkElement
{
    private INotifyCollectionChanged? _observableSource;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(HistoryMetricsChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    public static readonly DependencyProperty CodexBrushProperty = DependencyProperty.Register(
        nameof(CodexBrush), typeof(Brush), typeof(HistoryMetricsChart),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ClaudeBrushProperty = DependencyProperty.Register(
        nameof(ClaudeBrush), typeof(Brush), typeof(HistoryMetricsChart),
        new FrameworkPropertyMetadata(Brushes.DarkOrange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush), typeof(Brush), typeof(HistoryMetricsChart),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public Brush CodexBrush { get => (Brush)GetValue(CodexBrushProperty); set => SetValue(CodexBrushProperty, value); }
    public Brush ClaudeBrush { get => (Brush)GetValue(ClaudeBrushProperty); set => SetValue(ClaudeBrushProperty, value); }
    public Brush LabelBrush { get => (Brush)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    protected ProviderMetricViewModel[] Metrics =>
        ItemsSource?.Cast<object>().OfType<ProviderMetricViewModel>().ToArray() ?? [];

    protected Brush ProviderBrush(string providerId) =>
        providerId.Equals("claude-code", StringComparison.OrdinalIgnoreCase) ? ClaudeBrush : CodexBrush;

    protected static FormattedText Text(string value, double size, Brush brush, double dpi, FontWeight? weight = null) =>
        new(
            value,
            CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
            size,
            brush,
            dpi);

    private static void OnItemsSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var chart = (HistoryMetricsChart)dependencyObject;
        if (chart._observableSource is not null) chart._observableSource.CollectionChanged -= chart.OnCollectionChanged;
        chart._observableSource = eventArgs.NewValue as INotifyCollectionChanged;
        if (chart._observableSource is not null) chart._observableSource.CollectionChanged += chart.OnCollectionChanged;
        chart.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs) => InvalidateVisual();
}

public sealed class ProviderComparisonChart : HistoryMetricsChart
{
    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 420 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 190 : availableSize.Height);

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var metrics = Metrics;
        if (metrics.Length == 0) return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var plot = new Rect(34, 12, Math.Max(0, ActualWidth - 46), Math.Max(0, ActualHeight - 42));
        if (plot.Width <= 0 || plot.Height <= 0) return;
        var slot = plot.Width / metrics.Length;
        var barWidth = Math.Min(64, slot * .42);

        for (var level = 0; level <= 4; level++)
        {
            var value = level * 25;
            var y = plot.Bottom - plot.Height * value / 100d;
            context.DrawLine(new Pen(LabelBrush, .35), new Point(plot.Left, y), new Point(plot.Right, y));
            context.DrawText(Text($"{value}%", 9, LabelBrush, dpi), new Point(0, y - 7));
        }

        for (var index = 0; index < metrics.Length; index++)
        {
            var metric = metrics[index];
            var height = Math.Max(2, plot.Height * Math.Clamp(metric.Average, 0, 100) / 100d);
            var center = plot.Left + slot * (index + .5);
            context.DrawRoundedRectangle(
                ProviderBrush(metric.ProviderId),
                null,
                new Rect(center - barWidth / 2, plot.Bottom - height, barWidth, height),
                5,
                5);
            var valueText = Text($"{metric.Average:0.#}%", 10, LabelBrush, dpi, FontWeights.SemiBold);
            context.DrawText(valueText, new Point(center - valueText.Width / 2, Math.Max(0, plot.Bottom - height - 19)));
            var label = Text(metric.Provider, 10, LabelBrush, dpi);
            context.DrawText(label, new Point(Math.Clamp(center - label.Width / 2, plot.Left, plot.Right - label.Width), plot.Bottom + 8));
        }
    }
}

public sealed class UsageShareChart : HistoryMetricsChart
{
    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 420 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 190 : availableSize.Height);

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var metrics = Metrics.Where(item => item.Share > 0).ToArray();
        if (metrics.Length == 0) return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var radius = Math.Max(24, Math.Min(ActualHeight * .34, ActualWidth * .2));
        var center = new Point(Math.Max(radius + 10, ActualWidth * .3), ActualHeight / 2);
        var thickness = Math.Max(12, radius * .3);
        var startAngle = -90d;

        if (metrics.Length == 1)
        {
            context.DrawEllipse(null, new Pen(ProviderBrush(metrics[0].ProviderId), thickness), center, radius, radius);
        }
        else
        {
            foreach (var metric in metrics)
            {
                var sweep = metric.Share / 100d * 360d;
                DrawArc(context, center, radius, startAngle, sweep, ProviderBrush(metric.ProviderId), thickness);
                startAngle += sweep;
            }
        }

        var total = metrics.Sum(item => item.Average);
        var totalText = Text($"{total:0.#}", 18, LabelBrush, dpi, FontWeights.Bold);
        context.DrawText(totalText, new Point(center.X - totalText.Width / 2, center.Y - totalText.Height / 2));

        var legendX = Math.Max(center.X + radius + 26, ActualWidth * .57);
        for (var index = 0; index < metrics.Length; index++)
        {
            var metric = metrics[index];
            var y = 34 + index * 34;
            context.DrawRoundedRectangle(ProviderBrush(metric.ProviderId), null, new Rect(legendX, y + 3, 9, 9), 2, 2);
            context.DrawText(Text(metric.Provider, 10, LabelBrush, dpi, FontWeights.SemiBold), new Point(legendX + 16, y));
            context.DrawText(Text($"{metric.Share:0.#}%", 10, LabelBrush, dpi), new Point(legendX + 16, y + 15));
        }
    }

    private static void DrawArc(
        DrawingContext context,
        Point center,
        double radius,
        double startAngle,
        double sweepAngle,
        Brush brush,
        double thickness)
    {
        static Point At(Point centerPoint, double r, double degrees)
        {
            var radians = degrees * Math.PI / 180d;
            return new Point(centerPoint.X + r * Math.Cos(radians), centerPoint.Y + r * Math.Sin(radians));
        }

        var geometry = new StreamGeometry();
        using (var drawing = geometry.Open())
        {
            drawing.BeginFigure(At(center, radius, startAngle), false, false);
            drawing.ArcTo(
                At(center, radius, startAngle + sweepAngle),
                new Size(radius, radius),
                0,
                sweepAngle > 180,
                SweepDirection.Clockwise,
                true,
                false);
        }
        geometry.Freeze();
        context.DrawGeometry(null, new Pen(brush, thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat }, geometry);
    }
}
