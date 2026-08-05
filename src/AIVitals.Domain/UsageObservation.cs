namespace AIVitals.Domain;

public enum UsageCapability
{
    QuotaWindow,
    TokenActivity,
    SessionActivity,
    Cost,
    Health
}

public enum DataQuality
{
    Exact,
    Estimated,
    Unavailable
}

public enum Freshness
{
    Current,
    Delayed,
    Stale
}

public sealed record QuotaWindow
{
    public QuotaWindow(DateTimeOffset startsAtUtc, DateTimeOffset? resetsAtUtc)
    {
        StartsAtUtc = startsAtUtc.ToUniversalTime();
        ResetsAtUtc = resetsAtUtc?.ToUniversalTime();

        if (ResetsAtUtc is not null && ResetsAtUtc <= StartsAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(resetsAtUtc), "The reset must be later than the start.");
        }
    }

    public DateTimeOffset StartsAtUtc { get; }

    public DateTimeOffset? ResetsAtUtc { get; }
}

public sealed record UsageObservation
{
    public UsageObservation(
        Guid id,
        string providerId,
        string connectionId,
        UsageCapability capability,
        decimal? value,
        string unit,
        DateTimeOffset observedAtUtc,
        string source,
        DataQuality quality,
        QuotaWindow? window = null,
        string? model = null,
        string? anonymousSessionId = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("An observation needs an id.", nameof(id));
        if (string.IsNullOrWhiteSpace(providerId)) throw new ArgumentException("Provider is required.", nameof(providerId));
        if (string.IsNullOrWhiteSpace(connectionId)) throw new ArgumentException("Connection is required.", nameof(connectionId));
        if (string.IsNullOrWhiteSpace(unit)) throw new ArgumentException("Unit is required.", nameof(unit));
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Source is required.", nameof(source));
        if (quality == DataQuality.Unavailable && value is not null)
            throw new ArgumentException("Unavailable observations cannot contain a value.", nameof(value));
        if (quality != DataQuality.Unavailable && value is null)
            throw new ArgumentException("Available observations must contain a value.", nameof(value));
        if (unit.Equals("percent", StringComparison.OrdinalIgnoreCase) && value is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(value), "Percentages must be between 0 and 100.");

        Id = id;
        ProviderId = providerId.Trim();
        ConnectionId = connectionId.Trim();
        Capability = capability;
        Value = value;
        Unit = unit.Trim();
        ObservedAtUtc = observedAtUtc.ToUniversalTime();
        Source = source.Trim();
        Quality = quality;
        Window = window;
        Model = NullIfWhiteSpace(model);
        AnonymousSessionId = NullIfWhiteSpace(anonymousSessionId);
    }

    public Guid Id { get; }
    public string ProviderId { get; }
    public string ConnectionId { get; }
    public UsageCapability Capability { get; }
    public decimal? Value { get; }
    public string Unit { get; }
    public DateTimeOffset ObservedAtUtc { get; }
    public string Source { get; }
    public DataQuality Quality { get; }
    public QuotaWindow? Window { get; }
    public string? Model { get; }
    public string? AnonymousSessionId { get; }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
