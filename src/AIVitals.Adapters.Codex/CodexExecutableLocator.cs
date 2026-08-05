namespace AIVitals.Adapters.Codex;

internal sealed record CodexLaunchCommand(string FileName, IReadOnlyList<string> Arguments);

internal static class CodexExecutableLocator
{
    public static CodexLaunchCommand Resolve(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var fullPath = Path.GetFullPath(configuredPath);
            if (!File.Exists(fullPath)) throw new FileNotFoundException("The configured Codex executable does not exist.", fullPath);
            return CreateCommand(fullPath);
        }

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var entry in pathEntries)
        {
            foreach (var extension in new[] { ".exe", ".cmd" })
            {
                var candidate = Path.Combine(entry.Trim('"'), "codex" + extension);
                if (File.Exists(candidate)) return CreateCommand(candidate);
            }
        }

        throw new FileNotFoundException("Codex CLI was not found on PATH.");
    }

    private static CodexLaunchCommand CreateCommand(string executablePath)
    {
        if (Path.GetExtension(executablePath).Equals(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(executablePath) ?? string.Empty;
            var nodeExecutable = Path.Combine(directory, "node.exe");
            var codexScript = Path.Combine(directory, "node_modules", "@openai", "codex", "bin", "codex.js");
            if (File.Exists(nodeExecutable) && File.Exists(codexScript))
                return new CodexLaunchCommand(nodeExecutable, [codexScript, "app-server", "--stdio"]);

            var commandInterpreter = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            return new CodexLaunchCommand(commandInterpreter, ["/d", "/s", "/c", "call", executablePath, "app-server", "--stdio"]);
        }

        return new CodexLaunchCommand(executablePath, ["app-server", "--stdio"]);
    }
}
