using System.IO.Pipes;

namespace AIVitals.AgentActivity;

public sealed class AgentActivityMonitor : IAgentActivitySource, IAsyncDisposable
{
    private const int ListenerCount = 4;
    private readonly object _gate = new();
    private readonly AgentActivityStateReducer _reducer;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _pipeName;
    private AgentActivityProvider[] _includedProviders;
    private Task[]? _tasks;

    /// <param name="pipeName">
    /// Defaults to the per-user pipe. Tests pass their own so a running AI Vitals instance cannot
    /// consume the frame they just sent.
    /// </param>
    public AgentActivityMonitor(
        IEnumerable<AgentActivityProvider>? includedProviders = null,
        AgentActivityFreshness? freshness = null,
        TimeProvider? timeProvider = null,
        string? pipeName = null)
    {
        _pipeName = pipeName ?? AgentActivityBridgeProtocol.PipeName;
        _includedProviders = NormalizeProviders(includedProviders);
        _reducer = new AgentActivityStateReducer(freshness);
        _timeProvider = timeProvider ?? TimeProvider.System;
        Current = _reducer.Snapshot(_timeProvider.GetUtcNow(), _includedProviders);
    }

    public AgentActivitySnapshot Current { get; private set; }
    public event Action<AgentActivitySnapshot>? SnapshotChanged;

    public void Start()
    {
        if (_tasks is not null) return;
        _tasks = Enumerable.Range(0, ListenerCount)
            .Select(_ => Task.Run(() => ListenAsync(_shutdown.Token)))
            .Append(Task.Run(() => RefreshFreshnessAsync(_shutdown.Token)))
            .ToArray();
    }

    public void SetIncludedProviders(IEnumerable<AgentActivityProvider> providers)
    {
        AgentActivitySnapshot snapshot;
        lock (_gate)
        {
            _includedProviders = NormalizeProviders(providers);
            snapshot = Current = _reducer.Snapshot(_timeProvider.GetUtcNow(), _includedProviders);
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        if (_tasks is not null)
        {
            try
            {
                await Task.WhenAll(_tasks);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _shutdown.Dispose();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken);
                var payload = await AgentActivityBridgeProtocol.ReadFrameAsync(pipe, cancellationToken);
                if (payload is null) continue;
                var message = AgentActivityBridgeProtocol.Deserialize(payload);
                if (message is null) continue;
                Apply(message);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
            }
        }
    }

    private void Apply(AgentActivityWireMessage message)
    {
        AgentActivitySnapshot snapshot;
        lock (_gate)
        {
            _reducer.Apply(new AgentActivitySignal(
                message.Provider,
                message.SessionKey,
                message.Event,
                message.ToolKey,
                message.OccurredAt,
                message.WorkspaceLabel));
            snapshot = Current = _reducer.Snapshot(_timeProvider.GetUtcNow(), _includedProviders);
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    private async Task RefreshFreshnessAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), _timeProvider);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            AgentActivitySnapshot snapshot;
            lock (_gate)
                snapshot = Current = _reducer.Snapshot(_timeProvider.GetUtcNow(), _includedProviders);
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    private static AgentActivityProvider[] NormalizeProviders(IEnumerable<AgentActivityProvider>? providers)
    {
        var normalized = (providers ?? Enum.GetValues<AgentActivityProvider>()).Distinct().ToArray();
        return normalized.Length == 0 ? Enum.GetValues<AgentActivityProvider>() : normalized;
    }
}
