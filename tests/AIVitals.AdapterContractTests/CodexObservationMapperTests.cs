using System.Text.Json;
using AIVitals.Adapters.Codex;
using AIVitals.Domain;

namespace AIVitals.AdapterContractTests;

public sealed class CodexObservationMapperTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Multi_bucket_payload_preserves_each_native_window()
    {
        using var fixture = LoadFixture("rate-limits.multi.json");

        var observations = CodexObservationMapper.MapRateLimits(fixture.RootElement, ObservedAt);

        Assert.Equal(3, observations.Count);
        Assert.Equal([61m, 25m, 42m], observations.Select(item => item.Value));
        Assert.All(observations, item => Assert.Equal(DataQuality.Exact, item.Quality));
        Assert.All(observations, item => Assert.NotNull(item.Window));
        Assert.Contains(observations, item => item.Source.EndsWith("codex:secondary", StringComparison.Ordinal));
        Assert.Contains(observations, item => item.Source.EndsWith("codex_other:primary", StringComparison.Ordinal));

        var repeated = CodexObservationMapper.MapRateLimits(fixture.RootElement, ObservedAt.AddMinutes(1));
        Assert.Equal(observations.Select(item => item.Id), repeated.Select(item => item.Id));
    }

    [Fact]
    public void Partial_payload_keeps_known_usage_without_inventing_a_window_or_zero()
    {
        using var fixture = LoadFixture("rate-limits.partial.json");

        var observations = CodexObservationMapper.MapRateLimits(fixture.RootElement, ObservedAt);

        var observation = Assert.Single(observations);
        Assert.Equal(18m, observation.Value);
        Assert.Null(observation.Window);
        Assert.DoesNotContain(observations, item => item.Value == 0m);
    }

    [Fact]
    public void Token_activity_maps_lifetime_and_latest_daily_bucket()
    {
        using var fixture = LoadFixture("token-usage.json");

        var observations = CodexObservationMapper.MapTokenUsage(fixture.RootElement, ObservedAt);

        Assert.Equal(2, observations.Count);
        Assert.Equal([1234567m, 6789m], observations.Select(item => item.Value));
        Assert.All(observations, item => Assert.Equal(UsageCapability.TokenActivity, item.Capability));
    }

    private static JsonDocument LoadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Codex", name);
        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
