using System;
using System.Collections.Generic;
using System.Linq;
using AutoMapper;
using Microsoft.Extensions.Configuration;
using Quartz;
using ReactiveUI;
using Splat;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using ITrigger = Quartz.ITrigger;

namespace Unlimotion
{
    public static class TaskStorages
    {
        public static string DefaultStoragePath = string.Empty;

        public static void SetSettingsCommands()
        {
            var configuration = Locator.Current.GetService<IConfiguration>();
            var settingsViewModel = Locator.Current.GetService<SettingsViewModel>();
            var mapper = Locator.Current.GetService<IMapper>();
            settingsViewModel.ConnectCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                RegisterStorage(settingsViewModel.IsServerMode, configuration);
                var mainWindowViewModel = Locator.Current.GetService<MainWindowViewModel>();
                await mainWindowViewModel.Connect();
                var notify = Locator.Current.GetService<INotificationManagerWrapper>();
                notify?.SuccessToast("Хранилище задач подключено и все задачи из него загружены");
            });
            settingsViewModel.ObservableForProperty(m => m.GitBackupEnabled, true)
                .Subscribe(c =>
                {
                    var scheduler = Locator.Current.GetService<IScheduler>();

                    if (c.Value)
                        scheduler.ResumeAll();
                    else
                        scheduler.PauseAll();
                });
            settingsViewModel.ObservableForProperty(m => m.GitPullIntervalSeconds, true)
                .Subscribe(c =>
                {
                    if (c.Value == null)
                        return;
                    var scheduler = Locator.Current.GetService<IScheduler>();
                    var triggerKey = new TriggerKey("PullTrigger", "GitPullJob");
                    scheduler.RescheduleJob(triggerKey, GenerateTriggerBySecondsInterval("PullTrigger", "GitPullJob", c.Value));
                });
            settingsViewModel.ObservableForProperty(m => m.GitPushIntervalSeconds, true)
                .Subscribe(c =>
                {
                    if (c.Value == null)
                        return;
                    var scheduler = Locator.Current.GetService<IScheduler>();
                    var triggerKey = new TriggerKey("PushTrigger", "GitPushJob");
                    scheduler.RescheduleJob(triggerKey, GenerateTriggerBySecondsInterval("PushTrigger", "GitPushJob", c.Value));
                });
            settingsViewModel.MigrateCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                var serverTaskStorage = Locator.Current.GetService<ITaskStorage>() as ServerTaskStorage;
                if (serverTaskStorage == null)
                {
                    return;
                }
                var storagePath = configuration.Get<TaskStorageSettings>("TaskStorage")?.Path;
                var fileStorage = CreateFileStorage(storagePath);
                var tasks = new List<TaskItem>();
                await foreach (var task in fileStorage.GetAll())
                    tasks.Add(task);
                await serverTaskStorage.BulkInsert(tasks);
            });
            settingsViewModel.BackupCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                var serverTaskStorage = Locator.Current.GetService<ITaskStorage>() as ServerTaskStorage;
                if (serverTaskStorage == null)
                {
                    return;
                }
                var storagePath = configuration.Get<TaskStorageSettings>("TaskStorage")?.Path;
                var fileStorage = CreateFileStorage(storagePath);
                await foreach (var task in serverTaskStorage.GetAll())
                {
                      task.Id = task.Id.Replace("TaskItem/", "");
                      if (task.BlocksTasks != null)
                      {
                        task.BlocksTasks = task.BlocksTasks.Select(s => s.Replace("TaskItem/", "")).ToList();
                      }
                      if (task.ContainsTasks != null)
                      {
                        task.ContainsTasks = task.ContainsTasks.Select(s => s.Replace("TaskItem/", "")).ToList();
                      }
                      await fileStorage.Save(task);
                }
            });
            settingsViewModel.ResaveCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                var storagePath = configuration.Get<TaskStorageSettings>("TaskStorage")?.Path;
                var fileStorage = CreateFileTaskStorage(storagePath);
                foreach (var task in fileStorage.Tasks.Items)
                {
                      task.SaveItemCommand.Execute();
                }
            });
            settingsViewModel.BrowseTaskStoragePathCommand = ReactiveCommand.CreateFromTask(async param =>
            {
                var dialogs = Locator.Current.GetService<IDialogs>();
                var path = await dialogs?.ShowOpenFolderDialogAsync("Task Storage Path")!;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    settingsViewModel.TaskStoragePath = path;
                    //TODO сделать относительный путь
                }
            });
            settingsViewModel.CloneCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                var gitService = Locator.Current.GetService<IRemoteBackupService>();
                gitService?.CloneOrUpdateRepo();
            });
            settingsViewModel.PullCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                var gitService = Locator.Current.GetService<IRemoteBackupService>();
                gitService?.Pull();
            });
            settingsViewModel.PushCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                var gitService = Locator.Current.GetService<IRemoteBackupService>();
                gitService?.Push("Manual backup");
            });
            settingsViewModel.CloneCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                var gitService = Locator.Current.GetService<IRemoteBackupService>();
                gitService?.CloneOrUpdateRepo();
            });
            settingsViewModel.PullCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                var gitService = Locator.Current.GetService<IRemoteBackupService>();
                gitService?.Pull();
            });
            settingsViewModel.PushCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                var gitService = Locator.Current.GetService<IRemoteBackupService>();
                gitService?.Push("Manual backup");
            });
        }

        public static void RegisterStorage(bool isServerMode, IConfiguration configuration)
        {
            var settings = configuration.Get<TaskStorageSettings>("TaskStorage");
            var prevStorage = Locator.Current.GetService<ITaskStorage>();
            if (prevStorage != null)
            {
                prevStorage.Disconnect();
            }

            if (isServerMode)
            {
                RegisterServerTaskStorage(settings?.URL);
            }
            else
            {
                RegisterFileTaskStorage(settings?.Path);
            }
        }

        public static ITaskStorage RegisterServerTaskStorage(string? settingsUrl)
        {
            ITaskStorage taskStorage;
            taskStorage = new ServerTaskStorage(settingsUrl);
            Locator.CurrentMutable.UnregisterAll<IDatabaseWatcher>();
            Locator.CurrentMutable.RegisterConstant<ITaskStorage>(taskStorage);
            //taskStorage.Connect().GetAwaiter().GetResult();
            return taskStorage;
        }

        public static ITaskStorage RegisterFileTaskStorage(string storagePath)
        {
            ITaskStorage taskStorage;
            taskStorage = CreateFileTaskStorage(storagePath);
            Locator.CurrentMutable.RegisterConstant<ITaskStorage>(taskStorage);
            return taskStorage;
        }

        private static FileStorage CreateFileStorage(string? path)
        {
            var storagePath = GetStoragePath(path);
            var fileStorage = new FileStorage(storagePath, true);
            return fileStorage;
        }

        private static FileTaskStorage CreateFileTaskStorage(string? path)
        {
            var fileStorage = CreateFileStorage(path);
            var taskTreeManager = new TaskTreeManager(fileStorage);
            var taskStorage = new FileTaskStorage(taskTreeManager);
            Locator.CurrentMutable.RegisterConstant(taskStorage, typeof(FileTaskStorage));
            return taskStorage;
        }

        private static string GetStoragePath(string? path)
        {
            var storagePath = path;
            if (string.IsNullOrEmpty(path))
                storagePath = DefaultStoragePath;
            return storagePath ?? string.Empty;
        }
        
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