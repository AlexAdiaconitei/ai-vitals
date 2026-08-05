using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Size = System.Windows.Size;

namespace AIVitals.App;

public sealed class SvgLogo : FrameworkElement
{
    private Geometry? _geometry;
    private Rect _viewBox = new(0, 0, 24, 24);
    private Brush? _sourceBrush;

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(Uri), typeof(SvgLogo),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSourceChanged));

    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(
        nameof(Tint), typeof(Brush), typeof(SvgLogo),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Uri? Source { get => (Uri?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public Brush? Tint { get => (Brush?)GetValue(TintProperty); set => SetValue(TintProperty, value); }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 24 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 24 : availableSize.Height);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (_geometry is null || ActualWidth <= 0 || ActualHeight <= 0) return;

        var scale = Math.Min(ActualWidth / _viewBox.Width, ActualHeight / _viewBox.Height);
        var x = (ActualWidth - _viewBox.Width * scale) / 2 - _viewBox.X * scale;
        var y = (ActualHeight - _viewBox.Height * scale) / 2 - _viewBox.Y * scale;
        var matrix = new Matrix(scale, 0, 0, scale, x, y);
        drawingContext.PushTransform(new MatrixTransform(matrix));
        drawingContext.DrawGeometry(Tint ?? _sourceBrush ?? Brushes.White, null, _geometry);
        drawingContext.Pop();
    }

    private static void OnSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var logo = (SvgLogo)dependencyObject;
        logo.LoadSource();
        logo.InvalidateVisual();
    }

    private void LoadSource()
    {
        _geometry = null;
        if (Source is null) return;
        var resource = System.Windows.Application.GetResourceStream(Source);
        if (resource is null) return;

        using var stream = resource.Stream;
        var document = XDocument.Load(stream);
        var root = document.Root;
        var path = root?.Descendants().FirstOrDefault(element => element.Name.LocalName == "path");
        var data = path?.Attribute("d")?.Value;
        if (string.IsNullOrWhiteSpace(data)) return;

        var parsed = Geometry.Parse(data);
        var fillRule = path?.Attribute("fill-rule")?.Value ?? root?.Attribute("fill-rule")?.Value;
        if (parsed is PathGeometry pathGeometry &&
            string.Equals(fillRule, "evenodd", StringComparison.OrdinalIgnoreCase))
            pathGeometry.FillRule = FillRule.EvenOdd;
        parsed.Freeze();
        _geometry = parsed;

        var viewBox = root?.Attribute("viewBox")?.Value?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (viewBox is { Length: 4 } && viewBox.Select(value => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)).All(valid => valid))
            _viewBox = new Rect(viewBox.Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray()[0],
                double.Parse(viewBox[1], CultureInfo.InvariantCulture),
                double.Parse(viewBox[2], CultureInfo.InvariantCulture),
                double.Parse(viewBox[3], CultureInfo.InvariantCulture));

        var fill = path?.Attribute("fill")?.Value ?? root?.Attribute("fill")?.Value;
        if (!string.IsNullOrWhiteSpace(fill) && fill != "none")
        {
            try { _sourceBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fill)); }
            catch (FormatException) { _sourceBrush = null; }
        }
    }
}
