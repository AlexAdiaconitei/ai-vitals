namespace AIVitals.Application;

public enum CodexResumeDiagnosticEvent { ScanCompleted, ScanFailed, Decision, Attempt, QuotaRejected, AttemptFailed }
public enum CodexResumeFailureKind { None, CliNotFound, Rpc, Timeout, Transport, Other }
public sealed record CodexResumeDiagnostic(DateTimeOffset ObservedAtUtc, CodexResumeDiagnosticEvent Event,
    int ThreadCount = 0, string? ThreadId = null, string? BlockedTurnId = null,
    PausedThreadPhase? Phase = null, bool Armed = false, DateTimeOffset? ResetsAtUtc = null,
    CodexResumeFailureKind Failure = CodexResumeFailureKind.None, int? RpcCode = null,
    int CurrentQuotaCount = 0, int QuotaCount = 0, string? Detail = null);

public static class CodexResumeScanPolicy
{
    public static TimeSpan Interval(bool automaticEnabled, bool watching, bool exhausted, bool hasPausedThreads) =>
        automaticEnabled || watching || exhausted || hasPausedThreads ? TimeSpan.FromMinutes(3) : TimeSpan.FromMinutes(30);
}
