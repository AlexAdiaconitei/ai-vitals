using System.Text.Json;
using AIVitals.Application;
using AIVitals.Infrastructure;

namespace AIVitals.IntegrationTests;

public sealed class CodexResumeDiagnosticLogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AIVitals.ResumeLog", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Writes_a_bounded_log_with_safe_failure_categories()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "resume.jsonl");
        File.WriteAllText(path, new string(' ', 512 * 1024));
        new CodexResumeDiagnosticLog(path).Write(new(DateTimeOffset.UtcNow,
            CodexResumeDiagnosticEvent.ScanFailed, Failure: CodexResumeFailureKind.Rpc, RpcCode: -32601));
        Assert.True(File.Exists(path + ".previous"));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("ScanFailed", document.RootElement.GetProperty("event").GetString());
        Assert.Equal(-32601, document.RootElement.GetProperty("rpcCode").GetInt32());
        Assert.DoesNotContain("message", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unwritable_log_does_not_break_automation()
    {
        Directory.CreateDirectory(_root);
        new CodexResumeDiagnosticLog(_root).Write(new(DateTimeOffset.UtcNow, CodexResumeDiagnosticEvent.ScanCompleted));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
