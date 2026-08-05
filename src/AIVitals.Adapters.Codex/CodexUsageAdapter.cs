using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using AIVitals.Adapters.Abstractions;
using AIVitals.Domain;

namespace AIVitals.Adapters.Codex;

public sealed class CodexUsageAdapter : IUsageAdapter
{
    private readonly ICodexAppServerClientFactory _clientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _pollInterval;
    private AdapterHealth? _lastHealth;

    public CodexUsageAdapter(string? executablePath = null, TimeSpan? pollInterval = null, TimeProvider? timeProvider = null)
        : this(new CodexAppServerClientFactory(executablePath), pollInterval, timeProvider)
    {
    }

    internal CodexUsageAdapter(
        ICodexAppServerClientFactory clientFactory,
        TimeSpan? pollInterval = null,
        TimeProvider? timeProvider = null)
    {
        _clientFactory = clientFactory;
        _pollInterval = pollInterval ?? TimeSpan.FromMinutes(1);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AdapterDescriptor Descriptor { get; } = new(
        "codex",
        "Codex",
        new HashSet<UsageCapability>
        {
            UsageCapability.QuotaWindow,
            UsageCapability.TokenActivity,
            UsageCapability.Health
        },
        new FreshnessPolicy(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)));

    public async IAsyncEnumerable<AdapterEvent> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<AdapterEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });
        var producer = RunAsync(channel.Writer, cancellationToken);

        await foreach (var adapterEvent in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return adapterEvent;

        await producer.ConfigureAwait(false);
    }

    private async Task RunAsync(ChannelWriter<AdapterEvent> output, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await using var client = _clientFactory.Create();
                    await client.StartAsync(cancellationToken).ConfigureAwait(false);
                    await RunSessionAsync(client, output, cancellationToken).ConfigureAwait(false);
                }
                catch (FileNotFoundException)
                {
                    await WriteHealthAsync(output, AdapterHealth.Unavailable, "Codex CLI no está instalado.", cancellationToken)
                        .ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromMinutes(2), _timeProvider, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    var failureKind = exception is CodexRpcException rpc
                        ? $"RPC {rpc.Code}"
                        : exception.GetType().Name;
                    await WriteHealthAsync(
                            output,
                            AdapterHealth.Degraded,
                            $"Codex app-server no responde ({failureKind}).",
                            cancellationToken)
                        .ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromSeconds(15), _timeProvider, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            output.TryComplete();
        }
    }

    private async Task RunSessionAsync(
        ICodexAppServerClient client,
        ChannelWriter<AdapterEvent> output,
        CancellationToken cancellationToken)
    {
        await RefreshAsync(client, output, cancellationToken).ConfigureAwait(false);
        await using var notifications = client.ReadNotificationsAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        var notificationTask = notifications.MoveNextAsync().AsTask();
        var pollTask = Task.Delay(_pollInterval, _timeProvider, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            var completed = await Task.WhenAny(notificationTask, pollTask).ConfigureAwait(false);
            if (completed == pollTask)
            {
                await pollTask.ConfigureAwait(false);
                await RefreshAsync(client, output, cancellationToken).ConfigureAwait(false);
                pollTask = Task.Delay(_pollInterval, _timeProvider, cancellationToken);
                continue;
            }

            if (!await notificationTask.ConfigureAwait(false))
                throw new EndOfStreamException("Codex app-server notification stream ended.");

            var notification = notifications.Current;
            if (notification.Method == "account/rateLimits/updated")
            {
                await WriteObservationsAsync(
                    output,
                    CodexObservationMapper.MapRateLimitsNotification(notification.Params, _timeProvider.GetUtcNow()),
                    cancellationToken).ConfigureAwait(false);
            }
            else if (notification.Method == "account/updated")
            {
                await RefreshAsync(client, output, cancellationToken).ConfigureAwait(false);
            }

            notificationTask = notifications.MoveNextAsync().AsTask();
        }
    }

    private async Task RefreshAsync(
        ICodexAppServerClient client,
        ChannelWriter<AdapterEvent> output,
        CancellationToken cancellationToken)
    {
        var account = await client.RequestAsync(
            "account/read",
            new { refreshToken = false },
            cancellationToken).ConfigureAwait(false);

        if (!CodexObservationMapper.HasAccount(account))
        {
            await WriteHealthAsync(output, AdapterHealth.Unavailable, "Codex no tiene una sesión activa.", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        JsonElement limits;
        try
        {
            limits = await client.RequestAsync("account/rateLimits/read", null, cancellationToken).ConfigureAwait(false);
        }
        catch (CodexRpcException)
        {
            await WriteHealthAsync(output, AdapterHealth.Unavailable, "La sesión actual no expone cuotas de ChatGPT.", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var now = _timeProvider.GetUtcNow();
        try
        {
            var usage = await client.RequestAsync("account/usage/read", null, cancellationToken).ConfigureAwait(false);
            await WriteObservationsAsync(output, CodexObservationMapper.MapTokenUsage(usage, now), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CodexRpcException)
        {
            // Token activity is optional for API-key-only and other unsupported auth modes.
        }

        var observations = CodexObservationMapper.MapRateLimits(limits, now);
        await WriteObservationsAsync(output, observations, cancellationToken).ConfigureAwait(false);
        await WriteHealthAsync(
            output,
            observations.Count > 0 ? AdapterHealth.Available : AdapterHealth.Degraded,
            observations.Count > 0 ? null : "Codex respondió sin ventanas de cuota utilizables.",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteObservationsAsync(
        ChannelWriter<AdapterEvent> output,
        IReadOnlyList<UsageObservation> observations,
        CancellationToken cancellationToken)
    {
        foreach (var observation in observations)
            await output.WriteAsync(new ObservationReceived(observation), cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteHealthAsync(
        ChannelWriter<AdapterEvent> output,
        AdapterHealth health,
        string? detail,
        CancellationToken cancellationToken)
    {
        if (_lastHealth == health) return;
        _lastHealth = health;
        await output.WriteAsync(
            new AdapterHealthChanged(Descriptor.Id, health, _timeProvider.GetUtcNow(), detail),
            cancellationToken).ConfigureAwait(false);
    }
}
