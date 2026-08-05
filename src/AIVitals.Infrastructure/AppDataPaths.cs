namespace AIVitals.Infrastructure;

public sealed record AppDataPaths(string RootDirectory)
{
    public const string DataDirectoryEnvironmentVariable = "AI_VITALS_DATA_DIRECTORY";

    public string DatabasePath => Path.Combine(RootDirectory, "usage.db");
    public string PreferencesPath => Path.Combine(RootDirectory, "preferences.json");

    /// <summary>
    /// Where the Claude Code status line helper is staged. It lives outside the versioned
    /// application directory so that the path written into Claude Code's settings survives
    /// updates, and so that a helper running while an update is applied cannot lock it.
    /// </summary>
    public string BridgeDirectory => Path.Combine(RootDirectory, "bridge");

    public static AppDataPaths ForCurrentUser()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
            return new AppDataPaths(Path.GetFullPath(overrideDirectory));

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new AppDataPaths(Path.Combine(localAppData, "AIVitals"));
    }

    public void EnsureCreated() => Directory.CreateDirectory(RootDirectory);
}
