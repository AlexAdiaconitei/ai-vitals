using System.Text.Json;

namespace AIVitals.Adapters.Codex;

internal static class CodexResumeQuota
{
    public static bool IsAllowed(JsonElement response, DateTimeOffset now)
    {
        // The CLI schema explicitly forbids inferring recovery from percentages when this is unavailable.
        if (!response.TryGetProperty("ordinaryUsageAllowed", out var allowed) || allowed.ValueKind != JsonValueKind.True)
            return false;
        var quotas = CodexObservationMapper.MapRateLimits(response, now);
        return quotas.Count > 0 && quotas.All(quota => quota.Value is >= 0 and < 100 &&
            (quota.Window?.ResetsAtUtc is not { } reset || reset > now));
    }
}
