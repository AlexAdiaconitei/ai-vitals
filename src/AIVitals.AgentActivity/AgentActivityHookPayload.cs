using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIVitals.AgentActivity;

public static class AgentActivityHookPayload
{
    public static byte[]? Project(
        ReadOnlySpan<byte> hookInput,
        AgentActivityProvider provider,
        AgentActivityEvent activityEvent,
        ReadOnlySpan<byte> hmacKey,
        DateTimeOffset occurredAt,
        bool includeWorkspaceLabel = false,
        string? currentDirectory = null)
    {
        if (hookInput.IsEmpty || hookInput.Length > AgentActivityBridgeProtocol.MaximumHookInputBytes || hmacKey.Length < 32)
            return null;

        try
        {
            using var document = JsonDocument.Parse(hookInput.ToArray());
            var root = document.RootElement;
            var sessionId = ReadString(root, "session_id") ?? ReadString(root, "sessionId");
            if (string.IsNullOrWhiteSpace(sessionId)) return null;

            var toolId = ReadString(root, "tool_use_id")
                         ?? ReadString(root, "toolUseId")
                         ?? ReadString(root, "tool_call_id")
                         ?? ReadString(root, "toolCallId");

            var workspaceLabel = includeWorkspaceLabel
                ? WorkspaceLabelFor(ReadString(root, "cwd") ?? ReadString(root, "workspace") ?? currentDirectory)
                : null;

            var message = new AgentActivityWireMessage(
                AgentActivityBridgeProtocol.CurrentSchemaVersion,
                provider,
                activityEvent,
                Pseudonymize(provider, sessionId, hmacKey),
                string.IsNullOrWhiteSpace(toolId) ? null : Pseudonymize(provider, toolId, hmacKey),
                occurredAt,
                workspaceLabel);
            return AgentActivityBridgeProtocol.Serialize(message);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static byte[] LoadOrCreateKey()
    {
        var path = AgentActivityBridgeProtocol.KeyPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            var existing = File.ReadAllBytes(path);
            if (existing.Length >= 32) return existing;
        }
        catch (FileNotFoundException)
        {
        }

        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            stream.Write(key);
            return key;
        }
        catch (IOException)
        {
            var existing = File.ReadAllBytes(path);
            if (existing.Length >= 32) return existing;
            throw new InvalidDataException("The agent activity key is invalid.");
        }
    }

    /// <summary>
    /// Reduces a working directory to its leaf folder name. The parent path never leaves the helper,
    /// so a session shows as <c>AI Vitals</c> and never as <c>D:\PROGRAMACION\...\AI Vitals</c>.
    /// Overlong names keep their beginning, because that is the part that identifies the workspace.
    /// </summary>
    public static string? WorkspaceLabelFor(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory)) return null;

        var leaf = Path.GetFileName(workingDirectory.TrimEnd('/', '\\', ' '));
        if (string.IsNullOrWhiteSpace(leaf)) return null;

        var sanitized = new string(leaf
            .Where(character => !char.IsControl(character) && character is not ('/' or '\\' or ':'))
            .ToArray())
            .Trim();
        if (sanitized.Length == 0) return null;

        return sanitized.Length <= AgentActivityBridgeProtocol.MaximumWorkspaceLabelLength
            ? sanitized
            : sanitized[..AgentActivityBridgeProtocol.MaximumWorkspaceLabelLength];
    }

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string Pseudonymize(AgentActivityProvider provider, string value, ReadOnlySpan<byte> key)
    {
        var input = Encoding.UTF8.GetBytes($"{provider}\0{value}");
        var digest = HMACSHA256.HashData(key, input);
        return Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
