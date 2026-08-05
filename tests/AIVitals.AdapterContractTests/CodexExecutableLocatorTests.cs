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
}
