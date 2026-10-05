using AIVitals.Adapters.Codex;

namespace AIVitals.AdapterContractTests;

public sealed class CodexExecutableLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AIVitals.CodexLocator", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Npm_shim_resolves_to_node_process_for_redirected_stdio()
    {
        var codexCommand = Path.Combine(_root, "codex.cmd");
        var nodeExecutable = Path.Combine(_root, "node.exe");
        var codexScript = Path.Combine(_root, "node_modules", "@openai", "codex", "bin", "codex.js");
        Directory.CreateDirectory(Path.GetDirectoryName(codexScript)!);
        File.WriteAllText(codexCommand, "@echo off");
        File.WriteAllBytes(nodeExecutable, []);
        File.WriteAllText(codexScript, string.Empty);

        var command = CodexExecutableLocator.Resolve(codexCommand);

        Assert.Equal(nodeExecutable, command.FileName);
        Assert.Equal([codexScript, "app-server", "--stdio"], command.Arguments);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Pnpm_shim_is_resolved_without_cmd_and_preserves_literal_arguments()
    {
        Directory.CreateDirectory(_root);
        var script = Path.Combine(_root, "package", "bin", "codex.js");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllText(script, string.Empty);
        File.WriteAllBytes(Path.Combine(_root, "node.exe"), []);
        var shim = Path.Combine(_root, "codex.cmd");
        File.WriteAllText(shim, "node \"%~dp0\\package\\bin\\codex.js\" %*");
        var arguments = new[] { "resume", "id", "literal & %PATH% \" text" };
        var command = CodexExecutableLocator.CreateCliCommand(shim, arguments);
        Assert.Equal(Path.Combine(_root, "node.exe"), command.FileName);
        Assert.Equal(new[] { script }.Concat(arguments), command.Arguments);
    }
}
