using System;
using Splat;
using Microsoft.Extensions.Configuration;
using ReactiveUI;
using Unlimotion.ViewModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using ServiceStack.Logging;
using System.ComponentModel;

namespace Unlimotion
{
    public static class TaskStorages {
        private static IDatabaseWatcher _dbWatcher;
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

            ITaskStorage taskStorage;
            if (isServerMode)
            {
                taskStorage = new ServerTaskStorage(settings?.URL);
            }
            else
            {
                taskStorage = CreateFileTaskStorage(settings?.Path); 
                _dbWatcher = new FileDbWatcher(settings?.Path);
                //_dbWatcher.Start();
                //Locator.CurrentMutable.RegisterConstant<IDatabaseWatcher>(fileDbWather);
            }

            Locator.CurrentMutable.RegisterConstant<ITaskStorage>(taskStorage);
            var taskRepository = new TaskRepository(taskStorage, _dbWatcher);
            Locator.CurrentMutable.RegisterConstant<ITaskRepository>(taskRepository);
        }

        private static FileTaskStorage CreateFileTaskStorage(string? path)
        {
            var storagePath = path;
            if (string.IsNullOrEmpty(path))
                storagePath = DefaultStoragePath;
            var taskStorage = new FileTaskStorage(storagePath);
            return taskStorage;
        }
    }

}