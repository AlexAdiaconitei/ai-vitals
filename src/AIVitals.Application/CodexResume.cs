namespace AIVitals.Application;

public sealed record PausedThreadInfo(string ThreadId, string Name, string WorkingDirectory,
    string BlockedTurnId, DateTimeOffset BlockedAtUtc);

public enum PausedThreadPhase
{
    WaitingForReset, Ready, Queued, HeldByAnotherApp, Starting, Launched, LaunchFailed,
    RequiresConfirmation, OutcomeUnknown, WaitingForMetadata, WaitingForQuota
}

public sealed record PausedThreadStatus(PausedThreadInfo Thread, PausedThreadPhase Phase,
    DateTimeOffset? ResetsAtUtc, bool Armed = false);
public enum PausedThreadLaunchOutcome { Launched, HeldByAnotherApp, Failed, OutcomeUnknown, NoLongerPaused, QuotaUnavailable }
public sealed record CodexResumeDecision(IReadOnlyList<PausedThreadStatus> Threads,
    IReadOnlyList<PausedThreadInfo> Announce, PausedThreadInfo? Launch);
public sealed record CodexResumeWindow(string Source, DateTimeOffset ResetsAtUtc);

// Resume state contains no names, paths, prompts or credentials.
public sealed record CodexResumeEntry(string ThreadId, string BlockedTurnId, DateTimeOffset BlockedAtUtc,
    CodexResumeWindow[] Windows, bool Armed = false, bool Announced = false,
    PausedThreadPhase Phase = PausedThreadPhase.WaitingForReset, DateTimeOffset? RetryAtUtc = null,
    bool WasArmedBeforeAttempt = false, bool? UserDisarmed = null, DateTimeOffset? AttemptWindowUtc = null);
public sealed record CodexResumeResetCount(DateTimeOffset ResetsAtUtc, int Count);
public sealed record CodexResumeState(CodexResumeEntry[] Entries, CodexResumeResetCount[] LaunchCounts,
    DateTimeOffset? LastLaunchUtc = null, DateTimeOffset? LastEvaluationUtc = null);

public sealed class CodexResumeTracker
{
    private static readonly TimeSpan ResetMargin = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RecentFailureBindingWindow = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(8);
    private readonly TimeSpan _launchSpacing;
    private readonly TimeSpan _heldRetryInterval;
    private readonly int _maxLaunchesPerReset;
    private readonly Dictionary<string, CodexResumeEntry> _entries;
    private readonly Dictionary<DateTimeOffset, int> _launchCounts;
    private DateTimeOffset? _lastLaunchUtc;
    private DateTimeOffset? _lastEvaluationUtc;

    public CodexResumeTracker(TimeSpan? launchSpacing = null, int maxLaunchesPerReset = int.MaxValue,
        TimeSpan? heldRetryInterval = null, CodexResumeState? state = null)
    {
        _launchSpacing = launchSpacing ?? TimeSpan.FromMinutes(2);
        _heldRetryInterval = heldRetryInterval ?? TimeSpan.FromMinutes(5);
        _maxLaunchesPerReset = maxLaunchesPerReset;
        _entries = (state?.Entries ?? []).DistinctBy(entry => entry.BlockedTurnId)
            .ToDictionary(entry => entry.BlockedTurnId, StringComparer.Ordinal);
        _launchCounts = (state?.LaunchCounts ?? []).DistinctBy(count => count.ResetsAtUtc)
            .ToDictionary(count => count.ResetsAtUtc, count => count.Count);
        _lastLaunchUtc = state?.LastLaunchUtc;
        _lastEvaluationUtc = state?.LastEvaluationUtc;
        foreach (var saved in _entries.Values.ToArray())
        {
            // Older versions did not distinguish a manual exclusion from automatic disarming.
            // Preserve unarmed entries with a known window; windowless entries could not be
            // toggled in that UI and were disarmed by the faulty historical-reset rule.
            var entry = saved.UserDisarmed is null ? saved with
            {
                UserDisarmed = !saved.Armed && saved.Windows.Length > 0 &&
                    saved.Phase is PausedThreadPhase.WaitingForReset or PausedThreadPhase.Ready
            } : saved;
            _entries[entry.BlockedTurnId] = entry;
            if (entry.Phase == PausedThreadPhase.Starting)
                _entries[entry.BlockedTurnId] = entry with { Phase = PausedThreadPhase.OutcomeUnknown, Armed = false };
        }
    }

    public bool IsWatching => _entries.Values.Any(entry => entry.Armed || entry.Phase == PausedThreadPhase.WaitingForReset);
    public CodexResumeState State => new(_entries.Values.OrderBy(entry => entry.BlockedTurnId).ToArray(),
        _launchCounts.OrderBy(pair => pair.Key).Select(pair => new CodexResumeResetCount(pair.Key, pair.Value)).ToArray(),
        _lastLaunchUtc, _lastEvaluationUtc);

