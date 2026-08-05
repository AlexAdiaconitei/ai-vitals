using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIVitals.Domain;

namespace AIVitals.Adapters.Codex;

internal static class CodexObservationMapper
{
    public static IReadOnlyList<UsageObservation> MapRateLimits(JsonElement result, DateTimeOffset observedAtUtc)
    {
        var observations = new List<UsageObservation>();
        if (result.TryGetProperty("rateLimitsByLimitId", out var byLimitId) && byLimitId.ValueKind == JsonValueKind.Object)
        {
            foreach (var bucket in byLimitId.EnumerateObject())
                MapSnapshot(bucket.Value, bucket.Name, observedAtUtc, observations);
            return observations;
        }

        if (result.TryGetProperty("rateLimits", out var rateLimits) && rateLimits.ValueKind == JsonValueKind.Object)
            MapSnapshot(rateLimits, "codex", observedAtUtc, observations);
        return observations;
    }

    public static IReadOnlyList<UsageObservation> MapRateLimitsNotification(
        JsonElement parameters,
        DateTimeOffset observedAtUtc)
    {
        var observations = new List<UsageObservation>();
        if (parameters.TryGetProperty("rateLimits", out var snapshot) && snapshot.ValueKind == JsonValueKind.Object)
            MapSnapshot(snapshot, "codex", observedAtUtc, observations);
        return observations;
    }

    public static IReadOnlyList<UsageObservation> MapTokenUsage(JsonElement result, DateTimeOffset observedAtUtc)
    {
        var observations = new List<UsageObservation>();
        if (result.TryGetProperty("summary", out var summary) &&
            summary.ValueKind == JsonValueKind.Object &&
            TryGetInt64(summary, "lifetimeTokens", out var lifetimeTokens))
        {
            observations.Add(CreateTokenObservation(
                lifetimeTokens,
                observedAtUtc,
                "codex:app-server:usage:lifetime"));
        }

        if (result.TryGetProperty("dailyUsageBuckets", out var buckets) && buckets.ValueKind == JsonValueKind.Array)
        {
            var latest = buckets.EnumerateArray()
                .Where(bucket => bucket.ValueKind == JsonValueKind.Object)
                .LastOrDefault();
            if (latest.ValueKind == JsonValueKind.Object &&
                latest.TryGetProperty("startDate", out var dateElement) &&
                dateElement.GetString() is { Length: > 0 } date &&
                TryGetInt64(latest, "tokens", out var dailyTokens))
            {
                observations.Add(CreateTokenObservation(
                    dailyTokens,
                    observedAtUtc,
                    $"codex:app-server:usage:daily:{Sanitize(date)}"));
            }
        }

        return observations;
    }

    public static bool HasAccount(JsonElement accountResult)
    {
        return accountResult.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object;
    }

    private static void MapSnapshot(
        JsonElement snapshot,
        string fallbackLimitId,
        DateTimeOffset observedAtUtc,
        ICollection<UsageObservation> observations)
    {
        var limitId = snapshot.TryGetProperty("limitId", out var limitIdElement)
            ? limitIdElement.GetString() ?? fallbackLimitId
            : fallbackLimitId;

        // Keep primary last so the minimal single-row phase-01 UI favors the main quota.
        MapWindow(snapshot, "secondary", limitId, observedAtUtc, observations);
        MapWindow(snapshot, "primary", limitId, observedAtUtc, observations);
    }

    private static void MapWindow(
        JsonElement snapshot,
        string propertyName,
        string limitId,
        DateTimeOffset observedAtUtc,
        ICollection<UsageObservation> observations)
    {
        if (!snapshot.TryGetProperty(propertyName, out var windowElement) || windowElement.ValueKind != JsonValueKind.Object)
            return;
        if (!TryGetInt32(windowElement, "usedPercent", out var usedPercent) || usedPercent is < 0 or > 100)
            return;

        QuotaWindow? window = null;
        if (TryGetInt64(windowElement, "resetsAt", out var resetsAt) &&
            TryGetInt64(windowElement, "windowDurationMins", out var durationMinutes) &&
            durationMinutes > 0)
        {
            try
            {
                var reset = DateTimeOffset.FromUnixTimeSeconds(resetsAt);
                window = new QuotaWindow(reset.AddMinutes(-durationMinutes), reset);
            }
            catch (ArgumentOutOfRangeException)
            {
                window = null;
            }
        }

        observations.Add(new UsageObservation(
            CreateObservationId(
                $"codex:app-server:rate-limit:{Sanitize(limitId)}:{propertyName}",
                usedPercent.ToString(System.Globalization.CultureInfo.InvariantCulture),
                window?.ResetsAtUtc is { } resetForIdentity
                    ? (resetForIdentity.ToUnixTimeSeconds() / 60).ToString()
                    : "no-reset"),
            "codex",
            "codex:default",
            UsageCapability.QuotaWindow,
            usedPercent,
            "percent",
            observedAtUtc,
            $"codex:app-server:rate-limit:{Sanitize(limitId)}:{propertyName}",
            DataQuality.Exact,
            window));
    }

    private static UsageObservation CreateTokenObservation(long tokens, DateTimeOffset observedAtUtc, string source) =>
        new(
            CreateObservationId(source, tokens.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "codex",
            "codex:default",
            UsageCapability.TokenActivity,
            tokens,
            "tokens",
            observedAtUtc,
            source,
            DataQuality.Exact);

    private static Guid CreateObservationId(params string[] parts)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static bool TryGetInt32(JsonElement element, string name, out int value)
    {
        value = default;
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value);
    }

    private static bool TryGetInt64(JsonElement element, string name, out long value)
    {
        value = default;
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value);
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.' ? character : '_');
        return builder.ToString();
    }
}
