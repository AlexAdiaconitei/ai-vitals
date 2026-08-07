using AIVitals.Adapters.Abstractions;
using AIVitals.Domain;

namespace AIVitals.Application;

public sealed record UsageMonitorState(
    AppPreferences Preferences,
    IReadOnlyDictionary<string, UsageObservation> LatestByProvider,
    IReadOnlyDictionary<string, IReadOnlyList<UsageObservation>> LatestQuotaByProvider,
    IReadOnlyDictionary<string, AdapterHealth> AdapterHealth)
{
    /// <summary>
    /// Why each adapter is in the health it reports, keyed by adapter id. A frozen reading with no
    /// explanation is indistinguishable from a live one that simply has not moved.
    /// </summary>
    public IReadOnlyDictionary<string, string?> AdapterHealthDetail { get; init; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    public UsageObservation? LatestObservation => LatestByProvider.Values
        .OrderByDescending(observation => observation.ObservedAtUtc)
        .FirstOrDefault();
}

public sealed class UsageMonitorService : IAsyncDisposable
{
    private readonly IReadOnlyList<IUsageAdapter> _adapters;
    private readonly IReadOnlySet<string> _adapterIds;
    private readonly IObservationRepository _repository;
    private readonly IAppPreferencesStore _preferencesStore;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Task> _watchers = [];
    private readonly object _stateLock = new();
    private UsageMonitorState _state = new(
        new AppPreferences(),
        new Dictionary<string, UsageObservation>(),
        new Dictionary<string, IReadOnlyList<UsageObservation>>(),
        new Dictionary<string, AdapterHealth>());
    private bool _started;

    public UsageMonitorService(
        IEnumerable<IUsageAdapter> adapters,
        IObservationRepository repository,
        IAppPreferencesStore preferencesStore)
    {
        _adapters = adapters.ToArray();
        _adapterIds = _adapters.Select(item => item.Descriptor.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _repository = repository;
        _preferencesStore = preferencesStore;
    }

    public event EventHandler<UsageMonitorState>? StateChanged;

    public UsageMonitorState State
    {
        get { lock (_stateLock) return _state; }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started) return;

        await _repository.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var preferences = await _preferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        await _preferencesStore.SaveAsync(preferences, cancellationToken).ConfigureAwait(false);
        var latest = FilterProviders(await _repository.GetLatestByProviderAsync(cancellationToken).ConfigureAwait(false));
        var latestQuota = await LoadLatestQuotaWindowsAsync(cancellationToken).ConfigureAwait(false);

        UpdateState(current => current with
        {
            Preferences = preferences,
            LatestByProvider = latest,
            LatestQuotaByProvider = latestQuota
        });
        _started = true;

        foreach (var adapter in _adapters)
        {
            _watchers.Add(WatchAdapterAsync(adapter, _lifetime.Token));
        }
    }

    public async Task SavePreferencesAsync(AppPreferences preferences, CancellationToken cancellationToken = default)
    {
        await _preferencesStore.SaveAsync(preferences, cancellationToken).ConfigureAwait(false);
        UpdateState(current => current with { Preferences = preferences });
    }

    public async Task<IReadOnlyList<UsageObservation>> QueryObservationsAsync(
        ObservationQuery query,
        CancellationToken cancellationToken = default)
    {
        var observations = await _repository.QueryAsync(query.Normalize(), cancellationToken).ConfigureAwait(false);
        return _adapterIds.Count == 0
            ? observations
            : observations.Where(item => _adapterIds.Contains(item.ProviderId)).ToArray();
    }

    public async Task<int> DeleteObservationsAsync(
        ObservationQuery query,
        CancellationToken cancellationToken = default)
    {
        var deleted = await _repository.DeleteAsync(query.Normalize(), cancellationToken).ConfigureAwait(false);
        var latest = FilterProviders(await _repository.GetLatestByProviderAsync(cancellationToken).ConfigureAwait(false));
        var latestQuota = await LoadLatestQuotaWindowsAsync(cancellationToken).ConfigureAwait(false);
        UpdateState(current => current with
        {
            LatestByProvider = latest,
            LatestQuotaByProvider = latestQuota
        });
        return deleted;
    }

    private IReadOnlyDictionary<string, UsageObservation> FilterProviders(
        IReadOnlyDictionary<string, UsageObservation> observations) =>
        _adapterIds.Count == 0
            ? observations
            : observations
                .Where(item => _adapterIds.Contains(item.Key))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(_watchers).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        _lifetime.Dispose();
    }

    private async Task WatchAdapterAsync(IUsageAdapter adapter, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var adapterEvent in adapter.WatchAsync(cancellationToken).ConfigureAwait(false))
            {
                switch (adapterEvent)
                {
                    case ObservationReceived received:
                        await _repository.AppendAsync(received.Observation, cancellationToken).ConfigureAwait(false);
                        UpdateState(current => current with
                        {
                            LatestByProvider = CopyObservation(current.LatestByProvider, received.Observation),
                            LatestQuotaByProvider = CopyQuotaObservation(
                                current.LatestQuotaByProvider,
                                received.Observation)
                        });
                        break;
                    case AdapterHealthChanged health:
                        UpdateState(current => current with
                        {
                            AdapterHealth = CopyHealth(current.AdapterHealth, health.AdapterId, health.Health),
                            AdapterHealthDetail = CopyHealthDetail(
                                current.AdapterHealthDetail,
                                health.AdapterId,
                                health.Detail)
                        });
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            UpdateState(current => current with
            {
                AdapterHealth = CopyHealth(current.AdapterHealth, adapter.Descriptor.Id, AdapterHealth.Degraded),
                AdapterHealthDetail = CopyHealthDetail(current.AdapterHealthDetail, adapter.Descriptor.Id, null)
            });
        }
    }

    private void UpdateState(Func<UsageMonitorState, UsageMonitorState> update)
    {
        UsageMonitorState next;
        lock (_stateLock)
        {
            next = update(_state);
            _state = next;
        }

        StateChanged?.Invoke(this, next);
    }

    private static IReadOnlyDictionary<string, AdapterHealth> CopyHealth(
        IReadOnlyDictionary<string, AdapterHealth> current,
        string adapterId,
        AdapterHealth health)
    {
        var copy = new Dictionary<string, AdapterHealth>(current) { [adapterId] = health };
        return copy;
    }

    private static IReadOnlyDictionary<string, string?> CopyHealthDetail(
        IReadOnlyDictionary<string, string?> current,
        string adapterId,
        string? detail)
    {
        var copy = new Dictionary<string, string?>(current, StringComparer.OrdinalIgnoreCase)
        {
            [adapterId] = detail
        };
        return copy;
    }

    private static IReadOnlyDictionary<string, UsageObservation> CopyObservation(
        IReadOnlyDictionary<string, UsageObservation> current,
        UsageObservation observation)
    {
        var copy = new Dictionary<string, UsageObservation>(current)
        {
            [observation.ProviderId] = observation
        };
        return copy;
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<UsageObservation>>> LoadLatestQuotaWindowsAsync(
        CancellationToken cancellationToken)
    {
        var observations = await _repository.QueryAsync(
            new ObservationQuery(Capability: UsageCapability.QuotaWindow, Limit: 10_000),
            cancellationToken).ConfigureAwait(false);
        return observations
            .Where(item => _adapterIds.Count == 0 || _adapterIds.Contains(item.ProviderId))
            .GroupBy(item => item.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                provider => provider.Key,
                provider => (IReadOnlyList<UsageObservation>)provider
                    .GroupBy(QuotaStreamIdentity, StringComparer.OrdinalIgnoreCase)
                    .Select(source => source
                        .OrderByDescending(item => item.ObservedAtUtc)
                        .ThenByDescending(item => item.Window?.ResetsAtUtc)
                        .First())
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<UsageObservation>> CopyQuotaObservation(
        IReadOnlyDictionary<string, IReadOnlyList<UsageObservation>> current,
        UsageObservation observation)
    {
        if (observation.Capability != UsageCapability.QuotaWindow) return current;

        var copy = new Dictionary<string, IReadOnlyList<UsageObservation>>(current, StringComparer.OrdinalIgnoreCase);
        var provider = copy.TryGetValue(observation.ProviderId, out var existing)
            ? existing.ToList()
            : [];
        var streamIdentity = QuotaStreamIdentity(observation);
        provider.RemoveAll(item => QuotaStreamIdentity(item).Equals(streamIdentity, StringComparison.OrdinalIgnoreCase));
        provider.Add(observation);
        copy[observation.ProviderId] = provider;
        return copy;
    }

    private static string QuotaStreamIdentity(UsageObservation observation)
    {
        var isStatuslineQuota = observation.Source.Contains(
            ":statusline:rate-limit:",
            StringComparison.OrdinalIgnoreCase);
        return isStatuslineQuota && observation.AnonymousSessionId is { Length: > 0 } sessionId
            ? $"{observation.Source}|{sessionId}"
            : observation.Source;
    }
}
