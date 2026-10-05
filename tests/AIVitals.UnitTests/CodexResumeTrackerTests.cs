using System.Text.Json;
using AIVitals.Application;
using AIVitals.Domain;

namespace AIVitals.UnitTests;

public sealed class CodexResumeTrackerTests
{
    private static readonly DateTimeOffset Reset = new(2026, 10, 3, 2, 15, 15, TimeSpan.Zero);
    private static readonly DateTimeOffset BeforeReset = Reset.AddHours(-1);
    private static readonly DateTimeOffset AfterReset = Reset.AddMinutes(1);
    private static readonly IReadOnlySet<string> NoneDismissed = new HashSet<string>();
    private static PausedThreadInfo Thread(string id, DateTimeOffset? blockedAt = null, string? turn = null) =>
        new(id, id, $@"D:\work\{id}", turn ?? $"turn-{id}", blockedAt ?? BeforeReset);
    private static QuotaBandSnapshot Band(decimal used, bool current = true, DateTimeOffset? reset = null,
        string source = "codex:primary", TimeSpan? period = null, DateTimeOffset? observedAt = null)
    {
        var end = reset ?? Reset;
        var duration = period ?? TimeSpan.FromHours(5);
        var observation = new UsageObservation(Guid.NewGuid(), "codex", "connection", UsageCapability.QuotaWindow,
            used, "percent", observedAt ?? (used < 100 ? AfterReset : BeforeReset), source, DataQuality.Exact,
            new QuotaWindow(end - duration, end));
        return new(QuotaBandKind.Immediate, QuotaPeriod.FiveHours, used, true, current, end, duration, observation);
    }
    private static IReadOnlyList<QuotaBandSnapshot> Exhausted() => [Band(100)];
    private static IReadOnlyList<QuotaBandSnapshot> Available() => [Band(0, reset: Reset.AddHours(5))];
    private static CodexResumeTracker Waiting(params PausedThreadInfo[] threads)
    {
        var tracker = new CodexResumeTracker();
        tracker.Evaluate(threads, Exhausted(), true, NoneDismissed, BeforeReset);
        return tracker;
    }

