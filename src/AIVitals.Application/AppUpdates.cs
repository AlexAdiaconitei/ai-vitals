namespace AIVitals.Application;

public enum AppUpdateState
{
    /// <summary>The running build was not installed by the updater, so no update path exists.</summary>
    Unsupported,
    Idle,
    Checking,
    Downloading,
    ReadyToApply,
    Failed
}

public sealed record AppUpdateStatus(
    AppUpdateState State,
    string InstalledVersion,
    string Channel,
    string? AvailableVersion = null,
    DateTimeOffset? LastCheckedUtc = null,
    string? FailureDetail = null)
{
    public bool IsPending => State == AppUpdateState.ReadyToApply;
    public bool IsBusy => State is AppUpdateState.Checking or AppUpdateState.Downloading;
}

/// <summary>
/// Consented application updates. Checking and downloading may happen without the user asking,
/// but nothing is ever applied until <see cref="ApplyAndRestartAsync"/> is called explicitly.
/// </summary>
public interface IAppUpdateService
{
    AppUpdateStatus Status { get; }
    event EventHandler<AppUpdateStatus>? StatusChanged;
    Task CheckAsync(bool userInitiated, CancellationToken cancellationToken = default);
    Task ApplyAndRestartAsync(CancellationToken cancellationToken = default);
}

/// <summary>Opt-in registration of the application in the per-user Windows startup list.</summary>
public interface IStartupRegistration
{
    bool IsSupported { get; }
    bool IsEnabled();
    void SetEnabled(bool enabled);
}

public static class UpdateCheckSchedule
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    public static bool ShouldCheck(bool automaticCheckEnabled, DateTimeOffset? lastCheckedUtc, DateTimeOffset nowUtc) =>
        automaticCheckEnabled && (lastCheckedUtc is null || nowUtc - lastCheckedUtc.Value >= Interval);
}
