using System.Reflection;
using System.Runtime.InteropServices;
using AIVitals.Application;
using Velopack;
using Velopack.Sources;

namespace AIVitals.Infrastructure;

/// <summary>
/// Consented updates served by the project's GitHub Releases. Checking and downloading run on their
/// own; installing always waits for an explicit request, so a restart never surprises the user.
/// </summary>
public sealed class VelopackUpdateService : IAppUpdateService
{
    public const string RepositoryUrl = "https://github.com/AlexAdiaconitei/ai-vitals";

    private readonly UpdateManager _manager;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UpdateInfo? _pending;
    private AppUpdateStatus _status;

    public VelopackUpdateService(string? repositoryUrl = null, string? channel = null)
    {
        var resolvedChannel = channel ?? ChannelForCurrentArchitecture();
        _manager = new UpdateManager(
            new GithubSource(repositoryUrl ?? RepositoryUrl, null, prerelease: false),
            new UpdateOptions { ExplicitChannel = resolvedChannel });

        var installedVersion = _manager.CurrentVersion?.ToString() ?? EntryAssemblyVersion();
        var alreadyDownloaded = _manager.IsInstalled ? _manager.UpdatePendingRestart : null;
        _status = new AppUpdateStatus(
            _manager.IsInstalled
                ? alreadyDownloaded is null ? AppUpdateState.Idle : AppUpdateState.ReadyToApply
                : AppUpdateState.Unsupported,
            installedVersion,
            resolvedChannel,
            alreadyDownloaded?.Version.ToString());
    }

    public AppUpdateStatus Status => _status;

    public event EventHandler<AppUpdateStatus>? StatusChanged;

    /// <summary>The channel each architecture publishes to, so an ARM64 install never pulls x64 packages.</summary>
    public static string ChannelForCurrentArchitecture() =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";

    public async Task CheckAsync(bool userInitiated, CancellationToken cancellationToken = default)
    {
        if (_status.State == AppUpdateState.Unsupported) return;
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;

        try
        {
            Publish(_status with { State = AppUpdateState.Checking, FailureDetail = null });
            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            var checkedAtUtc = DateTimeOffset.UtcNow;
            if (update is null)
            {
                _pending = null;
                Publish(_status with
                {
                    State = AppUpdateState.Idle,
                    AvailableVersion = null,
                    LastCheckedUtc = checkedAtUtc,
                    FailureDetail = null
                });
                return;
            }

            var availableVersion = update.TargetFullRelease.Version.ToString();
            Publish(_status with
            {
                State = AppUpdateState.Downloading,
                AvailableVersion = availableVersion,
                LastCheckedUtc = checkedAtUtc,
                FailureDetail = null
            });

            await _manager.DownloadUpdatesAsync(update, null, cancellationToken).ConfigureAwait(false);
            _pending = update;
            Publish(_status with { State = AppUpdateState.ReadyToApply, AvailableVersion = availableVersion });
        }
        catch (OperationCanceledException)
        {
            Publish(_status with { State = AppUpdateState.Idle, FailureDetail = null });
        }
        catch (Exception exception)
        {
            // Being offline is ordinary for a local-first app: only an explicit check reports the failure.
            Publish(_status with
            {
                State = userInitiated ? AppUpdateState.Failed : AppUpdateState.Idle,
                LastCheckedUtc = DateTimeOffset.UtcNow,
                FailureDetail = userInitiated ? exception.Message : null
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task ApplyAndRestartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var asset = _pending?.TargetFullRelease ?? _manager.UpdatePendingRestart;
        if (asset is null) return Task.CompletedTask;

        // Velopack hands control to the updater and ends this process.
        _manager.ApplyUpdatesAndRestart(asset);
        return Task.CompletedTask;
    }

    private void Publish(AppUpdateStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(this, status);
    }

    private static string EntryAssemblyVersion()
    {
        var informational = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational)) return "0.0.0";
        var buildMetadata = informational.IndexOf('+');
        return buildMetadata < 0 ? informational : informational[..buildMetadata];
    }
}
