using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Configuration;
using Unlimotion;
using Unlimotion.Domain;
using Unlimotion.Services;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using Unlimotion.Views;
using WritableJsonConfiguration;

namespace Unlimotion.Test;

[NotInParallel]
public class MainScreenLoadingUiTests
{
    [Test]
    public async Task MainScreen_TogglesTasksLoadingOverlay_WithLoadingState()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                var view = new MainScreen { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var overlay = FindControlByAutomationId<Grid>(view, "TasksLoadingOverlay");
                var spinner = FindControlByAutomationId<Grid>(view, "TasksLoadingSpinner");

                await Assert.That(overlay.IsVisible).IsFalse();

                SetTasksLoading(vm, true);
                var becameVisible = WaitFor(() => overlay.IsVisible && spinner.IsVisible);
                await Assert.That(becameVisible).IsTrue();

                SetTasksLoading(vm, false);
                var becameHidden = WaitFor(() => !overlay.IsVisible);
                await Assert.That(becameHidden).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task MainScreen_Connect_KeepsUiResponsive_DuringBlockingInitialLoad()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            using var context = TestMainWindowContext.Create(TimeSpan.FromMilliseconds(400));
            Window? window = null;

            try
            {
                var vm = context.MainWindowViewModel;
                var view = new MainScreen { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var overlay = FindControlByAutomationId<Grid>(view, "TasksLoadingOverlay");
                var spinner = FindControlByAutomationId<Grid>(view, "TasksLoadingSpinner");
                var connectTask = vm.Connect();

                var postedCallbackRan = false;
                Dispatcher.UIThread.Post(() => postedCallbackRan = true);
                var initialAngle = GetSpinnerAngle(spinner);

                var stayedResponsive = WaitFor(
                    () => postedCallbackRan &&
                          overlay.IsVisible &&
                          !connectTask.IsCompleted &&
                          GetSpinnerAngle(spinner) != initialAngle,
                    timeoutMilliseconds: 2000);
                await Assert.That(stayedResponsive).IsTrue();

                await connectTask;

                var finishedLoading = WaitFor(
                    () => !overlay.IsVisible && vm.taskRepository?.Tasks.Count == 1,
                    timeoutMilliseconds: 2000);
                await Assert.That(finishedLoading).IsTrue();
            }
            finally
            {
                window?.Close();
            }
        }, CancellationToken.None);
    }

    private static Window CreateWindow(Control content)
    {
        return new Window
        {
            Width = 1200,
            Height = 800,
            Content = content
        };
    }

    private static T FindControlByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        var control = root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(candidate =>
                string.Equals(
                    AutomationProperties.GetAutomationId(candidate),
                    automationId,
                    StringComparison.Ordinal));

        return control ?? throw new InvalidOperationException(
            $"Control with AutomationId '{automationId}' was not found.");
    }

    private static void SetTasksLoading(MainWindowViewModel vm, bool value)
    {
        var property = typeof(MainWindowViewModel).GetProperty(
            nameof(MainWindowViewModel.IsTasksLoading),
            BindingFlags.Instance | BindingFlags.Public);
        var setter = property?.GetSetMethod(nonPublic: true);

        if (setter == null)
        {
            throw new InvalidOperationException("Could not access MainWindowViewModel.IsTasksLoading setter.");
        }

        setter.Invoke(vm, [value]);
    }

    private static bool WaitFor(Func<bool> predicate, int timeoutMilliseconds = 2000)
    {
        return SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return predicate();
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));
    }

    private static double GetSpinnerAngle(Control spinner)
    {
        return spinner.RenderTransform is RotateTransform rotateTransform
            ? rotateTransform.Angle
            : 0d;
    }

    private sealed class TestMainWindowContext : IDisposable
    {
        private readonly string _configPath;

        private TestMainWindowContext(string configPath, MainWindowViewModel mainWindowViewModel)
        {
            _configPath = configPath;
            MainWindowViewModel = mainWindowViewModel;
        }

        public MainWindowViewModel MainWindowViewModel { get; }

        public static TestMainWindowContext Create(TimeSpan loadDelay)
        {
            var configPath = Path.Combine(
                Environment.CurrentDirectory,
                $"LoadingUiTests_{Guid.NewGuid():N}.json");
            File.WriteAllText(configPath, "{}");

            IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(configPath);
            var notificationManager = new NotificationManagerWrapperMock();
            var storage = new UnifiedTaskStorage(new TaskTreeManager(new BlockingInitialLoadStorage(loadDelay)));
            var settings = new SettingsViewModel(configuration);
            var mainWindowViewModel = new MainWindowViewModel(
                new AppNameDefinitionService(),
                notificationManager,
                configuration,
                () => storage,
                settings);

            TaskItemViewModel.NotificationManagerInstance = notificationManager;
            TaskItemViewModel.MainWindowInstance = mainWindowViewModel;

            return new TestMainWindowContext(configPath, mainWindowViewModel);
        }

        public void Dispose()
        {
            if (File.Exists(_configPath))
            {
                File.Delete(_configPath);
            }
        }
    }

    private sealed class BlockingInitialLoadStorage : IStorage
    {
        private readonly Dictionary<string, TaskItem> _tasks;
        private readonly TimeSpan _loadDelay;

        public BlockingInitialLoadStorage(TimeSpan loadDelay)
        {
            _loadDelay = loadDelay;
            _tasks = new Dictionary<string, TaskItem>(StringComparer.Ordinal)
            {
                ["task-1"] = CreateTask("task-1", "Loaded task")
            };
        }

        public event EventHandler<TaskStorageUpdateEventArgs>? Updating;
        public event Action<Exception?>? OnConnectionError;

        public Task<bool> Connect()
        {
            return Task.FromResult(true);
        }

        public Task Disconnect()
        {
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<TaskItem> GetAll()
        {
            Thread.Sleep(_loadDelay);

            foreach (var task in _tasks.Values)
            {
                yield return Clone(task);
            }
        }

        public Task BulkInsert(IEnumerable<TaskItem> taskItems)
        {
            foreach (var taskItem in taskItems)
            {
                _tasks[taskItem.Id ?? Guid.NewGuid().ToString("N")] = Clone(taskItem);
            }

            return Task.CompletedTask;
        }

        public Task<TaskItem?> Load(string itemId)
        {
            return Task.FromResult(
                _tasks.TryGetValue(itemId, out var task)
                    ? Clone(task)
                    : null);
        }

        public Task<bool> Remove(string itemId)
        {
            return Task.FromResult(_tasks.Remove(itemId));
        }

        public Task<TaskItem> Save(TaskItem item)
        {
            var clone = Clone(item);
            clone.Id ??= Guid.NewGuid().ToString("N");
            item.Id = clone.Id;
            _tasks[clone.Id] = clone;
            return Task.FromResult(item);
        }

        private static TaskItem CreateTask(string id, string title)
        {
            return new TaskItem
            {
                Id = id,
                Title = title,
                CreatedDateTime = DateTimeOffset.UtcNow,
                UpdatedDateTime = DateTimeOffset.UtcNow,
                ContainsTasks = new List<string>(),
                ParentTasks = new List<string>(),
                BlocksTasks = new List<string>(),
                BlockedByTasks = new List<string>(),
                IsCompleted = false,
                IsCanBeCompleted = true,
            };
        }

        private static TaskItem Clone(TaskItem task)
        {
            return new TaskItem
            {
                Id = task.Id,
                Title = task.Title,
                Description = task.Description,
                CreatedDateTime = task.CreatedDateTime,
                UpdatedDateTime = task.UpdatedDateTime,
                CompletedDateTime = task.CompletedDateTime,
                ArchiveDateTime = task.ArchiveDateTime,
                PlannedBeginDateTime = task.PlannedBeginDateTime,
                PlannedEndDateTime = task.PlannedEndDateTime,
                PlannedDuration = task.PlannedDuration,
                UnlockedDateTime = task.UnlockedDateTime,
                Repeater = task.Repeater,
                Importance = task.Importance,
                Wanted = task.Wanted,
                Version = task.Version,
                IsCompleted = task.IsCompleted,
                IsCanBeCompleted = task.IsCanBeCompleted,
                ContainsTasks = task.ContainsTasks?.ToList() ?? new List<string>(),
                ParentTasks = task.ParentTasks?.ToList() ?? new List<string>(),
                BlocksTasks = task.BlocksTasks?.ToList() ?? new List<string>(),
                BlockedByTasks = task.BlockedByTasks?.ToList() ?? new List<string>(),
            };
        }
    }
}
