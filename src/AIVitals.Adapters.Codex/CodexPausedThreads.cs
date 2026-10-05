using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using AIVitals.Domain;

namespace AIVitals.Adapters.Codex;

/// <summary>
/// A root Codex thread whose most recent turn failed because the account ran out of usage.
/// Only metadata is carried: no prompt, message or tool output ever leaves the rollout.
/// </summary>
public sealed record CodexPausedThread(
    string ThreadId,
    string Name,
    string WorkingDirectory,
    string BlockedTurnId,
    DateTimeOffset BlockedAtUtc,
    string? RolloutPath,
    string? Model,
    string? ReasoningEffort,
    string? HomeDirectory = null);

public sealed record CodexThreadScanFailure(string ThreadId, int? RpcCode = null, bool TimedOut = false);
public sealed record CodexPausedThreadScan(IReadOnlyList<CodexPausedThread> Threads,
    IReadOnlyList<CodexThreadScanFailure> Failures, IReadOnlySet<string>? ObservedThreadIds = null);

/// <summary>
/// Lists paused threads through a private app-server. Reading a thread this way never takes its
/// writer lock, so it works while ChatGPT Desktop has the same thread open.
/// </summary>
public sealed class CodexPausedThreadScanner
{
    private const string UsageLimitExceeded = "usageLimitExceeded";
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(8);
    private readonly ICodexAppServerClientFactory _clientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly string? _executablePath;
    private readonly string? _homeDirectory;
    private readonly bool _discoverHomes;

    public CodexPausedThreadScanner(string? executablePath = null, TimeProvider? timeProvider = null, string? codexHome = null)
        : this(new CodexAppServerClientFactory(executablePath, codexHome ?? CodexHome.Directory), timeProvider)
    {
        _executablePath = executablePath;
        _homeDirectory = codexHome ?? CodexHome.Directory;
        _discoverHomes = codexHome is null;
    }

