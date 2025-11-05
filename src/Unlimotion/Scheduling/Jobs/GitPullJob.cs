using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Quartz;
using Splat;
using Unlimotion.ViewModel;

namespace Unlimotion.Scheduling.Jobs;

public class GitPullJob : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        if (Locator.Current.GetService<IConfiguration>()?.Get<GitSettings>("Git")?.BackupEnabled == true)
        {
            var gitService = Locator.Current.GetService<IRemoteBackupService>();
            gitService?.Pull();
        }
    }
}