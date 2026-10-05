using System.Diagnostics;
using System.Text.Json;

namespace AIVitals.Adapters.Codex;

public enum OrcaCodexContinueResult { NotOwned, Held, Continued, NoLongerPaused, QuotaUnavailable, OutcomeUnknown }

public sealed record OrcaCodexTerminal(string Handle, string IncarnationId);

public sealed class OrcaCodexContinuation
{
    private readonly string _orcaDirectory;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<JsonElement>> _run;
    private readonly ICodexAppServerClientFactory? _clientFactory;

    public OrcaCodexContinuation() : this(CodexHome.OrcaDirectory, RunCliAsync) { }

    internal OrcaCodexContinuation(string orcaDirectory,
        Func<IReadOnlyList<string>, CancellationToken, Task<JsonElement>> run,
        ICodexAppServerClientFactory? clientFactory = null)
    { _orcaDirectory = orcaDirectory; _run = run; _clientFactory = clientFactory; }

    public async Task<OrcaCodexTerminal?> FindAsync(CodexPausedThread thread, CancellationToken cancellationToken)
    {
        var statusPath = Path.Combine(_orcaDirectory, "agent-hooks", "last-status.json");
        if (!File.Exists(statusPath)) return null;
        try
        {
            var list = await _run(["terminal", "list", "--json"], cancellationToken).ConfigureAwait(false);
            return Match(thread, File.ReadAllText(statusPath), list);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return null; }
    }

    internal static OrcaCodexTerminal? Match(CodexPausedThread thread, string statusJson, JsonElement list)
    {
        using var status = JsonDocument.Parse(statusJson);
        if (!status.RootElement.TryGetProperty("version", out var version) || !version.TryGetInt32(out var schema) || schema != 2 ||
            !status.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Object ||
            !list.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True ||
            !list.TryGetProperty("result", out var result) || !result.TryGetProperty("terminals", out var terminals) ||
            terminals.ValueKind != JsonValueKind.Array) return null;
        var matches = new List<OrcaCodexTerminal>();
        foreach (var terminal in terminals.EnumerateArray())
        {
            if (!EligibleTerminal(terminal) || !SamePath(Text(terminal, "worktreePath"), thread.WorkingDirectory)) continue;
            var paneKey = Text(terminal, "tabId") + ":" + Text(terminal, "leafId");
            if (!entries.TryGetProperty(paneKey, out var entry) || Text(entry, "source") != "codex" ||
                Text(entry, "worktreeId") != Text(terminal, "worktreeId") ||
                entry.TryGetProperty("connectionId", out var connection) && connection.ValueKind != JsonValueKind.Null ||
                !entry.TryGetProperty("providerSession", out var session) || Text(session, "key") != "session_id" ||
                Text(session, "id") != thread.ThreadId || !SamePath(Text(session, "transcriptPath"), thread.RolloutPath)) continue;
            if (Text(terminal, "handle") is { Length: > 0 } handle && Text(terminal, "incarnationId") is { Length: > 0 } incarnation)
                matches.Add(new(handle, incarnation));
        }
        return matches.Count == 1 ? matches[0] : null;
    }

