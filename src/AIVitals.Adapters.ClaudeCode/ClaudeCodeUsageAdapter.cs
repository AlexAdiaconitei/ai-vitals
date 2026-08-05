using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using AIVitals.Adapters.Abstractions;
using AIVitals.Domain;

namespace AIVitals.Adapters.ClaudeCode;

public sealed class ClaudeCodeUsageAdapter : IUsageAdapter
{
    /// <summary>
    /// Claude Code fires one status line process per session, so several payloads can
    /// arrive at once. A single listener would make the extra clients time out and drop
    /// their snapshot silently.
    /// </summary>
    private const int ConcurrentBridgeListeners = 4;

    /// <summary>
    /// Session duration is a monotonic wall-clock counter that changes on every refresh.
    /// Persisting each tick floods the history, so only whole-minute progress is emitted.
    /// </summary>
    private const decimal SessionActivityResolutionMilliseconds = 60_000m;

    private static readonly TimeSpan BridgeRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly TimeProvider _timeProvider;
    private readonly byte[] _sessionPseudonymKey;
    private readonly string _pipeName;
    private readonly ClaudeCodeOAuthUsageClient _oauthClient;
    private readonly TimeSpan _oauthPollInterval;
    private readonly object _healthLock = new();
    private readonly object _streamLock = new();
    private readonly Dictionary<string, Guid> _lastStatuslineQuotaByStream = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, decimal> _lastSessionActivityByStream = new(StringComparer.OrdinalIgnoreCase);
    private AdapterHealth? _bridgeHealth;
    private string? _bridgeDetail;
    private AdapterHealth? _oauthHealth;
    private string? _oauthDetail;
    private AdapterHealth? _publishedHealth;
    private string? _publishedDetail;

