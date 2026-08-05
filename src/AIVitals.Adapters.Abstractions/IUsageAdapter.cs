using AIVitals.Domain;

namespace AIVitals.Adapters.Abstractions;

public sealed record AdapterDescriptor(
    string Id,
    string DisplayName,
    IReadOnlySet<UsageCapability> Capabilities,
    FreshnessPolicy FreshnessPolicy);

public enum AdapterHealth
{
    Available,
    Degraded,
    Unavailable
}

public abstract record AdapterEvent(DateTimeOffset OccurredAtUtc);

public sealed record ObservationReceived(UsageObservation Observation)
    : AdapterEvent(Observation.ObservedAtUtc);

public sealed record AdapterHealthChanged(
    string AdapterId,
    AdapterHealth Health,
    DateTimeOffset ChangedAtUtc,
    string? Detail = null)
    : AdapterEvent(ChangedAtUtc);

public interface IUsageAdapter
{
    AdapterDescriptor Descriptor { get; }

    IAsyncEnumerable<AdapterEvent> WatchAsync(CancellationToken cancellationToken);
}
