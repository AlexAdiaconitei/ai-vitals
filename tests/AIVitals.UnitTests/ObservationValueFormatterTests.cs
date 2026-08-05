using AIVitals.Application;

namespace AIVitals.UnitTests;

public sealed class ObservationValueFormatterTests
{
    [Fact]
    public void Session_duration_is_shown_as_time_instead_of_raw_milliseconds()
    {
        Assert.Equal("45,7 s", ObservationValueFormatter.Format(45_678m, "milliseconds", "es"));
    }

    [Theory]
    [InlineData(185_000, "3 min 05 s")]
    [InlineData(7_385_000, "2 h 03 min")]
    public void Long_session_durations_remain_compact(decimal milliseconds, string expected)
    {
        Assert.Equal(expected, ObservationValueFormatter.Format(milliseconds, "milliseconds", "es"));
    }
}
