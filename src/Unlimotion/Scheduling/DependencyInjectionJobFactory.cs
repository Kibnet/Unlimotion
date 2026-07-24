using System;
using Microsoft.Extensions.Configuration;
using Quartz;
using Quartz.Spi;
using Unlimotion.Scheduling.Jobs;
using Unlimotion.Services;
using Unlimotion.ViewModel;

namespace Unlimotion.Scheduling;

public class DependencyInjectionJobFactory : IJobFactory
{
    private readonly IConfiguration _configuration;
    private readonly IRemoteBackupService _backupService;
    private readonly ITaskSpaceOperationRunner? _operationRunner;
    private readonly Func<string?>? _activeSourceIdProvider;
    private readonly IActiveTaskSpaceConfiguration? _activeTaskSpaceConfiguration;

    public DependencyInjectionJobFactory(
        IConfiguration configuration,
        IRemoteBackupService backupService,
        ITaskSpaceOperationRunner? operationRunner = null,
        Func<string?>? activeSourceIdProvider = null,
        IActiveTaskSpaceConfiguration? activeTaskSpaceConfiguration = null)
    {
        _configuration = configuration;
        _backupService = backupService;
        _operationRunner = operationRunner;
        _activeSourceIdProvider = activeSourceIdProvider;
        _activeTaskSpaceConfiguration = activeTaskSpaceConfiguration;
    }

    public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
    {
        var jobType = bundle.JobDetail.JobType;

        if (jobType == typeof(GitPullJob))
        {
            return new GitPullJob(
                _configuration,
                _backupService,
                _operationRunner,
                _activeSourceIdProvider,
                _activeTaskSpaceConfiguration);
        }

        if (jobType == typeof(GitPushJob))
        {
            return new GitPushJob(
                _configuration,
                _backupService,
                _operationRunner,
                _activeSourceIdProvider,
                _activeTaskSpaceConfiguration);
        }

        throw new NotSupportedException($"Job type {jobType.Name} is not supported by this factory.");
    }

    public void ReturnJob(IJob job)
    {
        if (job is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
