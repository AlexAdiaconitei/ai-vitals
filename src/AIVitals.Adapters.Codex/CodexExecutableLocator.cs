namespace AIVitals.Adapters.Codex;

internal sealed record CodexLaunchCommand(string FileName, IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? EnvironmentOverrides = null);

internal static class CodexExecutableLocator
{
    public static CodexLaunchCommand Resolve(string? configuredPath = null) =>
        CreateCliCommand(ResolveExecutable(configuredPath), ["app-server", "--stdio"]);

    public static string ResolveExecutable(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var fullPath = Path.GetFullPath(configuredPath);
            if (!File.Exists(fullPath)) throw new FileNotFoundException("The configured Codex executable does not exist.", fullPath);
            return fullPath;
        }

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var entry in pathEntries)
        {
            foreach (var extension in new[] { ".exe", ".cmd" })
            {
                var candidate = Path.Combine(entry.Trim('"'), "codex" + extension);
                if (File.Exists(candidate)) return candidate;
            }
        }

        throw new FileNotFoundException("Codex CLI was not found on PATH.");
    }

    public static CodexLaunchCommand CreateCliCommand(string executablePath, IReadOnlyList<string> arguments)
    {
        if (Path.GetExtension(executablePath).Equals(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(executablePath) ?? string.Empty;
            var nodeExecutable = Path.Combine(directory, "node.exe");
            var codexScript = Path.Combine(directory, "node_modules", "@openai", "codex", "bin", "codex.js");
            if (File.Exists(nodeExecutable) && File.Exists(codexScript))
                return new CodexLaunchCommand(nodeExecutable, [codexScript, .. arguments]);

            var shim = File.ReadAllText(executablePath);
            var match = System.Text.RegularExpressions.Regex.Match(shim, "\"([^\"]*codex\\.js)\"");
            if (match.Success)
            {
                var script = Path.GetFullPath(match.Groups[1].Value.Replace("%~dp0", directory + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase));
                var node = File.Exists(nodeExecutable) ? nodeExecutable : FindOnPath("node.exe");
                if (File.Exists(script) && node is not null) return new(node, [script, .. arguments]);
            }
            throw new FileNotFoundException("The Codex shim cannot be resolved without a command shell.", executablePath);
        }

        return new CodexLaunchCommand(executablePath, arguments);
    }

    private static string? FindOnPath(string fileName) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(entry => Path.Combine(entry.Trim('"'), fileName)).FirstOrDefault(File.Exists);
}
