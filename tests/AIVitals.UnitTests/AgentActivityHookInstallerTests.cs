using System.Text.Json.Nodes;
using AIVitals.AgentActivity;

namespace AIVitals.UnitTests;

public sealed class AgentActivityHookInstallerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aivitals-hook-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Install_is_additive_and_idempotent()
    {
        Directory.CreateDirectory(_directory);
        var helper = Path.Combine(_directory, "activity hook.exe");
        await File.WriteAllTextAsync(helper, "test");
        var settings = Path.Combine(_directory, "hooks.json");
        await File.WriteAllTextAsync(settings, """
            {
              "description": "keep me",
              "hooks": {
                "PreToolUse": [
                  { "matcher": "Bash", "hooks": [{ "type": "command", "command": "existing-command" }] }
                ]
              }
            }
            """);
        var installer = new AgentActivityHookInstaller(helper, settings, AgentActivityProvider.Codex, includeWindowsCommand: true);

        Assert.Equal(AgentActivityHookInstallationResult.Installed, await installer.InstallAsync());
        var firstWrite = await File.ReadAllTextAsync(settings);
        Assert.Equal(AgentActivityHookInstallationResult.AlreadyInstalled, await installer.InstallAsync());
        Assert.Equal(firstWrite, await File.ReadAllTextAsync(settings));

        var root = JsonNode.Parse(firstWrite)!.AsObject();
        Assert.Equal("keep me", root["description"]!.GetValue<string>());
        Assert.Contains("existing-command", firstWrite);
        Assert.Contains("commandWindows", firstWrite);
        Assert.True(await installer.IsInstalledAsync());
    }

    [Fact]
    public async Task Uninstall_removes_only_exact_owned_handlers()
    {
        Directory.CreateDirectory(_directory);
        var helper = Path.Combine(_directory, "activity.exe");
        await File.WriteAllTextAsync(helper, "test");
        var settings = Path.Combine(_directory, "settings.json");
        var installer = new AgentActivityHookInstaller(helper, settings, AgentActivityProvider.ClaudeCode);
        await installer.InstallAsync();

        var root = JsonNode.Parse(await File.ReadAllTextAsync(settings))!.AsObject();
        var preToolGroups = root["hooks"]!["PreToolUse"]!.AsArray();
        preToolGroups.Add(JsonNode.Parse("""{ "matcher": "Bash", "hooks": [{ "type": "command", "command": "user-command" }] }"""));
        await File.WriteAllTextAsync(settings, root.ToJsonString());

        Assert.Equal(AgentActivityHookInstallationResult.Removed, await installer.UninstallAsync());
        var remaining = await File.ReadAllTextAsync(settings);
        Assert.Contains("user-command", remaining);
        Assert.DoesNotContain("--provider ClaudeCode", remaining);
    }

    [Fact]
    public async Task Turning_session_labels_on_replaces_the_previous_command_instead_of_duplicating_it()
    {
        Directory.CreateDirectory(_directory);
        var helper = Path.Combine(_directory, "activity.exe");
        await File.WriteAllTextAsync(helper, "test");
        var settings = Path.Combine(_directory, "settings.json");
        var withoutLabels = new AgentActivityHookInstaller(helper, settings, AgentActivityProvider.ClaudeCode);
        var withLabels = new AgentActivityHookInstaller(
            helper,
            settings,
            AgentActivityProvider.ClaudeCode,
            includeWorkspaceLabels: true);

        await withoutLabels.InstallAsync();
        Assert.False(await withLabels.IsInstalledAsync());

        Assert.Equal(AgentActivityHookInstallationResult.Installed, await withLabels.InstallAsync());
        var written = await File.ReadAllTextAsync(settings);

        Assert.True(await withLabels.IsInstalledAsync());
        Assert.False(await withoutLabels.IsInstalledAsync());
        Assert.Equal(1, written.Split("--event PromptSubmitted").Length - 1);
        Assert.Equal(
            written.Split("--provider ClaudeCode").Length - 1,
            written.Split("--labels").Length - 1);
    }

    [Fact]
    public async Task Uninstall_clears_hooks_written_with_a_different_label_setting()
    {
        Directory.CreateDirectory(_directory);
        var helper = Path.Combine(_directory, "activity.exe");
        await File.WriteAllTextAsync(helper, "test");
        var settings = Path.Combine(_directory, "settings.json");
        await new AgentActivityHookInstaller(
                helper,
                settings,
                AgentActivityProvider.ClaudeCode,
                includeWorkspaceLabels: true)
            .InstallAsync();

        var withoutLabels = new AgentActivityHookInstaller(helper, settings, AgentActivityProvider.ClaudeCode);

        Assert.Equal(AgentActivityHookInstallationResult.Removed, await withoutLabels.UninstallAsync());
        Assert.DoesNotContain("--provider ClaudeCode", await File.ReadAllTextAsync(settings));
    }

    [Fact]
    public async Task The_windows_command_is_callable_by_powershell()
    {
        Directory.CreateDirectory(_directory);
        var helper = Path.Combine(_directory, "AIVitals.AgentActivity.Hook.exe");
        await File.WriteAllTextAsync(helper, "test");
        var settings = Path.Combine(_directory, "hooks.json");
        await new AgentActivityHookInstaller(helper, settings, AgentActivityProvider.Codex, includeWindowsCommand: true)
            .InstallAsync();

        var handler = JsonNode.Parse(await File.ReadAllTextAsync(settings))!
            ["hooks"]!["UserPromptSubmit"]!.AsArray()[0]!["hooks"]!.AsArray()[0]!;
        var windows = handler["commandWindows"]!.GetValue<string>();

        // Without the call operator PowerShell reads the quoted path as a string and fails on the
        // first argument, which is what made every Codex hook exit 1.
        Assert.StartsWith("& \"", windows);
        Assert.EndsWith(handler["command"]!.GetValue<string>(), windows);
    }

    [Fact]
    public async Task An_entry_left_by_a_helper_at_another_path_is_cleaned_up()
    {
        Directory.CreateDirectory(_directory);
        var helper = Path.Combine(_directory, "AIVitals.AgentActivity.Hook.exe");
        await File.WriteAllTextAsync(helper, "test");
        var settings = Path.Combine(_directory, "hooks.json");
        await File.WriteAllTextAsync(settings, """
            {
              "hooks": {
                "UserPromptSubmit": [
                  { "hooks": [{ "type": "command", "command": "\"D:/somewhere/else/AIVitals.AgentActivity.Hook.exe\" --provider Codex --event PromptSubmitted" }] },
                  { "hooks": [{ "type": "command", "command": "unrelated-user-hook" }] }
                ]
              }
            }
            """);
        var installer = new AgentActivityHookInstaller(helper, settings, AgentActivityProvider.Codex, includeWindowsCommand: true);

        Assert.Equal(AgentActivityHookInstallationResult.Installed, await installer.InstallAsync());
        var written = await File.ReadAllTextAsync(settings);

        var groups = JsonNode.Parse(written)!["hooks"]!["UserPromptSubmit"]!.AsArray();
        var commands = groups
            .SelectMany(group => group!["hooks"]!.AsArray())
            .Select(handler => handler!["command"]!.GetValue<string>())
            .ToArray();

        Assert.DoesNotContain("somewhere/else", written);
        Assert.Equal(2, commands.Length);
        Assert.Contains("unrelated-user-hook", commands);
        Assert.Single(commands, command => command.Contains("--event PromptSubmitted"));
        Assert.True(await installer.IsInstalledAsync());
    }

    [Fact]
    public async Task Uninstall_reaches_an_entry_whose_helper_has_moved()
    {
        Directory.CreateDirectory(_directory);
        var helper = Path.Combine(_directory, "AIVitals.AgentActivity.Hook.exe");
        await File.WriteAllTextAsync(helper, "test");
        var settings = Path.Combine(_directory, "hooks.json");
        await File.WriteAllTextAsync(settings, """
            {
              "hooks": {
                "Stop": [
                  { "hooks": [{ "type": "command", "command": "\"C:/old/install/AIVitals.AgentActivity.Hook.exe\" --provider Codex --event TurnStopped --labels" }] }
                ]
              }
            }
            """);

        var installer = new AgentActivityHookInstaller(helper, settings, AgentActivityProvider.Codex, includeWindowsCommand: true);

        Assert.Equal(AgentActivityHookInstallationResult.Removed, await installer.UninstallAsync());
        Assert.DoesNotContain("AgentActivity.Hook", await File.ReadAllTextAsync(settings));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
