namespace AIVitals.Adapters.ClaudeCode;

/// <summary>
/// Stable reasons for the Claude Code adapter's health. They travel as codes rather than prose so
/// the interface can say why a reading stopped in the language the user chose, instead of showing a
/// number that has quietly stopped moving.
/// </summary>
public static class ClaudeCodeHealthDetail
{
    public const string WaitingForActivity = "claude-code.waiting-for-activity";
    public const string BridgeUnreadable = "claude-code.bridge-unreadable";
    public const string PayloadUnsupported = "claude-code.payload-unsupported";
    public const string PayloadWithoutMetrics = "claude-code.payload-without-metrics";
    public const string CredentialsMissing = "claude-code.credentials-missing";
    public const string CredentialsExpired = "claude-code.credentials-expired";
    public const string AccountRejected = "claude-code.account-rejected";
    public const string AccountUnreachable = "claude-code.account-unreachable";
}
