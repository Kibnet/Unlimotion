using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using ServiceStack;
using Unlimotion.Services;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;

namespace Unlimotion.Test
{
    public class MainWindowViewModelFixture : IAsyncDisposable
    {
        private const string DefaultConfigName = "TestSettings.json";
        private string UniquiId => guid.ToString()!;
        private readonly ThreadLocal<Guid> guid;
        private readonly IDisposable? configurationDisposable;
        private readonly ITaskStorage ownedTaskStorage;

        private readonly string fixtureDirectory;
        private readonly string uniqueConfigName;
        private readonly object cleanupLock = new();
        private Task? cleanupTask;
        internal Func<string, string, Exception?>? DeleteFailureInjector { get; set; }
        internal Func<ITaskStorage, TaskItemViewModel[]> TaskItemsSnapshotProvider { get; set; } =
            static repository => repository.Tasks.Items.ToArray();
        public MainWindowViewModel MainWindowViewModelTest { get; private set; }
        public string ConfigPath => Path.GetFullPath(uniqueConfigName);
        internal string FixtureDirectoryPath => fixtureDirectory;
        public string? TaskTreeExpansionStatePath => TaskTreeExpansionStateStore.GetDefaultPath(ConfigPath);
        public readonly string DefaultTasksFolderPath;
        private readonly string defaultSnapshotsFolderPath;
        public string DefaultRootTaskPath;
        public const string RootTask1Id = "baaf00ad-e250-4828-8bec-a6b42525fda0";
        public const string RootTask2Id = "10c107c1-a6f0-41fe-9b44-1f2fc5ff0fcf";
        public const string SubTask22Id = "53c5b18d-3818-4467-b8bb-0346b21ebbc7";
        public const string RootTask3Id = "d63fbe66-4a91-44e4-b704-d85091831c56";
        public const string BlockedTask2Id = "a1d12137-8bca-46d2-bb1c-a413149123d8";
        public const string RootTask4Id = "c119a20a-6b75-40df-97c2-d2ca3822085f";
        public const string SubTask41Id = "b5a0c236-e738-4619-8f2a-d9454414fe6f";
        public const string ArchiveTask1Id = "0f154faf-7e8e-4cb2-9824-c9f1bfcf1984";
        public const string ArchiveTask11Id = "c136273b-99c9-4157-a8f2-5a128cb8b6de";
        public const string ArchivedTask1Id = "f6c3c536-217a-4190-b548-4d41a5c88bc2";
        public const string ArchivedTask11Id = "35250eba-d745-4928-ae1c-740601a71b58";
        public const string CompletedTaskId = "a0cc3a70-1fb1-41f7-895c-c3425d893d39";

        public const string RootTask5Id = "262653d2-3e1c-4ab0-a1ce-b4aaea1a80dd";
        public const string BlockedTask5Id = "411df323-a873-4aac-bd35-9dc0cc976ea2";

        public const string RootTask6Id = "91c641a1-db98-4689-bd45-54d7ffc92d98";
        public const string BlockedTask6Id = "5c06d648-9c04-47f4-8d5a-12f936bcd883";
        public const string DeadlockTask6Id = "6a34c1cc-5283-4f60-9138-aee91bf6a6cb";
        public const string DeadlockBlockedTask6Id = "0d8726d4-ea55-491e-9f52-6215ccb1ef19";

        public const string RootTask7Id = "18718dff-9364-4651-98ed-75be265a7751";
        public const string BlockedTask7Id = "f41774af-38f6-486c-9c5d-e4ba3300438c";
        public const string DeadlockTask7Id = "9b4b876e-6d4f-47f4-8007-f36fc291ed72";
        public const string DeadlockBlockedTask7Id = "4bdbac51-11f8-4629-b592-4641dd387867";

