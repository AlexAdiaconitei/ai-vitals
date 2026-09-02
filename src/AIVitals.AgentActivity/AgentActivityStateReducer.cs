namespace AIVitals.AgentActivity;

public sealed class AgentActivityStateReducer
{
    /// <summary>Bounds the widget and the reducer when an agent never reports <c>SessionEnd</c>.</summary>
    public const int MaximumSessionsPerProvider = 32;

    private readonly AgentActivityFreshness _freshness;
    private readonly Dictionary<SessionIdentity, SessionState> _sessions = [];

    public AgentActivityStateReducer(AgentActivityFreshness? freshness = null)
    {
        _freshness = freshness ?? AgentActivityFreshness.Default;
    }

    public void Apply(AgentActivitySignal signal)
    {
        if (string.IsNullOrWhiteSpace(signal.SessionKey)) return;

        var identity = new SessionIdentity(signal.Provider, signal.SessionKey);
        if (signal.Event == AgentActivityEvent.SessionEnded)
        {
            if (!_sessions.TryGetValue(identity, out var existing) || signal.OccurredAt >= existing.LastSignalAt)
                _sessions.Remove(identity);
            return;
        }

        if (!_sessions.TryGetValue(identity, out var session))
        {
            // The arrival time counts as a sighting straight away, so eviction never picks the
            // session that was just created over the ones already going stale.
            session = new SessionState { FirstSeenAt = signal.OccurredAt, LastSignalAt = signal.OccurredAt };
            _sessions.Add(identity, session);
            EvictOldestSessions(signal.Provider);
        }

        if (signal.WorkspaceLabel is { Length: > 0 }) session.WorkspaceLabel = signal.WorkspaceLabel;
        if (signal.OccurredAt < session.FirstSeenAt) session.FirstSeenAt = signal.OccurredAt;

        if (IsTurnBoundary(signal.Event))
        {
            if (signal.OccurredAt < session.LastTurnBoundaryAt) return;
            session.LastTurnBoundaryAt = signal.OccurredAt;
        }
        else if (signal.OccurredAt < session.LastTurnBoundaryAt)
        {
            return;
        }

        if (signal.OccurredAt > session.LastSignalAt) session.LastSignalAt = signal.OccurredAt;
        switch (signal.Event)
        {
            case AgentActivityEvent.SessionStarted:
                session.Reset(turnActive: false);
                break;
            case AgentActivityEvent.PromptSubmitted:
                session.Reset(turnActive: true);
                break;
            case AgentActivityEvent.ToolStarted:
                session.TurnActive = true;
                session.StartTool(signal.ToolKey);
                break;
            case AgentActivityEvent.ToolFinished:
                session.FinishTool(signal.ToolKey);
                break;
            case AgentActivityEvent.TurnStopped:
                session.Reset(turnActive: false);
                break;
        }
    }

    public AgentActivitySnapshot Snapshot(
        DateTimeOffset now,
        IEnumerable<AgentActivityProvider>? includedProviders = null)
    {
        PurgeExpiredSessions(now);
        var providers = (includedProviders ?? Enum.GetValues<AgentActivityProvider>()).Distinct().ToArray();
        var providerSnapshots = providers.Select(provider => ProjectProvider(provider, now)).ToArray();
        var sessions = ProjectSessions(providers, now);
        var aggregate = Aggregate(providerSnapshots.Select(snapshot => snapshot.Color));

        return new AgentActivitySnapshot(
            ToPhase(aggregate),
            aggregate,
            providerSnapshots,
            sessions,
            now);
    }

    /// <summary>
    /// Projects one entry per live session, ordered so the widget never reshuffles its tiles:
    /// by the caller's provider order, then by first sighting, then by the opaque session key.
    /// </summary>
    private IReadOnlyList<AgentActivitySessionSnapshot> ProjectSessions(
        IReadOnlyList<AgentActivityProvider> providers,
        DateTimeOffset now)
    {
        var order = providers
            .Select((provider, index) => (provider, index))
            .ToDictionary(item => item.provider, item => item.index);

        return _sessions
            .Where(pair => order.ContainsKey(pair.Key.Provider))
            .OrderBy(pair => order[pair.Key.Provider])
            .ThenBy(pair => pair.Value.FirstSeenAt)
            .ThenBy(pair => pair.Key.SessionKey, StringComparer.Ordinal)
            .Select(pair =>
            {
                var color = ProjectColor(pair.Value, now);
                return new AgentActivitySessionSnapshot(
                    pair.Key.Provider,
                    pair.Key.SessionKey,
                    pair.Value.WorkspaceLabel,
                    ToPhase(color),
                    color,
                    pair.Value.ActiveToolCount,
                    pair.Value.FirstSeenAt,
                    pair.Value.LastSignalAt);
            })
            .ToArray();
    }

