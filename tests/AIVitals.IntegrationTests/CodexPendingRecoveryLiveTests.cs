using AIVitals.Adapters.Codex;
using AIVitals.Application;
using AIVitals.Infrastructure;

namespace AIVitals.IntegrationTests;

public sealed class CodexPendingRecoveryLiveTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task Persisted_pending_task_is_selected_after_backend_recovery_without_sending_a_prompt()
    {
        if (Environment.GetEnvironmentVariable("AI_VITALS_LIVE_CODEX_RECOVERY") != "1") return;
        var preferences = await new JsonPreferencesStore(AppDataPaths.ForCurrentUser().PreferencesPath).LoadAsync();
        var resume = preferences.EffectiveCodexResume;
        Assert.True(resume.AutoResumeEnabled);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var scanner = new CodexPausedThreadScanner(codexHome: CodexHome.Directory);
        var scan = await scanner.ScanDetailedAsync(timeout.Token);
        var pending = scan.Threads.Select(thread => new PausedThreadInfo(thread.ThreadId, thread.Name,
            thread.WorkingDirectory, thread.BlockedTurnId, thread.BlockedAtUtc)).ToArray();
        Assert.NotEmpty(pending);
        var validation = await scanner.ValidateAsync(scan.Threads[0], timeout.Token);
        Assert.NotNull(validation.Thread);
        Assert.True(validation.AllowedByBackend, "The backend must currently confirm included usage is allowed to exercise this recovery.");
        var now = DateTimeOffset.UtcNow;
        var bands = QuotaBandProjection.Project(validation.Quotas, now);
        Assert.True(CodexResumeTracker.HasAvailableQuota(bands));
        var tracker = new CodexResumeTracker(state: resume.State);
        var decision = tracker.Evaluate(pending, bands, resume.AutoResumeEnabled,
            resume.EffectiveDismissedTurnIds, now, scan.Failures.Select(failure => failure.ThreadId).ToHashSet(StringComparer.Ordinal));
        var selected = Assert.IsType<PausedThreadInfo>(decision.Launch);
        Assert.Contains(selected, pending);
        var loaded = await CodexDaemonContinuation.LoadedThreadIdsAsync(timeout.Token);
        var held = CodexThreadLock.IsHeld(selected.ThreadId);
        var selectedThread = scan.Threads.Single(thread => thread.ThreadId == selected.ThreadId);
        var context = selectedThread.RolloutPath is { } path ? CodexTurnContextReader.ReadLatest(path) : null;
        output.WriteLine($"Recorded terminal permissions supported: {context is not null}. Selection alone is not proof of submission.");
        output.WriteLine($"Paused tasks: {pending.Length}; backend allowed: {validation.AllowedByBackend}; fresh quota bands: {bands.Count}; continuation selected: {decision.Launch is not null}");
        output.WriteLine($"Writer lock held: {held}; loaded in shared daemon: {loaded.Contains(selected.ThreadId)}");
        // Stop before reservation/turn/start: neither user preferences nor user tasks are changed.
    }
}
