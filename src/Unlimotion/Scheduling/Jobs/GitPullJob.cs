using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Quartz;
using Unlimotion.Services;
using Unlimotion.ViewModel;

namespace Unlimotion.Scheduling.Jobs;

public class GitPullJob : IJob
{
    private readonly IConfiguration _configuration;
    private readonly IRemoteBackupService _backupService;
    private readonly ITaskSpaceOperationRunner? _operationRunner;
    private readonly Func<string?>? _activeSourceIdProvider;
    private readonly IActiveTaskSpaceConfiguration? _activeTaskSpaceConfiguration;

    public GitPullJob(
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

    public Task Execute(IJobExecutionContext context)
    {
        if (_operationRunner == null)
        {
            ExecuteCore();
            return Task.CompletedTask;
        }

        var sourceId = _activeSourceIdProvider?.Invoke();
        return _operationRunner.RunExclusiveAsync(
            "ScheduledGitPull",
            sourceId,
            operationContext =>
            {
                using var backupScope =
                    (_backupService as ITaskSpaceBackupOperationScope)
                    ?.BeginTaskSpaceOperation(operationContext);
                try
                {
                    ExecuteCore();
                }
                finally
                {
                    PersistProjectionCore(operationContext, sourceId);
                }

                return Task.CompletedTask;
            },
            context?.CancellationToken ?? default);
    }

    private void ExecuteCore()
    {
        if (_configuration.Get<GitSettings>("Git")?.BackupEnabled == true &&
            _backupService.GetConflictStatus().IsInProgress != true)
        {
            _backupService.Pull();
        }
    }

    private void PersistProjectionCore(TaskSpaceOperationContext context, string? sourceId)
    {
        if (!string.IsNullOrWhiteSpace(sourceId) && _activeTaskSpaceConfiguration != null)
        {
            _activeTaskSpaceConfiguration.PersistCore(
                context,
                _activeTaskSpaceConfiguration.CaptureActiveProjection(sourceId));
        }
    }
}
