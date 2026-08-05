using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIVitals.Domain;

namespace AIVitals.Adapters.ClaudeCode;

public static class ClaudeCodeObservationMapper
{
    public static IReadOnlyList<UsageObservation> Map(
        JsonElement payload,
        DateTimeOffset observedAtUtc,
        ReadOnlySpan<byte> sessionPseudonymKey)
    {
        var observations = new List<UsageObservation>();
        var model = ReadString(payload, "model", "id");
        var anonymousSessionId = HashSessionId(ReadString(payload, "session_id"), sessionPseudonymKey);

        MapCost(payload, observedAtUtc, model, anonymousSessionId, observations);
        MapContext(payload, observedAtUtc, model, anonymousSessionId, observations);
        MapRateLimit(payload, "seven_day", TimeSpan.FromDays(7), observedAtUtc, model, anonymousSessionId, observations);
        MapRateLimit(payload, "five_hour", TimeSpan.FromHours(5), observedAtUtc, model, anonymousSessionId, observations);
        MapOAuthRateLimits(payload, observedAtUtc, observations);
        return observations;
    }

    private static void MapOAuthRateLimits(
        JsonElement payload,
        DateTimeOffset observedAtUtc,
        ICollection<UsageObservation> observations)
    {
        if (payload.ValueKind != JsonValueKind.Object) return;

        foreach (var property in payload.EnumerateObject())
        {
            if (property.NameEquals("extra_usage") ||
                property.Value.ValueKind != JsonValueKind.Object ||
                !TryGetDecimal(property.Value, "utilization", out var utilization) ||
                utilization is < 0 or > 100)
                continue;

            var duration = property.Name switch
            {
                "five_hour" => TimeSpan.FromHours(5),
                _ when property.Name.StartsWith("seven_day", StringComparison.Ordinal) => TimeSpan.FromDays(7),
                _ => (TimeSpan?)null
            };
            QuotaWindow? window = null;
            if (duration is { } knownDuration &&
                TryGetDateTimeOffset(property.Value, "resets_at", out var reset))
                window = new QuotaWindow(reset - knownDuration, reset);

            observations.Add(CreateObservation(
                $"claude-code:oauth:rate-limit:{property.Name.Replace('_', '-')}",
                UsageCapability.QuotaWindow,
                utilization,
                "percent",
                DataQuality.Exact,
                observedAtUtc,
                model: null,
                anonymousSessionId: null,
                window));
        }
    }

    private static void MapCost(
        JsonElement payload,
        DateTimeOffset observedAtUtc,
        string? model,
        string? anonymousSessionId,
        ICollection<UsageObservation> observations)
    {
        if (!TryGetObject(payload, "cost", out var cost)) return;

        if (TryGetDecimal(cost, "total_cost_usd", out var totalCost) && totalCost >= 0)
        {
            observations.Add(CreateObservation(
                "claude-code:statusline:cost:session",
                UsageCapability.Cost,
                totalCost,
                "usd",
                DataQuality.Estimated,
                observedAtUtc,
                model,
                anonymousSessionId));
        }

        if (TryGetDecimal(cost, "total_duration_ms", out var duration) && duration >= 0)
        {
            observations.Add(CreateObservation(
                "claude-code:statusline:session:duration",
                UsageCapability.SessionActivity,
                duration,
                "milliseconds",
                DataQuality.Exact,
                observedAtUtc,
                model,
                anonymousSessionId));
        }
    }

