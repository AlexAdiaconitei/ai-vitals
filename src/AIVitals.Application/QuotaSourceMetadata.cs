namespace AIVitals.Application;

/// <summary>
/// A quota source name already states which window it belongs to. That knowledge has to survive a
/// provider that omits the reset instant while a window carries no usage yet: without it the band
/// loses its duration, falls to the end of the ordering, and lets a weekly quota pose as the
/// immediate one.
/// </summary>
public static class QuotaSourceMetadata
{
    private const string RateLimitMarker = "rate-limit:";

    /// <summary>The window name a provider published, or null when the source is not a rate limit.</summary>
    public static string? Suffix(string source)
    {
        var markerIndex = source.IndexOf(RateLimitMarker, StringComparison.OrdinalIgnoreCase);
        return markerIndex >= 0 ? source[(markerIndex + RateLimitMarker.Length)..] : null;
    }

    /// <summary>
    /// The duration the window name implies. Only names this application understands are answered,
    /// so a provider that publishes a real window is never overruled by a guess.
    /// </summary>
    public static TimeSpan? NominalDuration(string source) => Suffix(source) switch
    {
        null => null,
        var suffix when suffix.Equals("five-hour", StringComparison.OrdinalIgnoreCase) => TimeSpan.FromHours(5),
        var suffix when suffix.StartsWith("seven-day", StringComparison.OrdinalIgnoreCase) => TimeSpan.FromDays(7),
        _ => null
    };

    /// <summary>The model or client a window is scoped to, when the provider splits its quota.</summary>
    public static string? Variant(string source) => Suffix(source) switch
    {
        null => null,
        var suffix when suffix.Contains("oauth-apps", StringComparison.OrdinalIgnoreCase) => "apps",
        var suffix when suffix.Contains("sonnet", StringComparison.OrdinalIgnoreCase) => "sonnet",
        var suffix when suffix.Contains("opus", StringComparison.OrdinalIgnoreCase) => "opus",
        _ => null
    };
}
