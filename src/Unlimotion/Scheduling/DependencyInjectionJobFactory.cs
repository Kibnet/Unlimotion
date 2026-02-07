using System;
using Microsoft.Extensions.Configuration;
using Quartz;
using Quartz.Spi;
using Unlimotion.Scheduling.Jobs;
using Unlimotion.ViewModel;

namespace Unlimotion.Scheduling;

public class DependencyInjectionJobFactory : IJobFactory
{
    private readonly IConfiguration _configuration;
    private readonly IRemoteBackupService _backupService;

    public DependencyInjectionJobFactory(IConfiguration configuration, IRemoteBackupService backupService)
    {
        _configuration = configuration;
        _backupService = backupService;
    }

    public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
    {
        var jobType = bundle.JobDetail.JobType;

        if (jobType == typeof(GitPullJob))
        {
            return new GitPullJob(_configuration, _backupService);
        }

        if (jobType == typeof(GitPushJob))
        {
            return new GitPushJob(_configuration, _backupService);
        }

        throw new NotSupportedException($"Job type {jobType.Name} is not supported by this factory.");
    }

    public void ReturnJob(IJob job)
    {
        // Quartz calls this when a job is complete
        // For simple jobs, we don't need to do anything
        // If jobs implement IDisposable, we could dispose them here
        if (job is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
