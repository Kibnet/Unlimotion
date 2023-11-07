using System;
using Splat;
using Microsoft.Extensions.Configuration;
using ReactiveUI;
using Unlimotion.ViewModel;
using System.Linq;
using ServiceStack.Logging;
using Unlimotion.Services;

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
            settingsViewModel.MigrateCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                var serverTaskStorage = Locator.Current.GetService<ITaskStorage>() as ServerTaskStorage;
                if (serverTaskStorage == null)
                {
                    return;
                }
                var storageSettings = configuration.Get<TaskStorageSettings>("TaskStorage");
                var fileTaskStorage = CreateFileTaskStorage(storageSettings);
                await serverTaskStorage.BulkInsert(fileTaskStorage.GetAll());
            });
            settingsViewModel.BackupCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                var serverTaskStorage = Locator.Current.GetService<ITaskStorage>() as ServerTaskStorage;
                if (serverTaskStorage == null)
                {
                    return;
                }
                var storageSettings = configuration.Get<TaskStorageSettings>("TaskStorage");
                var fileTaskStorage = CreateFileTaskStorage(storageSettings);
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
                var storageSettings = configuration.Get<TaskStorageSettings>("TaskStorage");
                var fileTaskStorage = CreateFileTaskStorage(storageSettings);
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
            var storageSettings = configuration.Get<TaskStorageSettings>("TaskStorage");
            var prevStorage = Locator.Current.GetService<ITaskStorage>();
            if (prevStorage != null)
            {
                prevStorage.Disconnect();
            }

            ITaskStorage taskStorage;
            IDatabaseWatcher dbWatcher;
            if (isServerMode)
            {
                taskStorage = new ServerTaskStorage(storageSettings?.URL);
                dbWatcher = null;
            }
            else
            {
                taskStorage = CreateFileTaskStorage(storageSettings);
                dbWatcher = new FileDbWatcher(GetStoragePath(storageSettings?.Path));
            }

            Locator.CurrentMutable.RegisterConstant<ITaskStorage>(taskStorage);
            var taskRepository = new TaskRepository(taskStorage, dbWatcher);
            Locator.CurrentMutable.RegisterConstant<ITaskRepository>(taskRepository);
        }

        private static FileTaskStorage CreateFileTaskStorage(TaskStorageSettings? settings)
        {
            var storagePath = GetStoragePath(settings?.Path);
            var taskStorage = new FileTaskStorage(storagePath, 
                settings?.GitBackupEnabled == true
                    ? new BackupViaGitService(settings.GitUserName, settings.GitPassword, storagePath)
                    : null);
            return taskStorage;
        }

        private static string GetStoragePath(string? path)
        {
            var storagePath = path;
            if (string.IsNullOrEmpty(path))
                storagePath = DefaultStoragePath;
            return storagePath;
        }
    }

}