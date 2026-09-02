using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIVitals.AgentActivity;

public static class AgentActivityBridgeProtocol
{
    public const int MaximumHookInputBytes = 1024 * 1024;
    public const int MaximumMessageBytes = 4096;

    /// <summary>Schema 2 adds the optional workspace label; schema 1 helpers stay accepted.</summary>
    public const int CurrentSchemaVersion = 2;
    public const int MaximumWorkspaceLabelLength = 32;

    public static string PipeName { get; } = BuildPipeName();

    public static string KeyPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIVitals",
        "agent-activity.key");

    public static byte[] Serialize(AgentActivityWireMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions);

    public static AgentActivityWireMessage? Deserialize(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty || payload.Length > MaximumMessageBytes) return null;
        try
        {
            var message = JsonSerializer.Deserialize<AgentActivityWireMessage>(payload, SerializerOptions);
            if (message is null
                || message.SchemaVersion is < 1 or > CurrentSchemaVersion
                || !Enum.IsDefined(message.Provider)
                || !Enum.IsDefined(message.Event)
                || !IsOpaqueIdentifier(message.SessionKey)
                || (message.ToolKey is not null && !IsOpaqueIdentifier(message.ToolKey)))
                return null;

            // A malformed label is dropped on its own; the transition it carries is still useful.
            return IsWorkspaceLabel(message.WorkspaceLabel)
                ? message
                : message with { WorkspaceLabel = null };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static async Task WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (payload.IsEmpty || payload.Length > MaximumMessageBytes)
            throw new ArgumentOutOfRangeException(nameof(payload));

        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
        await stream.WriteAsync(length, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[sizeof(int)];
        if (!await ReadExactlyAsync(stream, lengthBytes, cancellationToken)) return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length is <= 0 or > MaximumMessageBytes) return null;

        var payload = new byte[length];
        return await ReadExactlyAsync(stream, payload, cancellationToken) ? payload : null;
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[read..], cancellationToken);
            if (count == 0) return false;
            read += count;
        }

        return true;
    }

    /// <summary>
    /// A workspace label is the leaf directory name of an agent session and nothing else: no path
    /// separators, no drive letters and no control characters ever cross the pipe.
    /// </summary>
    public static bool IsWorkspaceLabel(string? value) =>
        value is null
        || (value.Length is > 0 and <= MaximumWorkspaceLabelLength
            && !value.Any(character =>
                char.IsControl(character)
                || character is '/' or '\\' or ':'));

    private static bool IsOpaqueIdentifier(string value) =>
        value.Length is >= 32 and <= 64
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string BuildPipeName()
    {
        var userIdentity = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userIdentity));
        return $"aivitals-agent-activity-{Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant()}";
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public sealed record AgentActivityWireMessage(
    int SchemaVersion,
    AgentActivityProvider Provider,
    AgentActivityEvent Event,
    string SessionKey,
    string? ToolKey,
    DateTimeOffset OccurredAt,
    string? WorkspaceLabel = null);
