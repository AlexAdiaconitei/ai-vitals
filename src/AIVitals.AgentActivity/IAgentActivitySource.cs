namespace AIVitals.AgentActivity;

/// <summary>
/// The activity feed a traffic light widget renders. Keeping this separate from
/// <see cref="AgentActivityMonitor"/> lets the settings preview show a sample without ever
/// subscribing to the real named pipe.
/// </summary>
public interface IAgentActivitySource
{
    AgentActivitySnapshot Current { get; }

    event Action<AgentActivitySnapshot>? SnapshotChanged;
}
