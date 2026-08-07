using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AIVitals.Domain;

namespace AIVitals.Adapters.ClaudeCode;

public enum ClaudeCodeOAuthUsageStatus
{
    /// <summary>No usable Claude Code credentials exist, so the account endpoint is simply not in play.</summary>
    NotConfigured,

    /// <summary>Credentials exist but the account usage could not be read this time.</summary>
    Failed,

    /// <summary>
    /// The stored access token is no longer valid. Only Claude Code itself renews it, so this is a
    /// wait for the user to come back rather than a fault to retry out of.
    /// </summary>
    Expired,

    Succeeded
}

public sealed record ClaudeCodeOAuthUsageResult(
    ClaudeCodeOAuthUsageStatus Status,
    IReadOnlyList<UsageObservation> Observations,
    string? Detail)
{
    public static readonly ClaudeCodeOAuthUsageResult NotConfigured =
        new(ClaudeCodeOAuthUsageStatus.NotConfigured, [], ClaudeCodeHealthDetail.CredentialsMissing);

    public static ClaudeCodeOAuthUsageResult Failed(string detail) =>
        new(ClaudeCodeOAuthUsageStatus.Failed, [], detail);

    public static ClaudeCodeOAuthUsageResult Expired(string detail) =>
        new(ClaudeCodeOAuthUsageStatus.Expired, [], detail);

    public static ClaudeCodeOAuthUsageResult Succeeded(IReadOnlyList<UsageObservation> observations) =>
        new(ClaudeCodeOAuthUsageStatus.Succeeded, observations, null);
}

public sealed class ClaudeCodeOAuthUsageClient
{
    public const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";
    public const string OAuthBeta = "oauth-2025-04-20";

    /// <summary>
    /// A token about to expire is treated as expired: a poll started just before the boundary would
    /// otherwise be reported as a rejection by the server rather than as credentials to renew.
    /// </summary>
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(1);

    private readonly HttpClient _httpClient;
    private readonly string _credentialsPath;
    private readonly byte[] _sessionPseudonymKey;

    public ClaudeCodeOAuthUsageClient(
        HttpClient httpClient,
        string credentialsPath,
        byte[] sessionPseudonymKey)
    {
        _httpClient = httpClient;
        _credentialsPath = credentialsPath;
        _sessionPseudonymKey = sessionPseudonymKey;
    }

    public string CredentialsPath => _credentialsPath;

    public async Task<ClaudeCodeOAuthUsageResult> TryGetUsageAsync(
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        var credentials = await TryReadCredentialsAsync(cancellationToken).ConfigureAwait(false);
        if (credentials is null) return ClaudeCodeOAuthUsageResult.NotConfigured;

        // Asking with a token we already know is dead only turns a recoverable wait into a request
        // failure, and the answer is the same either way: Claude Code has to renew it.
        if (credentials.ExpiresAtUtc is { } expiry && expiry - ExpiryMargin <= observedAtUtc)
            return ClaudeCodeOAuthUsageResult.Expired(ClaudeCodeHealthDetail.CredentialsExpired);

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.TryAddWithoutValidation("anthropic-beta", OAuthBeta);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return ClaudeCodeOAuthUsageResult.Expired(ClaudeCodeHealthDetail.CredentialsExpired);
            if (!response.IsSuccessStatusCode)
                return ClaudeCodeOAuthUsageResult.Failed(ClaudeCodeHealthDetail.AccountRejected);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var observations = ClaudeCodeObservationMapper.Map(
                document.RootElement,
                observedAtUtc,
                _sessionPseudonymKey);
            return ClaudeCodeOAuthUsageResult.Succeeded(
                observations.Where(item => item.Capability == UsageCapability.QuotaWindow).ToArray());
        }
        // A poll must never take the adapter down: an HttpClient timeout surfaces as
        // OperationCanceledException even though nobody asked to stop watching.
        catch (Exception exception) when (
            exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return ClaudeCodeOAuthUsageResult.Failed(ClaudeCodeHealthDetail.AccountUnreachable);
        }
    }

    private async Task<ClaudeCodeCredentials?> TryReadCredentialsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                _credentialsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (!TryGetOAuthObject(root, out var oauth) ||
                !oauth.TryGetProperty("accessToken", out var token) ||
                token.ValueKind != JsonValueKind.String ||
                token.GetString() is not { Length: > 0 } accessToken)
                return null;
            return new ClaudeCodeCredentials(accessToken, ReadExpiry(oauth));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Claude Code stores the expiry as epoch milliseconds. An unreadable or absent value means the
    /// token is used until the server says otherwise, which is what happened before it was read.
    /// </summary>
    private static DateTimeOffset? ReadExpiry(JsonElement oauth)
    {
        if (!oauth.TryGetProperty("expiresAt", out var expiresAt)) return null;

        switch (expiresAt.ValueKind)
        {
            case JsonValueKind.Number when expiresAt.TryGetInt64(out var milliseconds):
                return milliseconds is >= -62135596800000 and <= 253402300799999
                    ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
                    : null;
            case JsonValueKind.String when expiresAt.TryGetDateTimeOffset(out var moment):
                return moment;
            default:
                return null;
        }
    }

    private static bool TryGetOAuthObject(JsonElement root, out JsonElement oauth)
    {
        if (root.TryGetProperty("claudeAiOauth", out oauth) && oauth.ValueKind == JsonValueKind.Object)
            return true;
        return root.TryGetProperty("claude.ai_oauth", out oauth) && oauth.ValueKind == JsonValueKind.Object;
    }

    private sealed record ClaudeCodeCredentials(string AccessToken, DateTimeOffset? ExpiresAtUtc);
}
