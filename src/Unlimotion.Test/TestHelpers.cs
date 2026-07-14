using System;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using KellermanSoftware.CompareNetObjects;
using ReactiveUI;
using Unlimotion.Domain;
using Unlimotion.ViewModel;

namespace Unlimotion.Test
{
    public static class TestHelpers
    {
        private static readonly JsonSerializerOptions StorageJsonSerializerOptions = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        public static TaskItemViewModel SetCurrentTask(MainWindowViewModel viewModel, string taskId)
        {
            var task = GetTask(viewModel, taskId);
            viewModel.CurrentTaskItem = task;
            return task;
        }

        public static async Task ActionNotCreateItems(Action action,
            ITaskStorage taskRepository, int changeCount = 0)
        {
            var taskCountBefore = taskRepository.Tasks.Count;
            action.Invoke();
            await WaitThrottleTime();
            await WaitForPendingSavesAsync(taskRepository);
            await Assert.That(taskRepository.Tasks.Count).IsEqualTo(taskCountBefore + changeCount);
        }

        public static async Task ActionNotCreateItemsAsync(Func<Task> action,
            ITaskStorage taskRepository, int changeCount = 0)
        {
            var taskCountBefore = taskRepository.Tasks.Count;
            await action.Invoke();
            await WaitThrottleTime();
            await WaitForPendingSavesAsync(taskRepository);
            await Assert.That(taskRepository.Tasks.Count).IsEqualTo(taskCountBefore + changeCount);
        }

        public static async Task<TaskItemViewModel> CreateAndReturnNewTaskItem(Action action,
            ITaskStorage taskRepository,
            int expectedNewTasks = 1)
        {
            var taskCountBefore = taskRepository.Tasks.Count;
            action.Invoke();
            await Assert.That(await WaitUntilAsync(
                    () => taskRepository.Tasks.Count == taskCountBefore + expectedNewTasks,
                    TimeSpan.FromSeconds(5)))
                .IsTrue();
            await WaitForPendingSavesAsync(taskRepository);
            await Assert.That(taskRepository.Tasks.Count).IsEqualTo(taskCountBefore + expectedNewTasks);
            return taskRepository.Tasks.Items.OrderBy(m => m.CreatedDateTime).Last();
        }

        public static async Task<TaskItemViewModel> CreateAndReturnNewTaskItemAsync(Func<Task> action,
            ITaskStorage taskRepository,
            int expectedNewTasks = 1)
        {
            var taskCountBefore = taskRepository.Tasks.Count;
            await action.Invoke();
            await Assert.That(await WaitUntilAsync(
                    () => taskRepository.Tasks.Count == taskCountBefore + expectedNewTasks,
                    TimeSpan.FromSeconds(5)))
                .IsTrue();
            await WaitForPendingSavesAsync(taskRepository);
            await Assert.That(taskRepository.Tasks.Count).IsEqualTo(taskCountBefore + expectedNewTasks);
            return taskRepository.Tasks.Items.OrderBy(m => m.CreatedDateTime).Last();
        }

        public static async Task<TaskItemViewModel> CreateAndReturnNewTaskItem(ICommand command,
            ITaskStorage taskRepository,
            int expectedNewTasks = 1)
        {
            var taskCountBefore = taskRepository.Tasks.Count;
            var commandCompletion = ExecuteCommandAsync(command);

            await Assert.That(await WaitUntilAsync(
                    () => taskRepository.Tasks.Count == taskCountBefore + expectedNewTasks,
                    TimeSpan.FromSeconds(5)))
                .IsTrue();
            await commandCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForPendingSavesAsync(taskRepository);
            await Assert.That(taskRepository.Tasks.Count).IsEqualTo(taskCountBefore + expectedNewTasks);
            return taskRepository.Tasks.Items.OrderBy(m => m.CreatedDateTime).Last();
        }

