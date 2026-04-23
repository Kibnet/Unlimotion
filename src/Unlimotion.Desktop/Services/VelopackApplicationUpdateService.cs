using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.ViewModel;
using Velopack;
using Velopack.Sources;

namespace Unlimotion.Desktop.Services;

public sealed class VelopackApplicationUpdateService : IApplicationUpdateService
{
    private const string ReleasesRepositoryUrl = "https://github.com/Kibnet/Unlimotion";

    private readonly UpdateManager _updateManager;
    private UpdateInfo? _lastUpdateInfo;

    public VelopackApplicationUpdateService()
        : this(ReleasesRepositoryUrl)
    {
    }

    public VelopackApplicationUpdateService(string repositoryUrl)
    {
        var source = new GithubSource(repositoryUrl, accessToken: null, prerelease: false, downloader: null);
        _updateManager = new UpdateManager(source);
    }

    public bool IsSupported => _updateManager.IsInstalled;

    public string CurrentVersion
    {
        get
        {
            try
            {
                return _updateManager.CurrentVersion?.ToString() ?? GetEntryAssemblyVersion();
            }
            catch
            {
                return GetEntryAssemblyVersion();
            }
        }
    }

    public ApplicationUpdateInfo? PendingUpdate =>
        _updateManager.UpdatePendingRestart != null
            ? ToApplicationUpdateInfo(_updateManager.UpdatePendingRestart)
            : null;

    public async Task<ApplicationUpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        EnsureSupported();

        if (_updateManager.UpdatePendingRestart != null)
        {
            return ToApplicationUpdateInfo(_updateManager.UpdatePendingRestart);
        }

        cancellationToken.ThrowIfCancellationRequested();
        _lastUpdateInfo = await _updateManager.CheckForUpdatesAsync();

        return _lastUpdateInfo == null
            ? null
            : ToApplicationUpdateInfo(_lastUpdateInfo.TargetFullRelease);
    }

    public async Task DownloadUpdateAsync(CancellationToken cancellationToken = default)
    {
        EnsureSupported();

        if (_lastUpdateInfo == null)
        {
            if (_updateManager.UpdatePendingRestart != null)
            {
                return;
            }

            throw new InvalidOperationException("No update was selected for download.");
        }

        await _updateManager.DownloadUpdatesAsync(_lastUpdateInfo, progress: null, cancelToken: cancellationToken);
    }

    public void ApplyUpdateAndRestart()
    {
        EnsureSupported();

        var release = _lastUpdateInfo?.TargetFullRelease ?? _updateManager.UpdatePendingRestart;
        if (release == null)
        {
            throw new InvalidOperationException("No downloaded update is ready to apply.");
        }

        _updateManager.ApplyUpdatesAndRestart(release);
    }

    private void EnsureSupported()
    {
        if (!IsSupported)
        {
            throw new InvalidOperationException("The current application instance is not managed by Velopack.");
        }
    }

    private static ApplicationUpdateInfo ToApplicationUpdateInfo(VelopackAsset asset)
    {
        return new ApplicationUpdateInfo(
            asset.Version?.ToString() ?? string.Empty,
            asset.NotesMarkdown);
    }

    private static string GetEntryAssemblyVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
               ?? typeof(VelopackApplicationUpdateService).Assembly.GetName().Version?.ToString()
               ?? string.Empty;
    }
}
