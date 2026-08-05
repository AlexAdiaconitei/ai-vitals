using AIVitals.Application;
using AIVitals.Domain;
using System.Text.Json;

namespace AIVitals.UnitTests;

public sealed class ObservationExporterTests
{
    [Fact]
    public void Csv_escapes_fields_and_uses_invariant_values()
    {
        var observation = CreateObservation("model,\"quoted\"");

        var csv = ObservationExporter.ToCsv([observation]);

        Assert.Contains("12.5", csv, StringComparison.Ordinal);
        Assert.Contains("\"model,\"\"quoted\"\"\"", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("12,5", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_contains_only_the_export_contract()
    {
        var json = ObservationExporter.ToJson([CreateObservation("claude-opus")]);

        Assert.Contains("\"providerId\": \"claude-code\"", json, StringComparison.Ordinal);
        Assert.Contains("\"anonymousSessionId\": \"session-safe\"", json, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement[0].TryGetProperty("id", out _));
    }

    private static UsageObservation CreateObservation(string model) => new(
        Guid.NewGuid(),
        "claude-code",
        "claude-code:default",
        UsageCapability.QuotaWindow,
        12.5m,
        "percent",
        new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero),
        "claude-code:test",
        DataQuality.Exact,
        model: model,
        anonymousSessionId: "session-safe");
}