        private static Task ExecuteCommandAsync(ICommand command)
        {
            if (command is ReactiveCommand<Unit, Unit> parameterlessCommand)
            {
                return parameterlessCommand.Execute().ToTask();
            }

            if (command is ReactiveCommand<bool, Unit> booleanCommand)
            {
                return booleanCommand.Execute(false).ToTask();
            }

            command.Execute(null);
            return Task.CompletedTask;
        }

        public static async Task WaitThrottleTime()
        {
            var sleepTime = TaskItemViewModel.DefaultThrottleTime.Add(TimeSpan.FromSeconds(0.1));
            await Task.Delay(sleepTime);
        }

        public static Task WaitForPendingSavesAsync(ITaskStorage taskRepository) =>
            Task.WhenAll(taskRepository.Tasks.Items.Select(task => task.WaitForPendingSavesAsync()));

        public static async Task<bool> WaitUntilAsync(
            Func<bool> predicate,
            TimeSpan timeout,
            TimeSpan? pollInterval = null)
        {
            var delay = pollInterval ?? TimeSpan.FromMilliseconds(20);
            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                if (predicate())
                {
                    return true;
                }

                await Task.Delay(delay);
            }

            return predicate();
        }

        public static TaskItemViewModel GetTask(MainWindowViewModel viewModel, string taskId)
        {
            return GetTask(viewModel, taskId, assertIfMissing: true)!;
        }

        public static TaskItemViewModel? GetTask(MainWindowViewModel viewModel, string taskId, bool assertIfMissing)
        {
            if (viewModel.taskRepository != null)
            {
                var result = viewModel.taskRepository.Tasks.Lookup(taskId);
                if (result.HasValue)
                {
                    return result.Value;
                }
            }

            if (assertIfMissing)
                throw new Exception($"Task with id {taskId} not found in repository.");

            return null;
        }

        public static TaskItem? GetStorageTaskItem(string folderPath, string taskId)
        {
            var path = Path.Combine(folderPath, taskId);
            if (!File.Exists(path)) return null;

            return ReadStorageTaskItemWithRetry(path);
        }

        public static ComparisonResult CompareStorageVersions(TaskItem before, TaskItem after)
        {
            var compareLogic = new CompareLogic
            {
                Config =
                {
                    MaxDifferences = 10
                }
            };
            return compareLogic.Compare(before, after);
        }

        public static async Task ShouldHaveTitleAndAUpdatedDateChanged(ComparisonResult result, string oldTitle, string newTitle)
        {
            var names = result.Differences.Select(d => d.PropertyName).ToList();
            var titleDiff = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(TaskItem.Title));
            var updatedDateDiff = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(TaskItem.UpdatedDateTime));
            await Assert.That(titleDiff).IsNotNull();
            await Assert.That(updatedDateDiff).IsNotNull();
            await Assert.That((titleDiff.Object1 ?? "").ToString()).IsEqualTo(oldTitle);
            await Assert.That((titleDiff.Object2 ?? "").ToString()).IsEqualTo(newTitle);
            await Assert.That(updatedDateDiff.Object1).IsNotEqualTo(updatedDateDiff.Object2);
        }

        public static async Task ShouldContainOnlyDifference(ComparisonResult result, string propertyName)
        {
            await Assert.That(result.Differences.Count(d => d.PropertyName == propertyName)).IsEqualTo(1);
        }

        public static async Task AssertTaskExistsOnDisk(string folderPath, string taskId)
        {
            var path = Path.Combine(folderPath, taskId);
            await Assert.That(File.Exists(path)).IsTrue().Because($"Task file not found: {path}");
        }

        private static TaskItem? ReadStorageTaskItemWithRetry(string path, int attempts = 5)
        {
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                try
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<TaskItem>(json, StorageJsonSerializerOptions);
                }
                catch (IOException) when (attempt < attempts - 1)
                {
                    Thread.Sleep(50);
                }
            }

            var finalJson = File.ReadAllText(path);
            return JsonSerializer.Deserialize<TaskItem>(finalJson, StorageJsonSerializerOptions);
        }
    }
}
