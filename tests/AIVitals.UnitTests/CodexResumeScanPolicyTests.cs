using AIVitals.Application;

namespace AIVitals.UnitTests;

public sealed class CodexResumeScanPolicyTests
{
    [Fact]
    public void Enabled_automation_keeps_scanning_even_before_a_quota_read_or_failed_turn()
    {
        Assert.Equal(TimeSpan.FromMinutes(3), CodexResumeScanPolicy.Interval(true, false, false, false));
        Assert.Equal(TimeSpan.FromMinutes(30), CodexResumeScanPolicy.Interval(false, false, false, false));
    }
}