    private AgentActivityProviderSnapshot ProjectProvider(AgentActivityProvider provider, DateTimeOffset now)
    {
        var sessions = _sessions
            .Where(pair => pair.Key.Provider == provider)
            .Select(pair => pair.Value)
            .ToArray();

        if (sessions.Length == 0)
        {
            return new AgentActivityProviderSnapshot(
                provider,
                AgentActivityPhase.Unknown,
                TrafficLightColor.Unknown,
                0,
                0,
                null);
        }

        var colors = sessions.Select(session => ProjectColor(session, now)).ToArray();
        var aggregate = Aggregate(colors);
        return new AgentActivityProviderSnapshot(
            provider,
            ToPhase(aggregate),
            aggregate,
            sessions.Count(session => session.TurnActive || session.ActiveToolCount > 0),
            sessions.Sum(session => session.ActiveToolCount),
            sessions.Max(session => session.LastSignalAt));
    }

    /// <summary>
    /// Drops sessions that went silent for longer than any state can survive. Without this an agent
    /// that crashes before <c>SessionEnd</c> would keep a grey tile in the widget forever.
    /// </summary>
    private void PurgeExpiredSessions(DateTimeOffset now)
    {
        var maximumTtl = _freshness.ActiveStateTtl > _freshness.IdleStateTtl
            ? _freshness.ActiveStateTtl
            : _freshness.IdleStateTtl;
        var expired = _sessions
            .Where(pair => now - pair.Value.LastSignalAt > maximumTtl)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var identity in expired) _sessions.Remove(identity);
    }

    private void EvictOldestSessions(AgentActivityProvider provider)
    {
        var identities = _sessions
            .Where(pair => pair.Key.Provider == provider)
            .OrderByDescending(pair => pair.Value.LastSignalAt)
            .ThenByDescending(pair => pair.Value.FirstSeenAt)
            .Skip(MaximumSessionsPerProvider)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var identity in identities) _sessions.Remove(identity);
    }

    private TrafficLightColor ProjectColor(SessionState session, DateTimeOffset now)
    {
        var ttl = session.TurnActive || session.ActiveToolCount > 0
            ? _freshness.ActiveStateTtl
            : _freshness.IdleStateTtl;
        if (now - session.LastSignalAt > ttl) return TrafficLightColor.Unknown;
        if (session.ActiveToolCount > 0) return TrafficLightColor.Red;
        return session.TurnActive ? TrafficLightColor.Yellow : TrafficLightColor.Green;
    }

    private static TrafficLightColor Aggregate(IEnumerable<TrafficLightColor> colors)
    {
        var colorSet = colors.ToHashSet();
        if (colorSet.Contains(TrafficLightColor.Red)) return TrafficLightColor.Red;
        if (colorSet.Contains(TrafficLightColor.Yellow)) return TrafficLightColor.Yellow;
        if (colorSet.Contains(TrafficLightColor.Unknown)) return TrafficLightColor.Unknown;
        return colorSet.Contains(TrafficLightColor.Green)
            ? TrafficLightColor.Green
            : TrafficLightColor.Unknown;
    }

    private static AgentActivityPhase ToPhase(TrafficLightColor color) => color switch
    {
        TrafficLightColor.Red => AgentActivityPhase.ToolRunning,
        TrafficLightColor.Yellow => AgentActivityPhase.Thinking,
        TrafficLightColor.Green => AgentActivityPhase.Idle,
        _ => AgentActivityPhase.Unknown
    };

    private static bool IsTurnBoundary(AgentActivityEvent activityEvent) => activityEvent is
        AgentActivityEvent.SessionStarted or
        AgentActivityEvent.PromptSubmitted or
        AgentActivityEvent.TurnStopped;

    private readonly record struct SessionIdentity(AgentActivityProvider Provider, string SessionKey);

    private sealed class SessionState
    {
        private readonly HashSet<string> _activeToolKeys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _finishedToolKeys = new(StringComparer.Ordinal);
        private int _anonymousToolCount;

        public bool TurnActive { get; set; }
        public string? WorkspaceLabel { get; set; }
        public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.MinValue;
        public DateTimeOffset LastSignalAt { get; set; } = DateTimeOffset.MinValue;
        public DateTimeOffset LastTurnBoundaryAt { get; set; } = DateTimeOffset.MinValue;
        public int ActiveToolCount => _activeToolKeys.Count + _anonymousToolCount;

        public void Reset(bool turnActive)
        {
            TurnActive = turnActive;
            _activeToolKeys.Clear();
            _finishedToolKeys.Clear();
            _anonymousToolCount = 0;
        }

        public void StartTool(string? toolKey)
        {
            if (string.IsNullOrWhiteSpace(toolKey))
            {
                _anonymousToolCount++;
                return;
            }

            if (!_finishedToolKeys.Contains(toolKey)) _activeToolKeys.Add(toolKey);
        }

        public void FinishTool(string? toolKey)
        {
            if (string.IsNullOrWhiteSpace(toolKey))
            {
                if (_anonymousToolCount > 0) _anonymousToolCount--;
                return;
            }

            _activeToolKeys.Remove(toolKey);
            _finishedToolKeys.Add(toolKey);
        }
    }
}
