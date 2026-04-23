using System.Threading;
using System.Threading.Tasks;

namespace Unlimotion.ViewModel;

public interface IApplicationUpdateService
{
    bool IsSupported { get; }

    string CurrentVersion { get; }

    ApplicationUpdateInfo? PendingUpdate { get; }

    Task<ApplicationUpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    Task DownloadUpdateAsync(CancellationToken cancellationToken = default);

    void ApplyUpdateAndRestart();
}