    [Fact]
    public void Waiting_thread_reports_its_reset()
    {
        var thread = Thread("a");
        var decision = new CodexResumeTracker().Evaluate([thread], Exhausted(), false, NoneDismissed, BeforeReset);
        Assert.Equal(Reset, Assert.Single(decision.Threads).ResetsAtUtc);
        Assert.Null(decision.Launch);
        Assert.Empty(decision.Announce);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Automatic_resume_recovers_a_pending_task_discovered_after_its_reset(bool persisted)
    {
        var now = new DateTimeOffset(2026, 10, 5, 0, 25, 0, TimeSpan.Zero);
        var thread = Thread("pending", new DateTimeOffset(2026, 10, 3, 0, 21, 48, TimeSpan.Zero));
        var state = persisted ? new CodexResumeState(
            [new(thread.ThreadId, thread.BlockedTurnId, thread.BlockedAtUtc, [], Armed: false, Phase: PausedThreadPhase.Ready)],
            [], LastEvaluationUtc: now.AddMinutes(-1)) : null;
        var tracker = new CodexResumeTracker(state: state);
        var bands = new[] { Band(0, reset: now.AddHours(5), observedAt: now) };
        var decision = tracker.Evaluate([thread], bands, true, NoneDismissed, now);
        Assert.Equal(thread, decision.Launch);
    }

    [Fact]
    public void Starting_the_app_hours_into_the_same_five_hour_block_still_resumes_after_reset()
    {
        var thread = Thread("a", Reset.AddHours(-3));
        var tracker = new CodexResumeTracker();
        var waiting = tracker.Evaluate([thread], Exhausted(), true, NoneDismissed, BeforeReset);
        Assert.Equal(Reset, Assert.Single(waiting.Threads).ResetsAtUtc);
        Assert.True(Assert.Single(waiting.Threads).Armed);
        Assert.Equal(thread, tracker.Evaluate([thread], Available(), true, NoneDismissed, AfterReset).Launch);
    }

    [Fact]
    public void Five_hour_failure_does_not_bind_to_a_later_five_hour_window()
    {
        var thread = Thread("old", Reset.AddHours(-6));
        var decision = new CodexResumeTracker().Evaluate([thread], Exhausted(), true, NoneDismissed, BeforeReset);
        Assert.Null(Assert.Single(decision.Threads).ResetsAtUtc);
        Assert.True(Assert.Single(decision.Threads).Armed);
        Assert.Null(decision.Launch);
    }

    [Fact]
    public void A_stale_exhausted_window_is_not_cleared_by_a_fresh_other_window()
    {
        var thread = Thread("a");
        var decision = Waiting(thread).Evaluate([thread],
            [Band(100, current: false), Band(20, reset: Reset.AddDays(7), source: "codex:secondary")],
            true, NoneDismissed, BeforeReset.AddMinutes(1));
        Assert.Null(decision.Launch);
        Assert.Empty(decision.Announce);
    }

    [Fact]
    public void Missing_the_blocking_bucket_cannot_verify_a_reset()
    {
        var thread = Thread("a");
        var decision = Waiting(thread).Evaluate([thread], [Band(0, source: "codex:secondary")], true, NoneDismissed, AfterReset);
        Assert.Null(decision.Launch);
    }

    [Fact]
    public void Reset_requires_a_full_fresh_snapshot_after_the_reset_and_margin()
    {
        var thread = Thread("a");
        var tracker = Waiting(thread);
        Assert.Null(tracker.Evaluate([thread], [Band(0, reset: Reset.AddHours(5))], true, NoneDismissed, Reset.AddSeconds(30)).Launch);
        Assert.Null(tracker.Evaluate([thread], [Band(0, reset: Reset.AddHours(5), observedAt: BeforeReset)], true, NoneDismissed, AfterReset).Launch);
        Assert.Equal(thread, tracker.Evaluate([thread], Available(), true, NoneDismissed, AfterReset).Launch);
    }

    [Fact]
    public void Notification_is_once_per_blocked_turn()
    {
        var thread = Thread("a");
        var tracker = Waiting(thread);
        Assert.Equal([thread], tracker.Evaluate([thread], Available(), false, NoneDismissed, AfterReset).Announce);
        Assert.Empty(tracker.Evaluate([thread], Available(), false, NoneDismissed, AfterReset.AddMinutes(1)).Announce);
    }

    [Fact]
    public void An_unobserved_reset_does_not_prevent_an_authorized_continuation_with_fresh_quota()
    {
        var thread = Thread("a");
        var decision = new CodexResumeTracker().Evaluate([thread], Available(), true, NoneDismissed, AfterReset);
        Assert.Equal(thread, decision.Launch);
        Assert.Equal([thread], decision.Announce);
        Assert.Equal(PausedThreadPhase.Queued, Assert.Single(decision.Threads).Phase);
    }

    [Fact]
    public void Pending_authorization_survives_json_round_trip_and_restart()
    {
        var thread = Thread("a");
        var tracker = Waiting(thread);
        var state = JsonSerializer.Deserialize<CodexResumeState>(JsonSerializer.Serialize(tracker.State));
        var restarted = new CodexResumeTracker(state: state);
        Assert.Equal(thread, restarted.Evaluate([thread], Available(), false, NoneDismissed, AfterReset).Launch);
    }

    [Fact]
    public void Waking_hours_after_reset_preserves_authorization_and_continues_with_fresh_quota()
    {
        var thread = Thread("a");
        var decision = Waiting(thread).Evaluate([thread], Available(), true, NoneDismissed, Reset.AddHours(3));
        Assert.Equal(thread, decision.Launch);
        Assert.True(Assert.Single(decision.Threads).Armed);
        Assert.Equal(PausedThreadPhase.Queued, Assert.Single(decision.Threads).Phase);
    }

    [Fact]
    public void A_queued_thread_can_still_continue_after_a_long_sleep()
    {
        var thread = Thread("a");
        var tracker = Waiting(thread);
        tracker.Evaluate([thread], Available(), true, NoneDismissed, AfterReset);
        var decision = tracker.Evaluate([thread], Available(), true, NoneDismissed, Reset.AddHours(1));
        Assert.Equal(thread, decision.Launch);
        Assert.Equal(PausedThreadPhase.Queued, Assert.Single(decision.Threads).Phase);
    }

    [Fact]
    public void Weekly_exhaustion_does_not_adopt_an_abandoned_five_hour_failure()
    {
        var decision = new CodexResumeTracker().Evaluate([Thread("old", BeforeReset.AddDays(-2))],
            [Band(100, reset: Reset.AddDays(3), source: "codex:secondary", period: TimeSpan.FromDays(7))],
            true, NoneDismissed, BeforeReset);
        Assert.Null(decision.Launch);
        Assert.Null(Assert.Single(decision.Threads).ResetsAtUtc);
    }

    [Fact]
    public void Moved_reset_is_waited_for_instead_of_using_the_original_deadline()
    {
        var thread = Thread("a");
        var tracker = Waiting(thread);
        tracker.Evaluate([thread], [Band(100, reset: Reset.AddHours(1))], true, NoneDismissed, Reset);
        Assert.Null(tracker.Evaluate([thread], Available(), true, NoneDismissed, AfterReset).Launch);
    }

    [Fact]
    public void Per_thread_arming_works_with_the_global_default_off()
    {
        var thread = Thread("a");
        var tracker = new CodexResumeTracker();
        tracker.Evaluate([thread], Exhausted(), false, NoneDismissed, BeforeReset);
        tracker.SetArmed(thread.BlockedTurnId, true);
        Assert.Equal(thread, tracker.Evaluate([thread], Available(), false, NoneDismissed, AfterReset).Launch);
    }

    [Fact]
    public void Enabling_automatic_resume_includes_existing_pending_threads()
    {
        var thread = Thread("a");
        var tracker = new CodexResumeTracker();
        tracker.Evaluate([thread], Exhausted(), false, NoneDismissed, BeforeReset);
        Assert.Equal(thread, tracker.Evaluate([thread], Available(), true, NoneDismissed, AfterReset).Launch);
    }

    [Fact]
    public void A_new_quota_failure_can_resume_again_when_automatic_resume_remains_enabled()
    {
        var thread = Thread("a");
        var tracker = Waiting(thread);
        tracker.ReportLaunch(thread, PausedThreadLaunchOutcome.Launched, AfterReset);
        var again = Thread("a", AfterReset.AddMinutes(1), "new-failure");
        var decision = tracker.Evaluate([again], [Band(100, reset: Reset.AddHours(5))], true, NoneDismissed, AfterReset.AddMinutes(1));
        Assert.True(Assert.Single(decision.Threads).Armed);
        Assert.Null(decision.Launch);
        var later = Reset.AddHours(5).AddMinutes(1);
        Assert.Equal(again, tracker.Evaluate([again], [Band(0, reset: Reset.AddHours(10), observedAt: later)],
            true, NoneDismissed, later).Launch);
    }

    [Fact]
    public void Armed_threads_are_spaced_and_cap_is_preserved_across_restart()
    {
        var threads = new[] { Thread("a"), Thread("b"), Thread("c"), Thread("d") };
        var tracker = new CodexResumeTracker(maxLaunchesPerReset: 3);
        tracker.Evaluate(threads, Exhausted(), true, NoneDismissed, BeforeReset);
        for (var i = 0; i < 3; i++)
        {
            var now = AfterReset.AddMinutes(i * 2);
            var decision = tracker.Evaluate(threads, Available(), true, NoneDismissed, now);
            Assert.NotNull(decision.Launch);
            tracker.ReportLaunch(decision.Launch!, PausedThreadLaunchOutcome.Launched, now);
            Assert.Null(tracker.Evaluate(threads, Available(), true, NoneDismissed, now.AddMinutes(1)).Launch);
            tracker = new(state: tracker.State, maxLaunchesPerReset: 3);
        }
        Assert.Null(tracker.Evaluate(threads, Available(), true, NoneDismissed, AfterReset.AddMinutes(10)).Launch);
    }

    [Fact]
    public void Held_threads_retry_without_consuming_the_cap_or_expiring_the_authorization()
    {
        var thread = Thread("a");
        var tracker = Waiting(thread);
        tracker.Evaluate([thread], Available(), true, NoneDismissed, AfterReset);
        tracker.ReportLaunch(thread, PausedThreadLaunchOutcome.HeldByAnotherApp, AfterReset);
        Assert.Null(tracker.Evaluate([thread], Available(), true, NoneDismissed, AfterReset.AddMinutes(1)).Launch);
        Assert.Equal(thread, tracker.Evaluate([thread], Available(), true, NoneDismissed, AfterReset.AddMinutes(5)).Launch);
        Assert.All(tracker.State.LaunchCounts, count => Assert.Equal(0, count.Count));
        Assert.Equal(thread, tracker.Evaluate([thread], Available(), true, NoneDismissed, AfterReset.AddHours(3)).Launch);
    }

    [Fact]
    public void Dismissed_or_manually_continued_threads_are_disarmed()
    {
        var thread = Thread("a");
        var tracker = Waiting(thread);
        var decision = tracker.Evaluate([thread], Available(), true, new HashSet<string> { thread.BlockedTurnId }, AfterReset);
        Assert.Empty(decision.Threads);
        Assert.False(Assert.Single(tracker.State.Entries).Armed);
        Assert.Null(decision.Launch);
    }

    [Fact]
    public void Explicit_disarming_prevents_continuation()
    {
        var thread = Thread("a");
        var tracker = Waiting(thread);
        tracker.SetArmed(thread.BlockedTurnId, false);
        Assert.Null(tracker.Evaluate([thread], Available(), true, NoneDismissed, AfterReset).Launch);
        tracker = new(state: JsonSerializer.Deserialize<CodexResumeState>(JsonSerializer.Serialize(tracker.State)));
        Assert.Null(tracker.Evaluate([thread], Available(), true, NoneDismissed, AfterReset.AddMinutes(1)).Launch);
    }

    [Fact]
    public void Global_off_and_on_does_not_lose_manual_exclusions()
    {
        var a = Thread("a");
        var b = Thread("b");
        var tracker = Waiting(a, b);
        tracker.SetArmed(a.BlockedTurnId, false);
        tracker.DisarmAll();
        Assert.Null(tracker.Evaluate([a, b], Available(), false, NoneDismissed, AfterReset).Launch);
        Assert.Equal(b, tracker.Evaluate([a, b], Available(), true, NoneDismissed, AfterReset).Launch);
        Assert.False(tracker.State.Entries.Single(entry => entry.ThreadId == a.ThreadId).Armed);
    }

    [Fact]
    public void A_legacy_unarmed_task_with_a_known_reset_preserves_its_exclusion()
    {
        var thread = Thread("a");
        var state = new CodexResumeState([new(thread.ThreadId, thread.BlockedTurnId, thread.BlockedAtUtc,
            [new("codex:primary", Reset)], Armed: false, Phase: PausedThreadPhase.Ready)], []);
        var tracker = new CodexResumeTracker(state: state);
        Assert.Null(tracker.Evaluate([thread], Available(), true, NoneDismissed, AfterReset).Launch);
        Assert.True(Assert.Single(tracker.State.Entries).UserDisarmed);
    }

    [Fact]
    public void A_manually_armed_old_task_can_continue_without_a_recorded_reset()
    {
        var thread = Thread("old", BeforeReset.AddDays(-2));
        var tracker = new CodexResumeTracker();
        tracker.Evaluate([thread], Available(), false, NoneDismissed, AfterReset);
        tracker.SetArmed(thread.BlockedTurnId, true);
        Assert.Equal(thread, tracker.Evaluate([thread], Available(), false, NoneDismissed, AfterReset).Launch);
    }

    [Fact]
    public void More_than_three_pending_tasks_can_continue_with_spacing_and_available_quota()
    {
        var threads = Enumerable.Range(0, 5).Select(i => Thread(i.ToString())).ToArray();
        var tracker = Waiting(threads);
        for (var i = 0; i < threads.Length; i++)
        {
            var now = AfterReset.AddMinutes(i * 2);
            var decision = tracker.Evaluate(threads, Available(), true, NoneDismissed, now);
            Assert.Equal(threads[i], decision.Launch);
            tracker.ReportLaunch(decision.Launch!, PausedThreadLaunchOutcome.Launched, now);
            Assert.Null(tracker.Evaluate(threads, Available(), true, NoneDismissed, now.AddMinutes(1)).Launch);
            tracker = new(state: tracker.State);
        }
    }

    [Fact]
    public void An_ambiguous_previous_send_prevents_automatic_continuation_of_the_same_thread()
    {
        var thread = Thread("a");
        var tracker = Waiting(thread);
        tracker.ReportLaunch(thread, PausedThreadLaunchOutcome.OutcomeUnknown, AfterReset);
        var another = Thread("a", AfterReset.AddMinutes(1), "another-turn");
        var decision = tracker.Evaluate([another], Available(), true, NoneDismissed, AfterReset.AddMinutes(2));
        Assert.Null(decision.Launch);
        Assert.False(Assert.Single(decision.Threads).Armed);
    }

    [Fact]
    public void A_crash_after_reserving_the_send_never_resends_on_restart()
    {
        var thread = Thread("a");
        var tracker = Waiting(thread);
        tracker.BeginLaunch(thread, AfterReset);
        var restarted = new CodexResumeTracker(state: tracker.State);
        var decision = restarted.Evaluate([thread], Available(), true, NoneDismissed, AfterReset.AddMinutes(2));
        Assert.Null(decision.Launch);
        Assert.Equal(PausedThreadPhase.OutcomeUnknown, Assert.Single(decision.Threads).Phase);
        restarted.SetArmed(thread.BlockedTurnId, true);
        Assert.Null(restarted.Evaluate([thread], Available(), true, NoneDismissed, AfterReset.AddMinutes(3)).Launch);
    }

    [Fact]
    public void Empty_or_stale_snapshots_never_count_as_quota()
    {
        Assert.False(CodexResumeTracker.HasAvailableQuota([]));
        Assert.False(CodexResumeTracker.HasAvailableQuota([Band(0, current: false)]));
        Assert.False(CodexResumeTracker.HasAvailableQuota([Band(0), Band(100, source: "codex:secondary")]));
    }

    [Fact]
    public void An_unreadable_thread_keeps_its_authorization_without_blocking_another_thread()
    {
        var a = Thread("a");
        var b = Thread("b");
        var tracker = Waiting(a, b);
        var decision = tracker.Evaluate([a, b], Available(), true, NoneDismissed, AfterReset,
            new HashSet<string> { "a" });
        Assert.Equal(b, decision.Launch);
        Assert.Equal(PausedThreadPhase.WaitingForMetadata, decision.Threads.Single(status => status.Thread == a).Phase);
        Assert.True(decision.Threads.Single(status => status.Thread == a).Armed);
        Assert.Equal(a, tracker.Evaluate([a, b], Available(), true, NoneDismissed, AfterReset.AddMinutes(1)).Launch);
    }

    [Fact]
    public void Quota_denial_in_the_daemon_refunds_the_attempt_and_waits_before_retrying()
    {
        var thread = Thread("a");
        var tracker = Waiting(thread);
        tracker.Evaluate([thread], Available(), true, NoneDismissed, AfterReset);
        tracker.BeginLaunch(thread, AfterReset);
        tracker.ReportLaunch(thread, PausedThreadLaunchOutcome.QuotaUnavailable, AfterReset);
        Assert.True(Assert.Single(tracker.State.Entries).Armed);
        Assert.Equal(0, Assert.Single(tracker.State.LaunchCounts).Count);
        Assert.Null(tracker.Evaluate([thread], Available(), true, NoneDismissed, AfterReset.AddMinutes(1)).Launch);
        Assert.Equal(thread, tracker.Evaluate([thread], Available(), true, NoneDismissed, AfterReset.AddMinutes(5)).Launch);
    }
}
