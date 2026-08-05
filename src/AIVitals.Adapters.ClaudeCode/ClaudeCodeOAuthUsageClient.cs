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

    Succeeded
}

public sealed record ClaudeCodeOAuthUsageResult(
    ClaudeCodeOAuthUsageStatus Status,
    IReadOnlyList<UsageObservation> Observations,
    string? Detail)
{
    public static readonly ClaudeCodeOAuthUsageResult NotConfigured =
        new(ClaudeCodeOAuthUsageStatus.NotConfigured, [], null);

    public static ClaudeCodeOAuthUsageResult Failed(string detail) =>
        new(ClaudeCodeOAuthUsageStatus.Failed, [], detail);

    public static ClaudeCodeOAuthUsageResult Succeeded(IReadOnlyList<UsageObservation> observations) =>
        new(ClaudeCodeOAuthUsageStatus.Succeeded, observations, null);
}

public sealed class ClaudeCodeOAuthUsageClient
{
    public const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";
    public const string OAuthBeta = "oauth-2025-04-20";

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

    public async Task<ClaudeCodeOAuthUsageResult> TryGetUsageAsync(
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        var accessToken = await TryReadAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken)) return ClaudeCodeOAuthUsageResult.NotConfigured;

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("anthropic-beta", OAuthBeta);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return ClaudeCodeOAuthUsageResult.Failed(
                    $"Anthropic rechazó la consulta de uso de la cuenta ({(int)response.StatusCode}).");

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
            return ClaudeCodeOAuthUsageResult.Failed("No se pudo leer el uso de la cuenta de Claude.");
        }
    }

    private async Task<string?> TryReadAccessTokenAsync(CancellationToken cancellationToken)
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
                token.ValueKind != JsonValueKind.String)
                return null;
            return token.GetString();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool TryGetOAuthObject(JsonElement root, out JsonElement oauth)
    {
        if (root.TryGetProperty("claudeAiOauth", out oauth) && oauth.ValueKind == JsonValueKind.Object)
            return true;
        return root.TryGetProperty("claude.ai_oauth", out oauth) && oauth.ValueKind == JsonValueKind.Object;
    }
}
