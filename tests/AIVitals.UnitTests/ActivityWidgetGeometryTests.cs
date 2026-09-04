using AIVitals.Application;

namespace AIVitals.UnitTests;

public sealed class ActivityWidgetGeometryTests
{
    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 2, 2)]
    [InlineData(4, 2, 2)]
    [InlineData(9, 2, 2)]
    public void Never_grows_past_two_rows_by_two_columns(int tiles, int columns, int rows)
    {
        var layout = ActivityWidgetGeometry.Calculate(tiles);

        Assert.Equal(columns, layout.Columns);
        Assert.Equal(rows, layout.Rows);
    }

    [Fact]
    public void A_single_traffic_light_is_narrower_than_a_pair()
    {
        Assert.True(ActivityWidgetGeometry.Calculate(1).Width < ActivityWidgetGeometry.Calculate(2).Width);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(12)]
    public void Width_stays_within_the_range_the_vertical_usage_widget_occupies(int tiles)
    {
        var width = ActivityWidgetGeometry.Calculate(tiles).Width;

        Assert.InRange(width, 104, 190);
    }

    [Fact]
    public void Two_columns_share_one_width_so_paging_never_resizes_the_window()
    {
        var three = ActivityWidgetGeometry.Calculate(3);
        var four = ActivityWidgetGeometry.Calculate(4);
        var many = ActivityWidgetGeometry.Calculate(11);

        Assert.Equal(three.Width, four.Width);
        Assert.Equal(four.Width, many.Width);
        Assert.Equal(four.Height + 16, many.Height);
    }

    [Fact]
    public void The_carousel_only_appears_past_four_sessions()
    {
        Assert.False(ActivityWidgetGeometry.Calculate(4).UsesCarousel);
        Assert.True(ActivityWidgetGeometry.Calculate(5).UsesCarousel);
    }

    [Fact]
    public void A_second_row_makes_the_widget_taller()
    {
        Assert.True(ActivityWidgetGeometry.Calculate(3).Height > ActivityWidgetGeometry.Calculate(2).Height);
        Assert.Equal(ActivityWidgetGeometry.Calculate(1).Height, ActivityWidgetGeometry.Calculate(2).Height);
    }
}
