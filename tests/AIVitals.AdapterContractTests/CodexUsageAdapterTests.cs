using System.Text.Json;
using AIVitals.Adapters.Abstractions;
using AIVitals.Adapters.Codex;

namespace AIVitals.AdapterContractTests;

public sealed class CodexUsageAdapterTests
{
    [Fact]
    public async Task Logged_out_account_reports_unavailable_without_an_observation()
    {
        var client = new ScriptedClient(new Dictionary<string, string>
        {
            ["account/read"] = """{ "account": null, "requiresOpenaiAuth": true }"""
        });
        var adapter = new CodexUsageAdapter(new ScriptedClientFactory(client), TimeSpan.FromHours(1));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var events = adapter.WatchAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);

        Assert.True(await events.MoveNextAsync());
        var health = Assert.IsType<AdapterHealthChanged>(events.Current);
        Assert.Equal(AdapterHealth.Unavailable, health.Health);
        Assert.Equal(0, client.ObservationProducingRequestCount);
        await cancellation.CancelAsync();
    }

    private sealed class ScriptedClientFactory(ICodexAppServerClient client) : ICodexAppServerClientFactory
    {
        public ICodexAppServerClient Create() => client;
    }

    private sealed class ScriptedClient(IReadOnlyDictionary<string, string> responses) : ICodexAppServerClient
    {
        public int ObservationProducingRequestCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellationToken)
        {
            if (method is "account/rateLimits/read" or "account/usage/read") ObservationProducingRequestCount++;
            if (!responses.TryGetValue(method, out var json)) throw new CodexRpcException(-32601, "Unexpected method");
            using var document = JsonDocument.Parse(json);
            return Task.FromResult(document.RootElement.Clone());
        }

        public async IAsyncEnumerable<CodexServerNotification> ReadNotificationsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
