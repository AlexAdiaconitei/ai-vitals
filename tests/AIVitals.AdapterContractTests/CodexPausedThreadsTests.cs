using System.Text.Json;
using AIVitals.Adapters.Codex;

namespace AIVitals.AdapterContractTests;

public sealed class CodexPausedThreadsTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1790994600);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AIVitals.CodexPaused", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Scanner_keeps_root_threads_whose_last_turn_hit_the_usage_limit()
    {
        var client = new ScriptedClient(
            """
            { "data": [
              { "id": "11111111-1111-1111-1111-111111111111", "name": "Crear marcos", "cwd": "D:\\work\\prints",
                "updatedAt": 1790994611, "path": "C:\\rollout-a.jsonl", "model": "gpt-6.1-sol", "reasoningEffort": "high",
                "parentThreadId": null, "ephemeral": false },
              { "id": "22222222-2222-2222-2222-222222222222", "name": "Subagente", "cwd": "D:\\work\\prints",
                "updatedAt": 1790994611, "parentThreadId": "11111111-1111-1111-1111-111111111111" },
              { "id": "33333333-3333-3333-3333-333333333333", "name": "Terminado", "cwd": "D:\\work\\done",
                "updatedAt": 1790994611 },
              { "id": "44444444-4444-4444-4444-444444444444", "name": "Antiguo", "cwd": "D:\\work\\old",
                "updatedAt": 1785000000 }
            ] }
            """,
            new Dictionary<string, string>
            {
                ["11111111-1111-1111-1111-111111111111"] = FailedTurn("turn-a", "usageLimitExceeded", 1790982672),
                ["22222222-2222-2222-2222-222222222222"] = FailedTurn("turn-b", "usageLimitExceeded", 1790982669),
                ["33333333-3333-3333-3333-333333333333"] = """{ "data": [ { "id": "turn-c", "status": "completed", "error": null } ] }""",
                ["44444444-4444-4444-4444-444444444444"] = FailedTurn("turn-d", "usageLimitExceeded", 1785000000)
            });
        var scanner = new CodexPausedThreadScanner(new ScriptedClientFactory(client), new FixedTime(Now));

        var paused = Assert.Single(await scanner.ScanAsync(CancellationToken.None, showNames: true));

        Assert.Equal("11111111-1111-1111-1111-111111111111", paused.ThreadId);
        Assert.Equal("Crear marcos", paused.Name);
        Assert.Equal(@"D:\work\prints", paused.WorkingDirectory);
        Assert.Equal("turn-a", paused.BlockedTurnId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790982672), paused.BlockedAtUtc);
        Assert.Equal(@"C:\rollout-a.jsonl", paused.RolloutPath);
        // The subagent and the stale thread are never asked for their turns.
        Assert.DoesNotContain("22222222-2222-2222-2222-222222222222", client.TurnRequests);
        Assert.DoesNotContain("44444444-4444-4444-4444-444444444444", client.TurnRequests);
    }

    [Theory]
    [InlineData("contextWindowExceeded")]
    [InlineData("rateLimitExceeded")]
    public void Other_failures_are_not_paused_threads(string errorInfo)
    {
        using var thread = JsonDocument.Parse("""{ "id": "t", "cwd": "D:\\work", "updatedAt": 1790994611 }""");
        using var turns = JsonDocument.Parse(FailedTurn("turn", errorInfo, 1790982672));

        Assert.Null(CodexPausedThreadScanner.MapPausedThread(thread.RootElement, turns.RootElement));
    }

    [Fact]
    public void Turn_context_is_read_from_the_newest_entry_even_when_the_tail_starts_mid_line()
    {
        var tail = string.Join('\n',
            "_policy\":\"never\"}}",
            """{"type":"turn_context","payload":{"approval_policy":"on-request","sandbox_policy":{"type":"read-only"},"model":"gpt-5","collaboration_mode":{"settings":{"reasoning_effort":"low"}}}}""",
            """{"type":"event_msg","payload":{"type":"task_complete"}}""",
            """{"type":"turn_context","payload":{"approval_policy":"never","sandbox_policy":{"type":"danger-full-access"},"model":"gpt-6.1-sol","collaboration_mode":{"settings":{"reasoning_effort":"high"}}}}""",
            """{"type":"event_msg","payload":{"type":"task_complete","error":{"codex_error_info":"usage_limit_exceeded"}}}""");

        var context = CodexTurnContextReader.ParseLatest(tail);

        Assert.NotNull(context);
        Assert.Equal("gpt-6.1-sol", context.Model);
        Assert.Equal("high", context.ReasoningEffort);
        Assert.Equal("never", context.ApprovalPolicy);
        Assert.Equal("danger-full-access", context.SandboxMode);
    }

    [Fact]
    public void Resume_arguments_carry_the_threads_own_permissions()
    {
        var arguments = CodexResumeLauncher.BuildArguments(
            Paused(),
            new CodexTurnContext("gpt-6.1-sol", "high", "never", "danger-full-access"),
            "Continúa la tarea donde la dejaste.");

        Assert.Equal(
            [
                "resume", "11111111-1111-1111-1111-111111111111", "Continúa la tarea donde la dejaste.", "--no-daemon",
                "-c", "model=gpt-6.1-sol",
                "-c", "model_reasoning_effort=high",
                "-c", "approval_policy=never",
                "-c", "approvals_reviewer=user",
                "-c", "sandbox_mode=danger-full-access"
            ],
            arguments);
    }

    [Theory]
    [InlineData("never", "full-trust", "gpt-6.1-sol")]
    [InlineData("always", "read-only", "gpt-6.1-sol")]
    [InlineData("never", "read-only", "gpt\"; rm")]
    [InlineData("never", "read-only", "5")]
    public void Unrecognised_permissions_refuse_to_launch(string approval, string sandbox, string model)
    {
        var arguments = CodexResumeLauncher.BuildArguments(Paused(), new CodexTurnContext(model, null, approval, sandbox), "go");

        Assert.Null(arguments);
    }

    [Fact]
    public void Unknown_permissions_refuse_to_launch()
    {
        Assert.Null(CodexResumeLauncher.BuildArguments(Paused(), null, "go"));
    }

    [Fact]
    public async Task Scanner_follows_next_cursor_to_find_a_paused_thread()
    {
        const string id = "11111111-1111-1111-1111-111111111111";
        var client = new ScriptedClient("""{"data":[],"nextCursor":"page-two"}""",
            new Dictionary<string, string> { [id] = FailedTurn("turn-a", "usageLimitExceeded", 1790994600) },
            """{"data":[{"id":"11111111-1111-1111-1111-111111111111","cwd":"D:/work","updatedAt":1790994600}]}""");
        var result = await new CodexPausedThreadScanner(new ScriptedClientFactory(client), new FixedTime(Now))
            .ScanAsync(CancellationToken.None);
        Assert.Equal(id, Assert.Single(result).ThreadId);
    }

    [Fact]
    public async Task An_unsupported_thread_does_not_hide_other_paused_threads()
    {
        var client = new ScriptedClient("""{"data":[{"id":"unsupported","cwd":"D:/work","updatedAt":1790994600},{"id":"valid","cwd":"D:/work","updatedAt":1790994600}]}""",
            new Dictionary<string, string> { ["valid"] = FailedTurn("turn-valid", "usageLimitExceeded", 1790994600) });
        var paused = await new CodexPausedThreadScanner(new ScriptedClientFactory(client), new FixedTime(Now)).ScanAsync(CancellationToken.None);
        Assert.Equal("valid", Assert.Single(paused).ThreadId);
    }

    [Fact]
    public void Titles_and_prompt_previews_are_hidden_by_default()
    {
        var thread = JsonSerializer.Deserialize<JsonElement>("""{"id":"11111111-1111-1111-1111-111111111111","cwd":"D:/work","name":"private title","preview":"private prompt"}""");
        var turns = JsonSerializer.Deserialize<JsonElement>(FailedTurn("turn-a", "usageLimitExceeded", 1790994600));
        Assert.Equal("Codex 11111111", CodexPausedThreadScanner.MapPausedThread(thread, turns)!.Name);
    }

    [Theory]
    [InlineData("completed", "turn-a")]
    [InlineData("failed", "new-turn")]
    public async Task Preflight_rejects_threads_that_changed_since_scan(string status, string turnId)
    {
        var paused = Paused();
        var last = JsonSerializer.Serialize(new { data = new[] { new { id = turnId, status,
            error = new { codexErrorInfo = "usageLimitExceeded" } } } });
        var client = new ScriptedClient("{}", new Dictionary<string, string> { [paused.ThreadId] = last },
            threadRead: JsonSerializer.Serialize(new { thread = new { id = paused.ThreadId, cwd = paused.WorkingDirectory, updatedAt = 1790994600 } }));
        var result = await new CodexPausedThreadScanner(new ScriptedClientFactory(client), new FixedTime(Now))
            .ValidateAsync(paused, CancellationToken.None);
        Assert.Null(result.Thread);
        Assert.Empty(result.Quotas);
    }

    [Fact]
    public void Workspace_permissions_explicitly_override_roots_network_and_temp_flags()
    {
        var context = new CodexTurnContext("gpt-6.1-sol", "high", "on-request", "workspace-write",
            """{"type":"workspace-write","writable_roots":["D:\\restricted folder"],"network_access":false,"exclude_tmpdir_env_var":true,"exclude_slash_tmp":true}""");
        var args = CodexResumeLauncher.BuildArguments(Paused(), context, "go")!;
        Assert.Contains("sandbox_workspace_write.writable_roots=[\"D:\\\\restricted folder\"]", args);
        Assert.Contains("sandbox_workspace_write.network_access=false", args);
        Assert.Contains("sandbox_workspace_write.exclude_tmpdir_env_var=true", args);
        Assert.Contains("sandbox_workspace_write.exclude_slash_tmp=true", args);
        Assert.Contains("--no-daemon", args);
    }

    [Theory]
    [InlineData("""{"type":"read-only","access":{"type":"restricted","readable_roots":["D:/private"]}}""", "read-only")]
    [InlineData("""{"type":"workspace-write","read_only_access":{"type":"restricted"}}""", "workspace-write")]
    [InlineData("""{"type":"danger-full-access","future_constraint":true}""", "danger-full-access")]
    public void Policies_that_cannot_be_reproduced_are_refused(string policy, string mode)
    {
        Assert.Null(CodexResumeLauncher.BuildArguments(Paused(),
            new("gpt-6.1-sol", "high", "never", mode, policy), "go"));
    }

    [Fact]
    public void Custom_permission_profiles_are_not_replaced_with_legacy_defaults()
    {
        Assert.Null(CodexTurnContextReader.ParseLatest("""{"type":"turn_context","payload":{"model":"gpt-6.1-sol","effort":"high","approval_policy":"never","sandbox_policy":{"type":"read-only"},"permission_profile":{"type":"restricted"}}}"""));
    }

    [Fact]
    public void Top_level_effort_and_turn_identity_are_read()
    {
        var context = CodexTurnContextReader.ParseLatest("""{"type":"turn_context","payload":{"turn_id":"turn-a","model":"gpt-6.1-sol","effort":"high","approval_policy":"never","sandbox_policy":{"type":"danger-full-access"},"permission_profile":{"type":"disabled"},"active_permission_profile":{"id":":danger-full-access"}}}""");
        Assert.Equal("high", context!.ReasoningEffort);
        Assert.Equal("turn-a", context.TurnId);
    }

    [Fact]
    public void Builtin_profile_with_a_null_parent_is_supported()
    {
        Assert.NotNull(CodexTurnContextReader.ParseLatest("""{"type":"turn_context","payload":{"model":"gpt-6.1-sol","effort":"high","approval_policy":"never","sandbox_policy":{"type":"danger-full-access"},"active_permission_profile":{"id":":danger-full-access","extends":null}}}"""));
        Assert.Null(CodexTurnContextReader.ParseLatest("""{"type":"turn_context","payload":{"model":"gpt-6.1-sol","effort":"high","approval_policy":"never","sandbox_policy":{"type":"danger-full-access"},"active_permission_profile":{"id":":danger-full-access","extends":"custom"}}}"""));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Writer_lock_probe_tells_held_from_free_and_missing()
    {
        Directory.CreateDirectory(_root);
        var lockPath = Path.Combine(_root, "thread.lock");
        File.WriteAllBytes(lockPath, []);

        Assert.False(CodexThreadLock.IsHeldAt(Path.Combine(_root, "missing.lock")));
        Assert.False(CodexThreadLock.IsHeldAt(lockPath));
        using (var owner = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
        {
            owner.Lock(0, long.MaxValue);
            Assert.True(CodexThreadLock.IsHeldAt(lockPath));
            owner.Unlock(0, long.MaxValue);
        }
        Assert.False(CodexThreadLock.IsHeldAt(lockPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static CodexPausedThread Paused() =>
        new("11111111-1111-1111-1111-111111111111", "Crear marcos", @"D:\work\prints", "turn-a", Now, null, null, null);

    private static string FailedTurn(string turnId, string errorInfo, long completedAt) =>
        $$"""
        { "data": [ { "id": "{{turnId}}", "status": "failed", "completedAt": {{completedAt}},
          "error": { "message": "You've hit your usage limit.", "codexErrorInfo": "{{errorInfo}}" } } ] }
        """;

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ScriptedClientFactory(ICodexAppServerClient client) : ICodexAppServerClientFactory
    {
        public ICodexAppServerClient Create() => client;
    }

    private sealed class ScriptedClient(string threadList, IReadOnlyDictionary<string, string> turnsByThread,
        string? secondPage = null, string? threadRead = null) : ICodexAppServerClient
    {
        public List<string> TurnRequests { get; } = [];

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellationToken)
        {
            var json = method switch
            {
                "thread/list" => JsonSerializer.SerializeToElement(parameters).TryGetProperty("cursor", out var cursor)
                    && cursor.ValueKind == JsonValueKind.String ? secondPage! : threadList,
                "thread/read" => threadRead!,
                "thread/turns/list" => TurnsFor(parameters),
                "account/rateLimits/read" => """{"rateLimits":{"primary":{"usedPercent":0,"windowDurationMins":300,"resetsAt":1791012600}}}""",
                _ => throw new CodexRpcException(-32601, "Unexpected method")
            };
            using var document = JsonDocument.Parse(json);
            return Task.FromResult(document.RootElement.Clone());
        }

        private string TurnsFor(object? parameters)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(parameters));
            var threadId = document.RootElement.GetProperty("threadId").GetString()!;
            TurnRequests.Add(threadId);
            if (!turnsByThread.TryGetValue(threadId, out var turns)) throw new CodexRpcException(-32601, "Unsupported turn history");
            return turns;
        }

        public async IAsyncEnumerable<CodexServerNotification> ReadNotificationsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