    public ClaudeCodeUsageAdapter(
        TimeProvider? timeProvider = null,
        byte[]? sessionPseudonymKey = null,
        string? pipeName = null,
        HttpClient? httpClient = null,
        string? credentialsPath = null,
        TimeSpan? oauthPollInterval = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _sessionPseudonymKey = sessionPseudonymKey ?? ClaudeCodeSessionKeyStore.LoadOrCreate();
        _pipeName = pipeName ?? ClaudeCodeBridgeProtocol.PipeName;
        if (_sessionPseudonymKey.Length < 32)
            throw new ArgumentException("The session pseudonym key must contain at least 32 bytes.", nameof(sessionPseudonymKey));
        _oauthClient = new ClaudeCodeOAuthUsageClient(
            httpClient ?? SharedHttpClient,
            credentialsPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude",
                ".credentials.json"),
            _sessionPseudonymKey);
        _oauthPollInterval = oauthPollInterval ?? TimeSpan.FromMinutes(1);
    }

    public AdapterDescriptor Descriptor { get; } = new(
        "claude-code",
        "Claude Code",
        new HashSet<UsageCapability>
        {
            UsageCapability.QuotaWindow,
            UsageCapability.TokenActivity,
            UsageCapability.SessionActivity,
            UsageCapability.Cost,
            UsageCapability.Health
        },
        new FreshnessPolicy(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)));

    public async IAsyncEnumerable<AdapterEvent> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<AdapterEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        var producer = RunProducersAsync(channel.Writer, cancellationToken);

        await foreach (var adapterEvent in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return adapterEvent;

        await producer.ConfigureAwait(false);
    }

    private async Task RunProducersAsync(ChannelWriter<AdapterEvent> output, CancellationToken cancellationToken)
    {
        try
        {
            await SetBridgeHealthAsync(
                output,
                AdapterHealth.Unavailable,
                "Esperando actividad de Claude Code.",
                cancellationToken).ConfigureAwait(false);

            var producers = Enumerable
                .Range(0, ConcurrentBridgeListeners)
                .Select(_ => RunServerAsync(output, cancellationToken))
                .Append(RunOAuthPollingAsync(output, cancellationToken));
            await Task.WhenAll(producers).ConfigureAwait(false);
        }
        finally
        {
            output.TryComplete();
        }
    }

    private async Task RunServerAsync(ChannelWriter<AdapterEvent> output, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await AcceptPayloadAsync(output, cancellationToken).ConfigureAwait(false);
                }
                // Losing one connection must never end the listener: the bridge is the only
                // source of per-session metrics and it cannot be restarted without the app.
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    await SetBridgeHealthAsync(
                        output,
                        AdapterHealth.Degraded,
                        "No se pudo leer el puente local de Claude Code.",
                        cancellationToken).ConfigureAwait(false);
                    await Task.Delay(BridgeRetryDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task AcceptPayloadAsync(ChannelWriter<AdapterEvent> output, CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.In,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var document = await ReadPayloadAsync(pipe, cancellationToken).ConfigureAwait(false);
            var observations = ClaudeCodeObservationMapper.Map(
                document.RootElement,
                _timeProvider.GetUtcNow(),
                _sessionPseudonymKey);
            foreach (var observation in observations)
            {
                if (ShouldEmitStatuslineObservation(observation))
                    await output.WriteAsync(new ObservationReceived(observation), cancellationToken).ConfigureAwait(false);
            }

            await SetBridgeHealthAsync(
                output,
                observations.Count > 0 ? AdapterHealth.Available : AdapterHealth.Degraded,
                observations.Count > 0 ? null : "Claude Code envió un payload sin métricas utilizables.",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or EndOfStreamException)
        {
            await SetBridgeHealthAsync(
                output,
                AdapterHealth.Degraded,
                "Claude Code envió un payload no compatible.",
                cancellationToken).ConfigureAwait(false);
        }
    }

    private bool ShouldEmitStatuslineObservation(UsageObservation observation)
    {
        var streamIdentity = $"{observation.Source}|{observation.AnonymousSessionId}";
        lock (_streamLock)
        {
            switch (observation.Capability)
            {
                case UsageCapability.QuotaWindow:
                    if (_lastStatuslineQuotaByStream.TryGetValue(streamIdentity, out var previousId) &&
                        previousId == observation.Id)
                        return false;
                    _lastStatuslineQuotaByStream[streamIdentity] = observation.Id;
                    return true;

                case UsageCapability.SessionActivity when observation.Value is { } value:
                    if (_lastSessionActivityByStream.TryGetValue(streamIdentity, out var previousValue) &&
                        Math.Abs(value - previousValue) < SessionActivityResolutionMilliseconds)
                        return false;
                    _lastSessionActivityByStream[streamIdentity] = value;
                    return true;

                default:
                    return true;
            }
        }
    }

    private async Task RunOAuthPollingAsync(ChannelWriter<AdapterEvent> output, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(_oauthPollInterval, _timeProvider);
            do
            {
                var result = await _oauthClient.TryGetUsageAsync(
                    _timeProvider.GetUtcNow(),
                    cancellationToken).ConfigureAwait(false);
                switch (result.Status)
                {
                    case ClaudeCodeOAuthUsageStatus.Succeeded:
                        foreach (var observation in result.Observations)
                            await output.WriteAsync(new ObservationReceived(observation), cancellationToken).ConfigureAwait(false);
                        await SetOAuthHealthAsync(output, AdapterHealth.Available, null, cancellationToken).ConfigureAwait(false);
                        break;

                    case ClaudeCodeOAuthUsageStatus.Failed:
                        await SetOAuthHealthAsync(output, AdapterHealth.Degraded, result.Detail, cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private Task SetBridgeHealthAsync(
        ChannelWriter<AdapterEvent> output,
        AdapterHealth health,
        string? detail,
        CancellationToken cancellationToken)
    {
        lock (_healthLock)
        {
            _bridgeHealth = health;
            _bridgeDetail = detail;
        }
        return PublishHealthAsync(output, cancellationToken);
    }

    private Task SetOAuthHealthAsync(
        ChannelWriter<AdapterEvent> output,
        AdapterHealth health,
        string? detail,
        CancellationToken cancellationToken)
    {
        lock (_healthLock)
        {
            _oauthHealth = health;
            _oauthDetail = detail;
        }
        return PublishHealthAsync(output, cancellationToken);
    }

    /// <summary>
    /// The bridge and the account endpoint fail independently. A real failure in either one
    /// degrades the adapter, but an idle bridge must not mask live account quotas: waiting
    /// for the first status line is an absence of data, not a fault.
    /// </summary>
    private async Task PublishHealthAsync(ChannelWriter<AdapterEvent> output, CancellationToken cancellationToken)
    {
        AdapterHealth health;
        string? detail;
        lock (_healthLock)
        {
            if (_bridgeHealth is null && _oauthHealth is null) return;

            if (_bridgeHealth == AdapterHealth.Degraded || _oauthHealth == AdapterHealth.Degraded)
            {
                health = AdapterHealth.Degraded;
                detail = _bridgeHealth == AdapterHealth.Degraded ? _bridgeDetail : _oauthDetail;
            }
            else if (_bridgeHealth == AdapterHealth.Available || _oauthHealth == AdapterHealth.Available)
            {
                health = AdapterHealth.Available;
                detail = null;
            }
            else
            {
                health = AdapterHealth.Unavailable;
                detail = _bridgeDetail ?? _oauthDetail;
            }

            if (_publishedHealth == health && _publishedDetail == detail) return;
            _publishedHealth = health;
            _publishedDetail = detail;
        }

        await output.WriteAsync(
            new AdapterHealthChanged(Descriptor.Id, health, _timeProvider.GetUtcNow(), detail),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReadPayloadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, lengthBytes, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length is <= 0 or > ClaudeCodeBridgeProtocol.MaximumPayloadBytes)
            throw new InvalidDataException("Invalid Claude Code payload size.");

        var payload = new byte[length];
        await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(payload);
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }
}
