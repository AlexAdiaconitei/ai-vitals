using AIVitals.Domain;

namespace AIVitals.UnitTests;

public sealed class UsageObservationTests
{
    [Fact]
    public void Exact_observation_requires_a_value()
    {
        var create = () => new UsageObservation(
            Guid.NewGuid(), "codex", "codex:default", UsageCapability.QuotaWindow,
            null, "percent", DateTimeOffset.UtcNow, "fixture", DataQuality.Exact);

        Assert.Throws<ArgumentException>(create);
    }

    [Fact]
    public void Unavailable_observation_never_carries_a_false_zero()
    {
        var observation = new UsageObservation(
            Guid.NewGuid(), "codex", "codex:default", UsageCapability.QuotaWindow,
            null, "percent", DateTimeOffset.UtcNow, "fixture", DataQuality.Unavailable);

        Assert.Null(observation.Value);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(100.1)]
    public void Percentage_is_bounded(double value)
    {
        var create = () => new UsageObservation(
            Guid.NewGuid(), "codex", "codex:default", UsageCapability.QuotaWindow,
            (decimal)value, "percent", DateTimeOffset.UtcNow, "fixture", DataQuality.Exact);

        Assert.Throws<ArgumentOutOfRangeException>(create);
    }

    [Fact]
    public void Freshness_uses_adapter_thresholds()
    {
        var policy = new FreshnessPolicy(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5));
        var observed = new DateTimeOffset(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);

        Assert.Equal(Freshness.Current, policy.Evaluate(observed, observed.AddMinutes(1)));
        Assert.Equal(Freshness.Delayed, policy.Evaluate(observed, observed.AddMinutes(2)));
        Assert.Equal(Freshness.Stale, policy.Evaluate(observed, observed.AddMinutes(5)));
    }
}
