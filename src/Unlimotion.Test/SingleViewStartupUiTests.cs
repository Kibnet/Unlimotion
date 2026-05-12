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
}
