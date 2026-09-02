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
        return _bindings.All(binding => ContainsCommand(root, binding.HookName, CommandFor(binding.Event)));
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
            var command = CommandFor(binding.Event);

            // Toggling session labels changes the command line, so an entry written by an earlier
            // preference has to go before the current one is added.
            changed |= RemoveOwnCommands(root, binding, keepCommand: command);
            if (ContainsCommand(root, binding.HookName, command)) continue;

            var handler = new JsonObject
            {
                ["type"] = "command",
                ["command"] = command,
                ["timeout"] = 1
            };
            if (_includeWindowsCommand) handler["commandWindows"] = command;

            var group = new JsonObject
            {
                ["hooks"] = new JsonArray(handler)
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
        foreach (var binding in _bindings) changed |= RemoveOwnCommands(root, binding, keepCommand: null);

        if (!changed) return AgentActivityHookInstallationResult.NotInstalled;
        if (hooks.Count == 0) root.Remove("hooks");
        await WriteAtomicallyAsync(root, cancellationToken).ConfigureAwait(false);
        return AgentActivityHookInstallationResult.Removed;
    }

    private string CommandFor(AgentActivityEvent activityEvent) => _includeWorkspaceLabels
        ? $"{OwnCommandPrefix(activityEvent)} --labels"
        : OwnCommandPrefix(activityEvent);

    private string OwnCommandPrefix(AgentActivityEvent activityEvent)
    {
        var portablePath = _helperExecutablePath.Replace('\\', '/').Replace("\"", "\\\"");
        return $"\"{portablePath}\" --provider {_provider} --event {activityEvent}";
    }

    /// <summary>
    /// Removes every handler this installer owns for one binding, whatever flags it was written with,
    /// optionally keeping the command that is wanted right now. Handlers written by anyone else stay.
    /// </summary>
    private bool RemoveOwnCommands(JsonObject root, HookBinding binding, string? keepCommand)
    {
        if (root["hooks"] is not JsonObject hooks || hooks[binding.HookName] is not JsonArray groups) return false;

        var prefix = OwnCommandPrefix(binding.Event);
        var changed = false;
        for (var groupIndex = groups.Count - 1; groupIndex >= 0; groupIndex--)
        {
            if (groups[groupIndex] is not JsonObject group || group["hooks"] is not JsonArray handlers) continue;
            for (var handlerIndex = handlers.Count - 1; handlerIndex >= 0; handlerIndex--)
            {
                var command = GetCommand(handlers[handlerIndex]);
                if (command is null || command == keepCommand) continue;
                if (command != prefix && !command.StartsWith(prefix + " ", StringComparison.Ordinal)) continue;
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

    private static bool ContainsCommand(JsonObject root, string hookName, string command) =>
        root["hooks"] is JsonObject hooks
        && hooks[hookName] is JsonArray groups
        && groups.OfType<JsonObject>()
            .SelectMany(group => (group["hooks"] as JsonArray)?.OfType<JsonNode>() ?? [])
            .Any(handler => GetCommand(handler) == command);

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