        public const string ClonedTask8Id = "2b39b656-f74b-4231-b0ed-ae283fbf9437";
        public const string ClonnedSubTask81Id = "2eef36b4-b557-4fd7-a202-1fa414f5e41f";
        public const string DestinationTask8Id = "a82ab7a0-e60c-40ba-b4b0-1c7e8c0d6a2b";

        public const string RepeateTask9Id = "3445eef8-4382-4607-b2fb-37a820467f1c";

        public MainWindowViewModelFixture()
        {
            guid = new ThreadLocal<Guid> { Value = Guid.NewGuid() };
            fixtureDirectory = Path.Combine(Environment.CurrentDirectory, $"MainWindowViewModelFixture_{UniquiId}");
            Directory.CreateDirectory(fixtureDirectory);
            uniqueConfigName = Path.Combine(fixtureDirectory, $"TestSettings_{UniquiId}.json");
            var defaultTasksFolderName = $"Tasks_{UniquiId}";
            DefaultTasksFolderPath = Path.Combine(fixtureDirectory, defaultTasksFolderName);
            defaultSnapshotsFolderPath = Path.Combine(Environment.CurrentDirectory, "Snapshots");
            DefaultRootTaskPath = Path.Combine(defaultSnapshotsFolderPath, RootTask1Id);
            Directory.CreateDirectory(DefaultTasksFolderPath);
            CopyTaskFromSnapshotsFolder();
            var fileInfo = new FileInfo(DefaultConfigName);
            var content = fileInfo.ReadAllText();
            content = content.Replace(
                "\"Path\": \"Tasks\"",
                $"\"Path\": \"{DefaultTasksFolderPath.Replace("\\", "\\\\")}\"");
            var configFile = File.Create(uniqueConfigName);
            configFile.Write(content);
            configFile.Close();

            // Create configuration
            IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(uniqueConfigName, reloadOnChange: false);
            configurationDisposable = configuration as IDisposable;

            // Create mapper
            var mapper = AppModelMapping.ConfigureMapping();

            // Create mock notification manager
            var notificationManagerMock = new NotificationManagerWrapperMock();

            // Create storage factory
            var storageFactory = new TaskStorageFactory(configuration, mapper, notificationManagerMock);
            // Create file storage
            ownedTaskStorage = storageFactory.CreateFileStorage(DefaultTasksFolderPath);

            // Create SettingsViewModel
            var settingsViewModel = new SettingsViewModel(configuration);

            // Create MainWindowViewModel with constructor injection
            MainWindowViewModelTest = new MainWindowViewModel(
                new AppNameDefinitionService(),
                notificationManagerMock,
                configuration,
                () => ownedTaskStorage,
                settingsViewModel,
                taskTreeExpansionStatePath: TaskTreeExpansionStatePath
            );
            var activeSource = storageFactory.SourceManager.ActiveSource;
            if (activeSource != null)
            {
                activeSource.TaskContext.MainWindow = MainWindowViewModelTest;
            }
        }

        private void CopyTaskFromSnapshotsFolder()
        {
            string[] files = Directory.GetFiles(defaultSnapshotsFolderPath);

            foreach (string file in files)
            {
                var fileInfo = new FileInfo(file);
                var newFilePath = Path.Combine(DefaultTasksFolderPath, fileInfo.Name);
                Try(() => fileInfo.CopyTo(newFilePath, true));
            }
        }

