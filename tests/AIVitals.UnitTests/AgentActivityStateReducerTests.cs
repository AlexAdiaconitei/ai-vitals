using AIVitals.AgentActivity;

namespace AIVitals.UnitTests;

public sealed class AgentActivityStateReducerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Starts_unknown_instead_of_assuming_idle()
    {
        var snapshot = new AgentActivityStateReducer().Snapshot(Now, [AgentActivityProvider.ClaudeCode]);

        Assert.Equal(TrafficLightColor.Unknown, snapshot.Color);
        Assert.Equal(AgentActivityPhase.Unknown, snapshot.Phase);
    }

    [Fact]
    public void Follows_prompt_tool_and_stop_lifecycle()
    {
        var reducer = new AgentActivityStateReducer();

        reducer.Apply(Signal(AgentActivityEvent.PromptSubmitted));
        Assert.Equal(TrafficLightColor.Yellow, SnapshotClaude(reducer, Now).Color);

        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool-1", 1));
        Assert.Equal(TrafficLightColor.Red, SnapshotClaude(reducer, Now.AddSeconds(1)).Color);

        reducer.Apply(Signal(AgentActivityEvent.ToolFinished, "tool-1", 2));
        Assert.Equal(TrafficLightColor.Yellow, SnapshotClaude(reducer, Now.AddSeconds(2)).Color);

        reducer.Apply(Signal(AgentActivityEvent.TurnStopped, seconds: 3));
        Assert.Equal(TrafficLightColor.Green, SnapshotClaude(reducer, Now.AddSeconds(3)).Color);
    }

    [Fact]
    public void Remains_red_until_all_parallel_tools_finish()
    {
        var reducer = new AgentActivityStateReducer();
        reducer.Apply(Signal(AgentActivityEvent.PromptSubmitted));
        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool-1", 1));
        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool-2", 2));

        reducer.Apply(Signal(AgentActivityEvent.ToolFinished, "tool-1", 3));
        var whileSecondRuns = reducer.Snapshot(Now.AddSeconds(3));
        Assert.Equal(TrafficLightColor.Red, whileSecondRuns.Color);
        Assert.Equal(1, whileSecondRuns.Providers.Single(p => p.Provider == AgentActivityProvider.ClaudeCode).ActiveToolCount);

        reducer.Apply(Signal(AgentActivityEvent.ToolFinished, "tool-2", 4));
        Assert.Equal(TrafficLightColor.Yellow, reducer.Snapshot(Now.AddSeconds(4)).Color);
    }

    [Fact]
    public void Accepts_parallel_tool_starts_that_arrive_out_of_timestamp_order()
    {
        var reducer = new AgentActivityStateReducer();
        reducer.Apply(Signal(AgentActivityEvent.PromptSubmitted));
        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool-later", 2));
        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool-earlier", 1));

        var provider = SnapshotClaude(reducer, Now.AddSeconds(2)).Providers.Single();

        Assert.Equal(TrafficLightColor.Red, provider.Color);
        Assert.Equal(2, provider.ActiveToolCount);
    }

    [Fact]
    public void Aggregates_sessions_by_safety_priority()
    {
        var reducer = new AgentActivityStateReducer();
        reducer.Apply(Signal(AgentActivityEvent.TurnStopped, session: "idle"));
        reducer.Apply(Signal(AgentActivityEvent.PromptSubmitted, session: "thinking", seconds: 1));
        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool", 2, "working"));

        var snapshot = reducer.Snapshot(Now.AddSeconds(2), [AgentActivityProvider.ClaudeCode]);

        Assert.Equal(TrafficLightColor.Red, snapshot.Color);
        Assert.Equal(3, snapshot.Providers.Single().ActiveSessionCount + 1);
    }

    [Fact]
    public void Ignores_duplicate_and_late_tool_start_events()
    {
        var reducer = new AgentActivityStateReducer();
        reducer.Apply(Signal(AgentActivityEvent.PromptSubmitted));
        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool-1", 1));
        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool-1", 2));
        reducer.Apply(Signal(AgentActivityEvent.ToolFinished, "tool-1", 3));
        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool-1", 4));

        var snapshot = reducer.Snapshot(Now.AddSeconds(4));

        Assert.Equal(TrafficLightColor.Yellow, snapshot.Color);
        Assert.Equal(0, snapshot.Providers.Single(p => p.Provider == AgentActivityProvider.ClaudeCode).ActiveToolCount);
    }

    [Fact]
    public void Stale_activity_becomes_unknown_never_green()
    {
        var reducer = new AgentActivityStateReducer(new AgentActivityFreshness(
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(10)));
        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool-1"));

        var snapshot = reducer.Snapshot(Now.AddMinutes(6), [AgentActivityProvider.ClaudeCode]);

        Assert.Equal(TrafficLightColor.Unknown, snapshot.Color);
    }

    [Fact]
    public void Session_end_removes_only_that_session()
    {
        var reducer = new AgentActivityStateReducer();
        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool-1", session: "one"));
        reducer.Apply(Signal(AgentActivityEvent.PromptSubmitted, session: "two", seconds: 1));
        reducer.Apply(Signal(AgentActivityEvent.SessionEnded, session: "one", seconds: 2));

        Assert.Equal(TrafficLightColor.Yellow, reducer.Snapshot(Now.AddSeconds(2)).Color);
    }

    [Fact]
    public void Projects_one_entry_per_live_session_in_a_stable_order()
    {
        var reducer = new AgentActivityStateReducer();
        reducer.Apply(Signal(AgentActivityEvent.PromptSubmitted, session: "first", workspace: "alpha"));
        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool-1", session: "second", seconds: 1, workspace: "beta"));
        reducer.Apply(new AgentActivitySignal(
            AgentActivityProvider.Codex,
            "third",
            AgentActivityEvent.TurnStopped,
            null,
            Now.AddSeconds(2),
            "gamma"));

        var snapshot = reducer.Snapshot(
            Now.AddSeconds(3),
            [AgentActivityProvider.ClaudeCode, AgentActivityProvider.Codex]);

        Assert.Equal(
            ["first", "second", "third"],
            snapshot.Sessions.Select(session => session.SessionKey));
        Assert.Equal(
            ["alpha", "beta", "gamma"],
            snapshot.Sessions.Select(session => session.WorkspaceLabel));
        Assert.Equal(
            [TrafficLightColor.Yellow, TrafficLightColor.Red, TrafficLightColor.Green],
            snapshot.Sessions.Select(session => session.Color));
        Assert.Equal(1, snapshot.Sessions[1].ActiveToolCount);
    }

    [Fact]
    public void Session_order_follows_the_requested_provider_order()
    {
        var reducer = new AgentActivityStateReducer();
        reducer.Apply(Signal(AgentActivityEvent.PromptSubmitted, session: "claude"));
        reducer.Apply(new AgentActivitySignal(
            AgentActivityProvider.Codex,
            "codex",
            AgentActivityEvent.PromptSubmitted,
            null,
            Now.AddSeconds(1)));

        var snapshot = reducer.Snapshot(
            Now.AddSeconds(2),
            [AgentActivityProvider.Codex, AgentActivityProvider.ClaudeCode]);

        Assert.Equal(["codex", "claude"], snapshot.Sessions.Select(session => session.SessionKey));
    }

    [Fact]
    public void Sessions_excluded_providers_are_left_out()
    {
        var reducer = new AgentActivityStateReducer();
        reducer.Apply(Signal(AgentActivityEvent.PromptSubmitted));

        Assert.Empty(reducer.Snapshot(Now, [AgentActivityProvider.Codex]).Sessions);
    }

    [Fact]
    public void A_session_that_never_reports_its_end_is_purged_instead_of_lingering()
    {
        var reducer = new AgentActivityStateReducer(new AgentActivityFreshness(
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(10)));
        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool-1"));

        Assert.Single(reducer.Snapshot(Now.AddMinutes(6), [AgentActivityProvider.ClaudeCode]).Sessions);
        Assert.Empty(reducer.Snapshot(Now.AddMinutes(11), [AgentActivityProvider.ClaudeCode]).Sessions);
    }

    [Fact]
    public void Session_count_per_provider_is_capped_by_dropping_the_least_recent()
    {
        var reducer = new AgentActivityStateReducer();
        var total = AgentActivityStateReducer.MaximumSessionsPerProvider + 3;
        for (var index = 0; index < total; index++)
            reducer.Apply(Signal(AgentActivityEvent.PromptSubmitted, session: $"session-{index:00}", seconds: index));

        var snapshot = reducer.Snapshot(Now.AddSeconds(total), [AgentActivityProvider.ClaudeCode]);

        Assert.Equal(AgentActivityStateReducer.MaximumSessionsPerProvider, snapshot.Sessions.Count);
        Assert.DoesNotContain("session-00", snapshot.Sessions.Select(session => session.SessionKey));
        Assert.Contains($"session-{total - 1:00}", snapshot.Sessions.Select(session => session.SessionKey));
    }

    [Fact]
    public void A_later_workspace_label_replaces_the_earlier_one()
    {
        var reducer = new AgentActivityStateReducer();
        reducer.Apply(Signal(AgentActivityEvent.PromptSubmitted, workspace: "alpha"));
        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool-1", seconds: 1, workspace: "beta"));
        reducer.Apply(Signal(AgentActivityEvent.ToolFinished, "tool-1", seconds: 2));

        var snapshot = reducer.Snapshot(Now.AddSeconds(3), [AgentActivityProvider.ClaudeCode]);

        Assert.Equal("beta", Assert.Single(snapshot.Sessions).WorkspaceLabel);
    }

    [Fact]
    public void A_prompt_starts_the_turn_clock_and_a_stop_clears_it()
    {
        var reducer = new AgentActivityStateReducer();
        reducer.Apply(Signal(AgentActivityEvent.PromptSubmitted, seconds: 5));

        Assert.Equal(
            Now.AddSeconds(5),
            Assert.Single(SnapshotClaude(reducer, Now.AddSeconds(6)).Sessions).TurnStartedAt);

        reducer.Apply(Signal(AgentActivityEvent.TurnStopped, seconds: 9));

        Assert.Null(Assert.Single(SnapshotClaude(reducer, Now.AddSeconds(10)).Sessions).TurnStartedAt);
    }

    [Fact]
    public void Tools_within_a_turn_do_not_restart_its_clock()
    {
        var reducer = new AgentActivityStateReducer();
        reducer.Apply(Signal(AgentActivityEvent.PromptSubmitted, seconds: 1));
        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool-1", seconds: 4));
        reducer.Apply(Signal(AgentActivityEvent.ToolFinished, "tool-1", seconds: 8));

        Assert.Equal(
            Now.AddSeconds(1),
            Assert.Single(SnapshotClaude(reducer, Now.AddSeconds(9)).Sessions).TurnStartedAt);
    }

    [Fact]
    public void A_turn_already_running_when_the_app_starts_is_timed_from_first_sight_as_a_lower_bound()
    {
        var reducer = new AgentActivityStateReducer();
        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool-1", seconds: 2));

        var session = Assert.Single(SnapshotClaude(reducer, Now.AddSeconds(3)).Sessions);

        Assert.Equal(TrafficLightColor.Red, session.Color);
        Assert.Null(session.TurnStartedAt);
        Assert.Equal(Now.AddSeconds(2), session.TurnObservedFrom);
    }

    [Fact]
    public void Later_tools_do_not_push_the_lower_bound_forward()
    {
        var reducer = new AgentActivityStateReducer();
        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool-1", seconds: 2));
        reducer.Apply(Signal(AgentActivityEvent.ToolFinished, "tool-1", seconds: 5));
        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool-2", seconds: 7));

        Assert.Equal(
            Now.AddSeconds(2),
            Assert.Single(SnapshotClaude(reducer, Now.AddSeconds(8)).Sessions).TurnObservedFrom);
    }

    [Fact]
    public void A_prompt_supersedes_the_lower_bound_with_a_real_beginning()
    {
        var reducer = new AgentActivityStateReducer();
        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool-1", seconds: 2));
        reducer.Apply(Signal(AgentActivityEvent.TurnStopped, seconds: 6));
        reducer.Apply(Signal(AgentActivityEvent.PromptSubmitted, seconds: 9));
        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool-2", seconds: 11));

        var session = Assert.Single(SnapshotClaude(reducer, Now.AddSeconds(12)).Sessions);

        Assert.Equal(Now.AddSeconds(9), session.TurnStartedAt);
        Assert.Null(session.TurnObservedFrom);
    }

    [Fact]
    public void Stopping_a_turn_clears_the_lower_bound_too()
    {
        var reducer = new AgentActivityStateReducer();
        reducer.Apply(Signal(AgentActivityEvent.ToolStarted, "tool-1", seconds: 2));
        reducer.Apply(Signal(AgentActivityEvent.TurnStopped, seconds: 5));

        var session = Assert.Single(SnapshotClaude(reducer, Now.AddSeconds(6)).Sessions);

        Assert.Null(session.TurnStartedAt);
        Assert.Null(session.TurnObservedFrom);
    }

    [Fact]
    public void A_new_session_event_clears_a_turn_left_over_from_the_previous_one()
    {
        var reducer = new AgentActivityStateReducer();
        reducer.Apply(Signal(AgentActivityEvent.PromptSubmitted, seconds: 1));
        reducer.Apply(Signal(AgentActivityEvent.SessionStarted, seconds: 6));

        Assert.Null(Assert.Single(SnapshotClaude(reducer, Now.AddSeconds(7)).Sessions).TurnStartedAt);
    }

    [Fact]
    public void A_second_prompt_restarts_the_turn_clock()
    {
        var reducer = new AgentActivityStateReducer();
        reducer.Apply(Signal(AgentActivityEvent.PromptSubmitted, seconds: 1));
        reducer.Apply(Signal(AgentActivityEvent.TurnStopped, seconds: 4));
        reducer.Apply(Signal(AgentActivityEvent.PromptSubmitted, seconds: 20));

        Assert.Equal(
            Now.AddSeconds(20),
            Assert.Single(SnapshotClaude(reducer, Now.AddSeconds(21)).Sessions).TurnStartedAt);
    }

    private static AgentActivitySignal Signal(
        AgentActivityEvent activityEvent,
        string? tool = null,
        int seconds = 0,
        string session = "session-1",
        string? workspace = null) =>
        new(AgentActivityProvider.ClaudeCode, session, activityEvent, tool, Now.AddSeconds(seconds), workspace);

    private static AgentActivitySnapshot SnapshotClaude(AgentActivityStateReducer reducer, DateTimeOffset now) =>
        reducer.Snapshot(now, [AgentActivityProvider.ClaudeCode]);
}
