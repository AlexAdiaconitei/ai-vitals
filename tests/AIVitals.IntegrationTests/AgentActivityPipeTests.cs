using System.IO.Pipes;
using AIVitals.AgentActivity;

namespace AIVitals.IntegrationTests;

public sealed class AgentActivityPipeTests
{
    [Fact]
    public async Task Current_user_pipe_delivers_only_the_minimal_wire_message()
    {
        var pipeName = $"aivitals-activity-test-{Guid.NewGuid():N}";
        await using var monitor = new AgentActivityMonitor([AgentActivityProvider.ClaudeCode], pipeName: pipeName);
        var received = new TaskCompletionSource<AgentActivitySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.SnapshotChanged += snapshot =>
        {
            if (snapshot.Color == TrafficLightColor.Red) received.TrySetResult(snapshot);
        };
        monitor.Start();

        var wire = AgentActivityBridgeProtocol.Serialize(new AgentActivityWireMessage(
            1,
            AgentActivityProvider.ClaudeCode,
            AgentActivityEvent.ToolStarted,
            new string('a', 43),
            new string('b', 43),
            DateTimeOffset.UtcNow));
        await using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await pipe.ConnectAsync(timeout.Token);
        await AgentActivityBridgeProtocol.WriteFrameAsync(pipe, wire, timeout.Token);

        var snapshot = await received.Task.WaitAsync(timeout.Token);

        Assert.Equal(TrafficLightColor.Red, snapshot.Color);
        Assert.Equal(1, snapshot.Providers.Single().ActiveToolCount);
    }
    [Fact]
    public async Task A_session_label_reaches_the_snapshot_the_widget_renders()
    {
        var pipeName = $"aivitals-activity-test-{Guid.NewGuid():N}";
        await using var monitor = new AgentActivityMonitor([AgentActivityProvider.Codex], pipeName: pipeName);
        var received = new TaskCompletionSource<AgentActivitySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.SnapshotChanged += snapshot =>
        {
            if (snapshot.Sessions.Count > 0) received.TrySetResult(snapshot);
        };
        monitor.Start();

        var wire = AgentActivityBridgeProtocol.Serialize(new AgentActivityWireMessage(
            AgentActivityBridgeProtocol.CurrentSchemaVersion,
            AgentActivityProvider.Codex,
            AgentActivityEvent.PromptSubmitted,
            new string('c', 43),
            null,
            DateTimeOffset.UtcNow,
            "api-gateway"));
        await using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await pipe.ConnectAsync(timeout.Token);
        await AgentActivityBridgeProtocol.WriteFrameAsync(pipe, wire, timeout.Token);

        var snapshot = await received.Task.WaitAsync(timeout.Token);
        var session = Assert.Single(snapshot.Sessions);

        Assert.Equal("api-gateway", session.WorkspaceLabel);
        Assert.Equal(TrafficLightColor.Yellow, session.Color);
    }
}
