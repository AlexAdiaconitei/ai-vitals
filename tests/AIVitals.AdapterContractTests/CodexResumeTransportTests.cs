using System.Diagnostics;
using System.Text.Json;
using AIVitals.Adapters.Codex;

namespace AIVitals.AdapterContractTests;

public sealed class CodexResumeTransportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AIVitals.Transport", Guid.NewGuid().ToString("N"));
    private static readonly CodexPausedThread Paused = new("11111111-1111-1111-1111-111111111111", "thread", "D:/work",
        "blocked-turn", DateTimeOffset.UtcNow, null, null, null);

    [Fact]
    public async Task Server_request_with_matching_id_cannot_complete_a_client_request()
    {
        Directory.CreateDirectory(_root);
        var script = Path.Combine(_root, "server.ps1");
        await File.WriteAllTextAsync(script, """
            while ($null -ne ($line = [Console]::ReadLine())) {
                $message = $line | ConvertFrom-Json
                if ($null -eq $message.id) { continue }
                @{ id = $message.id; method = 'approval/test'; params = @{} } | ConvertTo-Json -Compress | ForEach-Object { [Console]::WriteLine($_) }
                @{ id = $message.id; result = @{ actualResponse = $true } } | ConvertTo-Json -Compress | ForEach-Object { [Console]::WriteLine($_) }
            }
            """);
        await using var client = new CodexAppServerClient(new(PowerShell, ["-NoProfile", "-NonInteractive", "-File", script]));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.StartAsync(timeout.Token);
        var response = await client.RequestAsync("test/read", null, timeout.Token);
        Assert.True(response.GetProperty("actualResponse").GetBoolean());
    }

    [Fact]
    public async Task Process_exit_without_a_new_turn_is_failed_not_launched()
    {
        using var process = StartHidden("exit 7");
        await process.WaitForExitAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await CodexResumeLauncher.ConfirmStartedAsync(process, new TurnsClient("blocked-turn"), Paused, timeout.Token);
        Assert.Equal(CodexResumeLaunchResult.Failed, result);
    }

    [Fact]
    public async Task A_request_after_reader_failure_fails_immediately_instead_of_hanging()
    {
        Directory.CreateDirectory(_root);
        var script = Path.Combine(_root, "broken-server.ps1");
        await File.WriteAllTextAsync(script, """
            while ($null -ne ($line = [Console]::ReadLine())) {
                $message = $line | ConvertFrom-Json
                if ($message.method -eq 'initialize') {
                    @{ id = $message.id; result = @{} } | ConvertTo-Json -Compress | ForEach-Object { [Console]::WriteLine($_) }
                } elseif ($message.method -eq 'initialized') { [Console]::WriteLine('invalid json') }
            }
            """);
        await using var client = new CodexAppServerClient(new(PowerShell, ["-NoProfile", "-NonInteractive", "-File", script]));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.StartAsync(timeout.Token);
        await using var notifications = client.ReadNotificationsAsync(timeout.Token).GetAsyncEnumerator();
        await Assert.ThrowsAnyAsync<JsonException>(async () => await notifications.MoveNextAsync());
        await Assert.ThrowsAsync<IOException>(async () =>
            await client.RequestAsync("test/read", null, timeout.Token).WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task A_new_turn_confirms_startup_even_if_the_process_already_exited()
    {
        using var process = StartHidden("exit 0");
        await process.WaitForExitAsync();
        var result = await CodexResumeLauncher.ConfirmStartedAsync(process, new TurnsClient("new-turn"), Paused, CancellationToken.None);
        Assert.Equal(CodexResumeLaunchResult.Launched, result);
    }

    [Fact]
    public async Task Missing_startup_reply_is_unknown_without_killing_the_visible_cli()
    {
        using var process = StartHidden("Start-Sleep -Seconds 5");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            var result = await CodexResumeLauncher.ConfirmStartedAsync(process, new TurnsClient("blocked-turn"), Paused, canceled.Token);
            Assert.Equal(CodexResumeLaunchResult.OutcomeUnknown, result);
            Assert.False(process.HasExited);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("interrupted")]
    public async Task A_new_failed_turn_in_a_visible_cli_is_not_reported_as_launched(string status)
    {
        using var process = StartHidden("exit 0");
        await process.WaitForExitAsync();
        Assert.Equal(CodexResumeLaunchResult.Failed,
            await CodexResumeLauncher.ConfirmStartedAsync(process, new TurnsClient("new-turn", status), Paused, CancellationToken.None));
    }

    private static string PowerShell => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell", "v1.0", "powershell.exe");
    private static Process StartHidden(string command)
    {
        var info = new ProcessStartInfo(PowerShell) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", command }) info.ArgumentList.Add(arg);
        return Process.Start(info)!;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class TurnsClient(string turnId, string status = "completed") : ICodexAppServerClient
    {
        public Task StartAsync(CancellationToken token) => Task.CompletedTask;
        public Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(JsonSerializer.SerializeToElement(new { data = new[] { new { id = turnId, status } } }));
        }
        public async IAsyncEnumerable<CodexServerNotification> ReadNotificationsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            await Task.CompletedTask;
            yield break;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