    internal CodexPausedThreadScanner(ICodexAppServerClientFactory clientFactory, TimeProvider? timeProvider = null)
    {
        _clientFactory = clientFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<CodexPausedThread>> ScanAsync(CancellationToken cancellationToken, bool showNames = false) =>
        (await ScanDetailedAsync(cancellationToken, showNames).ConfigureAwait(false)).Threads;

    public async Task<CodexPausedThreadScan> ScanDetailedAsync(CancellationToken cancellationToken, bool showNames = false)
    {
        if (_discoverHomes)
        {
            var scans = new List<CodexPausedThreadScan>();
            foreach (var home in CodexHome.ResumeDirectories())
            {
                scans.Add(await new CodexPausedThreadScanner(_executablePath, _timeProvider, home)
                    .ScanDetailedAsync(cancellationToken, showNames).ConfigureAwait(false));
            }
            return MergeScans(scans);
        }
        await using var client = _clientFactory.Create();
        await client.StartAsync(cancellationToken).ConfigureAwait(false);

        var oldest = _timeProvider.GetUtcNow() - Horizon;
        var paused = new List<CodexPausedThread>();
        var failures = new List<CodexThreadScanFailure>();
        var observed = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            var threads = await client.RequestAsync("thread/list",
                new { limit = 50, cursor, sortKey = "updated_at", sortDirection = "desc",
                    sourceKinds = new[] { "cli", "vscode", "appServer", "exec", "unknown" } }, cancellationToken).ConfigureAwait(false);
            if (!threads.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new IOException("Codex returned an invalid thread page.");
            foreach (var thread in data.EnumerateArray())
            {
                if (ReadUnixSeconds(thread, "updatedAt") is { } date && date < oldest) return new(paused, failures, observed);
                if (!IsRootThread(thread)) continue;
                var threadId = ReadString(thread, "id");
                if (threadId is null) continue;
                observed.Add(threadId);
                using var turnTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                turnTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    var turns = await client.RequestAsync("thread/turns/list",
                        new { threadId, limit = 1, sortDirection = "desc", itemsView = "notLoaded" }, turnTimeout.Token).ConfigureAwait(false);
                    if (!turns.TryGetProperty("data", out var turnData) || turnData.ValueKind != JsonValueKind.Array)
                        throw new IOException("Codex returned an invalid turn page.");
                    if (MapPausedThread(thread, turns, showNames) is { } pausedThread && pausedThread.BlockedAtUtc >= oldest)
                        paused.Add(pausedThread with { HomeDirectory = _homeDirectory });
                }
                catch (CodexRpcException exception) { failures.Add(new(threadId, exception.Code)); }
                catch (IOException) { failures.Add(new(threadId)); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { failures.Add(new(threadId, TimedOut: true)); }
            }
            cursor = ReadString(threads, "nextCursor");
            if (cursor is not null && !cursors.Add(cursor)) throw new IOException("Codex repeated a thread cursor.");
        } while (cursor is not null);

        return new(paused, failures, observed);
    }

    internal static CodexPausedThreadScan MergeScans(IEnumerable<CodexPausedThreadScan> scans)
    {
        var merged = new Dictionary<string, CodexPausedThread>(StringComparer.Ordinal);
        var failures = new List<CodexThreadScanFailure>();
        foreach (var scan in scans)
        {
            foreach (var id in scan.ObservedThreadIds ?? new HashSet<string>()) merged.Remove(id);
            foreach (var thread in scan.Threads) merged[thread.ThreadId] = thread;
            failures.AddRange(scan.Failures);
        }
        return new(merged.Values.ToArray(), failures);
    }

    public async Task<CodexResumeValidation> ValidateAsync(CodexPausedThread expected, CancellationToken cancellationToken)
    {
        await using var client = expected.HomeDirectory is { } home
            ? new CodexAppServerClientFactory(_executablePath, home).Create() : _clientFactory.Create();
        await client.StartAsync(cancellationToken).ConfigureAwait(false);
        var response = await client.RequestAsync("thread/read", new { threadId = expected.ThreadId, includeTurns = false }, cancellationToken)
            .ConfigureAwait(false);
        var turns = await client.RequestAsync("thread/turns/list",
            new { threadId = expected.ThreadId, limit = 1, sortDirection = "desc", itemsView = "notLoaded" }, cancellationToken).ConfigureAwait(false);
        var actual = response.TryGetProperty("thread", out var thread) && IsRootThread(thread)
            ? MapPausedThread(thread, turns) : null;
        if (actual?.BlockedTurnId != expected.BlockedTurnId) return new(null, []);
        var limits = await client.RequestAsync("account/rateLimits/read", null, cancellationToken).ConfigureAwait(false);
        return new(actual with { Name = expected.Name, HomeDirectory = expected.HomeDirectory }, CodexObservationMapper.MapRateLimits(limits, _timeProvider.GetUtcNow()),
            CodexResumeQuota.IsAllowed(limits, _timeProvider.GetUtcNow()));
    }

    internal static bool IsSameBlockedTurn(JsonElement turns, string blockedTurnId) =>
        turns.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array &&
        data.EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } last &&
        ReadString(last, "id") == blockedTurnId && ReadString(last, "status") == "failed" &&
        last.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object &&
        ReadString(error, "codexErrorInfo") == UsageLimitExceeded;

