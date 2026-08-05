using System.Security.Cryptography;

namespace AIVitals.Adapters.ClaudeCode;

internal static class ClaudeCodeSessionKeyStore
{
    public static byte[] LoadOrCreate(string? path = null)
    {
        var keyPath = path ?? ClaudeCodeBridgeProtocol.SessionKeyPath;
        if (File.Exists(keyPath))
        {
            var existing = File.ReadAllBytes(keyPath);
            if (existing.Length >= 32) return existing;
            throw new InvalidDataException("The Claude Code session pseudonym key is invalid.");
        }

        var directory = Path.GetDirectoryName(keyPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var key = RandomNumberGenerator.GetBytes(32);
        var temporaryPath = keyPath + ".tmp";
        using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(key);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporaryPath, keyPath, overwrite: false);
        return key;
    }
}
