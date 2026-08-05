using AIVitals.Application;
using Microsoft.Win32;

namespace AIVitals.Platform.Windows;

/// <summary>
/// Opt-in "start with Windows" through the per-user Run key. The registered command must be a path
/// that survives updates, so callers pass the stable launcher rather than the running executable.
/// </summary>
public sealed class WindowsStartupRegistration : IStartupRegistration
{
    public const string ValueName = "AIVitals";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _command;

    public WindowsStartupRegistration(string launcherPath) =>
        _command = string.IsNullOrWhiteSpace(launcherPath) ? string.Empty : $"\"{launcherPath}\"";

    public bool IsSupported => _command.Length > 0;

    public bool IsEnabled()
    {
        if (!IsSupported) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string value &&
                   value.Equals(_command, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (!IsSupported) return;
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (key is null) return;

        if (enabled)
            key.SetValue(ValueName, _command, RegistryValueKind.String);
        else if (key.GetValue(ValueName) is not null)
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Removes the entry regardless of which path it points at. Used while uninstalling.</summary>
    public static void RemoveAny()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // An unwritable Run key leaves a stale entry that Windows ignores; uninstall continues.
        }
    }
}
