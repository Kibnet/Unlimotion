using System;
using System.Threading;
using System.Threading.Tasks;
using Quartz;
using Splat;
using Unlimotion.Services;

namespace Unlimotion.Scheduling.Jobs;

public class GitPushJob : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var gitService = Locator.Current.GetService<IRemoteBackupService>();
        new Thread(() => gitService?.Push($"Backup created {DateTime.Now}")).Start();
    }
}