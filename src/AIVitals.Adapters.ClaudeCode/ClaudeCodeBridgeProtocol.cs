using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIVitals.Adapters.ClaudeCode;

public static class ClaudeCodeBridgeProtocol
{
    public const int SchemaVersion = 1;
    public const int MaximumPayloadBytes = 1024 * 1024;

    public static string PipeName
    {
        get
        {
            var identity = $"{Environment.UserDomainName}\\{Environment.UserName}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
            return "ai-vitals-claude-v1-" + Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
        }
    }

    public static string ConfigurationPath => DataFilePath("claude-code-statusline.json");
    internal static string SessionKeyPath => DataFilePath("claude-session-pseudonym.key");

    public static async Task<ClaudeCodeBridgeConfiguration?> ReadConfigurationAsync(
        string? path = null,
        CancellationToken cancellationToken = default)
    {
        var configurationPath = path ?? ConfigurationPath;
        if (!File.Exists(configurationPath)) return null;

        await using var stream = new FileStream(configurationPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        var configuration = await JsonSerializer.DeserializeAsync<ClaudeCodeBridgeConfiguration>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        return configuration is { SchemaVersion: SchemaVersion } ? configuration : null;
    }

    private static string DataFilePath(string fileName) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIVitals",
        fileName);

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}

public sealed record ClaudeCodeBridgeConfiguration(
    int SchemaVersion,
    string InstalledCommand,
    JsonNode? PreviousStatusLine,
    JsonNode? InstalledStatusLine,
    string? OriginalSettingsSha256);