    private static void MapContext(
        JsonElement payload,
        DateTimeOffset observedAtUtc,
        string? model,
        string? anonymousSessionId,
        ICollection<UsageObservation> observations)
    {
        if (!TryGetObject(payload, "context_window", out var context) ||
            !context.TryGetProperty("current_usage", out var currentUsage) ||
            currentUsage.ValueKind != JsonValueKind.Object)
            return;

        if (TryGetDecimal(context, "total_input_tokens", out var inputTokens) && inputTokens >= 0)
        {
            observations.Add(CreateObservation(
                "claude-code:statusline:context:input",
                UsageCapability.TokenActivity,
                inputTokens,
                "tokens",
                DataQuality.Exact,
                observedAtUtc,
                model,
                anonymousSessionId));
        }

        if (TryGetDecimal(context, "total_output_tokens", out var outputTokens) && outputTokens >= 0)
        {
            observations.Add(CreateObservation(
                "claude-code:statusline:context:output",
                UsageCapability.TokenActivity,
                outputTokens,
                "tokens",
                DataQuality.Exact,
                observedAtUtc,
                model,
                anonymousSessionId));
        }

        if (TryGetDecimal(context, "used_percentage", out var usedPercentage) && usedPercentage is >= 0 and <= 100)
        {
            observations.Add(CreateObservation(
                "claude-code:statusline:context:used",
                UsageCapability.TokenActivity,
                usedPercentage,
                "percent",
                DataQuality.Exact,
                observedAtUtc,
                model,
                anonymousSessionId));
        }
    }

    private static void MapRateLimit(
        JsonElement payload,
        string propertyName,
        TimeSpan duration,
        DateTimeOffset observedAtUtc,
        string? model,
        string? anonymousSessionId,
        ICollection<UsageObservation> observations)
    {
        if (!TryGetObject(payload, "rate_limits", out var rateLimits) ||
            !TryGetObject(rateLimits, propertyName, out var windowElement) ||
            !TryGetDecimal(windowElement, "used_percentage", out var usedPercentage) ||
            usedPercentage is < 0 or > 100)
            return;

        QuotaWindow? window = null;
        if (TryGetInt64(windowElement, "resets_at", out var resetsAt))
        {
            try
            {
                var reset = DateTimeOffset.FromUnixTimeSeconds(resetsAt);
                window = new QuotaWindow(reset - duration, reset);
            }
            catch (ArgumentOutOfRangeException)
            {
                window = null;
            }
        }

        var label = propertyName.Replace('_', '-');
        observations.Add(CreateObservation(
            $"claude-code:statusline:rate-limit:{label}",
            UsageCapability.QuotaWindow,
            usedPercentage,
            "percent",
            DataQuality.Exact,
            observedAtUtc,
            model,
            anonymousSessionId,
            window));
    }

    private static UsageObservation CreateObservation(
        string source,
        UsageCapability capability,
        decimal value,
        string unit,
        DataQuality quality,
        DateTimeOffset observedAtUtc,
        string? model,
        string? anonymousSessionId,
        QuotaWindow? window = null)
    {
        var resetMinute = window?.ResetsAtUtc is { } reset ? reset.ToUnixTimeSeconds() / 60 : 0;
        var identity = string.Join('|', source, value, resetMinute, anonymousSessionId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));

        return new UsageObservation(
            new Guid(hash.AsSpan(0, 16)),
            "claude-code",
            "claude-code:default",
            capability,
            value,
            unit,
            observedAtUtc,
            source,
            quality,
            window,
            model,
            anonymousSessionId);
    }

    private static string? HashSessionId(string? sessionId, ReadOnlySpan<byte> key)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return null;
        if (key.Length < 32) throw new ArgumentException("The session pseudonym key must contain at least 32 bytes.", nameof(key));
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(sessionId));
        return "session-" + Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }

    private static string? ReadString(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadString(JsonElement parent, string objectName, string propertyName) =>
        TryGetObject(parent, objectName, out var child) ? ReadString(child, propertyName) : null;

    private static bool TryGetObject(JsonElement parent, string propertyName, out JsonElement value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object &&
               parent.TryGetProperty(propertyName, out value) &&
               value.ValueKind == JsonValueKind.Object;
    }

    private static bool TryGetDecimal(JsonElement parent, string propertyName, out decimal value)
    {
        value = default;
        return parent.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetDecimal(out value);
    }

    private static bool TryGetInt64(JsonElement parent, string propertyName, out long value)
    {
        value = default;
        return parent.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt64(out value);
    }

    private static bool TryGetDateTimeOffset(JsonElement parent, string propertyName, out DateTimeOffset value)
    {
        value = default;
        return parent.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               property.TryGetDateTimeOffset(out value);
    }
}