    internal static CodexPausedThread? MapPausedThread(JsonElement thread, JsonElement turns, bool showNames = false)
    {
        if (!turns.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return null;
        var last = data.EnumerateArray().FirstOrDefault();
        if (last.ValueKind != JsonValueKind.Object) return null;
        if (ReadString(last, "status") != "failed") return null;
        if (!last.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object) return null;
        if (!error.TryGetProperty("codexErrorInfo", out var info)
            || info.ValueKind != JsonValueKind.String
            || info.GetString() != UsageLimitExceeded)
            return null;

        var threadId = ReadString(thread, "id");
        var turnId = ReadString(last, "id");
        var cwd = ReadString(thread, "cwd");
        if (threadId is null || turnId is null || string.IsNullOrWhiteSpace(cwd)) return null;

        var blockedAt = ReadUnixSeconds(last, "completedAt")
                        ?? ReadUnixSeconds(last, "startedAt")
                        ?? ReadUnixSeconds(thread, "updatedAt");
        if (blockedAt is null) return null;

        var name = showNames ? ReadString(thread, "name") : null;
        if (string.IsNullOrWhiteSpace(name)) name = "Codex " + threadId[..Math.Min(8, threadId.Length)];

        return new CodexPausedThread(
            threadId,
            name!,
            cwd,
            turnId,
            blockedAt.Value,
            ReadString(thread, "path"),
            ReadString(thread, "model"),
            ReadString(thread, "reasoningEffort"));
    }

    private static bool IsRootThread(JsonElement thread) =>
        ReadString(thread, "parentThreadId") is null
        && !(thread.TryGetProperty("ephemeral", out var ephemeral) && ephemeral.ValueKind == JsonValueKind.True);

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? ReadUnixSeconds(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var seconds)) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(seconds); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}

public sealed record CodexResumeValidation(CodexPausedThread? Thread, IReadOnlyList<UsageObservation> Quotas,
    bool AllowedByBackend = false);

