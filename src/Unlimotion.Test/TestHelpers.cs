using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using KellermanSoftware.CompareNetObjects;
using Unlimotion.Domain;
using Unlimotion.ViewModel;

namespace Unlimotion.Test
{
    public static class TestHelpers
    {
        public static TaskItemViewModel? SetCurrentTask(MainWindowViewModel viewModel, string taskId)
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
            await Assert.That(taskRepository.Tasks.Count).IsEqualTo(taskCountBefore + changeCount);
        }

        public static async Task ActionNotCreateItemsAsync(Func<Task> action,
            ITaskStorage taskRepository, int changeCount = 0)
        {
            var taskCountBefore = taskRepository.Tasks.Count;
            await action.Invoke();
            await WaitThrottleTime();
            await Assert.That(taskRepository.Tasks.Count).IsEqualTo(taskCountBefore + changeCount);
        }

        public static async Task<TaskItemViewModel> CreateAndReturnNewTaskItem(Action action,
            ITaskStorage taskRepository,
            int expectedNewTasks = 1)
        {
            var taskCountBefore = taskRepository.Tasks.Count;
            action.Invoke();
            await WaitThrottleTime();
            await Assert.That(taskRepository.Tasks.Count).IsEqualTo(taskCountBefore + expectedNewTasks);
            return taskRepository.Tasks.Items.OrderBy(m => m.CreatedDateTime).Last();
        }

        public static async Task<TaskItemViewModel> CreateAndReturnNewTaskItemAsync(Func<Task> action,
            ITaskStorage taskRepository,
            int expectedNewTasks = 1)
        {
            var taskCountBefore = taskRepository.Tasks.Count;
            await action.Invoke();
            await WaitThrottleTime();
            await Assert.That(taskRepository.Tasks.Count).IsEqualTo(taskCountBefore + expectedNewTasks);
            return taskRepository.Tasks.Items.OrderBy(m => m.CreatedDateTime).Last();
        }

        public static async Task<TaskItemViewModel> CreateAndReturnNewTaskItem(ICommand command,
            ITaskStorage taskRepository,
            int expectedNewTasks = 1)
        {
            return await CreateAndReturnNewTaskItem(() => command.Execute(null), taskRepository, expectedNewTasks);
        }

        public static async Task WaitThrottleTime()
        {
            var sleepTime = TaskItemViewModel.DefaultThrottleTime.Add(TimeSpan.FromSeconds(0.1));
            await Task.Delay(sleepTime);
        }

        public static TaskItemViewModel? GetTask(MainWindowViewModel viewModel, string taskId, bool assertIfMissing = true)
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
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<TaskItem>(json);
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
    }
}
