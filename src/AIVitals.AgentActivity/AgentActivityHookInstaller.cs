using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIVitals.AgentActivity;

public enum AgentActivityHookInstallationResult
{
    Installed,
    AlreadyInstalled,
    Removed,
    NotInstalled
}

public sealed class AgentActivityHookInstaller
{
    private readonly string _helperExecutablePath;
    private readonly string _helperFileName;
    private readonly string _settingsPath;
    private readonly AgentActivityProvider _provider;
    private readonly bool _includeWindowsCommand;
    private readonly bool _includeWorkspaceLabels;
    private readonly IReadOnlyList<HookBinding> _bindings;

    public AgentActivityHookInstaller(
        string helperExecutablePath,
        string settingsPath,
        AgentActivityProvider provider,
        bool includeWindowsCommand = false,
        bool includeWorkspaceLabels = false)
    {
        _helperExecutablePath = Path.GetFullPath(helperExecutablePath);
        _helperFileName = Path.GetFileName(_helperExecutablePath);
        _settingsPath = Path.GetFullPath(settingsPath);
        _provider = provider;
        _includeWindowsCommand = includeWindowsCommand;
        _includeWorkspaceLabels = includeWorkspaceLabels;
        _bindings = BindingsFor(provider);
    }

    public static string DefaultSettingsPath(AgentActivityProvider provider) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        provider == AgentActivityProvider.ClaudeCode ? ".claude" : ".codex",
        provider == AgentActivityProvider.ClaudeCode ? "settings.json" : "hooks.json");

    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        var root = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return _bindings.All(binding => ContainsHandler(root, binding.HookName, DesiredHandler(binding.Event)));
    }

    public async Task<AgentActivityHookInstallationResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_helperExecutablePath))
            throw new FileNotFoundException("The AI Vitals activity hook helper was not found.", _helperExecutablePath);

        var root = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var hooks = EnsureObject(root, "hooks");
        var changed = false;
        foreach (var binding in _bindings)
        {
            var desired = DesiredHandler(binding.Event);

            // Anything of ours that is not exactly the handler we want goes, whatever differs:
            // a stale path, a changed label flag, or a Windows command written before it was
            // made runnable. Comparing only the command once left the latter unrepairable.
            changed |= RemoveOwnCommands(root, binding, desired);
            if (ContainsHandler(root, binding.HookName, desired)) continue;

            var group = new JsonObject
            {
                ["hooks"] = new JsonArray(desired)
            };
            EnsureArray(hooks, binding.HookName).Add(group);
            changed = true;
        }

        if (!changed) return AgentActivityHookInstallationResult.AlreadyInstalled;
        await WriteAtomicallyAsync(root, cancellationToken).ConfigureAwait(false);
        return AgentActivityHookInstallationResult.Installed;
    }

    public async Task<AgentActivityHookInstallationResult> UninstallAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath)) return AgentActivityHookInstallationResult.NotInstalled;

        var root = await ReadAsync(cancellationToken).ConfigureAwait(false);
        if (root["hooks"] is not JsonObject hooks) return AgentActivityHookInstallationResult.NotInstalled;
        var changed = false;
        foreach (var binding in _bindings) changed |= RemoveOwnCommands(root, binding, desired: null);

        if (!changed) return AgentActivityHookInstallationResult.NotInstalled;
        if (hooks.Count == 0) root.Remove("hooks");
        await WriteAtomicallyAsync(root, cancellationToken).ConfigureAwait(false);
        return AgentActivityHookInstallationResult.Removed;
    }

    /// <summary>The exact handler this installer wants written for one event.</summary>
    private JsonObject DesiredHandler(AgentActivityEvent activityEvent)
    {
        var command = CommandFor(activityEvent);
        var handler = new JsonObject
        {
            ["type"] = "command",
            ["command"] = command,
            ["timeout"] = 1
        };
        if (_includeWindowsCommand) handler["commandWindows"] = WindowsCommand(command);
        return handler;
    }

    private string CommandFor(AgentActivityEvent activityEvent) => _includeWorkspaceLabels
        ? $"{OwnCommandPrefix(activityEvent)} --labels"
        : OwnCommandPrefix(activityEvent);

    /// <summary>
    /// Codex runs the Windows override through PowerShell, where a quoted path on its own is a
    /// string expression and the arguments after it are a parse error. The call operator is what
    /// turns it back into an invocation, and without it every hook exits 1.
    /// </summary>
    private static string WindowsCommand(string command) => "& " + command;

    private string OwnCommandPrefix(AgentActivityEvent activityEvent)
    {
        var portablePath = _helperExecutablePath.Replace('\\', '/').Replace("\"", "\\\"");
        return $"\"{portablePath}\" --provider {_provider} --event {activityEvent}";
    }

    /// <summary>
    /// Recognises a handler as this installer's own by the helper it runs and the arguments it
    /// passes, not by where that helper happens to live. An entry written by an earlier install, a
    /// moved installation or a sandboxed run would otherwise survive every uninstall, leaving the
    /// agent with a duplicate hook and a second trust prompt for the same event.
    /// </summary>
    private bool IsOwnCommand(string command, AgentActivityEvent activityEvent) =>
        command.Contains(_helperFileName, StringComparison.OrdinalIgnoreCase)
        && command.Contains($"--provider {_provider} --event {activityEvent}", StringComparison.Ordinal);

    /// <summary>
    /// Removes every handler this installer owns for one binding, whatever flags or path it was
    /// written with, optionally keeping the command that is wanted right now. Handlers written by
    /// anyone else stay.
    /// </summary>
    private bool RemoveOwnCommands(JsonObject root, HookBinding binding, JsonObject? desired)
    {
        if (root["hooks"] is not JsonObject hooks || hooks[binding.HookName] is not JsonArray groups) return false;

        var changed = false;
        for (var groupIndex = groups.Count - 1; groupIndex >= 0; groupIndex--)
        {
            if (groups[groupIndex] is not JsonObject group || group["hooks"] is not JsonArray handlers) continue;
            for (var handlerIndex = handlers.Count - 1; handlerIndex >= 0; handlerIndex--)
            {
                var handler = handlers[handlerIndex];
                var command = GetCommand(handler);
                if (command is null) continue;
                if (desired is not null && JsonNode.DeepEquals(handler, desired)) continue;
                if (!IsOwnCommand(command, binding.Event)) continue;
                handlers.RemoveAt(handlerIndex);
                changed = true;
            }

            if (handlers.Count == 0 && group.Count == 1) groups.RemoveAt(groupIndex);
        }

        if (groups.Count == 0) hooks.Remove(binding.HookName);
        return changed;
    }

    private async Task<JsonObject> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath)) return new JsonObject();
        await using var stream = new FileStream(_settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        var node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return node as JsonObject ?? throw new JsonException("The hook settings file must contain a JSON object.");
    }

    private async Task WriteAtomicallyAsync(JsonObject root, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var temporaryPath = _settingsPath + ".ai-vitals.tmp";
        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         4096,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, root, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }

    /// <summary>
    /// True when the file already holds exactly the handler wanted, every field included. A weaker
    /// test on the command alone would leave a handler whose other fields drifted in place forever.
    /// </summary>
    private static bool ContainsHandler(JsonObject root, string hookName, JsonObject desired) =>
        root["hooks"] is JsonObject hooks
        && hooks[hookName] is JsonArray groups
        && groups.OfType<JsonObject>()
            .SelectMany(group => (group["hooks"] as JsonArray)?.OfType<JsonNode>() ?? [])
            .Any(handler => JsonNode.DeepEquals(handler, desired));

    private static string? GetCommand(JsonNode? handler) =>
        handler is JsonObject handlerObject
        && handlerObject["type"]?.GetValue<string>() == "command"
        && handlerObject["command"] is JsonValue commandValue
        && commandValue.TryGetValue<string>(out var command)
            ? command
            : null;

    private static JsonObject EnsureObject(JsonObject parent, string propertyName)
    {
        if (parent[propertyName] is JsonObject existing) return existing;
        var created = new JsonObject();
        parent[propertyName] = created;
        return created;
    }

    private static JsonArray EnsureArray(JsonObject parent, string propertyName)
    {
        if (parent[propertyName] is JsonArray existing) return existing;
        var created = new JsonArray();
        parent[propertyName] = created;
        return created;
    }

    private static IReadOnlyList<HookBinding> BindingsFor(AgentActivityProvider provider)
    {
        var common = new List<HookBinding>
        {
            new("SessionStart", AgentActivityEvent.SessionStarted),
            new("UserPromptSubmit", AgentActivityEvent.PromptSubmitted),
            new("PreToolUse", AgentActivityEvent.ToolStarted),
            new("PostToolUse", AgentActivityEvent.ToolFinished),
            new("Stop", AgentActivityEvent.TurnStopped),
            new("SessionEnd", AgentActivityEvent.SessionEnded)
        };
        if (provider == AgentActivityProvider.ClaudeCode)
        {
            common.Add(new HookBinding("PostToolUseFailure", AgentActivityEvent.ToolFinished));
            common.Add(new HookBinding("StopFailure", AgentActivityEvent.TurnStopped));
        }

        return common;
    }

    private sealed record HookBinding(string HookName, AgentActivityEvent Event);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}
