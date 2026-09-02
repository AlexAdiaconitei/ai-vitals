using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaFontFamily = System.Windows.Media.FontFamily;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace AIVitals.App;

/// <summary>
/// A single line of text that slides back and forth when it does not fit, so a long workspace name
/// can still be read inside a 64 px traffic light tile. Text that fits is centred and never moves.
/// The motion stops with the element, and never starts when Windows asks for no animation or
/// reports high contrast; in those cases the text is trimmed with an ellipsis instead.
/// </summary>
public sealed class MarqueeText : FrameworkElement
{
    /// <summary>Pixels per second. Slow enough to read a folder name, quick enough to finish a pass.</summary>
    private const double Speed = 26;

    private static readonly TimeSpan EndHold = TimeSpan.FromSeconds(1.4);

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(MarqueeText),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            OnTextChanged));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(MarqueeText),
        new FrameworkPropertyMetadata(
            MediaBrushes.Gray,
            FrameworkPropertyMetadataOptions.AffectsRender,
            OnTextChanged));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(
        typeof(MarqueeText),
        new FrameworkPropertyMetadata(
            System.Windows.SystemFonts.MessageFontFamily,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            OnTextChanged));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(
        typeof(MarqueeText),
        new FrameworkPropertyMetadata(
            System.Windows.SystemFonts.MessageFontSize,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            OnTextChanged));

    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(
        typeof(MarqueeText),
        new FrameworkPropertyMetadata(
            FontWeights.Normal,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            OnTextChanged));

    private static readonly DependencyProperty OffsetProperty = DependencyProperty.Register(
        nameof(Offset), typeof(double), typeof(MarqueeText),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    private FormattedText? _formatted;
    private double _animatedOverflow = double.NaN;
    private bool _trimmed;

    public MarqueeText()
    {
        ClipToBounds = true;
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public MediaBrush Foreground { get => (MediaBrush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public MediaFontFamily FontFamily { get => (MediaFontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }

    private double Offset => (double)GetValue(OffsetProperty);

    private static bool MotionAllowed => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _formatted = null;
        InvalidateMeasure();
    }

    protected override WpfSize MeasureOverride(WpfSize availableSize)
    {
        var formatted = Format();
        // Claim no width: the parent column decides how much room the label gets, and overflow is
        // what turns the marquee on.
        return new WpfSize(0, formatted.Height);
    }

    protected override WpfSize ArrangeOverride(WpfSize finalSize)
    {
        UpdateAnimation();
        return base.ArrangeOverride(finalSize);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (string.IsNullOrEmpty(Text) || ActualWidth <= 0) return;

        var formatted = Untrimmed();
        var overflow = formatted.Width - ActualWidth;
        var y = (ActualHeight - formatted.Height) / 2;

        if (overflow <= 0.5)
        {
            drawingContext.DrawText(formatted, new WpfPoint((ActualWidth - formatted.Width) / 2, y));
            return;
        }

        if (!MotionAllowed)
        {
            formatted.MaxTextWidth = ActualWidth;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
            _trimmed = true;
            drawingContext.DrawText(formatted, new WpfPoint(0, y));
            return;
        }

        // ClipToBounds already confines the run to the tile, so the slide needs no clip of its own.
        drawingContext.DrawText(formatted, new WpfPoint(-Offset, y));
    }

    /// <summary>
    /// Returns the run with any ellipsis undone. Trimming clamps <c>FormattedText.Width</c>, so a
    /// cached trimmed run would report no overflow and the marquee would never start again.
    /// </summary>
    private FormattedText Untrimmed()
    {
        var formatted = Format();
        if (!_trimmed) return formatted;
        formatted.MaxTextWidth = double.PositiveInfinity;
        formatted.Trimming = TextTrimming.None;
        _trimmed = false;
        return formatted;
    }

    private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs eventArgs)
    {
        var marquee = (MarqueeText)sender;
        marquee._formatted = null;
        marquee.UpdateAnimation();
    }

    private FormattedText Format()
    {
        if (_formatted is not null) return _formatted;

        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
        _formatted = new FormattedText(
            Text ?? string.Empty,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            FontSize,
            Foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxLineCount = 1
        };
        return _formatted;
    }

    /// <summary>
    /// Starts, retargets, or stops the slide. The animation is rebuilt only when the distance it
    /// has to cover actually changes, so a repaint never restarts a pass halfway through.
    /// </summary>
    private void UpdateAnimation()
    {
        var overflow = ActualWidth > 0 ? Untrimmed().Width - ActualWidth : 0;
        var shouldAnimate = IsVisible && MotionAllowed && overflow > 0.5;
        if (!shouldAnimate)
        {
            if (double.IsNaN(_animatedOverflow)) return;
            BeginAnimation(OffsetProperty, null);
            SetValue(OffsetProperty, 0d);
            _animatedOverflow = double.NaN;
            return;
        }

        if (Math.Abs(_animatedOverflow - overflow) < 0.5) return;
        _animatedOverflow = overflow;

        var travel = TimeSpan.FromSeconds(overflow / Speed);
        var toEnd = EndHold + travel;
        var backStart = toEnd + EndHold;
        var total = backStart + travel;

        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = total,
            RepeatBehavior = RepeatBehavior.Forever
        };
        // A slow slide reads the same at 30 fps, and this widget stays on screen all day.
        Timeline.SetDesiredFrameRate(animation, 30);
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(EndHold)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(overflow, KeyTime.FromTimeSpan(toEnd)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(overflow, KeyTime.FromTimeSpan(backStart)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(total)));
        BeginAnimation(OffsetProperty, animation);
    }
}
