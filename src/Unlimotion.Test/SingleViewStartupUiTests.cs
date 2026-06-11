using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using Unlimotion.Domain;
using Unlimotion.Services;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class SingleViewStartupUiTests
{
    [Test]
    public async Task SingleViewStartup_ConnectsExistingTaskStorage()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var context = SingleViewStartupContext.Create();

            var app = new App();
            var vm = context.MainWindowViewModel;

            var mainScreen = app.CreateSingleViewMainView(vm);
            var startupTask = app.InitializeStartupViewModelAsync(vm);
            var startupCompleted = WaitFor(() =>
                startupTask.IsCompleted &&
                vm.IsInitialized &&
                vm.taskRepository?.Tasks.Count == 1 &&
                vm.CurrentAllTasksItems.Count == 1);

            using (Assert.Multiple())
            {
                await Assert.That(startupCompleted).IsTrue();
                await Assert.That(mainScreen).IsNotNull();
                await Assert.That(mainScreen.DataContext).IsSameReferenceAs(vm);
                await Assert.That(vm.IsInitialized).IsTrue();
                await Assert.That(vm.taskRepository!.Tasks.Count).IsEqualTo(1);
                await Assert.That(vm.CurrentAllTasksItems.Count).IsEqualTo(1);
                await Assert.That(context.Storage.ConnectCallCount).IsEqualTo(1);
                await Assert.That(context.Storage.GetAllCallCount).IsEqualTo(1);
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task SingleViewStartup_DoesNotReloadInitializedTaskStorage()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var context = SingleViewStartupContext.Create();

            var app = new App();
            var vm = context.MainWindowViewModel;
            await app.InitializeStartupViewModelAsync(vm);

            var connectCallCount = context.Storage.ConnectCallCount;
            var getAllCallCount = context.Storage.GetAllCallCount;

            await app.InitializeStartupViewModelAsync(vm);

            using (Assert.Multiple())
            {
                await Assert.That(vm.IsInitialized).IsTrue();
                await Assert.That(vm.taskRepository!.Tasks.Count).IsEqualTo(1);
                await Assert.That(context.Storage.ConnectCallCount).IsEqualTo(connectCallCount);
                await Assert.That(context.Storage.GetAllCallCount).IsEqualTo(getAllCallCount);
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task SingleViewStartup_ConnectsMigratedLocalFolderOutsideGitAndAllowsCreate()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var context = FileStorageStartupContext.CreateWithLegacyTaskOutsideGit();

            var app = new App();
            var vm = context.MainWindowViewModel;

            var startupTask = app.InitializeStartupViewModelAsync(vm);
            var startupCompleted = WaitFor(() =>
                startupTask.IsCompleted &&
                vm.IsInitialized &&
                vm.taskRepository?.Tasks.Count == 1 &&
                vm.CurrentAllTasksItems.Count == 1);

            await Assert.That(startupCompleted).IsTrue();
            await Assert.That(vm.Settings.StorageConnectionState).IsEqualTo(SettingsConnectionState.Connected);
            await Assert.That(context.NotificationManager.LastErrorMessage).IsNull();
            await Assert.That(Directory.Exists(Path.Combine(context.TasksPath, ".git"))).IsFalse();
            await Assert.That(File.Exists(Path.Combine(
                    context.TasksPath,
                    "status-model.migration.backup",
                    "legacy-task")))
                .IsTrue();

            vm.Create.Execute(null);
            var createCompleted = WaitFor(() =>
                vm.taskRepository?.Tasks.Count == 2 &&
                vm.CurrentTaskItem is { Id: not null } &&
                vm.CurrentTaskItem.Id != "legacy-task");

            await Assert.That(createCompleted).IsTrue();
            await Assert.That(vm.CurrentAllTasksItems.Count).IsEqualTo(2);
        }, CancellationToken.None);
    }

    [Test]
    public async Task SingleViewStartup_ReplaysStartupUpdateCheck_WhenUpdateServiceAttachesAfterStartup()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var context = SingleViewStartupContext.Create();
            var app = new App();
            var updateService = new CountingApplicationUpdateService();

            try
            {
                App.ConfigureUpdateService(null);

                var startupTask = app.InitializeStartupViewModelAsync(context.MainWindowViewModel);
                var startupCompleted = WaitFor(() =>
                    startupTask.IsCompleted &&
                    context.Storage.ConnectCallCount == 1);

                await Assert.That(startupCompleted).IsTrue();
                await Assert.That(updateService.CheckCalls).IsEqualTo(0);

                App.ConfigureUpdateService(updateService);
                var updateCheckReplayed = WaitFor(() =>
                    updateService.CheckCalls == 1 &&
                    context.MainWindowViewModel.Settings.UpdateState == ApplicationUpdateState.NoUpdates);

                await Assert.That(updateCheckReplayed).IsTrue();
            }
            finally
            {
                App.ConfigureUpdateService(null);
            }
        }, CancellationToken.None);
    }

    private static bool WaitFor(Func<bool> predicate, int timeoutMilliseconds = 5000)
    {
        return SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return predicate();
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));
    }

    private sealed class SingleViewStartupContext : IDisposable
    {
        private readonly string _configPath;
        private readonly IDisposable? _configurationDisposable;

        private SingleViewStartupContext(
            string configPath,
            CountingStorage storage,
            MainWindowViewModel mainWindowViewModel,
            IDisposable? configurationDisposable)
        {
            _configPath = configPath;
            Storage = storage;
            MainWindowViewModel = mainWindowViewModel;
            _configurationDisposable = configurationDisposable;
        }

        public CountingStorage Storage { get; }

        public MainWindowViewModel MainWindowViewModel { get; }

        public static SingleViewStartupContext Create()
        {
            var configPath = Path.Combine(
                Environment.CurrentDirectory,
                $"SingleViewStartup_{Guid.NewGuid():N}.json");
            File.WriteAllText(configPath, "{}");

            IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(configPath, reloadOnChange: false);
            var notificationManager = new NotificationManagerWrapperMock();
            var storage = new CountingStorage();
            var taskStorage = new UnifiedTaskStorage(new TaskTreeManager(storage));
            var settings = new SettingsViewModel(configuration);
            var mainWindowViewModel = new MainWindowViewModel(
                new AppNameDefinitionService(),
                notificationManager,
                configuration,
                () => taskStorage,
                settings);

            TaskItemViewModel.NotificationManagerInstance = notificationManager;
            TaskItemViewModel.MainWindowInstance = mainWindowViewModel;

            return new SingleViewStartupContext(
                configPath,
                storage,
                mainWindowViewModel,
                configuration as IDisposable);
        }

        public void Dispose()
        {
            MainWindowViewModel.Dispose();
            _configurationDisposable?.Dispose();

            if (File.Exists(_configPath))
            {
                File.Delete(_configPath);
            }
        }
    }

    private sealed class FileStorageStartupContext : IDisposable
    {
        private readonly string _configPath;
        private readonly IDisposable? _configurationDisposable;

        private FileStorageStartupContext(
            string configPath,
            string tasksPath,
            NotificationManagerWrapperMock notificationManager,
            UnifiedTaskStorage taskStorage,
            MainWindowViewModel mainWindowViewModel,
            IDisposable? configurationDisposable)
        {
            _configPath = configPath;
            TasksPath = tasksPath;
            NotificationManager = notificationManager;
            TaskStorage = taskStorage;
            MainWindowViewModel = mainWindowViewModel;
            _configurationDisposable = configurationDisposable;
        }

        public string TasksPath { get; }

        public NotificationManagerWrapperMock NotificationManager { get; }

        public UnifiedTaskStorage TaskStorage { get; }

        public MainWindowViewModel MainWindowViewModel { get; }

        public static FileStorageStartupContext CreateWithLegacyTaskOutsideGit()
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                $"SingleViewStartup_{Guid.NewGuid():N}");
            var tasksPath = Path.Combine(rootPath, "Tasks");
            Directory.CreateDirectory(tasksPath);
            WriteLegacyTask(tasksPath, "legacy-task");

            var configPath = Path.Combine(rootPath, "settings.json");
            File.WriteAllText(configPath, "{}");

            IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(configPath, reloadOnChange: false);
            configuration.GetSection("TaskStorage").GetSection(nameof(TaskStorageSettings.Path)).Set(tasksPath);

            var notificationManager = new NotificationManagerWrapperMock();
            var fileStorage = new FileStorage(tasksPath, watcher: false, notificationManager);
            var taskStorage = new UnifiedTaskStorage(new TaskTreeManager(fileStorage));
            var settings = new SettingsViewModel(configuration)
            {
                TaskStoragePath = tasksPath
            };
            var mainWindowViewModel = new MainWindowViewModel(
                new AppNameDefinitionService(),
                notificationManager,
                configuration,
                () => taskStorage,
                settings);

            TaskItemViewModel.NotificationManagerInstance = notificationManager;
            TaskItemViewModel.MainWindowInstance = mainWindowViewModel;

            return new FileStorageStartupContext(
                configPath,
                tasksPath,
                notificationManager,
                taskStorage,
                mainWindowViewModel,
                configuration as IDisposable);
        }

        public void Dispose()
        {
            MainWindowViewModel.Dispose();
            TaskStorage.Dispose();
            _configurationDisposable?.Dispose();

            var rootPath = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrWhiteSpace(rootPath) && Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }

        private static void WriteLegacyTask(string tasksPath, string id)
        {
            var createdAt = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
            var json = $$"""
            {
              "Id": "{{id}}",
              "UserId": "startup-test",
              "Title": "{{id}}",
              "Description": "",
              "IsCompleted": false,
              "IsCanBeCompleted": true,
              "CreatedDateTime": "{{createdAt:O}}",
              "UpdatedDateTime": null,
              "UnlockedDateTime": null,
              "CompletedDateTime": null,
              "ArchiveDateTime": null,
              "PlannedBeginDateTime": null,
              "PlannedEndDateTime": null,
              "PlannedDuration": null,
              "ContainsTasks": [],
              "ParentTasks": [],
              "BlocksTasks": [],
              "BlockedByTasks": [],
              "Repeater": null,
              "Importance": 0,
              "Wanted": false,
              "Version": 1
            }
            """;

            File.WriteAllText(Path.Combine(tasksPath, id), json);
        }
    }

    private sealed class CountingStorage : IStorage
    {
        private readonly TaskItem _task = new()
        {
            Id = "single-view-startup-task",
            Title = "Single view startup task",
            CreatedDateTime = DateTimeOffset.UtcNow,
            UpdatedDateTime = DateTimeOffset.UtcNow,
            ContainsTasks = [],
            ParentTasks = [],
            BlocksTasks = [],
            BlockedByTasks = [],
            IsCompleted = false,
            IsCanBeCompleted = true,
        };

        public int ConnectCallCount { get; private set; }

        public int GetAllCallCount { get; private set; }

        public event EventHandler<TaskStorageUpdateEventArgs> Updating
        {
            add { }
            remove { }
        }

        public event Action<Exception?>? OnConnectionError
        {
            add { }
            remove { }
        }

        public Task<bool> Connect()
        {
            ConnectCallCount++;
            return Task.FromResult(true);
        }

        public Task Disconnect()
        {
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<TaskItem> GetAll()
        {
            GetAllCallCount++;
            await Task.Yield();
            yield return Clone(_task);
        }

        public Task BulkInsert(IEnumerable<TaskItem> taskItems)
        {
            return Task.CompletedTask;
        }

        public Task<TaskItem?> Load(string itemId)
        {
            return Task.FromResult<TaskItem?>(itemId == _task.Id ? Clone(_task) : null);
        }

        public Task<bool> Remove(string itemId)
        {
            return Task.FromResult(false);
        }

        public Task<TaskItem> Save(TaskItem item)
        {
            return Task.FromResult(item);
        }

        private static TaskItem Clone(TaskItem task)
        {
            return new TaskItem
            {
                Id = task.Id,
                Title = task.Title,
                CreatedDateTime = task.CreatedDateTime,
                UpdatedDateTime = task.UpdatedDateTime,
                ContainsTasks = new List<string>(task.ContainsTasks ?? []),
                ParentTasks = new List<string>(task.ParentTasks ?? []),
                BlocksTasks = new List<string>(task.BlocksTasks ?? []),
                BlockedByTasks = new List<string>(task.BlockedByTasks ?? []),
                IsCompleted = task.IsCompleted,
                IsCanBeCompleted = task.IsCanBeCompleted,
            };
        }
    }

    private sealed class CountingApplicationUpdateService : IApplicationUpdateService
    {
        public bool IsSupported => true;

        public string CurrentVersion => "1.0.0";

        public ApplicationUpdateInfo? PendingUpdate => null;

        public int CheckCalls { get; private set; }

        public Task<ApplicationUpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        {
            CheckCalls++;
            return Task.FromResult<ApplicationUpdateInfo?>(null);
        }

        public Task DownloadUpdateAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void ApplyUpdateAndRestart()
        {
        }
    }
}
