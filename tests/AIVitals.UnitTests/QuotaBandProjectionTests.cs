using AIVitals.Application;
using AIVitals.Domain;

namespace AIVitals.UnitTests;

public sealed class QuotaBandProjectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Expired_immediate_window_is_not_reported_as_current_usage()
    {
        var expired = Observation(
            value: 1m,
            duration: TimeSpan.FromHours(5),
            reset: Now.AddDays(-2),
            source: "claude-code:statusline:rate-limit:five-hour");

        var band = Assert.Single(QuotaBandProjection.Project([expired], Now));

        Assert.Equal(QuotaBandKind.Immediate, band.Kind);
        Assert.Equal(0m, band.UsedPercentage);
        Assert.False(band.IsActive);
        Assert.Null(band.ResetsAtUtc);
    }

    [Fact]
    public void Immediate_and_total_windows_are_both_preserved()
    {
        var fiveHours = Observation(
            value: 36m,
            duration: TimeSpan.FromHours(5),
            reset: Now.AddHours(3),
            source: "claude-code:statusline:rate-limit:five-hour");
        var week = Observation(
            value: 62m,
            duration: TimeSpan.FromDays(7),
            reset: Now.AddDays(4),
            source: "claude-code:statusline:rate-limit:seven-day");

        var bands = QuotaBandProjection.Project([week, fiveHours], Now);

        Assert.Collection(
            bands,
            immediate =>
            {
                Assert.Equal(QuotaBandKind.Immediate, immediate.Kind);
                Assert.Equal(QuotaPeriod.FiveHours, immediate.Period);
                Assert.Equal(36m, immediate.UsedPercentage);
            },
            total =>
            {
                Assert.Equal(QuotaBandKind.Total, total.Kind);
                Assert.Equal(QuotaPeriod.Weekly, total.Period);
                Assert.Equal(62m, total.UsedPercentage);
            });
    }

    [Fact]
    public void A_single_weekly_window_is_the_total_band()
    {
        var week = Observation(
            value: 91m,
            duration: TimeSpan.FromDays(7),
            reset: Now.AddDays(3),
            source: "codex:app-server:rate-limit:codex:primary");

        var band = Assert.Single(QuotaBandProjection.Project([week], Now));

        Assert.Equal(QuotaBandKind.Total, band.Kind);
        Assert.Equal(QuotaPeriod.Weekly, band.Period);
    }

    [Fact]
    public void A_stale_claude_percentage_is_last_known_data_not_current_usage()
    {
        var stale = Observation(
            value: 56m,
            duration: TimeSpan.FromDays(7),
            reset: Now.AddDays(3),
            source: "claude-code:statusline:rate-limit:seven-day",
            observedAt: Now.AddMinutes(-6));

        var band = Assert.Single(QuotaBandProjection.Project([stale], Now));

        Assert.False(band.IsCurrent);
        Assert.Equal(56m, band.UsedPercentage);
    }

    [Fact]
    public void A_new_claude_observation_replaces_the_stale_value_as_current()
    {
        var stale = Observation(
            value: 56m,
            duration: TimeSpan.FromDays(7),
            reset: Now.AddDays(3),
            source: "claude-code:statusline:rate-limit:seven-day",
            observedAt: Now.AddMinutes(-6));
        var current = Observation(
            value: 80m,
            duration: TimeSpan.FromDays(7),
            reset: Now.AddDays(3),
            source: "claude-code:statusline:rate-limit:seven-day",
            observedAt: Now);

        var band = Assert.Single(QuotaBandProjection.Project([stale, current], Now));

        Assert.True(band.IsCurrent);
        Assert.Equal(80m, band.UsedPercentage);
    }

    [Fact]
    public void Every_distinct_published_window_is_preserved_and_legacy_sources_are_deduplicated()
    {
        var fiveHours = Observation(37m, TimeSpan.FromHours(5), Now.AddHours(3), "claude-code:statusline:rate-limit:five-hour");
        var fiveHoursOAuth = Observation(38m, TimeSpan.FromHours(5), Now.AddHours(3), "claude-code:oauth:rate-limit:five-hour", Now.AddSeconds(1));
        var week = Observation(62m, TimeSpan.FromDays(7), Now.AddDays(4), "claude-code:oauth:rate-limit:seven-day");
        var sonnet = Observation(48m, TimeSpan.FromDays(7), Now.AddDays(4), "claude-code:oauth:rate-limit:seven-day-sonnet");
        var opus = Observation(19m, TimeSpan.FromDays(7), Now.AddDays(4), "claude-code:oauth:rate-limit:seven-day-opus");

        var bands = QuotaBandProjection.Project([fiveHours, fiveHoursOAuth, week, sonnet, opus], Now.AddSeconds(1));

        Assert.Equal(4, bands.Count);
        Assert.Equal(38m, bands[0].UsedPercentage);
        Assert.Equal(["five-hour", "seven-day", "seven-day-opus", "seven-day-sonnet"],
            bands.Select(item => item.Observation.Source[(item.Observation.Source.LastIndexOf(':') + 1)..]).ToArray());
    }

    [Fact]
    public void Active_statusline_window_beats_a_newer_expired_oauth_window()
    {
        var statusline = Observation(
            36m,
            TimeSpan.FromHours(5),
            Now.AddHours(3),
            "claude-code:statusline:rate-limit:five-hour",
            Now.AddSeconds(-1));
        var expiredOAuth = Observation(
            0m,
            TimeSpan.FromHours(5),
            Now.AddHours(-1),
            "claude-code:oauth:rate-limit:five-hour",
            Now);

        var band = Assert.Single(QuotaBandProjection.Project([statusline, expiredOAuth], Now));

        Assert.True(band.IsActive);
        Assert.Equal(36m, band.UsedPercentage);
        Assert.Equal(statusline.Source, band.Observation.Source);
    }

    [Fact]
    public void Current_oauth_window_stays_preferred_when_statusline_updates()
    {
        var oauth = Observation(
            37m,
            TimeSpan.FromHours(5),
            Now.AddHours(3),
            "claude-code:oauth:rate-limit:five-hour",
            Now.AddSeconds(-1));
        var statusline = Observation(
            36m,
            TimeSpan.FromHours(5),
            Now.AddHours(3),
            "claude-code:statusline:rate-limit:five-hour",
            Now);

        var band = Assert.Single(QuotaBandProjection.Project([oauth, statusline], Now));

        Assert.Equal(37m, band.UsedPercentage);
        Assert.Equal(oauth.Source, band.Observation.Source);
    }

    [Fact]
    public void Active_oauth_windows_remain_authoritative_when_statusline_is_newer()
    {
        var oauthFiveHour = Observation(
            10m,
            TimeSpan.FromHours(5),
            Now.AddHours(3),
            "claude-code:oauth:rate-limit:five-hour",
            Now.AddMinutes(-6));
        var oauthWeek = Observation(
            85m,
            TimeSpan.FromDays(7),
            Now.AddDays(3),
            "claude-code:oauth:rate-limit:seven-day",
            Now.AddMinutes(-6));
        var statuslineFiveHour = Observation(
            0m,
            TimeSpan.FromHours(5),
            Now.AddHours(3),
            "claude-code:statusline:rate-limit:five-hour",
            Now);
        var statuslineWeek = Observation(
            56m,
            TimeSpan.FromDays(7),
            Now.AddDays(3),
            "claude-code:statusline:rate-limit:seven-day",
            Now);

        var bands = QuotaBandProjection.Project(
            [oauthFiveHour, oauthWeek, statuslineFiveHour, statuslineWeek],
            Now);

        Assert.Collection(
            bands,
            immediate =>
            {
                Assert.Equal(10m, immediate.UsedPercentage);
                Assert.Contains(":oauth:", immediate.Observation.Source, StringComparison.Ordinal);
            },
            total =>
            {
                Assert.Equal(85m, total.UsedPercentage);
                Assert.Contains(":oauth:", total.Observation.Source, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void Statusline_fallback_does_not_dance_between_sessions_for_the_same_reset()
    {
        var newerLowerSnapshot = Observation(
            56m,
            TimeSpan.FromDays(7),
            Now.AddDays(3),
            "claude-code:statusline:rate-limit:seven-day",
            Now,
            "session-newer");
        var olderHigherSnapshot = Observation(
            67m,
            TimeSpan.FromDays(7),
            Now.AddDays(3),
            "claude-code:statusline:rate-limit:seven-day",
            Now.AddSeconds(-2),
            "session-older");

        var first = Assert.Single(QuotaBandProjection.Project(
            [olderHigherSnapshot, newerLowerSnapshot],
            Now));
        var reversed = Assert.Single(QuotaBandProjection.Project(
            [newerLowerSnapshot, olderHigherSnapshot],
            Now));

        Assert.Equal(67m, first.UsedPercentage);
        Assert.Equal(67m, reversed.UsedPercentage);
        Assert.Equal("session-older", first.Observation.AnonymousSessionId);
        Assert.Equal("session-older", reversed.Observation.AnonymousSessionId);
    }

    private static UsageObservation Observation(
        decimal value,
        TimeSpan duration,
        DateTimeOffset reset,
        string source,
        DateTimeOffset? observedAt = null,
        string? anonymousSessionId = null) =>
        new(
            Guid.NewGuid(),
            source.StartsWith("claude", StringComparison.Ordinal) ? "claude-code" : "codex",
            "connection",
            UsageCapability.QuotaWindow,
            value,
            "percent",
            observedAt ?? Now,
            source,
            DataQuality.Exact,
            new QuotaWindow(reset - duration, reset),
            anonymousSessionId: anonymousSessionId);
}
