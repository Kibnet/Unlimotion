using System.Threading.Tasks;
using Quartz;
using Splat;
using Unlimotion.Services;

namespace Unlimotion.Scheduling.Jobs;

public class GitPullJob : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var gitService = Locator.Current.GetService<IRemoteBackupService>();
        gitService?.Pull();
    }
}