    public void SetArmed(string turnId, bool armed)
    {
        if (!_entries.TryGetValue(turnId, out var entry)) return;
        if (entry.Phase is PausedThreadPhase.Starting or PausedThreadPhase.OutcomeUnknown or PausedThreadPhase.Launched) return;
        _entries[turnId] = entry with { Armed = armed, UserDisarmed = !armed, Phase = PausedThreadPhase.WaitingForReset, RetryAtUtc = null };
    }

    public void DisarmAll()
    {
        foreach (var entry in _entries.Values.ToArray())
            _entries[entry.BlockedTurnId] = entry with { Armed = false };
    }

    public CodexResumeDecision Evaluate(IReadOnlyList<PausedThreadInfo> paused,
        IReadOnlyList<QuotaBandSnapshot> codexBands, bool automaticEnabled,
        IReadOnlySet<string> dismissedTurnIds, DateTimeOffset nowUtc, IReadOnlySet<string>? unreadableThreadIds = null)
    {
        foreach (var entry in _entries.Values.Where(entry => entry.BlockedAtUtc < nowUtc - Retention).ToArray())
            _entries.Remove(entry.BlockedTurnId);
        foreach (var reset in _launchCounts.Keys.Where(reset => reset < nowUtc - Retention).ToArray())
            _launchCounts.Remove(reset);
        var visible = paused.Where(thread => !dismissedTurnIds.Contains(thread.BlockedTurnId)).ToArray();
        var available = HasAvailableQuota(codexBands);
        var exhausted = codexBands.Where(band => band.IsCurrent && band.UsedPercentage >= 100m).ToArray();
        var attemptWindow = codexBands.Where(band => band.IsCurrent && band.ResetsAtUtc is not null)
            .OrderBy(band => band.Duration ?? TimeSpan.MaxValue).FirstOrDefault()?.ResetsAtUtc;
        var announce = new List<PausedThreadInfo>();
        var statuses = new List<PausedThreadStatus>();
        PausedThreadInfo? launch = null;
        foreach (var thread in visible.OrderBy(thread => thread.BlockedAtUtc))
        {
            if (!_entries.TryGetValue(thread.BlockedTurnId, out var entry))
            {
                entry = new(thread.ThreadId, thread.BlockedTurnId, thread.BlockedAtUtc, [], UserDisarmed: false);
            }
            var uncertainThread = _entries.Values.Any(old => old.ThreadId == thread.ThreadId &&
                old.Phase is PausedThreadPhase.Starting or PausedThreadPhase.OutcomeUnknown);
            var terminal = entry.Phase is PausedThreadPhase.Starting or PausedThreadPhase.Launched or
                PausedThreadPhase.LaunchFailed or PausedThreadPhase.OutcomeUnknown;
            if (!terminal && uncertainThread) entry = entry with { Armed = false };
            if (!terminal && automaticEnabled && entry.UserDisarmed != true && !uncertainThread)
                entry = entry with { Armed = true };
            // An app started during an existing five-hour block can still observe its exact window.
            // Older failures may not acquire a weekly binding or a later five-hour window.
            if (entry.Windows.Length == 0 && thread.BlockedAtUtc <= nowUtc)
                entry = entry with
                {
                    Windows = exhausted
                    .Where(band => band.ResetsAtUtc is { } reset && band.Duration is { } duration &&
                        (nowUtc - thread.BlockedAtUtc <= RecentFailureBindingWindow || duration <= TimeSpan.FromHours(5.5)) &&
                        thread.BlockedAtUtc >= reset - duration && thread.BlockedAtUtc <= reset)
                    .Select(band => new CodexResumeWindow(band.Observation.Source, band.ResetsAtUtc!.Value)).ToArray()
                };
            if (entry.Windows.Length > 0)
                entry = entry with
                {
                    Windows = entry.Windows.Select(window => exhausted
                    .Where(band => band.Observation.Source == window.Source && band.ResetsAtUtc > window.ResetsAtUtc)
                    .Select(band => new CodexResumeWindow(window.Source, band.ResetsAtUtc!.Value))
                    .FirstOrDefault() ?? window).ToArray()
                };
            var resetAt = entry.Windows.Length == 0 ? (DateTimeOffset?)null : entry.Windows.Max(window => window.ResetsAtUtc);
            // An observed blocking window supplies a deadline. An unobserved historic reset does
            // not remove authorization: execution still rereads the exact turn and backend permission.
            var resetVerified = available && (resetAt is null || nowUtc >= resetAt.Value + ResetMargin) &&
                entry.Windows.All(window => codexBands.Any(band => band.Observation.Source == window.Source &&
                    band.IsCurrent && band.Observation.ObservedAtUtc >= window.ResetsAtUtc));
            if (!terminal)
            {
                if (unreadableThreadIds?.Contains(thread.ThreadId) == true)
                    entry = entry with { Phase = PausedThreadPhase.WaitingForMetadata };
                else if (resetVerified)
                {
                    if (!entry.Announced)
                    {
                        announce.Add(thread);
                        entry = entry with { Announced = true };
                    }
                    entry = entry with { AttemptWindowUtc = attemptWindow };
                    if (entry.Armed)
                        entry = entry with
                        {
                            Phase = entry.RetryAtUtc is null ? PausedThreadPhase.Queued :
                            entry.Phase == PausedThreadPhase.WaitingForQuota ? PausedThreadPhase.WaitingForQuota : PausedThreadPhase.HeldByAnotherApp
                        };
                    else
                        entry = entry with { Phase = PausedThreadPhase.Ready, RetryAtUtc = null };
                    if (launch is null && entry.Armed &&
                        (attemptWindow is null || _launchCounts.GetValueOrDefault(attemptWindow.Value) < _maxLaunchesPerReset) &&
                        (_lastLaunchUtc is null || nowUtc - _lastLaunchUtc >= _launchSpacing) &&
                        (entry.RetryAtUtc is null || nowUtc >= entry.RetryAtUtc)) launch = thread;
                }
                else
                    entry = entry with { Phase = available && resetAt is null ? PausedThreadPhase.Ready : PausedThreadPhase.WaitingForReset };
            }
            _entries[entry.BlockedTurnId] = entry;
            statuses.Add(new(thread, entry.Phase, resetAt, entry.Armed));
        }
        var visibleIds = visible.Select(thread => thread.BlockedTurnId).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in _entries.Values.Where(entry => !visibleIds.Contains(entry.BlockedTurnId)).ToArray())
            _entries[entry.BlockedTurnId] = entry with { Armed = false };
        _lastEvaluationUtc = nowUtc;
        return new(statuses.OrderByDescending(status => status.Thread.BlockedAtUtc).ToArray(), announce, launch);
    }

    public static bool HasAvailableQuota(IReadOnlyList<QuotaBandSnapshot> bands) =>
        bands.Count > 0 && bands.All(band => band.IsCurrent && band.UsedPercentage < 100m);

    public void BeginLaunch(PausedThreadInfo thread, DateTimeOffset nowUtc)
    {
        var entry = _entries[thread.BlockedTurnId];
        _entries[thread.BlockedTurnId] = entry with
        { Phase = PausedThreadPhase.Starting, Armed = false, WasArmedBeforeAttempt = entry.Armed };
        if (entry.AttemptWindowUtc is { } reset)
        {
            _launchCounts[reset] = _launchCounts.GetValueOrDefault(reset) + 1;
        }
        _lastLaunchUtc = nowUtc;
    }

    /// <summary>The preflight quota read rejected a start before anything was sent.</summary>
    public void DeferForQuota(PausedThreadInfo thread, DateTimeOffset nowUtc)
    {
        if (!_entries.TryGetValue(thread.BlockedTurnId, out var entry) ||
            entry.Phase is PausedThreadPhase.Starting or PausedThreadPhase.Launched or
                PausedThreadPhase.LaunchFailed or PausedThreadPhase.OutcomeUnknown) return;
        _entries[thread.BlockedTurnId] = entry with
        { Phase = PausedThreadPhase.WaitingForQuota, RetryAtUtc = nowUtc + _heldRetryInterval };
    }

    public void ReportLaunch(PausedThreadInfo thread, PausedThreadLaunchOutcome outcome, DateTimeOffset nowUtc)
    {
        if (!_entries.TryGetValue(thread.BlockedTurnId, out var entry)) return;
        if (outcome == PausedThreadLaunchOutcome.NoLongerPaused && entry.Phase != PausedThreadPhase.Starting)
        {
            _entries[thread.BlockedTurnId] = entry with { Armed = false, Phase = PausedThreadPhase.Launched };
            return;
        }
        if (entry.Phase != PausedThreadPhase.Starting)
        {
            BeginLaunch(thread, nowUtc);
            entry = _entries[thread.BlockedTurnId];
        }
        var held = outcome == PausedThreadLaunchOutcome.HeldByAnotherApp;
        var quotaUnavailable = outcome == PausedThreadLaunchOutcome.QuotaUnavailable;
        if ((held || quotaUnavailable) && entry.AttemptWindowUtc is { } reset)
        {
            _launchCounts[reset] = Math.Max(0, _launchCounts.GetValueOrDefault(reset) - 1);
        }
        _entries[thread.BlockedTurnId] = entry with
        {
            Armed = (held || quotaUnavailable) && entry.WasArmedBeforeAttempt,
            RetryAtUtc = held || quotaUnavailable ? nowUtc + _heldRetryInterval : null,
            Phase = outcome switch
            {
                PausedThreadLaunchOutcome.Launched or PausedThreadLaunchOutcome.NoLongerPaused => PausedThreadPhase.Launched,
                PausedThreadLaunchOutcome.HeldByAnotherApp => PausedThreadPhase.HeldByAnotherApp,
                PausedThreadLaunchOutcome.OutcomeUnknown => PausedThreadPhase.OutcomeUnknown,
                PausedThreadLaunchOutcome.QuotaUnavailable => PausedThreadPhase.WaitingForQuota,
                _ => PausedThreadPhase.LaunchFailed
            }
        };
    }
}
