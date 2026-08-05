using AIVitals.Adapters.Abstractions;
using AIVitals.Adapters.Codex;

namespace AIVitals.AdapterContractTests;

public sealed class CodexLiveAdapterTests
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task Installed_codex_completes_handshake_when_opted_in()
    {
        if (Environment.GetEnvironmentVariable("AI_VITALS_LIVE_CODEX") != "1") return;

        await using var client = new CodexAppServerClientFactory().Create();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));

        await client.StartAsync(cancellation.Token);
        var account = await client.RequestAsync("account/read", new { refreshToken = false }, cancellation.Token);

        Assert.True(account.TryGetProperty("requiresOpenaiAuth", out _));
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Installed_codex_emits_a_real_observation_when_opted_in()
    {
        if (Environment.GetEnvironmentVariable("AI_VITALS_LIVE_CODEX") != "1") return;

        var adapter = new CodexUsageAdapter(pollInterval: TimeSpan.FromSeconds(2));
        var healthEvents = new List<string>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));

        try
        {
            await foreach (var adapterEvent in adapter.WatchAsync(cancellation.Token))
            {
                if (adapterEvent is ObservationReceived observation)
                {
                    Assert.Equal("codex", observation.Observation.ProviderId);
                    return;
                }

                if (adapterEvent is AdapterHealthChanged health)
                    healthEvents.Add($"{health.Health}: {health.Detail}");
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }

        Assert.Fail("Codex produced no observation. Health: " + string.Join("; ", healthEvents));
    }
}
