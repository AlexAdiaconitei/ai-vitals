namespace AIVitals.Application;

public readonly record struct ActivityWidgetLayout(
    double Width,
    double Height,
    int Columns,
    int Rows,
    bool UsesCarousel);

/// <summary>
/// Sizes the traffic light widget so it never grows past two rows by two columns, and so its
/// width stays inside the range the vertical usage widget already occupies (104 to about 190).
/// Anything beyond four sessions pages through a carousel instead of making the window bigger.
/// </summary>
public static class ActivityWidgetGeometry
{
    public const int MaximumVisibleTiles = 4;
    public const double TileWidth = 64;
    public const double TileHeight = 90;
    public const double TileGap = 6;

    /// <summary>Border margin, padding and border of the widget frame, plus the 3 px margin each tile carries.</summary>
    private const double ChromeWidth = 44;
    private const double ChromeHeight = 57;
    private const double CarouselHeight = 16;

    public static ActivityWidgetLayout Calculate(int tileCount)
    {
        var usesCarousel = tileCount > MaximumVisibleTiles;
        var visible = Math.Clamp(tileCount, 1, MaximumVisibleTiles);
        var columns = visible == 1 ? 1 : 2;
        var rows = visible <= 2 ? 1 : 2;

        return new ActivityWidgetLayout(
            ChromeWidth + columns * TileWidth + (columns - 1) * TileGap,
            ChromeHeight + rows * TileHeight + (rows - 1) * TileGap + (usesCarousel ? CarouselHeight : 0),
            columns,
            rows,
            usesCarousel);
    }
}
