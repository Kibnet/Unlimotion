using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.ReactiveUI;
using ReactiveUI;
using AutoMapper;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Logging;
using Microsoft.Extensions.Configuration;
using Quartz;
using Quartz.Impl;
using ServiceStack;
using Splat;
using Unlimotion.Scheduling.Jobs;
using Unlimotion.Services;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;

namespace Unlimotion.Desktop
{
    class Program
    {
        private const string DefaultConfigName = "Settings.json";
        private const string TasksFolderName = "Tasks";
        private const string UnlimotionFolderName = "Unlimotion";
        
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            var configArg = args.FirstOrDefault(s => s.StartsWith("-config="));
            
#if DEBUG
            var configPath = DefaultConfigName;
#else
            var unlimotionFolder = Environment.GetFolderPath(Environment.SpecialFolder.Personal).CombineWith(UnlimotionFolderName);
            var configPath = unlimotionFolder.CombineWith(DefaultConfigName);
#endif

            if (configArg != null)
            {
                var path = configArg.Split("=").Last();
                if (!path.IsNullOrEmpty())
                {
                    configPath = path;
                }
            }
            else
            {
#if !DEBUG
                Directory.CreateDirectory(unlimotionFolder);
#endif
            }

            IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(configPath);
            var taskStorageSettings = configuration.Get<TaskStorageSettings>("TaskStorage");
            Locator.CurrentMutable.RegisterConstant(configuration, typeof(IConfiguration));
            Locator.CurrentMutable.RegisterConstant(new Dialogs(), typeof(IDialogs));
            var mapper = AppModelMapping.ConfigureMapping();
            Locator.CurrentMutable.Register<IMapper>(() => mapper);
            var repositoryPath = string.IsNullOrWhiteSpace(taskStorageSettings.Path)
                ? TasksFolderName
                : taskStorageSettings.Path;
            Locator.CurrentMutable.Register<IRemoteBackupService>(() => 
                new BackupViaGitService(taskStorageSettings.GitUserName, taskStorageSettings.GitPassword, 
                    repositoryPath));

            var isServerMode = taskStorageSettings?.IsServerMode == true;

#if DEBUG
            TaskStorages.DefaultStoragePath = TasksFolderName;
#else
            TaskStorages.DefaultStoragePath = Path.GetDirectoryName(configPath).CombineWith(TasksFolderName);
#endif
            TaskStorages.RegisterStorage(isServerMode, configuration);

            var notificationManager = new NotificationManagerWrapper();
            Locator.CurrentMutable.RegisterConstant<INotificationManagerWrapper>(notificationManager);
            
            if (taskStorageSettings?.GitBackupEnabled == true)
            {
                var schedulerFactory = new StdSchedulerFactory();
                var scheduler = schedulerFactory.GetScheduler().Result;

                var pullJob = JobBuilder.Create<GitPullJob>()
                    .WithIdentity("GitPullJob", "GitPullJob")
                    .Build();
                
                var pushJob = JobBuilder.Create<GitPushJob>()
                    .WithIdentity("GitPushJob", "GitPushJob")
                    .Build();

                var pullTrigger = GenerateTriggerBySecondsInterval("PullTrigger", "GitPullJob",
                    taskStorageSettings.GitPullIntervalSeconds);
                var pushTrigger = GenerateTriggerBySecondsInterval("PushTrigger", "GitPushJob",
                    taskStorageSettings.GitPushIntervalSeconds);

                scheduler.ScheduleJob(pullJob, pullTrigger);
                scheduler.ScheduleJob(pushJob, pushTrigger);

                scheduler.Start();
            }

            BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()            
                .UsePlatformDetect()
                .WithInterFont()
#if DEBUG
                .LogToTrace(LogEventLevel.Debug, LogArea.Binding)
#else
                .LogToTrace()
#endif

                .UseReactiveUI();
        
        private static ITrigger GenerateTriggerBySecondsInterval(string name, string group, int seconds) 
        {
            return TriggerBuilder.Create()
                .WithIdentity(name, group)
                .StartNow()
                .WithSimpleSchedule(x => x
                    .WithIntervalInSeconds(seconds)
                    .RepeatForever())
                .Build();
        }
    }
}

