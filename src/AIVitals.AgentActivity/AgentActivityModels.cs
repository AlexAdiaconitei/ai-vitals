namespace AIVitals.AgentActivity;

public enum AgentActivityProvider
{
    ClaudeCode,
    Codex
}

public enum AgentActivityEvent
{
    SessionStarted,
    PromptSubmitted,
    ToolStarted,
    ToolFinished,
    TurnStopped,
    SessionEnded
}

public enum AgentActivityPhase
{
    Unknown,
    Idle,
    Thinking,
    ToolRunning
}

public enum TrafficLightColor
{
    Unknown,
    Green,
    Yellow,
    Red
}

public sealed record AgentActivitySignal(
    AgentActivityProvider Provider,
    string SessionKey,
    AgentActivityEvent Event,
    string? ToolKey,
    DateTimeOffset OccurredAt,
    string? WorkspaceLabel = null);

/// <summary>
/// One traffic light in the activity widget. <see cref="WorkspaceLabel"/> is the leaf directory
/// name of the agent session, and is only ever populated when the user opted into session labels.
/// </summary>
public sealed record AgentActivitySessionSnapshot(
    AgentActivityProvider Provider,
    string SessionKey,
    string? WorkspaceLabel,
    AgentActivityPhase Phase,
    TrafficLightColor Color,
    int ActiveToolCount,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSignalAt,
    // When the turn now running began, or null when no prompt was seen.
    DateTimeOffset? TurnStartedAt,
    // When this application first saw the running turn, for a turn that was already under way
    // before it started listening. It is a lower bound, never the real beginning.
    DateTimeOffset? TurnObservedFrom);

public sealed record AgentActivityProviderSnapshot(
    AgentActivityProvider Provider,
    AgentActivityPhase Phase,
    TrafficLightColor Color,
    int ActiveSessionCount,
    int ActiveToolCount,
    DateTimeOffset? LastSignalAt);

public sealed record AgentActivitySnapshot(
    AgentActivityPhase Phase,
    TrafficLightColor Color,
    IReadOnlyList<AgentActivityProviderSnapshot> Providers,
    IReadOnlyList<AgentActivitySessionSnapshot> Sessions,
    DateTimeOffset CapturedAt)
{
    public static AgentActivitySnapshot Unknown(DateTimeOffset capturedAt, IEnumerable<AgentActivityProvider>? providers = null)
    {
        var included = (providers ?? Enum.GetValues<AgentActivityProvider>()).Distinct().ToArray();
        return new AgentActivitySnapshot(
            AgentActivityPhase.Unknown,
            TrafficLightColor.Unknown,
            included.Select(provider => new AgentActivityProviderSnapshot(
                provider,
                AgentActivityPhase.Unknown,
                TrafficLightColor.Unknown,
                0,
                0,
                null)).ToArray(),
            [],
            capturedAt);
    }
}

public sealed record AgentActivityFreshness(
    TimeSpan ActiveStateTtl,
    TimeSpan IdleStateTtl)
{
    public static AgentActivityFreshness Default { get; } = new(
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1));
}
