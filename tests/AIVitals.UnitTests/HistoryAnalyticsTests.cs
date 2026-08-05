using AIVitals.Application;
using AIVitals.Domain;

namespace AIVitals.UnitTests;

public sealed class HistoryAnalyticsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Active_statusline_fills_an_expired_oauth_bucket_without_duplicate_points()
    {
        var analytics = HistoryAnalytics.Build(
            [
                Observation("claude-code", 36, TimeSpan.FromHours(5), Now.AddHours(3), "claude-code:statusline:rate-limit:five-hour", Now),
                Observation("claude-code", 0, TimeSpan.FromHours(5), Now.AddHours(-1), "claude-code:oauth:rate-limit:five-hour", Now.AddSeconds(1)),
                Observation("claude-code", 62, TimeSpan.FromDays(7), Now.AddDays(3), "claude-code:oauth:rate-limit:seven-day", Now.AddSeconds(1))
            ],
            TimeSpan.FromMinutes(5));

        var point = Assert.Single(analytics.Points);
        Assert.Equal(36, point.Value);
    }

    [Fact]
    public void Overview_reports_average_peak_current_pressure_and_volatility()
    {
        var analytics = HistoryAnalytics.Build(
            [
                Observation("codex", 20, TimeSpan.FromHours(5), Now.AddHours(3), "codex:app-server:rate-limit:five-hour", Now.AddMinutes(-10)),
                Observation("codex", 40, TimeSpan.FromHours(5), Now.AddHours(3), "codex:app-server:rate-limit:five-hour", Now),
                Observation("claude-code", 80, TimeSpan.FromHours(5), Now.AddHours(3), "claude-code:oauth:rate-limit:five-hour", Now)
            ],
            TimeSpan.FromMinutes(5));

        Assert.Equal(2, analytics.ProviderCount);
        Assert.Equal(3, analytics.SnapshotCount);
        Assert.Equal(46.7, analytics.AverageUsage, 1);
        Assert.Equal("claude-code", analytics.PeakProviderId);
        Assert.Equal(80, analytics.PeakUsage);
        Assert.Equal("claude-code", analytics.CurrentProviderId);
        Assert.Equal(80, analytics.CurrentUsage);
        Assert.Equal(10, analytics.AverageVolatility);
    }

    private static UsageObservation Observation(
        string providerId,
        decimal value,
        TimeSpan duration,
        DateTimeOffset reset,
        string source,
        DateTimeOffset observedAt) =>
        new(
            Guid.NewGuid(),
            providerId,
            $"{providerId}:default",
            UsageCapability.QuotaWindow,
            value,
            "percent",
            observedAt,
            source,
            DataQuality.Exact,
            new QuotaWindow(reset - duration, reset));
}
