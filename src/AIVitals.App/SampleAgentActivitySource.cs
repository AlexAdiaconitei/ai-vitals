using AIVitals.AgentActivity;

namespace AIVitals.App;

/// <summary>
/// Feeds the settings preview a fixed scene. The preview must never subscribe to the real named
/// pipe, both because it would show whatever the user happens to be running and because the
/// preview exists to explain the layout, not to report live activity.
/// </summary>
public sealed class SampleAgentActivitySource : IAgentActivitySource
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public AgentActivitySnapshot Current { get; private set; } = Build([AgentActivityProvider.ClaudeCode, AgentActivityProvider.Codex]);

    public event Action<AgentActivitySnapshot>? SnapshotChanged;

    public void Show(IReadOnlyList<AgentActivityProvider> providers)
    {
        Current = Build(providers);
        SnapshotChanged?.Invoke(Current);
    }

    private static AgentActivitySnapshot Build(IReadOnlyList<AgentActivityProvider> providers)
    {
        var sessions = new List<AgentActivitySessionSnapshot>();
        foreach (var provider in providers)
        {
            var scenes = provider == AgentActivityProvider.ClaudeCode
                ? new[] { ("AI Vitals", TrafficLightColor.Red, 2), ("docs-site", TrafficLightColor.Green, 0) }
                : new[] { ("api-gateway", TrafficLightColor.Yellow, 0) };

            for (var index = 0; index < scenes.Length; index++)
            {
                var (label, color, tools) = scenes[index];
                sessions.Add(new AgentActivitySessionSnapshot(
                    provider,
                    $"{provider}-sample-{index}",
                    label,
                    ToPhase(color),
                    color,
                    tools,
                    CapturedAt,
                    CapturedAt));
            }
        }

        var providerSnapshots = providers
            .Select(provider =>
            {
                var owned = sessions.Where(session => session.Provider == provider).ToArray();
                var color = owned.Length == 0 ? TrafficLightColor.Unknown : owned.Max(session => session.Color);
                return new AgentActivityProviderSnapshot(
                    provider,
                    ToPhase(color),
                    color,
                    owned.Length,
                    owned.Sum(session => session.ActiveToolCount),
                    owned.Length == 0 ? null : CapturedAt);
            })
            .ToArray();

        var aggregate = sessions.Count == 0 ? TrafficLightColor.Unknown : sessions.Max(session => session.Color);
        return new AgentActivitySnapshot(ToPhase(aggregate), aggregate, providerSnapshots, sessions, CapturedAt);
    }

    private static AgentActivityPhase ToPhase(TrafficLightColor color) => color switch
    {
        TrafficLightColor.Red => AgentActivityPhase.ToolRunning,
        TrafficLightColor.Yellow => AgentActivityPhase.Thinking,
        TrafficLightColor.Green => AgentActivityPhase.Idle,
        _ => AgentActivityPhase.Unknown
    };
}