    public async Task<OrcaCodexContinueResult> TryContinueAsync(CodexPausedThread thread, string prompt,
        CancellationToken cancellationToken)
    {
        try { return await ContinueCoreAsync(thread, prompt, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or JsonException or CodexRpcException or
            OperationCanceledException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { return OrcaCodexContinueResult.Held; }
    }

    private async Task<OrcaCodexContinueResult> ContinueCoreAsync(CodexPausedThread thread, string prompt,
        CancellationToken cancellationToken)
    {
        var terminal = await FindAsync(thread, cancellationToken).ConfigureAwait(false);
        if (terminal is null) return OrcaCodexContinueResult.NotOwned;
        var wait = await _run(["terminal", "wait", "--terminal", terminal.Handle, "--for", "tui-idle",
            "--timeout-ms", "1000", "--json"], cancellationToken).ConfigureAwait(false);
        if (!wait.TryGetProperty("result", out var waitResult) || !waitResult.TryGetProperty("wait", out var waiting) ||
            !True(waiting, "satisfied")) return OrcaCodexContinueResult.Held;
        var shown = await _run(["terminal", "show", "--terminal", terminal.Handle, "--json"], cancellationToken).ConfigureAwait(false);
        if (!shown.TryGetProperty("result", out var showResult) || !showResult.TryGetProperty("terminal", out var current) ||
            !EligibleTerminal(current) || Text(current, "incarnationId") != terminal.IncarnationId ||
            await FindAsync(thread, cancellationToken).ConfigureAwait(false) != terminal)
            return OrcaCodexContinueResult.Held;
        var screen = await _run(["terminal", "read", "--terminal", terminal.Handle, "--limit", "100", "--json"], cancellationToken).ConfigureAwait(false);
        if (!screen.TryGetProperty("result", out var screenResult) || !screenResult.TryGetProperty("terminal", out var surface) ||
            !surface.TryGetProperty("tail", out var tail) || tail.ValueKind != JsonValueKind.Array ||
            !EmptyComposer(string.Join('\n', tail.EnumerateArray().Where(line => line.ValueKind == JsonValueKind.String).Select(line => line.GetString()))))
            return OrcaCodexContinueResult.Held;

        await using var client = (_clientFactory ?? new CodexAppServerClientFactory(codexHome: thread.HomeDirectory)).Create();
        await client.StartAsync(cancellationToken).ConfigureAwait(false);
        var turns = await LatestTurnAsync(client, thread, cancellationToken).ConfigureAwait(false);
        if (!CodexPausedThreadScanner.IsSameBlockedTurn(turns, thread.BlockedTurnId)) return OrcaCodexContinueResult.NoLongerPaused;
        var limits = await client.RequestAsync("account/rateLimits/read", null, cancellationToken).ConfigureAwait(false);
        if (!CodexResumeQuota.IsAllowed(limits, DateTimeOffset.UtcNow)) return OrcaCodexContinueResult.QuotaUnavailable;

        // Once input is attempted, every uncertain result is terminal. Never repeat the prompt on silence.
        try
        {
            await _run(["terminal", "send", "--terminal", terminal.Handle, "--text", prompt, "--enter",
                "--wait-submit", "10", "--json"], cancellationToken).ConfigureAwait(false);
            while (true)
            {
                turns = await LatestTurnAsync(client, thread, cancellationToken).ConfigureAwait(false);
                if (StartedNewTurn(turns, thread.BlockedTurnId)) return OrcaCodexContinueResult.Continued;
                if (!CodexPausedThreadScanner.IsSameBlockedTurn(turns, thread.BlockedTurnId))
                    return OrcaCodexContinueResult.OutcomeUnknown;
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or CodexRpcException or
            OperationCanceledException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { return OrcaCodexContinueResult.OutcomeUnknown; }
    }

    internal static bool StartedNewTurn(JsonElement turns, string blockedTurnId) =>
        turns.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array &&
        data.EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } last &&
        Text(last, "id") is { Length: > 0 } id && id != blockedTurnId && Text(last, "status") is "inProgress" or "completed";

    internal static bool EmptyComposer(string? preview) => preview is not null && preview.Split('\n')
        .Select(line => line.Trim()).LastOrDefault(line => line.StartsWith('›')) is "›" or "› Ask Codex to do anything";

    private static Task<JsonElement> LatestTurnAsync(ICodexAppServerClient client, CodexPausedThread thread,
        CancellationToken token) => client.RequestAsync("thread/turns/list",
        new { threadId = thread.ThreadId, limit = 1, sortDirection = "desc", itemsView = "notLoaded" }, token);

    private static bool EligibleTerminal(JsonElement terminal) => Text(terminal, "agentIdentity") == "codex" &&
        Text(terminal, "executionHostId") == "local" && True(terminal, "connected") && True(terminal, "writable") &&
        terminal.TryGetProperty("orphaned", out var orphaned) && orphaned.ValueKind == JsonValueKind.False;
    private static bool True(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
    private static string? Text(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool SamePath(string? left, string? right) => left is not null && right is not null &&
        string.Equals(left.Replace('/', '\\').TrimEnd('\\'), right.Replace('/', '\\').TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static async Task<JsonElement> RunCliAsync(IReadOnlyList<string> arguments, CancellationToken token)
    {
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "orca", "resources", "bin", "orca.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("Orca CLI is unavailable.");
        var start = new ProcessStartInfo(executable)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        // AI Vitals always targets this machine, even when launched inside a paired Orca terminal.
        start.Environment.Remove("ORCA_PAIRING_CODE");
        start.Environment.Remove("ORCA_ENVIRONMENT");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Orca CLI did not start.");
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(token);
            var stderr = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            var output = await stdout.ConfigureAwait(false);
            await stderr.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException("Orca CLI rejected the operation.");
            using var document = JsonDocument.Parse(output);
            return document.RootElement.Clone();
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
}
