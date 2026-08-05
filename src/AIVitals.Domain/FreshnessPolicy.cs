namespace AIVitals.Domain;

public sealed record FreshnessPolicy
{
    public FreshnessPolicy(TimeSpan delayedAfter, TimeSpan staleAfter)
    {
        if (delayedAfter <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delayedAfter));
        if (staleAfter <= delayedAfter) throw new ArgumentOutOfRangeException(nameof(staleAfter));

        DelayedAfter = delayedAfter;
        StaleAfter = staleAfter;
    }

    public TimeSpan DelayedAfter { get; }
    public TimeSpan StaleAfter { get; }

    public Freshness Evaluate(DateTimeOffset observedAtUtc, DateTimeOffset nowUtc)
    {
        var age = nowUtc.ToUniversalTime() - observedAtUtc.ToUniversalTime();
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;

        if (age >= StaleAfter) return Freshness.Stale;
        return age >= DelayedAfter ? Freshness.Delayed : Freshness.Current;
    }
}
