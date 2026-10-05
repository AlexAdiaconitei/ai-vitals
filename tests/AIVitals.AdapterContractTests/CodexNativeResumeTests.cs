using System.Text.Json;
using AIVitals.Adapters.Codex;

namespace AIVitals.AdapterContractTests;

public sealed class CodexNativeResumeTests(Xunit.Abstractions.ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AIVitals.NativeResume", Guid.NewGuid().ToString("N"));
    private readonly string _threadId = Guid.NewGuid().ToString();
    private readonly string _turnId = Guid.NewGuid().ToString();

    [Fact]
    [Trait("Category", "Native")]
    public async Task Installed_shared_daemon_still_responds_after_our_websocket_disconnects()
    {
        if (Environment.GetEnvironmentVariable("AI_VITALS_LIVE_CODEX") != "1") return;
        var home = Environment.GetEnvironmentVariable("AI_VITALS_TEST_CODEX_HOME") ?? CodexHome.Directory;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var client = CodexAppServerClient.ForSocket(CodexDaemonContinuation.DefaultSocketPath(home));
            await client.StartAsync(timeout.Token);
            var loaded = await client.RequestAsync("thread/loaded/list", new { }, timeout.Token);
            Assert.Equal(JsonValueKind.Array, loaded.GetProperty("data").ValueKind);
            var limits = await client.RequestAsync("account/rateLimits/read", null, timeout.Token);
            Assert.True(limits.TryGetProperty("ordinaryUsageAllowed", out var ordinary));
            Assert.Contains(ordinary.ValueKind, new[] { JsonValueKind.True, JsonValueKind.False, JsonValueKind.Null });
            output.WriteLine("Backend ordinaryUsageAllowed: " + ordinary.GetRawText());
        }
    }

    [Fact]
    [Trait("Category", "Native")]
    public async Task Real_codex_scans_a_synthetic_usage_limit_and_sees_a_later_turn_in_the_same_connection()
    {
        if (Environment.GetEnvironmentVariable("AI_VITALS_LIVE_CODEX") != "1") return;
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var directory = Path.Combine(_root, "sessions", now.ToString("yyyy"), now.ToString("MM"), now.ToString("dd"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"rollout-{now:yyyy-MM-ddTHH-mm-ss}-{_threadId}.jsonl");
        var records = new object[]
        {
            new { timestamp = now, type = "session_meta", payload = new { id = _threadId, timestamp = now,
                session_id = _threadId, cwd = _root, originator = "synthetic_test", cli_version = "0.160.0", source = "cli",
                thread_source = "cli", model_provider = "openai", base_instructions = new { text = "Synthetic test instructions" }, history_mode = "legacy" } },
            new { timestamp = now, type = "event_msg", payload = new { type = "task_started", turn_id = _turnId,
                started_at = now.ToUnixTimeSeconds(), model_context_window = (long?)null } },
            new { timestamp = now, type = "event_msg", payload = new { type = "user_message",
                message = "Synthetic metadata fixture", images = Array.Empty<string>(), local_images = Array.Empty<string>(), text_elements = Array.Empty<object>() } },
            new { timestamp = now, type = "response_item", payload = new { type = "message", role = "user",
                content = new[] { new { type = "input_text", text = "Synthetic metadata fixture" } } } },
            new { timestamp = now, type = "event_msg", payload = new { type = "task_complete", turn_id = _turnId,
                last_agent_message = (string?)null, error = new { message = "Synthetic usage limit", codex_error_info = "usage_limit_exceeded" },
                started_at = now.ToUnixTimeSeconds(), completed_at = now.ToUnixTimeSeconds(), duration_ms = 0 } }
        };
        await File.WriteAllLinesAsync(path, records.Select(record => JsonSerializer.Serialize(record)));
        var command = CodexExecutableLocator.Resolve() with
        { EnvironmentOverrides = new Dictionary<string, string> { ["CODEX_HOME"] = _root } };
        var factory = new Factory(command);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var scan = await new CodexPausedThreadScanner(factory).ScanDetailedAsync(timeout.Token);
        Assert.Empty(scan.Failures);
        Assert.Equal(_turnId, Assert.Single(scan.Threads).BlockedTurnId);

        await using var client = factory.Create();
        await client.StartAsync(timeout.Token);
        var before = await Turns(client, timeout.Token);
        Assert.True(CodexPausedThreadScanner.IsSameBlockedTurn(before, _turnId));
        var nextId = Guid.NewGuid().ToString();
        await File.AppendAllLinesAsync(path, new[]
        {
            JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, type = "event_msg",
                payload = new { type = "task_started", turn_id = nextId, started_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), model_context_window = (long?)null } }),
            JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, type = "event_msg",
                payload = new { type = "task_complete", turn_id = nextId, last_agent_message = (string?)null,
                    started_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), completed_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), duration_ms = 0 } })
        });
        var after = await Turns(client, timeout.Token);
        Assert.Equal(nextId, after.GetProperty("data")[0].GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("read-only", "readOnly")]
    [InlineData("workspace-write", "workspaceWrite")]
    [Trait("Category", "Native")]
    public async Task Resume_permission_overrides_preserve_the_sandbox_despite_a_different_global_profile(string mode, string expected)
    {
        if (Environment.GetEnvironmentVariable("AI_VITALS_LIVE_CODEX") != "1") return;
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "config.toml"),
            $"default_permissions = \":danger-full-access\"\n[windows]\nsandbox = \"unelevated\"\n[projects.{JsonSerializer.Serialize(_root)}]\ntrust_level = \"trusted\"\n");
        var context = new CodexTurnContext("gpt-6.1-sol", "high", "on-request", mode,
            mode == "workspace-write" ? """{"type":"workspace-write","writable_roots":[],"network_access":false,"exclude_tmpdir_env_var":true,"exclude_slash_tmp":true}""" : null);
        var paused = new CodexPausedThread(_threadId, "fixture", _root, _turnId, DateTimeOffset.UtcNow, null, null, null);
        var arguments = CodexResumeLauncher.BuildArguments(paused, context, "Synthetic fixture")!;
        var command = CodexExecutableLocator.CreateCliCommand(CodexExecutableLocator.ResolveExecutable(),
            ["app-server", "--stdio", .. arguments.Skip(4)]) with
        { EnvironmentOverrides = new Dictionary<string, string> { ["CODEX_HOME"] = _root } };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = new CodexAppServerClient(command);
        await client.StartAsync(timeout.Token);
        // Creating an ephemeral thread loads configuration; no turn or model request is sent.
        var response = await client.RequestAsync("thread/start", new { cwd = _root, ephemeral = true }, timeout.Token);
        var sandbox = response.GetProperty("sandbox");
        Assert.Equal(expected, sandbox.GetProperty("type").GetString());
        Assert.False(sandbox.GetProperty("networkAccess").GetBoolean());
        Assert.Equal("on-request", response.GetProperty("approvalPolicy").GetString());
        if (mode == "workspace-write")
        {
            Assert.Empty(sandbox.GetProperty("writableRoots").EnumerateArray());
            Assert.True(sandbox.GetProperty("excludeTmpdirEnvVar").GetBoolean());
            Assert.True(sandbox.GetProperty("excludeSlashTmp").GetBoolean());
        }
    }

    private Task<JsonElement> Turns(ICodexAppServerClient client, CancellationToken token) =>
        client.RequestAsync("thread/turns/list", new { threadId = _threadId, limit = 1, sortDirection = "desc", itemsView = "notLoaded" }, token);

    private sealed class Factory(CodexLaunchCommand command) : ICodexAppServerClientFactory
    { public ICodexAppServerClient Create() => new CodexAppServerClient(command); }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); return; }
            catch (IOException) { Thread.Sleep(100); }
        }
    }
}
