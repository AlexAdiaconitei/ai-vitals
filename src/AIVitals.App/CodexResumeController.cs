using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using AIVitals.Adapters.Codex;
using AIVitals.Application;
using Microsoft.Win32;

namespace AIVitals.App;

public sealed class CodexResumeController : IDisposable
{
    private readonly UsageMonitorService _monitor;
    private readonly CodexPausedThreadScanner _scanner;
    private readonly OrcaCodexContinuation _orca = new();
    private readonly CodexResumeTracker _tracker;
    private readonly Action<string, string> _notify;
    private readonly Action<CodexResumeDiagnostic> _diagnostic;
    private readonly Dictionary<string, string> _reportedDecisions = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _timer;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<CodexPausedThread> _paused = [];
    private IReadOnlySet<string> _unreadableThreadIds = new HashSet<string>();
    private int _scanFailureCount;
    private IReadOnlySet<string> _loadedInDaemon = new HashSet<string>();
    private IReadOnlyDictionary<string, CodexTurnContext?> _contexts = new Dictionary<string, CodexTurnContext?>();
    private DateTimeOffset? _lastScanUtc;
    private DateTimeOffset? _lastSuccessfulScanUtc;
    private string? _savedState;
    private bool _disposed;
    private CancellationTokenSource? _activeAttempt;
    private string? _activeAttemptTurnId;

    public CodexResumeController(UsageMonitorService monitor, Action<string, string> notify,
        Action<CodexResumeDiagnostic>? diagnostic = null)
        : this(monitor, notify, diagnostic, new CodexPausedThreadScanner()) { }

