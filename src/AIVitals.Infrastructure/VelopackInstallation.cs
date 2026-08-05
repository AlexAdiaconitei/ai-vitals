using Velopack.Locators;

namespace AIVitals.Infrastructure;

/// <summary>Facts about how the running build was installed, without leaking Velopack into the UI.</summary>
public static class VelopackInstallation
{
    public static bool IsInstalled =>
        VelopackLocator.IsCurrentSet && VelopackLocator.Current.CurrentlyInstalledVersion is not null;

    /// <summary>
    /// A path that keeps working after an update. Velopack swaps the versioned content directory on
    /// every update but keeps a stable launcher in the install root, so autostart must point there.
    /// </summary>
    public static string LauncherPath
    {
        get
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath)) return string.Empty;
            if (!IsInstalled) return processPath;

            var rootDirectory = VelopackLocator.Current.RootAppDir;
            if (string.IsNullOrEmpty(rootDirectory)) return processPath;

            var launcher = Path.Combine(rootDirectory, Path.GetFileName(processPath));
            return File.Exists(launcher) ? launcher : processPath;
        }
    }
}
