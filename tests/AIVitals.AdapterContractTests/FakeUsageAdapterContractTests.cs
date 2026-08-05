using AIVitals.Adapters.Abstractions;
using AIVitals.Adapters.Fake;
using AIVitals.Domain;

namespace AIVitals.AdapterContractTests;

public sealed class FakeUsageAdapterContractTests
{
    [Fact]
    public void Descriptor_declares_every_emitted_capability()
    {
        var adapter = new FakeUsageAdapter();

        Assert.Contains(UsageCapability.QuotaWindow, adapter.Descriptor.Capabilities);
        Assert.Contains(UsageCapability.Health, adapter.Descriptor.Capabilities);
        Assert.NotEmpty(adapter.Descriptor.Id);
    }

    [Fact]
    public async Task Stream_reports_health_then_a_valid_observation()
    {
        var adapter = new FakeUsageAdapter(TimeSpan.FromHours(1));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var events = adapter.WatchAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);

        Assert.True(await events.MoveNextAsync());
        var health = Assert.IsType<AdapterHealthChanged>(events.Current);
        Assert.Equal(AdapterHealth.Available, health.Health);

        Assert.True(await events.MoveNextAsync());
        var received = Assert.IsType<ObservationReceived>(events.Current);
        Assert.Equal("fake", received.Observation.ProviderId);
        Assert.Equal(DataQuality.Exact, received.Observation.Quality);
        Assert.InRange(received.Observation.Value!.Value, 0m, 100m);
        Assert.NotNull(received.Observation.Window);
    }
}
