using AIVitals.Domain;

namespace AIVitals.Application;

public sealed record HistoryTrendPoint(
    string ProviderId,
    DateTimeOffset ObservedAtUtc,
    double Value);

public sealed record ProviderHistorySummary(
    string ProviderId,
    double Average,
    double Latest,
    double Peak,
    double Volatility,
    double Share,
    int SnapshotCount);

public sealed record HistoryAnalyticsSnapshot(
    IReadOnlyList<HistoryTrendPoint> Points,
    IReadOnlyList<ProviderHistorySummary> Providers,
    int ProviderCount,
    int SnapshotCount,
    double AverageUsage,
    string? PeakProviderId,
    double PeakUsage,
    string? CurrentProviderId,
    double CurrentUsage,
    double AverageVolatility);

public static class HistoryAnalytics
{
    public static HistoryAnalyticsSnapshot Build(
        IEnumerable<UsageObservation> observations,
        TimeSpan bucketSize)
    {
        if (bucketSize <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(bucketSize));

        var bucketSeconds = Math.Max(1L, (long)bucketSize.TotalSeconds);
        var points = observations
            .Where(item =>
                item.Capability == UsageCapability.QuotaWindow &&
                item.Value is >= 0 and <= 100 &&
                item.Quality != DataQuality.Unavailable)
            .GroupBy(item => new
            {
                item.ProviderId,
                Bucket = item.ObservedAtUtc.ToUnixTimeSeconds() / bucketSeconds
            })
            .Select(group =>
            {
                var observedAt = group.Max(item => item.ObservedAtUtc);
                var bands = QuotaBandProjection.Project(group, observedAt);
                var primary = bands.FirstOrDefault(item => item.Kind == QuotaBandKind.Immediate && item.IsActive)
                    ?? bands.FirstOrDefault(item => item.IsActive);
                return primary is null
                    ? null
                    : new HistoryTrendPoint(group.Key.ProviderId, observedAt, (double)primary.UsedPercentage);
            })
            .OfType<HistoryTrendPoint>()
            .OrderBy(item => item.ObservedAtUtc)
            .ThenBy(item => item.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var providerDrafts = points
            .GroupBy(item => item.ProviderId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ordered = group.OrderBy(item => item.ObservedAtUtc).ToArray();
                var values = ordered.Select(item => item.Value).ToArray();
                var volatility = values.Length <= 1
                    ? 0
                    : values.Zip(values.Skip(1), (left, right) => Math.Abs(right - left)).Average();
                return new
                {
                    ProviderId = group.Key,
                    Average = values.Average(),
                    Latest = values[^1],
                    Peak = values.Max(),
                    Volatility = volatility,
                    SnapshotCount = values.Length
                };
            })
            .OrderBy(item => item.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var totalAverage = providerDrafts.Sum(item => item.Average);
        var providers = providerDrafts
            .Select(item => new ProviderHistorySummary(
                item.ProviderId,
                item.Average,
                item.Latest,
                item.Peak,
                item.Volatility,
                totalAverage <= 0 ? 0 : item.Average / totalAverage * 100,
                item.SnapshotCount))
            .ToArray();
        var peak = providers.OrderByDescending(item => item.Peak).FirstOrDefault();
        var current = providers.OrderByDescending(item => item.Latest).FirstOrDefault();

        return new HistoryAnalyticsSnapshot(
            points,
            providers,
            providers.Length,
            points.Length,
            points.Length == 0 ? 0 : points.Average(item => item.Value),
            peak?.ProviderId,
            peak?.Peak ?? 0,
            current?.ProviderId,
            current?.Latest ?? 0,
            providers.Length == 0 ? 0 : providers.Average(item => item.Volatility));
    }
}
