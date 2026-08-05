using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

namespace AIVitals.Adapters.ClaudeCode;

public enum ClaudeCodeIntegrationResult
{
    Installed,
    AlreadyInstalled,
    Restored,
    NotInstalled,
    ModifiedExternally
}

public sealed class ClaudeCodeStatusLineInstaller
{
    public const int RefreshIntervalSeconds = 30;
    private readonly string _helperExecutablePath;
    private readonly string _settingsPath;
    private readonly string _bridgeConfigurationPath;

    public ClaudeCodeStatusLineInstaller(
        string helperExecutablePath,
        string? settingsPath = null,
        string? bridgeConfigurationPath = null)
    {
        _helperExecutablePath = Path.GetFullPath(helperExecutablePath);
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude",
            "settings.json");
        _bridgeConfigurationPath = bridgeConfigurationPath ?? ClaudeCodeBridgeProtocol.ConfigurationPath;
    }

    public string InstalledCommand => QuoteCommand(_helperExecutablePath);

    private string BackupPath => _settingsPath + ".ai-vitals.bak";

    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        var settings = await ReadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var command = GetCommand(settings["statusLine"]);
        return command == InstalledCommand;
    }

    public async Task<ClaudeCodeIntegrationResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_helperExecutablePath))
            throw new FileNotFoundException("No se encontró el bridge de Claude Code.", _helperExecutablePath);

        var settings = await ReadSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (GetCommand(settings["statusLine"]) == InstalledCommand)
        {
            if (GetRefreshInterval(settings["statusLine"]) == RefreshIntervalSeconds)
                return ClaudeCodeIntegrationResult.AlreadyInstalled;

            var installedConfiguration = await ClaudeCodeBridgeProtocol.ReadConfigurationAsync(
                _bridgeConfigurationPath,
                cancellationToken).ConfigureAwait(false);
            if (installedConfiguration?.InstalledStatusLine is not JsonObject)
                return ClaudeCodeIntegrationResult.AlreadyInstalled;

            var upgradedStatusLine = settings["statusLine"]!.DeepClone().AsObject();
            upgradedStatusLine["refreshInterval"] = RefreshIntervalSeconds;
            var upgradedConfiguration = installedConfiguration with
            {
                InstalledStatusLine = upgradedStatusLine.DeepClone()
            };
            await WriteJsonAtomicallyAsync(
                _bridgeConfigurationPath,
                JsonSerializer.SerializeToNode(upgradedConfiguration, ClaudeCodeBridgeProtocol.JsonOptions)!,
                cancellationToken).ConfigureAwait(false);
            settings["statusLine"] = upgradedStatusLine;
            await WriteJsonAtomicallyAsync(_settingsPath, settings, cancellationToken).ConfigureAwait(false);
            return ClaudeCodeIntegrationResult.Installed;
        }

        var previousStatusLine = settings["statusLine"]?.DeepClone();
        var previousCommand = GetCommand(previousStatusLine);
        if (previousCommand is not null && !IsSafelyComposable(previousCommand))
        {
            throw new InvalidOperationException(
                "La status line existente usa un comando shell complejo. No se modificó; requiere composición manual.");
        }
        var replacement = previousStatusLine?.DeepClone() as JsonObject ?? new JsonObject();
        replacement["type"] = "command";
        replacement["command"] = InstalledCommand;
        replacement["refreshInterval"] = RefreshIntervalSeconds;
        var originalSettingsBytes = File.Exists(_settingsPath)
            ? await File.ReadAllBytesAsync(_settingsPath, cancellationToken).ConfigureAwait(false)
            : null;
        var bridgeConfiguration = new ClaudeCodeBridgeConfiguration(
            ClaudeCodeBridgeProtocol.SchemaVersion,
            InstalledCommand,
            previousStatusLine,
            replacement.DeepClone(),
            originalSettingsBytes is null ? null : Convert.ToHexString(SHA256.HashData(originalSettingsBytes)).ToLowerInvariant());

        await WriteJsonAtomicallyAsync(
            _bridgeConfigurationPath,
            JsonSerializer.SerializeToNode(bridgeConfiguration, ClaudeCodeBridgeProtocol.JsonOptions)!,
            cancellationToken).ConfigureAwait(false);

        if (File.Exists(_settingsPath))
            File.Copy(_settingsPath, BackupPath, overwrite: true);

        settings["statusLine"] = replacement;
        await WriteJsonAtomicallyAsync(_settingsPath, settings, cancellationToken).ConfigureAwait(false);
        var writtenSettings = await ReadSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (!JsonNode.DeepEquals(writtenSettings["statusLine"], bridgeConfiguration.InstalledStatusLine))
            throw new IOException("No se pudo verificar la configuración instalada de Claude Code.");
        return ClaudeCodeIntegrationResult.Installed;
    }

    public async Task<ClaudeCodeIntegrationResult> UninstallAsync(CancellationToken cancellationToken = default)
    {
        var bridgeConfiguration = await ClaudeCodeBridgeProtocol.ReadConfigurationAsync(
            _bridgeConfigurationPath,
            cancellationToken).ConfigureAwait(false);
        if (bridgeConfiguration is null) return ClaudeCodeIntegrationResult.NotInstalled;

        var settings = await ReadSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (bridgeConfiguration.InstalledStatusLine is null ||
            !JsonNode.DeepEquals(settings["statusLine"], bridgeConfiguration.InstalledStatusLine))
            return ClaudeCodeIntegrationResult.ModifiedExternally;

        if (bridgeConfiguration.PreviousStatusLine is null)
            settings.Remove("statusLine");
        else
            settings["statusLine"] = bridgeConfiguration.PreviousStatusLine.DeepClone();

        await WriteJsonAtomicallyAsync(_settingsPath, settings, cancellationToken).ConfigureAwait(false);
        File.Delete(_bridgeConfigurationPath);
        // The round trip is verified, so the installation backup has no reason to outlive it.
        if (File.Exists(BackupPath)) File.Delete(BackupPath);
        return ClaudeCodeIntegrationResult.Restored;
    }

    private async Task<JsonObject> ReadSettingsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath)) return new JsonObject();

        await using var stream = new FileStream(_settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        var node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return node as JsonObject ?? throw new JsonException("Claude Code settings must contain a JSON object.");
    }

    private static async Task WriteJsonAtomicallyAsync(
        string path,
        JsonNode document,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         4096,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, document, ClaudeCodeBridgeProtocol.JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    private static string? GetCommand(JsonNode? statusLine) =>
        statusLine is JsonObject statusLineObject &&
        statusLineObject["command"] is JsonValue commandValue &&
        commandValue.TryGetValue<string>(out var command)
            ? command
            : null;

    private static int? GetRefreshInterval(JsonNode? statusLine) =>
        statusLine is JsonObject statusLineObject &&
        statusLineObject["refreshInterval"] is JsonValue intervalValue &&
        intervalValue.TryGetValue<int>(out var interval)
            ? interval
            : null;

    private static bool IsSafelyComposable(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        string[] unsafeTokens = ["&", "|", ">", "<", ";", "`", "$(", "\r", "\n"];
        return unsafeTokens.All(token => !command.Contains(token, StringComparison.Ordinal));
    }

    private static string QuoteCommand(string path)
    {
        var portablePath = path.Replace('\\', '/').Replace("\"", "\\\"");
        return $"\"{portablePath}\"";
    }
}
