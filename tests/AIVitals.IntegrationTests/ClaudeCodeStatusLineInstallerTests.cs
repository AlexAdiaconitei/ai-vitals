using System.Text.Json.Nodes;
using AIVitals.Adapters.ClaudeCode;

namespace AIVitals.IntegrationTests;

public sealed class ClaudeCodeStatusLineInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AIVitals.ClaudeInstaller.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Install_preserves_settings_and_uninstall_restores_previous_status_line()
    {
        Directory.CreateDirectory(_root);
        var helperPath = Path.Combine(_root, "statusline", "bridge.exe");
        var settingsPath = Path.Combine(_root, ".claude", "settings.json");
        var bridgePath = Path.Combine(_root, "local", "bridge.json");
        Directory.CreateDirectory(Path.GetDirectoryName(helperPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllBytesAsync(helperPath, [0]);
        var original = """
            {
              "theme": "dark",
              "statusLine": {
                "type": "command",
                "command": "C:/tools/my-status.exe",
                "padding": 2
              }
            }
            """;
        await File.WriteAllTextAsync(settingsPath, original);
        var installer = new ClaudeCodeStatusLineInstaller(helperPath, settingsPath, bridgePath);

        var installed = await installer.InstallAsync();

        Assert.Equal(ClaudeCodeIntegrationResult.Installed, installed);
        var settings = JsonNode.Parse(await File.ReadAllTextAsync(settingsPath))!.AsObject();
        Assert.Equal("dark", settings["theme"]!.GetValue<string>());
        Assert.Equal(installer.InstalledCommand, settings["statusLine"]!["command"]!.GetValue<string>());
        Assert.Equal(2, settings["statusLine"]!["padding"]!.GetValue<int>());
        Assert.Equal(
            ClaudeCodeStatusLineInstaller.RefreshIntervalSeconds,
            settings["statusLine"]!["refreshInterval"]!.GetValue<int>());
        Assert.Equal(original, await File.ReadAllTextAsync(settingsPath + ".ai-vitals.bak"));

        var configuration = await ClaudeCodeBridgeProtocol.ReadConfigurationAsync(bridgePath);
        Assert.Equal("C:/tools/my-status.exe", configuration!.PreviousStatusLine!["command"]!.GetValue<string>());
        Assert.NotNull(configuration.OriginalSettingsSha256);
        Assert.Equal(64, configuration.OriginalSettingsSha256!.Length);

        settings["newSetting"] = true;
        await File.WriteAllTextAsync(settingsPath, settings.ToJsonString());
        var restored = await installer.UninstallAsync();

        Assert.Equal(ClaudeCodeIntegrationResult.Restored, restored);
        var afterRestore = JsonNode.Parse(await File.ReadAllTextAsync(settingsPath))!.AsObject();
        Assert.Equal("C:/tools/my-status.exe", afterRestore["statusLine"]!["command"]!.GetValue<string>());
        Assert.Equal(2, afterRestore["statusLine"]!["padding"]!.GetValue<int>());
        Assert.True(afterRestore["newSetting"]!.GetValue<bool>());
        Assert.False(File.Exists(bridgePath));
    }

    [Fact]
    public async Task Uninstall_does_not_overwrite_external_status_line_changes()
    {
        var (installer, settingsPath, bridgePath) = await CreateEmptyInstallationAsync();
        await installer.InstallAsync();
        var settings = JsonNode.Parse(await File.ReadAllTextAsync(settingsPath))!.AsObject();
        settings["statusLine"]!["padding"] = 8;
        await File.WriteAllTextAsync(settingsPath, settings.ToJsonString());

        var result = await installer.UninstallAsync();

        Assert.Equal(ClaudeCodeIntegrationResult.ModifiedExternally, result);
        var unchanged = JsonNode.Parse(await File.ReadAllTextAsync(settingsPath))!.AsObject();
        Assert.Equal(8, unchanged["statusLine"]!["padding"]!.GetValue<int>());
        Assert.True(File.Exists(bridgePath));
    }

    [Fact]
    public async Task Complex_existing_shell_command_requires_manual_composition()
    {
        Directory.CreateDirectory(_root);
        var helperPath = Path.Combine(_root, "bridge.exe");
        var settingsPath = Path.Combine(_root, "settings.json");
        var bridgePath = Path.Combine(_root, "bridge.json");
        await File.WriteAllBytesAsync(helperPath, [0]);
        var original = """{"statusLine":{"type":"command","command":"first.exe | second.exe"}}""";
        await File.WriteAllTextAsync(settingsPath, original);
        var installer = new ClaudeCodeStatusLineInstaller(helperPath, settingsPath, bridgePath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync());

        Assert.Equal(original, await File.ReadAllTextAsync(settingsPath));
        Assert.False(File.Exists(bridgePath));
    }

    [Fact]
    public async Task Reconnect_upgrades_an_existing_bridge_with_periodic_refresh_without_losing_restore_data()
    {
        var (installer, settingsPath, bridgePath) = await CreateEmptyInstallationAsync();
        await installer.InstallAsync();
        var settings = JsonNode.Parse(await File.ReadAllTextAsync(settingsPath))!.AsObject();
        settings["statusLine"]!.AsObject().Remove("refreshInterval");
        await File.WriteAllTextAsync(settingsPath, settings.ToJsonString());

        var upgraded = await installer.InstallAsync();

        Assert.Equal(ClaudeCodeIntegrationResult.Installed, upgraded);
        settings = JsonNode.Parse(await File.ReadAllTextAsync(settingsPath))!.AsObject();
        Assert.Equal(
            ClaudeCodeStatusLineInstaller.RefreshIntervalSeconds,
            settings["statusLine"]!["refreshInterval"]!.GetValue<int>());
        var configuration = await ClaudeCodeBridgeProtocol.ReadConfigurationAsync(bridgePath);
        Assert.Equal(
            ClaudeCodeStatusLineInstaller.RefreshIntervalSeconds,
            configuration!.InstalledStatusLine!["refreshInterval"]!.GetValue<int>());
        Assert.Null(configuration.PreviousStatusLine);
    }

    private async Task<(ClaudeCodeStatusLineInstaller Installer, string SettingsPath, string BridgePath)> CreateEmptyInstallationAsync()
    {
        Directory.CreateDirectory(_root);
        var helperPath = Path.Combine(_root, "bridge.exe");
        var settingsPath = Path.Combine(_root, "settings.json");
        var bridgePath = Path.Combine(_root, "bridge.json");
        await File.WriteAllBytesAsync(helperPath, [0]);
        await File.WriteAllTextAsync(settingsPath, "{\"theme\":\"dark\"}");
        return (new ClaudeCodeStatusLineInstaller(helperPath, settingsPath, bridgePath), settingsPath, bridgePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
