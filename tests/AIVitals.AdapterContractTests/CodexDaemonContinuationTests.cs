using System.Text.Json;
using AIVitals.Adapters.Codex;

namespace AIVitals.AdapterContractTests;

public sealed class CodexDaemonContinuationTests
{
    private const string ThreadId = "01a0ffab-1c06-7502-999d-fc0195e4a946";
    private const string BlockedTurnId = "blocked-turn";

    [Fact]
    public async Task Open_cli_gets_one_turn_only_after_revalidating_the_blocked_turn()
    {
        var client = new RecordingClient([ThreadId], "idle");
        var result = await CodexDaemonContinuation.ContinueAsync(client, ThreadId, BlockedTurnId, "Continua.", CancellationToken.None);
        Assert.Equal(CodexDaemonContinueResult.Continued, result);
        Assert.Equal(["thread/loaded/list", "thread/resume", "thread/turns/list", "account/rateLimits/read", "turn/start"], client.Methods);
        using var turn = JsonDocument.Parse(client.Parameters["turn/start"]);
        Assert.Equal(["threadId", "input"], turn.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task Unloaded_thread_is_left_for_a_visible_cli()
    {
        var client = new RecordingClient(["other"], "idle");
        Assert.Equal(CodexDaemonContinueResult.NotLoaded,
            await CodexDaemonContinuation.ContinueAsync(client, ThreadId, BlockedTurnId, "Continue", CancellationToken.None));
        Assert.Equal(["thread/loaded/list"], client.Methods);
    }

    [Fact]
    public async Task Backend_denial_blocks_continuation_even_with_zero_usage()
    {
        var client = new RecordingClient([ThreadId], "idle", ordinaryAllowed: false);
        var result = await CodexDaemonContinuation.ContinueAsync(client, ThreadId, BlockedTurnId, "Continue", CancellationToken.None);
        Assert.DoesNotContain("turn/start", client.Methods);
        Assert.NotEqual(CodexDaemonContinueResult.Continued, result);
    }

    [Fact]
    public async Task Thread_already_working_receives_nothing()
    {
        var client = new RecordingClient([ThreadId], "active");
        Assert.Equal(CodexDaemonContinueResult.AlreadyRunning,
            await CodexDaemonContinuation.ContinueAsync(client, ThreadId, BlockedTurnId, "Continue", CancellationToken.None));
        Assert.DoesNotContain("turn/start", client.Methods);
    }

    [Fact]
    public async Task Usage_limit_system_error_is_resumable_after_revalidating_the_turn()
    {
        var client = new RecordingClient([ThreadId], "systemError");
        Assert.Equal(CodexDaemonContinueResult.Continued,
            await CodexDaemonContinuation.ContinueAsync(client, ThreadId, BlockedTurnId, "Continue", CancellationToken.None));
        Assert.Single(client.Methods, method => method == "turn/start");
    }

    [Fact]
    public async Task A_system_error_that_is_not_a_usage_limit_never_receives_a_turn()
    {
        var client = new RecordingClient([ThreadId], "systemError", errorInfo: "contextWindowExceeded");
        Assert.Equal(CodexDaemonContinueResult.NoLongerPaused,
            await CodexDaemonContinuation.ContinueAsync(client, ThreadId, BlockedTurnId, "Continue", CancellationToken.None));
        Assert.DoesNotContain("turn/start", client.Methods);
    }

    [Theory]
    [InlineData("completed", BlockedTurnId)]
    [InlineData("failed", "another-failure")]
    public async Task Changed_thread_never_receives_another_continuation(string lastStatus, string lastTurnId)
    {
        var client = new RecordingClient([ThreadId], "idle", lastStatus, lastTurnId);
        Assert.Equal(CodexDaemonContinueResult.NoLongerPaused,
            await CodexDaemonContinuation.ContinueAsync(client, ThreadId, BlockedTurnId, "Continue", CancellationToken.None));
        Assert.DoesNotContain("turn/start", client.Methods);
    }

    [Fact]
    public async Task Lost_reply_after_turn_start_is_unknown_and_is_not_retried()
    {
        var client = new RecordingClient([ThreadId], "idle", loseReply: true);
        Assert.Equal(CodexDaemonContinueResult.OutcomeUnknown,
            await CodexDaemonContinuation.ContinueAsync(client, ThreadId, BlockedTurnId, "Continue", CancellationToken.None));
        Assert.Single(client.Methods, method => method == "turn/start");
    }

    [Fact]
    public async Task Missing_daemon_reports_unavailable()
    {
        var socketPath = Path.Combine(Path.GetTempPath(), "AIVitals.NoDaemon", Guid.NewGuid().ToString("N") + ".sock");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal(CodexDaemonContinueResult.DaemonUnavailable,
            await CodexDaemonContinuation.TryContinueAsync(ThreadId, BlockedTurnId, "Continue", timeout.Token, socketPath));
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("interrupted")]
    public async Task A_new_turn_that_already_failed_is_not_reported_as_continued(string startStatus)
    {
        var client = new RecordingClient([ThreadId], "idle", startStatus: startStatus);
        Assert.Equal(CodexDaemonContinueResult.Failed,
            await CodexDaemonContinuation.ContinueAsync(client, ThreadId, BlockedTurnId, "Continue", CancellationToken.None));
        Assert.Single(client.Methods, method => method == "turn/start");
    }

    [Fact]
    public async Task Canceled_handshake_never_propagates_to_the_ui()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var result = await CodexDaemonContinuation.TryContinueAsync(ThreadId, BlockedTurnId, "Continue", canceled.Token,
            Path.Combine(Path.GetTempPath(), "missing.sock"));
        Assert.Equal(CodexDaemonContinueResult.DaemonUnavailable, result);
    }

    private sealed class RecordingClient(string[] loaded, string status, string lastStatus = "failed",
        string lastTurnId = BlockedTurnId, bool loseReply = false, string errorInfo = "usageLimitExceeded", bool? ordinaryAllowed = true,
        string startStatus = "inProgress") : ICodexAppServerClient
    {
        public List<string> Methods { get; } = [];
        public Dictionary<string, string> Parameters { get; } = [];
        public Task StartAsync(CancellationToken token) => Task.CompletedTask;
        public Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken token)
        {
            Methods.Add(method);
            Parameters[method] = JsonSerializer.Serialize(parameters);
            if (method == "turn/start" && loseReply) return Task.FromException<JsonElement>(new OperationCanceledException());
            var json = method switch
            {
                "thread/loaded/list" => JsonSerializer.Serialize(new { data = loaded, nextCursor = (string?)null }),
                "thread/resume" => JsonSerializer.Serialize(new { thread = new { id = ThreadId, status = new { type = status } } }),
                "thread/turns/list" => JsonSerializer.Serialize(new
                {
                    data = new[] { new { id = lastTurnId, status = lastStatus,
                    error = new { codexErrorInfo = errorInfo } } }
                }),
                "turn/start" => JsonSerializer.Serialize(new { turn = new { id = "new-turn", status = startStatus } }),
                "account/rateLimits/read" => JsonSerializer.Serialize(new { ordinaryUsageAllowed = ordinaryAllowed,
                    rateLimits = new { primary = new { usedPercent = 0, windowDurationMins = 300, resetsAt = DateTimeOffset.UtcNow.AddHours(5).ToUnixTimeSeconds() } } }),
                _ => throw new CodexRpcException(-32601, "Unexpected method")
            };
            return Task.FromResult(JsonSerializer.Deserialize<JsonElement>(json));
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
