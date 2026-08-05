namespace AIVitals.Adapters.ClaudeCode;

/// <summary>
/// Copies the status line helper out of the versioned application directory into a stable location.
/// Claude Code stores the absolute helper path in its own settings and launches it on every refresh,
/// so the path must not change between versions and the running helper must not sit inside a folder
/// that an update is about to replace.
/// </summary>
public sealed class ClaudeCodeBridgeStaging
{
    public const string HelperFileName = "AIVitals.ClaudeCode.StatusLine.exe";
    private const string StampFileName = ".staged-version";

    private readonly string _sourceDirectory;
    private readonly string _targetDirectory;

    public ClaudeCodeBridgeStaging(string sourceDirectory, string targetDirectory)
    {
        _sourceDirectory = Path.GetFullPath(sourceDirectory);
        _targetDirectory = Path.GetFullPath(targetDirectory);
    }

    public string HelperPath => Path.Combine(_targetDirectory, HelperFileName);

    public bool IsStaged => File.Exists(HelperPath);

    /// <summary>
    /// Brings the staged copy up to <paramref name="version"/>. Returns false when the copy could not
    /// be refreshed - typically because Claude Code is running the helper right now - leaving the
    /// previous staged copy untouched so the bridge keeps working until the next start.
    /// </summary>
    public bool TryStage(string version)
    {
        if (!File.Exists(Path.Combine(_sourceDirectory, HelperFileName))) return IsStaged;
        if (IsStaged && ReadStamp() == version) return true;

        var stagingDirectory = _targetDirectory + ".staging";
        var previousDirectory = _targetDirectory + ".previous";
        try
        {
            DeleteDirectory(stagingDirectory);
            DeleteDirectory(previousDirectory);
            CopyDirectory(_sourceDirectory, stagingDirectory);
            File.WriteAllText(Path.Combine(stagingDirectory, StampFileName), version);

            if (Directory.Exists(_targetDirectory)) Directory.Move(_targetDirectory, previousDirectory);
            Directory.Move(stagingDirectory, _targetDirectory);
            DeleteDirectory(previousDirectory);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Restore whatever was already working before giving up on this attempt.
            if (!Directory.Exists(_targetDirectory) && Directory.Exists(previousDirectory))
                TryMoveBack(previousDirectory);
            DeleteDirectory(stagingDirectory);
            return IsStaged;
        }
    }

    /// <summary>Removes the staged copy. Used while uninstalling, after the bridge has been reverted.</summary>
    public void Remove() => DeleteDirectory(_targetDirectory);

    private string? ReadStamp()
    {
        var stampPath = Path.Combine(_targetDirectory, StampFileName);
        try
        {
            return File.Exists(stampPath) ? File.ReadAllText(stampPath).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void TryMoveBack(string previousDirectory)
    {
        try
        {
            Directory.Move(previousDirectory, _targetDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The next start stages again from scratch.
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Leftovers are replaced on the next attempt.
        }
    }
}
