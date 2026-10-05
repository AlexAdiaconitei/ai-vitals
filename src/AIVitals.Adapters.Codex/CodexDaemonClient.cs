using System.Text.Json;

namespace AIVitals.Adapters.Codex;

public enum CodexDaemonContinueResult
{
    Continued, AlreadyRunning, NotLoaded, DaemonUnavailable, Failed, NoLongerPaused, OutcomeUnknown, QuotaUnavailable
}

public static class CodexDaemonContinuation
{
    public static string DefaultSocketPath(string? codexHome = null) =>
        Path.Combine(codexHome ?? CodexHome.Directory, "app-server-control", "app-server-control.sock");

    private static ICodexAppServerClient CreateClient(string path) => CodexAppServerClient.ForSocket(path);

    public static async Task<IReadOnlySet<string>> LoadedThreadIdsAsync(CancellationToken cancellationToken, string? socketPath = null)
    {
        try
        {
            using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectionTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            await using var client = CreateClient(socketPath ?? DefaultSocketPath());
            await client.StartAsync(connectionTimeout.Token).ConfigureAwait(false);
            return await ListLoadedAsync(client, connectionTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsConnectionFailure(exception))
        {
            return new HashSet<string>();
        }
    }

    public static async Task<CodexDaemonContinueResult> TryContinueAsync(string threadId, string blockedTurnId,
        string prompt, CancellationToken cancellationToken, string? socketPath = null)
    {
        try
        {
            await using var client = CreateClient(socketPath ?? DefaultSocketPath());
            try
            {
                using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectionTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                await client.StartAsync(connectionTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsConnectionFailure(exception))
            {
                return CodexDaemonContinueResult.DaemonUnavailable;
            }
            return await ContinueAsync(client, threadId, blockedTurnId, prompt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsConnectionFailure(exception))
        {
            return CodexDaemonContinueResult.Failed;
        }
    }

    internal static async Task<CodexDaemonContinueResult> ContinueAsync(ICodexAppServerClient client,
        string threadId, string blockedTurnId, string prompt, CancellationToken cancellationToken)
    {
        try
        {
            var loaded = await ListLoadedAsync(client, cancellationToken).ConfigureAwait(false);
            if (!loaded.Contains(threadId)) return CodexDaemonContinueResult.NotLoaded;
            var resumed = await client.RequestAsync("thread/resume", new { threadId, excludeTurns = true }, cancellationToken)
                .ConfigureAwait(false);
            if (!resumed.TryGetProperty("thread", out var thread) ||
                !thread.TryGetProperty("status", out var status) ||
                !status.TryGetProperty("type", out var type)) return CodexDaemonContinueResult.Failed;
            if (type.GetString() == "active") return CodexDaemonContinueResult.AlreadyRunning;
            if (type.GetString() is not ("idle" or "systemError")) return CodexDaemonContinueResult.Failed;
            var turns = await client.RequestAsync("thread/turns/list",
                new { threadId, limit = 1, sortDirection = "desc", itemsView = "notLoaded" }, cancellationToken).ConfigureAwait(false);
            if (!CodexPausedThreadScanner.IsSameBlockedTurn(turns, blockedTurnId)) return CodexDaemonContinueResult.NoLongerPaused;
            var limits = await client.RequestAsync("account/rateLimits/read", null, cancellationToken).ConfigureAwait(false);
            if (!CodexResumeQuota.IsAllowed(limits, DateTimeOffset.UtcNow)) return CodexDaemonContinueResult.QuotaUnavailable;
            try
            {
                var response = await client.RequestAsync("turn/start",
                    new { threadId, input = new object[] { new { type = "text", text = prompt } } }, cancellationToken).ConfigureAwait(false);
                if (!response.TryGetProperty("turn", out var turn) || !turn.TryGetProperty("id", out var id) ||
                    id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()) || id.GetString() == blockedTurnId ||
                    !turn.TryGetProperty("status", out var turnStatus)) return CodexDaemonContinueResult.OutcomeUnknown;
                return turnStatus.GetString() switch
                {
                    "inProgress" or "completed" => CodexDaemonContinueResult.Continued,
                    "failed" or "interrupted" => CodexDaemonContinueResult.Failed,
                    _ => CodexDaemonContinueResult.OutcomeUnknown
                };
            }
            catch (CodexRpcException)
            {
                return CodexDaemonContinueResult.Failed;
            }
            catch (Exception exception) when (IsConnectionFailure(exception))
            {
                // The daemon may have accepted turn/start before the reply was lost.
                return CodexDaemonContinueResult.OutcomeUnknown;
            }
        }
        catch (Exception exception) when (IsConnectionFailure(exception))
        {
            return CodexDaemonContinueResult.Failed;
        }
    }

    private static bool IsConnectionFailure(Exception exception) =>
        exception is IOException or OperationCanceledException or CodexRpcException or
            System.ComponentModel.Win32Exception or InvalidOperationException or JsonException or
            System.Net.Sockets.SocketException or System.Net.WebSockets.WebSocketException or HttpRequestException;

    private static async Task<IReadOnlySet<string>> ListLoadedAsync(ICodexAppServerClient client, CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            var page = await client.RequestAsync("thread/loaded/list", new { cursor }, cancellationToken).ConfigureAwait(false);
            if (!page.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new IOException("Codex returned an invalid loaded-thread page.");
            foreach (var id in data.EnumerateArray())
                if (id.ValueKind == JsonValueKind.String && id.GetString() is { } value) ids.Add(value);
            cursor = page.TryGetProperty("nextCursor", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
            if (cursor is not null && !cursors.Add(cursor)) throw new IOException("Codex repeated a loaded-thread cursor.");
        } while (cursor is not null);
        return ids;
    }
}
