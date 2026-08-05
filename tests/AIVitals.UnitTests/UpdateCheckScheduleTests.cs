using AIVitals.Application;

namespace AIVitals.UnitTests;

public sealed class UpdateCheckScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Disabled_automatic_checks_never_reach_the_network()
    {
        Assert.False(UpdateCheckSchedule.ShouldCheck(automaticCheckEnabled: false, lastCheckedUtc: null, Now));
        Assert.False(UpdateCheckSchedule.ShouldCheck(
            automaticCheckEnabled: false,
            lastCheckedUtc: Now - TimeSpan.FromDays(30),
            Now));
    }

    [Fact]
    public void A_session_that_has_never_checked_checks_once()
    {
        Assert.True(UpdateCheckSchedule.ShouldCheck(automaticCheckEnabled: true, lastCheckedUtc: null, Now));
    }

    [Fact]
    public void A_recent_check_is_not_repeated_until_the_interval_elapses()
    {
        var lastChecked = Now - UpdateCheckSchedule.Interval + TimeSpan.FromMinutes(1);
        Assert.False(UpdateCheckSchedule.ShouldCheck(automaticCheckEnabled: true, lastChecked, Now));
        Assert.True(UpdateCheckSchedule.ShouldCheck(
            automaticCheckEnabled: true,
            Now - UpdateCheckSchedule.Interval,
            Now));
    }
}