        private static void Try(Action action, int attempts = 3)
        {
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    action.Invoke();
                    return;
                }
                catch (Exception)
                {
                    Thread.Sleep(100);
                }
            }
        }

        public Task CleanTasksAsync()
        {
            lock (cleanupLock)
            {
                return cleanupTask ??= CleanTasksCoreAsync();
            }
        }

        public ValueTask DisposeAsync() => new(CleanTasksAsync());

        private async Task CleanTasksCoreAsync()
        {
            var failures = new List<Exception>();
            var taskRepository = MainWindowViewModelTest.taskRepository;
            var filesystemDeletionSafe = true;

            CaptureFailure(failures, MainWindowViewModelTest.Dispose);

            if (taskRepository != null)
            {
                TaskItemViewModel[] taskItems = [];
                try
                {
                    taskItems = TaskItemsSnapshotProvider(taskRepository);
                }
                catch (Exception exception)
                {
                    filesystemDeletionSafe = false;
                    AddFailure(failures, exception);
                }

                var sealedSnapshots = new List<Task>(taskItems.Length);
                foreach (var taskItem in taskItems)
                {
                    try
                    {
                        sealedSnapshots.Add(taskItem.SealPendingSaves());
                    }
                    catch (Exception exception)
                    {
                        filesystemDeletionSafe = false;
                        AddFailure(failures, exception);
                    }
                }

                var drainTask = Task.WhenAll(sealedSnapshots);
                try
                {
                    await drainTask;
                }
                catch (Exception exception)
                {
                    if (drainTask.Exception != null)
                    {
                        foreach (var innerException in drainTask.Exception.Flatten().InnerExceptions)
                        {
                            failures.Add(innerException);
                        }
                    }
                    else
                    {
                        AddFailure(failures, exception);
                    }
                }
            }

            if (ownedTaskStorage is IDisposable taskStorageDisposable)
            {
                CaptureFailure(failures, taskStorageDisposable.Dispose);
            }

            if (configurationDisposable != null)
            {
                CaptureFailure(failures, configurationDisposable.Dispose);
            }

            if (filesystemDeletionSafe)
            {
                await DeleteWithRetryAsync(
                    "delete config file",
                    uniqueConfigName,
                    () => File.Exists(uniqueConfigName),
                    () => File.Delete(uniqueConfigName),
                    failures);

                if (!string.IsNullOrWhiteSpace(TaskTreeExpansionStatePath))
                {
                    var expansionStatePath = TaskTreeExpansionStatePath;
                    await DeleteWithRetryAsync(
                        "delete expansion-state file",
                        expansionStatePath,
                        () => File.Exists(expansionStatePath),
                        () => File.Delete(expansionStatePath),
                        failures);
                }

                await DeleteWithRetryAsync(
                    "delete tasks directory",
                    DefaultTasksFolderPath,
                    () => Directory.Exists(DefaultTasksFolderPath),
                    () => Directory.Delete(DefaultTasksFolderPath, true),
                    failures);
                await DeleteWithRetryAsync(
                    "delete fixture directory",
                    fixtureDirectory,
                    () => Directory.Exists(fixtureDirectory),
                    () => Directory.Delete(fixtureDirectory, true),
                    failures);
            }

            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }

            if (failures.Count > 1)
            {
                throw new AggregateException("Fixture cleanup failed.", failures);
            }
        }

        private async Task DeleteWithRetryAsync(
            string operation,
            string path,
            Func<bool> exists,
            Action delete,
            ICollection<Exception> failures,
            int attempts = 3)
        {
            Exception? lastException = null;
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    if (!exists())
                    {
                        return;
                    }

                    var injectedFailure = DeleteFailureInjector?.Invoke(operation, path);
                    if (injectedFailure != null)
                    {
                        throw injectedFailure;
                    }

                    delete();
                    return;
                }
                catch (Exception exception)
                {
                    lastException = exception;
                    if (attempt < attempts)
                    {
                        await Task.Delay(100);
                    }
                }
            }

            failures.Add(new IOException(
                $"Failed to {operation} '{path}' after {attempts} attempts.",
                lastException));
        }

        private static void CaptureFailure(ICollection<Exception> failures, Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                AddFailure(failures, exception);
            }
        }

        private static void AddFailure(ICollection<Exception> failures, Exception exception)
        {
            if (exception is AggregateException aggregateException)
            {
                foreach (var innerException in aggregateException.Flatten().InnerExceptions)
                {
                    failures.Add(innerException);
                }

                return;
            }

            failures.Add(exception);
        }
    }
}
