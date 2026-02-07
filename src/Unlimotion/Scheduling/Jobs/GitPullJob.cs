using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Quartz;
using Unlimotion.ViewModel;

namespace Unlimotion.Scheduling.Jobs;

public class GitPullJob : IJob
{
    private readonly IConfiguration _configuration;
    private readonly IRemoteBackupService _backupService;

    public GitPullJob(IConfiguration configuration, IRemoteBackupService backupService)
    {
        _configuration = configuration;
        _backupService = backupService;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        if (_configuration.Get<GitSettings>("Git")?.BackupEnabled == true)
        {
            _backupService.Pull();
        }
    }
}