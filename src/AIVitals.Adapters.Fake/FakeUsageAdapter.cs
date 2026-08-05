using System.Runtime.CompilerServices;
using AIVitals.Adapters.Abstractions;
using AIVitals.Domain;

namespace AIVitals.Adapters.Fake;

public sealed class FakeUsageAdapter : IUsageAdapter
{
    private static readonly decimal[] Values = [37m, 41m, 46m, 53m, 61m, 68m];
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _interval;

    public FakeUsageAdapter(TimeSpan? interval = null, TimeProvider? timeProvider = null)
    {
        _interval = interval ?? TimeSpan.FromMinutes(1);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AdapterDescriptor Descriptor { get; } = new(
        "fake",
        "Demo local",
        new HashSet<UsageCapability> { UsageCapability.QuotaWindow, UsageCapability.Health },
        new FreshnessPolicy(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)));

    public async IAsyncEnumerable<AdapterEvent> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var index = 0;
        yield return new AdapterHealthChanged(
            Descriptor.Id,
            AdapterHealth.Available,
            _timeProvider.GetUtcNow(),
            "Deterministic local data source");

        while (!cancellationToken.IsCancellationRequested)
        {
            var now = _timeProvider.GetUtcNow();
            var currentWindowStart = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour - now.Hour % 5, 0, 0, TimeSpan.Zero);
            var observation = new UsageObservation(
                Guid.NewGuid(),
                Descriptor.Id,
                "fake:default",
                UsageCapability.QuotaWindow,
                Values[index++ % Values.Length],
                "percent",
                now,
                "fake:deterministic-v1",
                DataQuality.Exact,
                new QuotaWindow(currentWindowStart, currentWindowStart.AddHours(5)));

            yield return new ObservationReceived(observation);
            await Task.Delay(_interval, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }
}