/// <summary>Where Codex keeps its local state: <c>CODEX_HOME</c>, or <c>~/.codex</c>.</summary>
public static class CodexHome
{
    public static string OrcaDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "orca");

    public static IReadOnlyList<string> ResumeDirectories()
    {
        var orca = Path.Combine(OrcaDirectory, "codex-runtime-home", "home");
        return new[] { Directory, orca }.Where(path => path == Directory || System.IO.Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string Directory =>
        Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
}

/// <summary>
/// Whether another process (ChatGPT Desktop or a CLI) holds a thread's writer lock. Codex takes an
/// exclusive OS lock on <c>thread-writer-locks/&lt;id&gt;.lock</c> while the thread is loaded. The
/// probe asks for a shared lock that fails immediately and releases it at once, so it never blocks
/// Codex. A missing file, or one left behind by a killed process, is a free lock.
/// </summary>
public static class CodexThreadLock
{
    private const uint LockfileFailImmediately = 0x1;

    public static bool IsHeld(string threadId, string? codexHome = null) =>
        IsHeldAt(Path.Combine(codexHome ?? CodexHome.Directory, "thread-writer-locks", threadId + ".lock"));

    internal static bool IsHeldAt(string lockPath)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var stream = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var overlapped = new NativeOverlapped();
            if (!LockFileEx(stream.SafeFileHandle, LockfileFailImmediately, 0, uint.MaxValue, uint.MaxValue, ref overlapped))
                return true;
            UnlockFileEx(stream.SafeFileHandle, 0, uint.MaxValue, uint.MaxValue, ref overlapped);
            return false;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeOverlapped
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public IntPtr EventHandle;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool LockFileEx(SafeFileHandle file, uint flags, uint reserved, uint bytesLow, uint bytesHigh, ref NativeOverlapped overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UnlockFileEx(SafeFileHandle file, uint reserved, uint bytesLow, uint bytesHigh, ref NativeOverlapped overlapped);
}

/// <summary>The permissions a thread's last turn ran with, read from its rollout.</summary>
public sealed record CodexTurnContext(string? Model, string? ReasoningEffort, string ApprovalPolicy, string SandboxMode,
    string? SandboxPolicyJson = null, string? TurnId = null, string? WorkingDirectory = null);

public static class CodexTurnContextReader
{
    private const int TailBytes = 512 * 1024;

    /// <summary>
    /// Reads only the end of the rollout: the newest <c>turn_context</c> sits near the tail, and
    /// rollouts grow to tens of megabytes.
    /// </summary>
    public static CodexTurnContext? ReadLatest(string rolloutPath)
    {
        try
        {
            using var stream = new FileStream(rolloutPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - TailBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var tail = reader.ReadToEnd();
            return ParseLatest(tail);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static CodexTurnContext? ParseLatest(string rolloutTail)
    {
        var lines = rolloutTail.Split('\n');
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var line = lines[index];
            if (!line.Contains("\"turn_context\"", StringComparison.Ordinal)) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("type", out var type) || type.GetString() != "turn_context") continue;
                if (!root.TryGetProperty("payload", out var payload)) continue;

                var approval = payload.TryGetProperty("approval_policy", out var approvalElement) ? approvalElement.GetString() : null;
                var sandbox = payload.TryGetProperty("sandbox_policy", out var sandboxElement)
                              && sandboxElement.ValueKind == JsonValueKind.Object
                              && sandboxElement.TryGetProperty("type", out var sandboxType)
                    ? sandboxType.GetString()
                    : null;
                if (approval is null || sandbox is null) return null;
                if (payload.TryGetProperty("permission_profile", out var profile) && profile.ValueKind != JsonValueKind.Null &&
                    (profile.ValueKind != JsonValueKind.Object || !profile.TryGetProperty("type", out var profileType) ||
                     profileType.GetString() != "disabled" || sandbox != "danger-full-access" || profile.EnumerateObject().Count() != 1))
                    return null;
                if (payload.TryGetProperty("active_permission_profile", out var active) && active.ValueKind != JsonValueKind.Null &&
                    (active.ValueKind != JsonValueKind.Object || !active.TryGetProperty("id", out var activeId) ||
                     activeId.GetString() != (sandbox == "danger-full-access" ? ":danger-full-access" : sandbox == "read-only" ? ":read-only" : ":workspace") ||
                     active.EnumerateObject().Any(property => property.Name is not ("id" or "extends")) ||
                     active.TryGetProperty("extends", out var parent) && parent.ValueKind != JsonValueKind.Null)) return null;
                if (approval == "on-request" && payload.TryGetProperty("approvals_reviewer", out var reviewer) &&
                    reviewer.ValueKind == JsonValueKind.String && reviewer.GetString() != "user") return null;

                string? model = payload.TryGetProperty("model", out var modelElement) ? modelElement.GetString() : null;
                string? effort = null;
                if (payload.TryGetProperty("collaboration_mode", out var mode) && mode.ValueKind == JsonValueKind.Object
                    && mode.TryGetProperty("settings", out var settings) && settings.ValueKind == JsonValueKind.Object
                    && settings.TryGetProperty("reasoning_effort", out var effortElement)
                    && effortElement.ValueKind == JsonValueKind.String)
                    effort = effortElement.GetString();

                if (effort is null && payload.TryGetProperty("effort", out var directEffort) && directEffort.ValueKind == JsonValueKind.String)
                    effort = directEffort.GetString();
                return new CodexTurnContext(model, effort, approval, sandbox, sandboxElement.GetRawText(),
                    payload.TryGetProperty("turn_id", out var turnId) && turnId.ValueKind == JsonValueKind.String ? turnId.GetString() : null,
                    payload.TryGetProperty("cwd", out var cwd) && cwd.ValueKind == JsonValueKind.String ? cwd.GetString() : null);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                // The first line of the tail is usually cut in half; keep looking further up.
            }
        }

        return null;
    }
}

public enum CodexResumeLaunchResult
{
    Launched,
    HeldByAnotherApp,
    UnknownPermissions,
    CodexNotFound,
    Failed,
    OutcomeUnknown,
    NoLongerPaused,
    QuotaUnavailable
}

/// <summary>
/// Continues a paused thread in a visible Codex CLI window. The thread keeps exactly the model,
/// effort, approval policy and sandbox of its last turn: without explicit overrides the CLI would
/// fall back to <c>config.toml</c>, which can be more or less permissive than the original thread.
/// </summary>
public static class CodexResumeLauncher
{
    private static readonly HashSet<string> ApprovalPolicies = new(StringComparer.Ordinal) { "on-request", "never" };
    private static readonly HashSet<string> SandboxModes = new(StringComparer.Ordinal) { "read-only", "workspace-write", "danger-full-access" };
    // Values go to `-c key=value` unquoted, which Codex reads as a literal string when it is not
    // valid TOML. Starting with a letter keeps a value from being parsed as a number or boolean.
    private static readonly Regex SafeToken = new("^[A-Za-z][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);

    public static async Task<CodexResumeLaunchResult> LaunchAsync(CodexPausedThread thread, string prompt,
        CancellationToken cancellationToken, string? executablePath = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Guid.TryParse(thread.ThreadId, out _)) return CodexResumeLaunchResult.Failed;
        if (CodexThreadLock.IsHeld(thread.ThreadId, thread.HomeDirectory)) return CodexResumeLaunchResult.HeldByAnotherApp;

        var context = thread.RolloutPath is null ? null : CodexTurnContextReader.ReadLatest(thread.RolloutPath);
        if (context?.TurnId != thread.BlockedTurnId || string.IsNullOrWhiteSpace(context.WorkingDirectory))
            return CodexResumeLaunchResult.UnknownPermissions;
        var arguments = BuildArguments(thread, context, prompt);
        if (arguments is null) return CodexResumeLaunchResult.UnknownPermissions;

        string executable;
        try
        {
            executable = CodexExecutableLocator.ResolveExecutable(executablePath);
        }
        catch (FileNotFoundException)
        {
            return CodexResumeLaunchResult.CodexNotFound;
        }

        var command = CodexExecutableLocator.CreateCliCommand(executable, arguments);
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            WorkingDirectory = context.WorkingDirectory,
            // A console child of a GUI process gets a console of its own, which Windows opens in the
            // user's default terminal. That window is the user's view of the unattended run.
            UseShellExecute = false,
            CreateNoWindow = false
        };
        foreach (var argument in command.Arguments) startInfo.ArgumentList.Add(argument);
        if (thread.HomeDirectory is { } homeDirectory) startInfo.Environment["CODEX_HOME"] = homeDirectory;

        try
        {
            await using var client = new CodexAppServerClientFactory(executablePath, thread.HomeDirectory).Create();
            await client.StartAsync(cancellationToken).ConfigureAwait(false);
            var turns = await client.RequestAsync("thread/turns/list",
                new { threadId = thread.ThreadId, limit = 1, sortDirection = "desc", itemsView = "notLoaded" }, cancellationToken).ConfigureAwait(false);
            if (!CodexPausedThreadScanner.IsSameBlockedTurn(turns, thread.BlockedTurnId))
                return CodexResumeLaunchResult.NoLongerPaused;
            var limits = await client.RequestAsync("account/rateLimits/read", null, cancellationToken).ConfigureAwait(false);
            if (!CodexResumeQuota.IsAllowed(limits, DateTimeOffset.UtcNow)) return CodexResumeLaunchResult.QuotaUnavailable;
            if (CodexThreadLock.IsHeld(thread.ThreadId, thread.HomeDirectory)) return CodexResumeLaunchResult.HeldByAnotherApp;
            using var process = Process.Start(startInfo);
            if (process is null) return CodexResumeLaunchResult.Failed;
            return await ConfirmStartedAsync(process, client, thread, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return CodexResumeLaunchResult.Failed;
        }
    }

    internal static async Task<CodexResumeLaunchResult> ConfirmStartedAsync(Process process, ICodexAppServerClient client,
        CodexPausedThread thread, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var turns = await client.RequestAsync("thread/turns/list",
                    new { threadId = thread.ThreadId, limit = 1, sortDirection = "desc", itemsView = "notLoaded" }, cancellationToken).ConfigureAwait(false);
                if (turns.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array &&
                    data.EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } last &&
                    last.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(id.GetString()) && id.GetString() != thread.BlockedTurnId &&
                    last.TryGetProperty("status", out var status))
                    return status.GetString() switch
                    {
                        "inProgress" or "completed" => CodexResumeLaunchResult.Launched,
                        "failed" or "interrupted" => CodexResumeLaunchResult.Failed,
                        _ => CodexResumeLaunchResult.OutcomeUnknown
                    };
                if (process.HasExited) return CodexResumeLaunchResult.Failed;
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or CodexRpcException)
        {
            return CodexResumeLaunchResult.OutcomeUnknown;
        }
    }

    /// <summary>
    /// Null when the thread's permissions cannot be read or are not ones AI Vitals recognises.
    /// Refusing is deliberate: launching with defaults could widen what an unattended run may do.
    /// </summary>
    internal static IReadOnlyList<string>? BuildArguments(CodexPausedThread thread, CodexTurnContext? context, string prompt)
    {
        if (context is null) return null;
        if (!Guid.TryParse(thread.ThreadId, out _)) return null;
        if (!ApprovalPolicies.Contains(context.ApprovalPolicy) || !SandboxModes.Contains(context.SandboxMode)) return null;

        var arguments = new List<string> { "resume", thread.ThreadId, prompt, "--no-daemon" };
        var model = context.Model ?? thread.Model;
        if (model is null) return null;
        if (model is not null)
        {
            if (!SafeToken.IsMatch(model)) return null;
            arguments.AddRange(["-c", $"model={model}"]);
        }

        var effort = context.ReasoningEffort ?? thread.ReasoningEffort;
        if (effort is null) return null;
        if (effort is not null)
        {
            if (!SafeToken.IsMatch(effort)) return null;
            arguments.AddRange(["-c", $"model_reasoning_effort={effort}"]);
        }

        arguments.AddRange(["-c", $"approval_policy={context.ApprovalPolicy}"]);
        arguments.AddRange(["-c", "approvals_reviewer=user"]);
        arguments.AddRange(["-c", $"sandbox_mode={context.SandboxMode}"]);
        if (!AddSandboxOverrides(arguments, context)) return null;
        return arguments;
    }

    private static bool AddSandboxOverrides(List<string> arguments, CodexTurnContext context)
    {
        if (context.SandboxPolicyJson is null) return context.SandboxMode != "workspace-write";
        try
        {
            using var document = JsonDocument.Parse(context.SandboxPolicyJson);
            var policy = document.RootElement;
            if (!policy.TryGetProperty("type", out var policyType) || policyType.GetString() != context.SandboxMode) return false;
            var allowed = context.SandboxMode == "workspace-write"
                ? new[] { "type", "writable_roots", "read_only_access", "network_access", "exclude_tmpdir_env_var", "exclude_slash_tmp" }
                : context.SandboxMode == "read-only" ? new[] { "type", "access" } : new[] { "type" };
            if (policy.EnumerateObject().Any(property => !allowed.Contains(property.Name, StringComparer.Ordinal))) return false;
            foreach (var key in new[] { "access", "read_only_access" })
                if (policy.TryGetProperty(key, out var access) &&
                    (access.ValueKind != JsonValueKind.Object || !access.TryGetProperty("type", out var type) ||
                     type.GetString() is not ("full-access" or "fullAccess") || access.EnumerateObject().Count() != 1)) return false;
            if (context.SandboxMode != "workspace-write") return true;
            var roots = policy.TryGetProperty("writable_roots", out var rootsValue) ? rootsValue : JsonSerializer.SerializeToElement(Array.Empty<string>());
            if (roots.ValueKind != JsonValueKind.Array || roots.EnumerateArray().Any(root => root.ValueKind != JsonValueKind.String)) return false;
            arguments.AddRange(["-c", "sandbox_workspace_write.writable_roots=" + JsonSerializer.Serialize(roots)]);
            foreach (var flag in new[] { "network_access", "exclude_tmpdir_env_var", "exclude_slash_tmp" })
            {
                var enabled = false;
                if (policy.TryGetProperty(flag, out var value))
                {
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                    enabled = value.GetBoolean();
                }
                arguments.AddRange(["-c", $"sandbox_workspace_write.{flag}={enabled.ToString().ToLowerInvariant()}"]);
            }
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return false; }
    }

    /// <summary>Opens the thread in ChatGPT Desktop. The deep link never sends anything on its own.</summary>
    public static bool OpenInDesktop(string threadId)
    {
        if (!Guid.TryParse(threadId, out _)) return false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo($"codex://threads/{threadId}") { UseShellExecute = true });
            return true;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
