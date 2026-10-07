using System.Text.Json;
using AIVitals.Adapters.Codex;

namespace AIVitals.AdapterContractTests;

public sealed class OrcaCodexContinuationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AIVitals.OrcaTests", Guid.NewGuid().ToString("N"));
    private static readonly CodexPausedThread Thread = new("thread-a", "Codex", "C:/work", "blocked-a",
        DateTimeOffset.UtcNow, "C:/orca/home/sessions/a.jsonl", null, null, "C:/orca/home");

    [Fact]
    public void Matches_session_and_pane_even_with_multiple_Codex_terminals_in_the_project()
    {
        var terminal = OrcaCodexContinuation.Match(Thread, Status(), Listing());
        Assert.Equal(new OrcaCodexTerminal("term-a", "instance-a"), terminal);
    }

    [Theory]
    [InlineData("wrong-session")]
    [InlineData("wrong-transcript")]
    [InlineData("remote-host")]
    [InlineData("duplicate-session")]
    [InlineData("new-schema")]
    public void Unproven_ownership_is_rejected(string change)
    {
        var status = change switch
        {
            "wrong-session" => Status().Replace("thread-a", "thread-b"),
            "wrong-transcript" => Status().Replace("sessions/a.jsonl", "sessions/b.jsonl"),
            "new-schema" => Status().Replace("\"version\":2", "\"version\":3"),
            _ => Status()
        };
        var listing = change == "remote-host" ? Listing("remote") : Listing();
        if (change == "duplicate-session") listing = Json(new { ok = true, result = new { terminals = new[] { Terminal(), Terminal() } } });
        Assert.Null(OrcaCodexContinuation.Match(Thread, status, listing));
    }

    [Fact]
    public void Hard_linked_transcript_in_the_Orca_runtime_home_proves_ownership()
    {
        // Orca mirrors default-home rollouts into its runtime home as hard links. The scanner may
        // report the mirror while Orca's hook status names the default-home path.
        var reported = Path.Combine(_root, "default", "a.jsonl");
        var mirror = Path.Combine(_root, "mirror", "a.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(reported)!);
        Directory.CreateDirectory(Path.GetDirectoryName(mirror)!);
        File.WriteAllText(reported, "{}");
        Assert.True(CreateHardLink(mirror, reported, IntPtr.Zero));
        var thread = Thread with { RolloutPath = mirror };
        var status = Status().Replace(JsonSerializer.Serialize(Thread.RolloutPath).Trim('"'), JsonSerializer.Serialize(reported).Trim('"'));

        Assert.Equal(new OrcaCodexTerminal("term-a", "instance-a"), OrcaCodexContinuation.Match(thread, status, Listing()));

        var copy = Path.Combine(_root, "copy", "a.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(reported, copy);
        Assert.Null(OrcaCodexContinuation.Match(Thread with { RolloutPath = copy }, status, Listing()));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

    [Theory]
    [InlineData("› Ask Codex to do anything", true)]
    [InlineData("›", true)]
    [InlineData("› User draft", false)]
    [InlineData("Trust this directory?", false)]
    public void Drafts_and_modals_are_not_overwritten(string preview, bool empty) =>
        Assert.Equal(empty, OrcaCodexContinuation.EmptyComposer(preview));

    [Fact]
    public async Task Original_terminal_is_used_and_only_a_new_backend_turn_confirms_resume()
    {
        var fixture = CreateFixture();
        var result = await fixture.Adapter.TryContinueAsync(Thread, "continue", CancellationToken.None);
        Assert.Equal(OrcaCodexContinueResult.Continued, result);
        var send = Assert.Single(fixture.Calls, args => args[1] == "send");
        Assert.Contains("term-a", send);
        Assert.Contains("--wait-submit", send);
        Assert.Equal(2, fixture.Client.TurnReads);
    }

    [Theory]
    [InlineData("busy", OrcaCodexContinueResult.Held, "not-idle")]
    [InlineData("draft", OrcaCodexContinueResult.Held, "composer-not-empty")]
    [InlineData("restarted", OrcaCodexContinueResult.Held, "terminal-changed")]
    [InlineData("quota", OrcaCodexContinueResult.QuotaUnavailable, "quota")]
    [InlineData("completed", OrcaCodexContinueResult.NoLongerPaused, "no-longer-paused")]
    public async Task Preflight_never_sends_to_busy_replaced_or_already_completed_tasks(string change,
        OrcaCodexContinueResult expected, string detail)
    {
        var fixture = CreateFixture(change);
        Assert.Equal(expected, await fixture.Adapter.TryContinueAsync(Thread, "continue", CancellationToken.None));
        Assert.Equal(detail, fixture.Adapter.LastDetail);
        Assert.DoesNotContain(fixture.Calls, args => args[1] == "send");
    }

    [Fact]
    public async Task Lost_send_reply_is_unknown_and_does_not_resend()
    {
        var fixture = CreateFixture("lost-reply");
        Assert.Equal(OrcaCodexContinueResult.OutcomeUnknown,
            await fixture.Adapter.TryContinueAsync(Thread, "continue", CancellationToken.None));
        Assert.Single(fixture.Calls, args => args[1] == "send");
    }

    [Fact]
    public void Runtime_completion_removes_a_stale_default_home_pause()
    {
        var result = CodexPausedThreadScanner.MergeScans([
            new([Thread with { HomeDirectory = "C:/default" }], [], new HashSet<string> { Thread.ThreadId }),
            new([], [], new HashSet<string> { Thread.ThreadId })]);
        Assert.Empty(result.Threads);
        var paused = CodexPausedThreadScanner.MergeScans([new([Thread with { HomeDirectory = "C:/default" }], []), new([Thread], [])]);
        Assert.Equal(Thread.HomeDirectory, Assert.Single(paused.Threads).HomeDirectory);
    }

    private (OrcaCodexContinuation Adapter, List<IReadOnlyList<string>> Calls, Client Client) CreateFixture(string? change = null)
    {
        Directory.CreateDirectory(Path.Combine(_root, "agent-hooks"));
        File.WriteAllText(Path.Combine(_root, "agent-hooks", "last-status.json"), Status());
        var calls = new List<IReadOnlyList<string>>();
        var client = new Client(change);
        Task<JsonElement> Run(IReadOnlyList<string> args, CancellationToken token)
        {
            calls.Add(args);
            return Task.FromResult(args[1] switch
            {
                "list" => Listing(),
                "wait" => Json(new { result = new { wait = new { satisfied = change != "busy" } } }),
                "show" => Json(new { result = new { terminal = Terminal(incarnation: change == "restarted" ? "instance-b" : "instance-a",
                    preview: change == "draft" ? "› User draft" : "› Ask Codex to do anything") } }),
                "read" => Json(new { result = new { terminal = new { tail = new[] { change == "draft" ? "› User draft" : "› Ask Codex to do anything" } } } }),
                "send" when change == "lost-reply" => throw new IOException("Lost reply"),
                "send" => Json(new { ok = true, result = new { accepted = true, stage = "input_accepted" } }),
                _ => throw new InvalidOperationException()
            });
        }
        return (new(_root, Run, client), calls, client);
    }

    private static object Terminal(string host = "local", string incarnation = "instance-a", string preview = "›") => new
    { handle = "term-a", incarnationId = incarnation, tabId = "tab-a", leafId = "leaf-a", worktreeId = "repo::C:/work",
        worktreePath = "C:/work", agentIdentity = "codex", executionHostId = host, orphaned = false, connected = true, writable = true, preview };
    private static JsonElement Listing(string host = "local") => Json(new { ok = true, result = new { terminals = new[]
        { Terminal(host), new { handle = "term-b", tabId = "tab-b", leafId = "leaf-b" } as object } } });
    private static string Status() => JsonSerializer.Serialize(new { version = 2, entries = new Dictionary<string, object>
        { ["tab-a:leaf-a"] = new { source = "codex", worktreeId = "repo::C:/work", connectionId = (string?)null,
            providerSession = new { key = "session_id", id = Thread.ThreadId, transcriptPath = Thread.RolloutPath } } } });
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    private sealed class Client(string? change) : ICodexAppServerClient, ICodexAppServerClientFactory
    {
        public int TurnReads { get; private set; }
        public ICodexAppServerClient Create() => this;
        public Task StartAsync(CancellationToken token) => Task.CompletedTask;
        public Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken token)
        {
            if (method == "account/rateLimits/read") return Task.FromResult(Json(new { ordinaryUsageAllowed = change != "quota", rateLimits = new
                { primary = new { usedPercent = 0, windowDurationMins = 300,
                    resetsAt = DateTimeOffset.UtcNow.AddHours(3).ToUnixTimeSeconds() } } }));
            Assert.Equal("thread/turns/list", method);
            TurnReads++;
            return Task.FromResult(Json(new { data = new[] { new { id = TurnReads == 1 ? "blocked-a" : "new-a",
                status = TurnReads == 1 && change != "completed" ? "failed" : "completed",
                error = new { codexErrorInfo = "usageLimitExceeded" } } } }));
        }
        public async IAsyncEnumerable<CodexServerNotification> ReadNotificationsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        { await Task.CompletedTask; yield break; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
