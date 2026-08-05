using System.Text.Json;
using AIVitals.Adapters.ClaudeCode;
using AIVitals.Domain;

namespace AIVitals.AdapterContractTests;

public sealed class ClaudeCodeObservationMapperTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public void Full_payload_maps_only_allowlisted_usage_fields()
    {
        using var fixture = LoadFixture("statusline.full.json");

        var observations = ClaudeCodeObservationMapper.Map(fixture.RootElement, ObservedAt, Key);

        Assert.Equal(7, observations.Count);
        Assert.Equal(2, observations.Count(item => item.Capability == UsageCapability.QuotaWindow));
        Assert.Equal(3, observations.Count(item => item.Capability == UsageCapability.TokenActivity));
        Assert.Contains(observations, item => item.Capability == UsageCapability.Cost && item.Value == 1.2345m);
        Assert.Contains(observations, item => item.Capability == UsageCapability.SessionActivity && item.Value == 45678m);
        Assert.All(observations, item => Assert.Equal("claude-opus-4-1", item.Model));
        Assert.All(observations, item => Assert.StartsWith("session-", item.AnonymousSessionId));

        var persistedShape = JsonSerializer.Serialize(observations);
        Assert.DoesNotContain("raw-session-id-must-never-be-stored", persistedShape, StringComparison.Ordinal);
        Assert.DoesNotContain("private/customer-project", persistedShape, StringComparison.Ordinal);
        Assert.DoesNotContain("secret launch", persistedShape, StringComparison.Ordinal);
        Assert.DoesNotContain("transcripts", persistedShape, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_pseudonym_is_stable_per_key_and_unlinkable_across_keys()
    {
        using var fixture = LoadFixture("statusline.full.json");
        var anotherKey = Enumerable.Range(32, 32).Select(value => (byte)value).ToArray();

        var first = ClaudeCodeObservationMapper.Map(fixture.RootElement, ObservedAt, Key);
        var repeated = ClaudeCodeObservationMapper.Map(fixture.RootElement, ObservedAt.AddMinutes(1), Key);
        var withAnotherKey = ClaudeCodeObservationMapper.Map(fixture.RootElement, ObservedAt, anotherKey);

        Assert.Equal(first.Select(item => item.AnonymousSessionId), repeated.Select(item => item.AnonymousSessionId));
        Assert.NotEqual(first[0].AnonymousSessionId, withAnotherKey[0].AnonymousSessionId);
        Assert.Equal(first.Select(item => item.Id), repeated.Select(item => item.Id));
    }

    [Fact]
    public void Before_first_response_does_not_invent_token_or_quota_usage()
    {
        using var fixture = LoadFixture("statusline.before-first-response.json");

        var observations = ClaudeCodeObservationMapper.Map(fixture.RootElement, ObservedAt, Key);

        Assert.DoesNotContain(observations, item => item.Capability is UsageCapability.TokenActivity or UsageCapability.QuotaWindow);
        Assert.Equal(2, observations.Count);
    }

    [Fact]
    public void Partial_future_payload_keeps_valid_known_window_only()
    {
        using var fixture = LoadFixture("statusline.partial-future.json");

        var observation = Assert.Single(ClaudeCodeObservationMapper.Map(fixture.RootElement, ObservedAt, Key));

        Assert.Equal(19m, observation.Value);
        Assert.EndsWith("five-hour", observation.Source, StringComparison.Ordinal);
        Assert.NotNull(observation.Window);
    }

    [Fact]
    public void OAuth_usage_payload_maps_every_published_quota_window()
    {
        using var fixture = LoadFixture("oauth-usage.full.json");

        var observations = ClaudeCodeObservationMapper.Map(fixture.RootElement, ObservedAt, Key);

        Assert.Equal(4, observations.Count);
        Assert.All(observations, item => Assert.Equal(UsageCapability.QuotaWindow, item.Capability));
        Assert.Contains(observations, item => item.Source.EndsWith("five-hour", StringComparison.Ordinal) && item.Value == 37.5m);
        Assert.Contains(observations, item => item.Source.EndsWith("seven-day", StringComparison.Ordinal) && item.Value == 62m);
        Assert.Contains(observations, item => item.Source.EndsWith("seven-day-sonnet", StringComparison.Ordinal) && item.Value == 48m);
        Assert.Contains(observations, item => item.Source.EndsWith("seven-day-opus", StringComparison.Ordinal) && item.Value == 19m);
        Assert.All(observations, item => Assert.StartsWith("claude-code:oauth:rate-limit:", item.Source, StringComparison.Ordinal));
    }

    private static JsonDocument LoadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ClaudeCode", name);
        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
