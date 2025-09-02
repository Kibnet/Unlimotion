using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json;
using System.Windows.Input;
using FluentAssertions;
using KellermanSoftware.CompareNetObjects;
using Unlimotion.ViewModel;
using Unlimotion.Domain;
using System.Threading.Tasks;

namespace Unlimotion.Test
{
    public static class TestHelpers
    {
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
            taskRepository.Tasks.Count.Should().Be(taskCountBefore + changeCount);
        }

        public static async Task<TaskItemViewModel> CreateAndReturnNewTaskItem(Action action,
            ITaskStorage taskRepository,
            int expectedNewTasks = 1)
        {
            var taskCountBefore = taskRepository.Tasks.Count;
            action.Invoke();
            await WaitThrottleTime();
            taskRepository.Tasks.Count.Should().Be(taskCountBefore + expectedNewTasks);
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
            var sleepTime = TaskItemViewModel.DefaultThrottleTime.Add(TimeSpan.FromSeconds(1.5));
            await Task.Delay(sleepTime);
        }

        public static TaskItemViewModel GetTask(MainWindowViewModel viewModel, string taskId, bool assertIfMissing = true)
        {
            var result = viewModel.taskRepository.Tasks.Lookup(taskId);
            if (result.HasValue)
            {
                return result.Value;
            }

            if (assertIfMissing)
                throw new Exception($"Task with id {taskId} not found in repository.");

            return null;
        }

        public static TaskItem GetStorageTaskItem(string folderPath, string taskId)
        {
            var path = Path.Combine(folderPath, taskId);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<TaskItem>(json);
        }

        public static ComparisonResult CompareStorageVersions(TaskItem before, TaskItem after)
        {
            var compareLogic = new CompareLogic();
            compareLogic.Config.MaxDifferences = 10;
            return compareLogic.Compare(before, after);
        }

        public static void ShouldHaveOnlyTitleChanged(ComparisonResult result, string oldTitle, string newTitle)
        {
            result.Differences.Should().HaveCount(1);
            result.Differences[0].PropertyName.Should().Be(nameof(TaskItem.Title));
            result.DifferencesString.Should().StartWith($"\r\nBegin Differences (1 differences):\r\nTypes [String,String], Item Expected.Title != Actual.Title, Values ({oldTitle},{newTitle})");
        }

        public static void ShouldContainOnlyDifference(ComparisonResult result, string propertyName)
        {
            result.Differences.Should().ContainSingle(d => d.PropertyName == propertyName);
        }

        public static void AssertTaskLink(TaskItem task, string expectedId)
        {
            task.ContainsTasks.Should().Contain(expectedId);
        }

        public static async Task<TaskItemViewModel> CreateAndSetCurrent(MainWindowViewModel viewModel, Action action, ITaskStorage repository, int expectedNewTasks = 1)
        {
            var created = await CreateAndReturnNewTaskItem(action, repository, expectedNewTasks);
            viewModel.CurrentTaskItem = created;
            return created;
        }

        public static void AssertTaskExistsOnDisk(string folderPath, string taskId)
        {
            var path = Path.Combine(folderPath, taskId);
            File.Exists(path).Should().BeTrue($"Task file not found: {path}");
        }

        public static void AssertTaskNotExistsOnDisk(string folderPath, string taskId)
        {
            var path = Path.Combine(folderPath, taskId);
            File.Exists(path).Should().BeFalse($"Task file still exists: {path}");
        }
    }
}