    public CodexResumeController(UsageMonitorService monitor, Action<string, string> notify,
        Action<CodexResumeDiagnostic>? diagnostic, CodexPausedThreadScanner scanner)
    {
        _scanner = scanner;
        _monitor = monitor;
        _notify = notify;
        _diagnostic = diagnostic ?? (_ => { });
        _tracker = new(state: Preferences.State);
        _savedState = JsonSerializer.Serialize(Preferences.State);
        ViewModel.AutoResumeEnabled = Preferences.AutoResumeEnabled;
        ViewModel.ShowThreadNames = Preferences.ShowThreadNames;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) => ScheduleTick();
    }

    public CodexResumeViewModel ViewModel { get; } = new();
    private CodexResumePreferences Preferences => _monitor.State.Preferences.EffectiveCodexResume;
    private string Language => _monitor.State.Preferences.Language;

    public void Start()
    {
        _timer.Start();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        ScheduleTick();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Resume)
            _timer.Dispatcher.BeginInvoke(() => { _lastScanUtc = null; ScheduleTick(); });
    }

    private void ScheduleTick()
    {
        if (!_disposed && _operations.CurrentCount > 0) _ = GuardAsync(TickAsync);
    }

    public Task SetAutoResumeAsync(bool enabled)
    {
        if (!enabled) _activeAttempt?.Cancel();
        return GuardAsync(async () =>
        {
            await SavePreferencesAsync(Preferences with { AutoResumeEnabled = enabled });
            if (!enabled)
                _tracker.DisarmAll();
            if (enabled) await ScanAsync();
            await EvaluateAsync();
        });
    }

    public Task SetThreadArmedAsync(string blockedTurnId, bool armed)
    {
        if (!armed && _activeAttemptTurnId == blockedTurnId) _activeAttempt?.Cancel();
        return GuardAsync(async () =>
        {
            _tracker.SetArmed(blockedTurnId, armed);
            await EvaluateAsync();
        });
    }

    public Task SetShowThreadNamesAsync(bool show) => GuardAsync(async () =>
    {
        await SavePreferencesAsync(Preferences with { ShowThreadNames = show });
        ViewModel.ShowThreadNames = show;
        if (!show)
        {
            _paused = _paused.Select(thread => thread with { Name = "Codex " + thread.ThreadId[..Math.Min(8, thread.ThreadId.Length)] }).ToArray();
            ViewModel.ReplaceThreads(ViewModel.Threads.Select(row => row with
            { Name = "Codex " + row.ThreadId[..Math.Min(8, row.ThreadId.Length)] }).ToArray());
        }
        await ScanAsync();
        await EvaluateAsync();
    });

    public Task DismissAsync(string blockedTurnId)
    {
        if (_activeAttemptTurnId == blockedTurnId) _activeAttempt?.Cancel();
        return GuardAsync(async () =>
        {
            _tracker.SetArmed(blockedTurnId, false);
            await SavePreferencesAsync(Preferences.Dismiss(blockedTurnId));
            await EvaluateAsync();
        });
    }

    public void OpenInDesktop(string threadId)
    {
        if (!CodexResumeLauncher.OpenInDesktop(threadId)) ViewModel.Message = Text("CodexResumeMsgDeepLinkFailed");
    }

    public Task ContinueNowAsync(string blockedTurnId) => GuardAsync(async () =>
    {
        var thread = _paused.FirstOrDefault(item => item.BlockedTurnId == blockedTurnId);
        if (thread is null) return;
        var entry = _tracker.State.Entries.FirstOrDefault(item => item.BlockedTurnId == blockedTurnId);
        if (entry?.Phase is PausedThreadPhase.Starting or PausedThreadPhase.Launched or PausedThreadPhase.OutcomeUnknown) return;
        await LaunchAsync(thread, manual: true);
        await EvaluateAsync();
    });

    public void RefreshLanguage() => ScheduleTick();

    private async Task GuardAsync(Func<Task> operation)
    {
        var entered = false;
        try
        {
            await _operations.WaitAsync(_lifetime.Token);
            entered = true;
            if (!_disposed) await operation();
        }
        catch (OperationCanceledException) { if (!_disposed) ViewModel.Message = Text("CodexResumeMsgTimeout"); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            RecordFailure(CodexResumeDiagnosticEvent.AttemptFailed, exception);
            if (!_disposed) ViewModel.Message = Text("CodexResumeMsgFailed");
        }
        finally { if (entered) _operations.Release(); }
    }

    private async Task TickAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var interval = CodexResumeScanPolicy.Interval(Preferences.AutoResumeEnabled, _tracker.IsWatching,
            CodexBands(now).Any(band => band.IsCurrent && band.UsedPercentage >= 100m), _paused.Count > 0);
        if (_lastScanUtc is null || now - _lastScanUtc >= interval) await ScanAsync();
        await EvaluateAsync();
    }

    private async Task ScanAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            var scan = await Task.Run(() => _scanner.ScanDetailedAsync(timeout.Token, Preferences.ShowThreadNames), timeout.Token);
            _unreadableThreadIds = scan.Failures.Select(failure => failure.ThreadId).ToHashSet(StringComparer.Ordinal);
            _scanFailureCount = scan.Failures.Count;
            _paused = scan.Threads.Concat(_paused.Where(thread => _unreadableThreadIds.Contains(thread.ThreadId)))
                .DistinctBy(thread => thread.BlockedTurnId).ToArray();
            var paused = _paused;
            _lastSuccessfulScanUtc = DateTimeOffset.UtcNow;
            _diagnostic(new(DateTimeOffset.UtcNow, CodexResumeDiagnosticEvent.ScanCompleted, ThreadCount: paused.Count));
            foreach (var failure in scan.Failures)
                _diagnostic(new(DateTimeOffset.UtcNow, CodexResumeDiagnosticEvent.ScanFailed, ThreadId: failure.ThreadId,
                    Failure: failure.TimedOut ? CodexResumeFailureKind.Timeout :
                        failure.RpcCode is null ? CodexResumeFailureKind.Transport : CodexResumeFailureKind.Rpc,
                    RpcCode: failure.RpcCode));
            _contexts = await Task.Run(() => paused.ToDictionary(thread => thread.BlockedTurnId,
                thread => thread.RolloutPath is { } path ? CodexTurnContextReader.ReadLatest(path) : null,
                StringComparer.Ordinal), timeout.Token);
            var loaded = new HashSet<string>(StringComparer.Ordinal);
            foreach (var home in paused.Select(thread => thread.HomeDirectory).Distinct())
                loaded.UnionWith(await CodexDaemonContinuation.LoadedThreadIdsAsync(timeout.Token,
                    CodexDaemonContinuation.DefaultSocketPath(home)));
            foreach (var thread in paused)
                if (await _orca.FindAsync(thread, timeout.Token) is not null) loaded.Add(thread.ThreadId);
            _loadedInDaemon = loaded;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _unreadableThreadIds = _paused.Select(thread => thread.ThreadId).ToHashSet(StringComparer.Ordinal);
            _scanFailureCount = Math.Max(1, _paused.Count);
            RecordFailure(CodexResumeDiagnosticEvent.ScanFailed, exception);
            if (!_disposed) ViewModel.Message = Text("CodexResumeMsgScanFailed");
        }
        _lastScanUtc = DateTimeOffset.UtcNow;
    }

    private CodexResumeDecision Decide(DateTimeOffset now) => _tracker.Evaluate(
        _paused.Select(ToInfo).ToArray(), CodexBands(now), Preferences.AutoResumeEnabled,
        Preferences.EffectiveDismissedTurnIds, now, _unreadableThreadIds);

    private async Task EvaluateAsync()
    {
        var decision = Decide(DateTimeOffset.UtcNow);
        var bands = CodexBands(DateTimeOffset.UtcNow);
        var visibleIds = decision.Threads.Select(status => status.Thread.BlockedTurnId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _reportedDecisions.Keys.Where(id => !visibleIds.Contains(id)).ToArray())
            _reportedDecisions.Remove(id);
        foreach (var status in decision.Threads)
        {
            var signature = $"{status.Phase}|{status.Armed}|{status.ResetsAtUtc:O}|{bands.Count(band => band.IsCurrent)}|{bands.Count}";
            if (_reportedDecisions.GetValueOrDefault(status.Thread.BlockedTurnId) == signature) continue;
            _reportedDecisions[status.Thread.BlockedTurnId] = signature;
            _diagnostic(new(DateTimeOffset.UtcNow, CodexResumeDiagnosticEvent.Decision,
                ThreadId: status.Thread.ThreadId, BlockedTurnId: status.Thread.BlockedTurnId,
                Phase: status.Phase, Armed: status.Armed, ResetsAtUtc: status.ResetsAtUtc,
                CurrentQuotaCount: bands.Count(band => band.IsCurrent), QuotaCount: bands.Count));
        }
        await SaveStateAsync();
        if (decision.Announce.Count > 0)
            _notify(Text("CodexResetNotificationTitle"), string.Format(Text("CodexResetNotificationBody"), decision.Announce.Count));
        if (decision.Launch is { } next && _paused.FirstOrDefault(item => item.BlockedTurnId == next.BlockedTurnId) is { } thread)
        {
            await LaunchAsync(thread, manual: false);
            decision = Decide(DateTimeOffset.UtcNow);
            await SaveStateAsync();
        }
        ViewModel.AutoResumeEnabled = Preferences.AutoResumeEnabled;
        ViewModel.ShowThreadNames = Preferences.ShowThreadNames;
        ViewModel.ScanStatus = _lastSuccessfulScanUtc is { } scanned
            ? _scanFailureCount == 0 ? string.Format(Text("CodexResumeScanStatus"), FormatLocalTime(scanned), _paused.Count)
                : string.Format(Text("CodexResumeScanPartial"), FormatLocalTime(scanned), _paused.Count, _scanFailureCount) : null;
        ViewModel.ReplaceThreads(decision.Threads.Select(ToRow));
    }

    private async Task LaunchAsync(CodexPausedThread thread, bool manual)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        _activeAttempt = timeout;
        _activeAttemptTurnId = thread.BlockedTurnId;
        var info = ToInfo(thread);
        var reserved = false;
        var mayHaveSubmitted = false;
        try
        {
            // The scan is for display. Execution always uses a new read of this exact blocked turn and quota.
            var validation = await Task.Run(() => _scanner.ValidateAsync(thread, timeout.Token), timeout.Token);
            if (validation.Thread is null)
            {
                _paused = _paused.Where(item => item.BlockedTurnId != thread.BlockedTurnId).ToArray();
                _tracker.ReportLaunch(info, PausedThreadLaunchOutcome.NoLongerPaused, DateTimeOffset.UtcNow);
                await SaveStateAsync();
                ViewModel.Message = Text("CodexResumeMsgAlreadyContinued");
                return;
            }
            var bands = QuotaBandProjection.Project(validation.Quotas, DateTimeOffset.UtcNow);
            var entry = _tracker.State.Entries.First(item => item.BlockedTurnId == thread.BlockedTurnId);
            var requiredSources = CodexBands(DateTimeOffset.UtcNow).Select(band => band.Observation.Source)
                .Concat(entry.Windows.Select(window => window.Source)).Distinct(StringComparer.Ordinal);
            if (!validation.AllowedByBackend || !CodexResumeTracker.HasAvailableQuota(bands) ||
                requiredSources.Any(source => !bands.Any(band => band.Observation.Source == source && band.IsCurrent)) ||
                !manual && entry.Windows.Any(window => DateTimeOffset.UtcNow < window.ResetsAtUtc.AddMinutes(1)))
            {
                _diagnostic(new(DateTimeOffset.UtcNow, CodexResumeDiagnosticEvent.QuotaRejected,
                    ThreadId: thread.ThreadId, BlockedTurnId: thread.BlockedTurnId,
                    CurrentQuotaCount: bands.Count(band => band.IsCurrent), QuotaCount: bands.Count));
                ViewModel.Message = Text("CodexResumeMsgQuotaUnknown");
                return;
            }
            if (!manual && !entry.Armed) return;
            timeout.Token.ThrowIfCancellationRequested();
            _tracker.BeginLaunch(info, DateTimeOffset.UtcNow);
            // Persist the reservation before sending anything. A crash here cannot resend the same turn.
            await SaveStateAsync();
            reserved = true;
            _diagnostic(new(DateTimeOffset.UtcNow, CodexResumeDiagnosticEvent.Attempt,
                ThreadId: thread.ThreadId, BlockedTurnId: thread.BlockedTurnId, Phase: PausedThreadPhase.Starting));
            timeout.Token.ThrowIfCancellationRequested();
            mayHaveSubmitted = true;
            var viaOrca = await _orca.TryContinueAsync(validation.Thread, Text("CodexResumePrompt"), timeout.Token);
            if (viaOrca != OrcaCodexContinueResult.NotOwned)
            {
                Finish(info, viaOrca switch
                {
                    OrcaCodexContinueResult.Continued => PausedThreadLaunchOutcome.Launched,
                    OrcaCodexContinueResult.Held => PausedThreadLaunchOutcome.HeldByAnotherApp,
                    OrcaCodexContinueResult.NoLongerPaused => PausedThreadLaunchOutcome.NoLongerPaused,
                    OrcaCodexContinueResult.QuotaUnavailable => PausedThreadLaunchOutcome.QuotaUnavailable,
                    _ => PausedThreadLaunchOutcome.OutcomeUnknown
                }, viaOrca switch
                {
                    OrcaCodexContinueResult.Continued => "CodexResumeMsgContinuedInCli",
                    OrcaCodexContinueResult.Held => "CodexResumeMsgHeld",
                    OrcaCodexContinueResult.NoLongerPaused => "CodexResumeMsgAlreadyContinued",
                    OrcaCodexContinueResult.QuotaUnavailable => "CodexResumeMsgQuotaUnknown",
                    _ => "CodexResumeMsgOutcomeUnknown"
                });
                await SaveStateAsync();
                return;
            }
            var viaDaemon = await CodexDaemonContinuation.TryContinueAsync(thread.ThreadId, thread.BlockedTurnId,
                Text("CodexResumePrompt"), timeout.Token,
                CodexDaemonContinuation.DefaultSocketPath(thread.HomeDirectory));
            if (viaDaemon is CodexDaemonContinueResult.NotLoaded or CodexDaemonContinueResult.DaemonUnavailable)
            {
                timeout.Token.ThrowIfCancellationRequested();
                var result = await CodexResumeLauncher.LaunchAsync(validation.Thread, Text("CodexResumePrompt"), timeout.Token);
                Finish(info, result switch
                {
                    CodexResumeLaunchResult.Launched => PausedThreadLaunchOutcome.Launched,
                    CodexResumeLaunchResult.HeldByAnotherApp => PausedThreadLaunchOutcome.HeldByAnotherApp,
                    CodexResumeLaunchResult.OutcomeUnknown => PausedThreadLaunchOutcome.OutcomeUnknown,
                    CodexResumeLaunchResult.NoLongerPaused => PausedThreadLaunchOutcome.NoLongerPaused,
                    CodexResumeLaunchResult.QuotaUnavailable => PausedThreadLaunchOutcome.QuotaUnavailable,
                    _ => PausedThreadLaunchOutcome.Failed
                }, result switch
                {
                    CodexResumeLaunchResult.Launched => "CodexResumeMsgLaunched",
                    CodexResumeLaunchResult.HeldByAnotherApp => "CodexResumeMsgHeld",
                    CodexResumeLaunchResult.UnknownPermissions => "CodexResumeMsgPermissions",
                    CodexResumeLaunchResult.OutcomeUnknown => "CodexResumeMsgOutcomeUnknown",
                    CodexResumeLaunchResult.NoLongerPaused => "CodexResumeMsgAlreadyContinued",
                    CodexResumeLaunchResult.QuotaUnavailable => "CodexResumeMsgQuotaUnknown",
                    _ => "CodexResumeMsgFailed"
                });
            }
            else
                Finish(info, viaDaemon switch
                {
                    CodexDaemonContinueResult.Continued or CodexDaemonContinueResult.AlreadyRunning => PausedThreadLaunchOutcome.Launched,
                    CodexDaemonContinueResult.NoLongerPaused => PausedThreadLaunchOutcome.NoLongerPaused,
                    CodexDaemonContinueResult.OutcomeUnknown => PausedThreadLaunchOutcome.OutcomeUnknown,
                    CodexDaemonContinueResult.QuotaUnavailable => PausedThreadLaunchOutcome.QuotaUnavailable,
                    _ => PausedThreadLaunchOutcome.Failed
                }, viaDaemon switch
                {
                    CodexDaemonContinueResult.Continued => "CodexResumeMsgContinuedInCli",
                    CodexDaemonContinueResult.AlreadyRunning => "CodexResumeMsgAlreadyRunning",
                    CodexDaemonContinueResult.NoLongerPaused => "CodexResumeMsgAlreadyContinued",
                    CodexDaemonContinueResult.OutcomeUnknown => "CodexResumeMsgOutcomeUnknown",
                    CodexDaemonContinueResult.QuotaUnavailable => "CodexResumeMsgQuotaUnknown",
                    _ => "CodexResumeMsgFailed"
                });
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            RecordFailure(CodexResumeDiagnosticEvent.AttemptFailed, exception, thread);
            if (reserved) Finish(info, mayHaveSubmitted ? PausedThreadLaunchOutcome.OutcomeUnknown : PausedThreadLaunchOutcome.Failed,
                mayHaveSubmitted ? "CodexResumeMsgOutcomeUnknown" : "CodexResumeMsgTimeout");
            else ViewModel.Message = Text("CodexResumeMsgTimeout");
        }
        finally
        {
            _activeAttempt = null;
            _activeAttemptTurnId = null;
        }
        await SaveStateAsync();
    }

    private void Finish(PausedThreadInfo thread, PausedThreadLaunchOutcome outcome, string message)
    {
        _tracker.ReportLaunch(thread, outcome, DateTimeOffset.UtcNow);
        var entry = _tracker.State.Entries.FirstOrDefault(item => item.BlockedTurnId == thread.BlockedTurnId);
        _diagnostic(new(DateTimeOffset.UtcNow, CodexResumeDiagnosticEvent.Decision,
            ThreadId: thread.ThreadId, BlockedTurnId: thread.BlockedTurnId,
            Phase: entry?.Phase, Armed: entry?.Armed ?? false));
        ViewModel.Message = string.Format(Text(message), thread.Name);
        if (outcome == PausedThreadLaunchOutcome.NoLongerPaused)
            _paused = _paused.Where(item => item.BlockedTurnId != thread.BlockedTurnId).ToArray();
    }

    private PausedThreadRowViewModel ToRow(PausedThreadStatus status)
    {
        var text = status.Phase switch
        {
            PausedThreadPhase.WaitingForReset when status.ResetsAtUtc is { } reset => string.Format(Text("PausedWaiting"), FormatLocalTime(reset)),
            PausedThreadPhase.WaitingForReset => Text("PausedWaitingUnknown"),
            PausedThreadPhase.Ready => Text("PausedReady"),
            PausedThreadPhase.Queued => Text("PausedQueued"),
            PausedThreadPhase.HeldByAnotherApp => Text("PausedHeld"),
            PausedThreadPhase.Starting => Text("PausedStarting"),
            PausedThreadPhase.Launched => Text("PausedLaunched"),
            PausedThreadPhase.RequiresConfirmation => Text("PausedRequiresConfirmation"),
            PausedThreadPhase.OutcomeUnknown => Text("PausedOutcomeUnknown"),
            PausedThreadPhase.WaitingForMetadata => Text("PausedWaitingForMetadata"),
            PausedThreadPhase.WaitingForQuota => Text("CodexResumeMsgQuotaUnknown"),
            _ => Text("PausedLaunchFailed")
        };
        var terminal = status.Phase is PausedThreadPhase.Starting or PausedThreadPhase.Launched or PausedThreadPhase.OutcomeUnknown;
        return new()
        {
            ThreadId = status.Thread.ThreadId,
            BlockedTurnId = status.Thread.BlockedTurnId,
            Name = status.Thread.Name,
            Project = ProjectName(status.Thread.WorkingDirectory),
            StatusText = text,
            PermissionsText = PermissionsFor(status.Thread.BlockedTurnId),
            IsArmed = status.Armed,
            CanArm = !terminal,
            CanContinue = !terminal && status.Phase is not (PausedThreadPhase.WaitingForReset or PausedThreadPhase.WaitingForMetadata)
        };
    }

    private string PermissionsFor(string blockedTurnId)
    {
        var thread = _paused.FirstOrDefault(item => item.BlockedTurnId == blockedTurnId);
        if (thread is not null && _loadedInDaemon.Contains(thread.ThreadId)) return Text("PausedOpenInCli");
        var context = _contexts.GetValueOrDefault(blockedTurnId);
        return context is null ? Text("PausedPermissionsUnknown") : string.Format(Text("PausedPermissions"),
            string.Join(" · ", new[] { context.Model, context.ReasoningEffort, context.ApprovalPolicy, context.SandboxMode }
                .Where(part => !string.IsNullOrWhiteSpace(part))));
    }

    private IReadOnlyList<QuotaBandSnapshot> CodexBands(DateTimeOffset now) =>
        _monitor.State.LatestQuotaByProvider.TryGetValue("codex", out var observations) ? QuotaBandProjection.Project(observations, now) : [];

    private void RecordFailure(CodexResumeDiagnosticEvent kind, Exception exception, CodexPausedThread? thread = null) =>
        _diagnostic(new(DateTimeOffset.UtcNow, kind, ThreadId: thread?.ThreadId, BlockedTurnId: thread?.BlockedTurnId,
            Failure: exception switch
            {
                FileNotFoundException => CodexResumeFailureKind.CliNotFound,
                CodexRpcException => CodexResumeFailureKind.Rpc,
                OperationCanceledException => CodexResumeFailureKind.Timeout,
                IOException => CodexResumeFailureKind.Transport,
                _ => CodexResumeFailureKind.Other
            }, RpcCode: (exception as CodexRpcException)?.Code));

    private async Task SaveStateAsync()
    {
        var state = _tracker.State;
        var serialized = JsonSerializer.Serialize(state);
        if (_savedState == serialized) return;
        await SavePreferencesAsync(Preferences with { State = state });
        _savedState = serialized;
    }

    private Task SavePreferencesAsync(CodexResumePreferences preferences) =>
        _monitor.UpdatePreferencesAsync(current => current with { CodexResume = preferences }, _lifetime.Token);
    private static PausedThreadInfo ToInfo(CodexPausedThread thread) =>
        new(thread.ThreadId, thread.Name, thread.WorkingDirectory, thread.BlockedTurnId, thread.BlockedAtUtc);
    private static string ProjectName(string cwd) => Path.GetFileName(cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    private static string FormatLocalTime(DateTimeOffset utc) =>
        utc.ToLocalTime().ToString(utc.ToLocalTime().Date == DateTime.Today ? "HH:mm" : "dd/MM HH:mm");
    private string Text(string key) => UiLanguageCatalog.Get(Language, key);

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _lifetime.Cancel();
    }
}
