using System.Text.Json;
using AIVitals.Adapters.Codex;

namespace AIVitals.AdapterContractTests;

public sealed class CodexResumeQuotaTests
{
    [Theory]
    [InlineData(null, 0, false)]
    [InlineData(false, 0, false)]
    [InlineData(true, 100, false)]
    [InlineData(true, 0, true)]
    public void Backend_permission_is_required_independently_of_percentages(bool? allowed, int used, bool expected)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { ordinaryUsageAllowed = allowed,
            rateLimits = new { primary = new { usedPercent = used, windowDurationMins = 300,
                resetsAt = DateTimeOffset.UtcNow.AddHours(5).ToUnixTimeSeconds() } } }));
        Assert.Equal(expected, CodexResumeQuota.IsAllowed(document.RootElement, DateTimeOffset.UtcNow));
    }
}
