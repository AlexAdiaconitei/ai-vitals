using AIVitals.Domain;

namespace AIVitals.Application;

public enum QuotaBandKind
{
    Immediate,
    Total
}

public enum QuotaPeriod
{
    Unknown,
    FiveHours,
    Daily,
    Weekly,
    Monthly,
    Custom
}

public sealed record QuotaBandSnapshot(
    QuotaBandKind Kind,
    QuotaPeriod Period,
    decimal UsedPercentage,
    bool IsActive,
    bool IsCurrent,
    DateTimeOffset? ResetsAtUtc,
    TimeSpan? Duration,
    UsageObservation Observation);

public static class QuotaBandProjection
{
    private static readonly TimeSpan CurrentFor = TimeSpan.FromMinutes(5);

    public static IReadOnlyList<QuotaBandSnapshot> Project(
        IEnumerable<UsageObservation> observations,
        DateTimeOffset nowUtc)
    {
        nowUtc = nowUtc.ToUniversalTime();
        var candidates = observations
            .Where(item =>
                item.Capability == UsageCapability.QuotaWindow &&
                item.Value is >= 0 and <= 100 &&
                item.Unit.Equals("percent", StringComparison.OrdinalIgnoreCase))
            .GroupBy(QuotaIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => IsActive(item, nowUtc))
                .ThenByDescending(IsOAuth)
                .ThenByDescending(item => item.Window?.ResetsAtUtc)
                .ThenByDescending(item => item.Value)
                .ThenByDescending(item => IsCurrent(item, nowUtc))
                .ThenByDescending(item => item.ObservedAtUtc)
                .First())
            .OrderBy(item => DurationOf(item) ?? TimeSpan.MaxValue)
            .ThenBy(item => item.Source, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0) return [];

        if (candidates.Length == 1)
            return [Create(ClassifySingle(candidates[0]), candidates[0], nowUtc)];

        return candidates
            .Select((observation, index) => Create(
                index == 0 ? QuotaBandKind.Immediate : QuotaBandKind.Total,
                observation,
                nowUtc))
            .ToArray();
    }

    private static QuotaBandSnapshot Create(
        QuotaBandKind kind,
        UsageObservation observation,
        DateTimeOffset nowUtc)
    {
        var duration = DurationOf(observation);
        var starts = observation.Window?.StartsAtUtc;
        var reset = observation.Window?.ResetsAtUtc;
        var isActive = (starts is null || starts <= nowUtc) && (reset is null || reset > nowUtc);
        var age = nowUtc - observation.ObservedAtUtc.ToUniversalTime();
        var isCurrent = isActive && age <= CurrentFor;

        return new QuotaBandSnapshot(
            kind,
            ClassifyPeriod(duration),
            isActive ? observation.Value!.Value : 0m,
            isActive,
            isCurrent,
            isActive ? reset : null,
            duration,
            observation);
    }

    private static QuotaBandKind ClassifySingle(UsageObservation observation) =>
        DurationOf(observation) is { } duration && duration <= TimeSpan.FromDays(1)
            ? QuotaBandKind.Immediate
            : QuotaBandKind.Total;

    private static TimeSpan? DurationOf(UsageObservation observation) =>
        observation.Window?.ResetsAtUtc is { } reset
            ? reset - observation.Window.StartsAtUtc
            : null;

    private static bool IsActive(UsageObservation observation, DateTimeOffset nowUtc) =>
        (observation.Window?.StartsAtUtc is null || observation.Window.StartsAtUtc <= nowUtc) &&
        (observation.Window?.ResetsAtUtc is null || observation.Window.ResetsAtUtc > nowUtc);

    private static bool IsCurrent(UsageObservation observation, DateTimeOffset nowUtc) =>
        nowUtc - observation.ObservedAtUtc.ToUniversalTime() <= CurrentFor;

    private static bool IsOAuth(UsageObservation observation) =>
        observation.Source.Contains(":oauth:rate-limit:", StringComparison.OrdinalIgnoreCase);

    private static string QuotaIdentity(UsageObservation observation)
    {
        const string marker = "rate-limit:";
        var markerIndex = observation.Source.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return markerIndex >= 0
            ? $"{observation.ProviderId}:{observation.Source[(markerIndex + marker.Length)..]}"
            : $"{observation.ProviderId}:{observation.Source}";
    }

    private static QuotaPeriod ClassifyPeriod(TimeSpan? duration)
    {
        if (duration is null) return QuotaPeriod.Unknown;
        if (Near(duration.Value, TimeSpan.FromHours(5), TimeSpan.FromMinutes(30))) return QuotaPeriod.FiveHours;
        if (Near(duration.Value, TimeSpan.FromDays(1), TimeSpan.FromHours(2))) return QuotaPeriod.Daily;
        if (Near(duration.Value, TimeSpan.FromDays(7), TimeSpan.FromHours(12))) return QuotaPeriod.Weekly;
        if (duration.Value >= TimeSpan.FromDays(27) && duration.Value <= TimeSpan.FromDays(32)) return QuotaPeriod.Monthly;
        return QuotaPeriod.Custom;
    }

    private static bool Near(TimeSpan value, TimeSpan target, TimeSpan tolerance) =>
        Math.Abs((value - target).Ticks) <= tolerance.Ticks;
}
