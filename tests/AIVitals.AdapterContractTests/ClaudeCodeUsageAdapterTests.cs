using System.Buffers.Binary;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using AIVitals.Adapters.Abstractions;
using AIVitals.Adapters.ClaudeCode;
using AIVitals.Domain;

namespace AIVitals.AdapterContractTests;

public sealed class ClaudeCodeUsageAdapterTests
{
    [Fact]
    public async Task Statusline_fills_an_active_window_when_oauth_window_is_expired()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"ai-usage-claude-window-fallback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);
        try
        {
            var credentialsPath = Path.Combine(testRoot, ".credentials.json");
            await File.WriteAllTextAsync(credentialsPath, """{"claudeAiOauth":{"accessToken":"test-oauth-token"}}""");
            const string oauthResponse = """
                {
                  "five_hour": { "utilization": 0, "resets_at": "2026-08-05T07:00:00Z" },
                  "seven_day": { "utilization": 62, "resets_at": "2026-08-10T08:00:00Z" }
                }
                """;
            const string statuslineResponse = """
                {
                  "rate_limits": {
                    "five_hour": { "used_percentage": 36, "resets_at": 1785931200 },
                    "seven_day": { "used_percentage": 61, "resets_at": 1786406400 }
                  }
                }
                """;
            var pipeName = $"ai-vitals-window-fallback-{Guid.NewGuid():N}";
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 5, 8, 0, 0, TimeSpan.Zero));
            var adapter = new ClaudeCodeUsageAdapter(
                timeProvider: time,
                sessionPseudonymKey: new byte[32],
                pipeName: pipeName,
                httpClient: new HttpClient(new OAuthUsageHandler(oauthResponse)),
                credentialsPath: credentialsPath,
                oauthPollInterval: TimeSpan.FromHours(1));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var events = new ConcurrentQueue<AdapterEvent>();
            var watch = Task.Run(async () =>
            {
                await foreach (var adapterEvent in adapter.WatchAsync(cancellation.Token))
                    events.Enqueue(adapterEvent);
            }, cancellation.Token);

            await WaitUntilAsync(
                () => events.Any(item => item is ObservationReceived
                {
                    Observation.Source: "claude-code:oauth:rate-limit:seven-day"
                }),
                cancellation.Token);
            await WritePayloadAsync(pipeName, Encoding.UTF8.GetBytes(statuslineResponse), cancellation.Token);
            await WaitUntilAsync(
                () => events.Any(item => item is ObservationReceived
                {
                    Observation.Source: "claude-code:statusline:rate-limit:five-hour",
                    Observation.Value: 36m
                }),
                cancellation.Token);

            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watch);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task OAuth_account_usage_is_preferred_and_emits_every_quota_window()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"ai-usage-claude-oauth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);
        try
        {
            var credentialsPath = Path.Combine(testRoot, ".credentials.json");
            await File.WriteAllTextAsync(credentialsPath, """{"claudeAiOauth":{"accessToken":"test-oauth-token"}}""");
            var response = await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "ClaudeCode", "oauth-usage.full.json"));
            var handler = new OAuthUsageHandler(response);
            var adapter = new ClaudeCodeUsageAdapter(
                sessionPseudonymKey: new byte[32],
                pipeName: $"ai-vitals-oauth-test-{Guid.NewGuid():N}",
                httpClient: new HttpClient(handler),
                credentialsPath: credentialsPath,
                oauthPollInterval: TimeSpan.FromHours(1));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var quota = new List<UsageObservation>();

            await foreach (var adapterEvent in adapter.WatchAsync(cancellation.Token))
            {
                if (adapterEvent is ObservationReceived { Observation.Capability: UsageCapability.QuotaWindow } received)
                    quota.Add(received.Observation);
                if (quota.Count == 4) break;
            }

            Assert.Equal(4, quota.Count);
            Assert.All(quota, item => Assert.StartsWith("claude-code:oauth:rate-limit:", item.Source, StringComparison.Ordinal));
            Assert.Equal("Bearer", handler.Authorization?.Scheme);
            Assert.Equal("test-oauth-token", handler.Authorization?.Parameter);
            Assert.Equal(ClaudeCodeOAuthUsageClient.OAuthBeta, handler.OAuthBeta);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Repeated_statusline_payload_does_not_refresh_quota_observation_time()
    {
        var pipeName = $"ai-vitals-refresh-test-{Guid.NewGuid():N}";
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero));
        var adapter = new ClaudeCodeUsageAdapter(
            timeProvider: time,
            sessionPseudonymKey: new byte[32],
            pipeName: pipeName,
            credentialsPath: MissingCredentialsPath());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var events = new ConcurrentQueue<AdapterEvent>();
        var watch = Task.Run(async () =>
        {
            await foreach (var adapterEvent in adapter.WatchAsync(cancellation.Token))
                events.Enqueue(adapterEvent);
        }, cancellation.Token);

        await WaitUntilAsync(
            () => events.Any(item => item is AdapterHealthChanged { Health: AdapterHealth.Unavailable }),
            cancellation.Token);
        var payload = await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "ClaudeCode", "statusline.full.json"),
            cancellation.Token);
        await WritePayloadAsync(pipeName, payload, cancellation.Token);
        await WaitUntilAsync(
            () => events.Count(item => item is ObservationReceived { Observation.Capability: UsageCapability.QuotaWindow }) == 2,
            cancellation.Token);

        time.Advance(TimeSpan.FromMinutes(6));
        await WritePayloadAsync(pipeName, payload, cancellation.Token);
        await WaitUntilAsync(
            () => events.Any(item => item is ObservationReceived
            {
                Observation.Capability: UsageCapability.Cost,
                Observation.ObservedAtUtc: var observed
            } && observed == time.GetUtcNow()),
            cancellation.Token);
        Assert.Equal(
            2,
            events.Count(item => item is ObservationReceived
            {
                Observation.Capability: UsageCapability.QuotaWindow
            }));

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watch);
    }

    [Fact]
    public async Task Named_pipe_payload_emits_observations_and_available_health()
    {
        var pipeName = $"ai-vitals-test-{Guid.NewGuid():N}";
        var adapter = new ClaudeCodeUsageAdapter(
            sessionPseudonymKey: new byte[32],
            pipeName: pipeName,
            credentialsPath: MissingCredentialsPath());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var events = new List<AdapterEvent>();

        var watch = Task.Run(async () =>
        {
            await foreach (var adapterEvent in adapter.WatchAsync(cancellation.Token))
            {
                events.Add(adapterEvent);
                if (adapterEvent is AdapterHealthChanged { Health: AdapterHealth.Available }) break;
            }
        }, cancellation.Token);

        await WaitForInitialHealthAsync(events, cancellation.Token);
        var payload = await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "ClaudeCode", "statusline.full.json"),
            cancellation.Token);
        await WritePayloadAsync(pipeName, payload, cancellation.Token);

        await watch.WaitAsync(cancellation.Token);

        Assert.Contains(events, item => item is AdapterHealthChanged { Health: AdapterHealth.Unavailable });
        Assert.Contains(events, item => item is ObservationReceived);
        Assert.Contains(events, item => item is AdapterHealthChanged { Health: AdapterHealth.Available });
    }

    [Fact]
    public async Task Failed_account_poll_degrades_health_and_keeps_polling()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"ai-usage-claude-retry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);
        try
        {
            var credentialsPath = Path.Combine(testRoot, ".credentials.json");
            await File.WriteAllTextAsync(credentialsPath, """{"claudeAiOauth":{"accessToken":"test-oauth-token"}}""");
            var response = await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "ClaudeCode", "oauth-usage.full.json"));
            var adapter = new ClaudeCodeUsageAdapter(
                sessionPseudonymKey: new byte[32],
                pipeName: $"ai-vitals-retry-test-{Guid.NewGuid():N}",
                httpClient: new HttpClient(new TimingOutOnceHandler(response)),
                credentialsPath: credentialsPath,
                oauthPollInterval: TimeSpan.FromMilliseconds(200));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var events = new ConcurrentQueue<AdapterEvent>();
            var watch = Task.Run(async () =>
            {
                await foreach (var adapterEvent in adapter.WatchAsync(cancellation.Token))
                    events.Enqueue(adapterEvent);
            }, CancellationToken.None);

            await WaitUntilAsync(
                () => events.Any(item => item is AdapterHealthChanged { Health: AdapterHealth.Degraded }),
                cancellation.Token);
            await WaitUntilAsync(
                () => events.Any(item => item is ObservationReceived
                {
                    Observation.Source: "claude-code:oauth:rate-limit:five-hour"
                }),
                cancellation.Token);
            Assert.False(watch.IsCompleted);

            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watch);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Session_duration_is_only_emitted_once_per_whole_minute()
    {
        var pipeName = $"ai-vitals-duration-test-{Guid.NewGuid():N}";
        var adapter = new ClaudeCodeUsageAdapter(
            sessionPseudonymKey: new byte[32],
            pipeName: pipeName,
            credentialsPath: MissingCredentialsPath());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var events = new ConcurrentQueue<AdapterEvent>();
        var watch = Task.Run(async () =>
        {
            await foreach (var adapterEvent in adapter.WatchAsync(cancellation.Token))
                events.Enqueue(adapterEvent);
        }, CancellationToken.None);

        int SessionActivityCount() => events.Count(item => item is ObservationReceived
        {
            Observation.Capability: UsageCapability.SessionActivity
        });
        int CostCount() => events.Count(item => item is ObservationReceived
        {
            Observation.Capability: UsageCapability.Cost
        });

        await WritePayloadAsync(pipeName, DurationPayload(45_678, 1.00m), cancellation.Token);
        await WaitUntilAsync(() => CostCount() == 1, cancellation.Token);
        Assert.Equal(1, SessionActivityCount());

        await WritePayloadAsync(pipeName, DurationPayload(75_678, 1.10m), cancellation.Token);
        await WaitUntilAsync(() => CostCount() == 2, cancellation.Token);
        Assert.Equal(1, SessionActivityCount());

        await WritePayloadAsync(pipeName, DurationPayload(145_678, 1.20m), cancellation.Token);
        await WaitUntilAsync(() => CostCount() == 3, cancellation.Token);
        await WaitUntilAsync(() => SessionActivityCount() == 2, cancellation.Token);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watch);
    }

    private static byte[] DurationPayload(long durationMilliseconds, decimal costUsd) => Encoding.UTF8.GetBytes(
        $$"""
        {
          "session_id": "duration-session",
          "model": { "id": "claude-opus-5" },
          "cost": { "total_cost_usd": {{costUsd.ToString(CultureInfo.InvariantCulture)}}, "total_duration_ms": {{durationMilliseconds}} }
        }
        """);

    private static string MissingCredentialsPath() =>
        Path.Combine(Path.GetTempPath(), $"ai-vitals-absent-credentials-{Guid.NewGuid():N}.json");

    private static async Task WaitForInitialHealthAsync(List<AdapterEvent> events, CancellationToken cancellationToken)
    {
        while (!events.Any(item => item is AdapterHealthChanged { Health: AdapterHealth.Unavailable }))
            await Task.Delay(10, cancellationToken);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition()) await Task.Delay(10, cancellationToken);
    }

    private static async Task WritePayloadAsync(string pipeName, byte[] payload, CancellationToken cancellationToken)
    {
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(cancellationToken);
        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
        await client.WriteAsync(length, cancellationToken);
        await client.WriteAsync(payload, cancellationToken);
        await client.FlushAsync(cancellationToken);
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan elapsed) => _utcNow += elapsed;
    }

    /// <summary>Reproduces an HttpClient timeout, which surfaces as a cancelled task nobody asked for.</summary>
    private sealed class TimingOutOnceHandler(string response) : HttpMessageHandler
    {
        private int _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                throw new TaskCanceledException(
                    "The request was canceled due to the configured HttpClient.Timeout elapsing.",
                    new TimeoutException());

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class OAuthUsageHandler(string response) : HttpMessageHandler
    {
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string? OAuthBeta { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization;
            OAuthBeta = request.Headers.TryGetValues("anthropic-beta", out var values) ? values.Single() : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
        }
    }
}
