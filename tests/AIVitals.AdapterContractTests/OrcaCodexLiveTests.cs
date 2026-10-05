using System.Text.Json;
using AIVitals.Adapters.Codex;

namespace AIVitals.AdapterContractTests;

public sealed class OrcaCodexLiveTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task Runtime_home_reads_and_binds_live_Orca_sessions_without_sending_input()
    {
        if (Environment.GetEnvironmentVariable("AI_VITALS_LIVE_ORCA") != "1") return;
        var home = Path.Combine(CodexHome.OrcaDirectory, "codex-runtime-home", "home");
        Assert.Contains(home, CodexHome.ResumeDirectories());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var client = new CodexAppServerClientFactory(codexHome: home).Create();
        await client.StartAsync(timeout.Token);
        var list = await client.RequestAsync("thread/list", new { limit = 100, sourceKinds = new[] { "cli" } }, timeout.Token);
        var threads = list.GetProperty("data").EnumerateArray().ToArray();
        Assert.NotEmpty(threads);
        var orca = new OrcaCodexContinuation();
        var matched = 0;
        foreach (var row in threads.Take(15))
        {
            var id = row.GetProperty("id").GetString()!;
            var thread = new CodexPausedThread(id, "Codex", row.GetProperty("cwd").GetString()!, "unused",
                DateTimeOffset.UtcNow, row.GetProperty("path").GetString(), null, null, home);
            if (await orca.FindAsync(thread, timeout.Token) is null) continue;
            var turns = await client.RequestAsync("thread/turns/list",
                new { threadId = id, limit = 1, sortDirection = "desc", itemsView = "notLoaded" }, timeout.Token);
            Assert.True(turns.TryGetProperty("data", out var data));
            var last = data.EnumerateArray().FirstOrDefault();
            output.WriteLine($"Bound live session {id}: latest persisted turn status = {(last.ValueKind == JsonValueKind.Object ? last.GetProperty("status").GetString() : "none")}");
            matched++;
        }
        Assert.True(matched > 0, "At least one local Orca panel must have a proven session mapping.");
        var scan = await new CodexPausedThreadScanner(codexHome: home).ScanDetailedAsync(timeout.Token);
        output.WriteLine($"Matched panels: {matched}; pending quota failures: {scan.Threads.Count}; unreadable threads: {scan.Failures.Count}. No terminal send or turn/start was issued.");
    }
}
