using System;
using Splat;
using Microsoft.Extensions.Configuration;
using ReactiveUI;
using Unlimotion.ViewModel;
using System.Linq;
using Quartz;
using ITrigger = Quartz.ITrigger;
 
namespace Unlimotion
{
    public static class TaskStorages
    {
        public static string DefaultStoragePath;

        public static void SetSettingsCommands()
        {
            var configuration = Locator.Current.GetService<IConfiguration>();
            var settingsViewModel = Locator.Current.GetService<SettingsViewModel>();
            settingsViewModel.ObservableForProperty(m => m.IsServerMode)
                .Subscribe(c =>
                {
                    RegisterStorage(c.Value, configuration);
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
                var fileTaskStorage = CreateFileTaskStorage(storagePath);
                await serverTaskStorage.BulkInsert(fileTaskStorage.GetAll());
            });
            settingsViewModel.BackupCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                var serverTaskStorage = Locator.Current.GetService<ITaskStorage>() as ServerTaskStorage;
                if (serverTaskStorage == null)
                {
                    return;
                }
                var storagePath = configuration.Get<TaskStorageSettings>("TaskStorage")?.Path;
                var fileTaskStorage = CreateFileTaskStorage(storagePath);
                var tasks = serverTaskStorage.GetAll();
                foreach (var task in tasks)
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
                    await fileTaskStorage.Save(task);
                }
            });
            settingsViewModel.ResaveCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                var storagePath = configuration.Get<TaskStorageSettings>("TaskStorage")?.Path;
                var fileTaskStorage = CreateFileTaskStorage(storagePath);
                var tasks = fileTaskStorage.GetAll();
                foreach (var task in tasks)
                {
                    await fileTaskStorage.Save(task);
                }
            });
            settingsViewModel.BrowseTaskStoragePathCommand = ReactiveCommand.CreateFromTask(async (param) =>
            {
                var dialogs = Locator.Current.GetService<IDialogs>();
                var path = await dialogs?.ShowOpenFolderDialogAsync("Task Storage Path")!;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    settingsViewModel.TaskStoragePath = path;
                    //TODO сделать относительный путь
                }
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

            RegisterTaskRepository();
        }

        public static ITaskRepository RegisterTaskRepository()
        {
            var taskStorage = Locator.Current.GetService<ITaskStorage>();
            var dbWatcher = Locator.Current.GetService<IDatabaseWatcher>();

            var taskRepository = new TaskRepository(taskStorage, dbWatcher);
            Locator.CurrentMutable.RegisterConstant<ITaskRepository>(taskRepository);
            return taskRepository;
        }

        public static ITaskStorage RegisterServerTaskStorage(string? settingsUrl)
        {
            ITaskStorage taskStorage;
            taskStorage = new ServerTaskStorage(settingsUrl);
            Locator.CurrentMutable.UnregisterAll<IDatabaseWatcher>();
            Locator.CurrentMutable.RegisterConstant<ITaskStorage>(taskStorage);
            return taskStorage;
        }

        public static ITaskStorage RegisterFileTaskStorage(string storagePath)
        {
            ITaskStorage taskStorage;
            IDatabaseWatcher dbWatcher;
            taskStorage = CreateFileTaskStorage(storagePath);
            Locator.CurrentMutable.RegisterConstant<ITaskStorage>(taskStorage);
            try
            {
                dbWatcher = new FileDbWatcher(GetStoragePath(storagePath));
                Locator.CurrentMutable.RegisterConstant<IDatabaseWatcher>(dbWatcher);
            }
            catch (Exception ex)
            {
                dbWatcher = null;
                Locator.CurrentMutable.UnregisterAll<IDatabaseWatcher>();
            }
            return taskStorage;
        }

        private static FileTaskStorage CreateFileTaskStorage(string? path)
        {
            var storagePath = GetStoragePath(path);
            var taskStorage = new FileTaskStorage(storagePath);
            return taskStorage;
        }

        private static string GetStoragePath(string? path)
        {
            var storagePath = path;
            if (string.IsNullOrEmpty(path))
                storagePath = DefaultStoragePath;
            return storagePath;
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