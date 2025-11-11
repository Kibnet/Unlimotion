using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using KellermanSoftware.CompareNetObjects;
using Unlimotion.Domain;
using Unlimotion.ViewModel;
using Xunit;

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
            Assert.Equal(taskCountBefore + changeCount, taskRepository.Tasks.Count);
        }

        public static async Task<TaskItemViewModel> CreateAndReturnNewTaskItem(Action action,
            ITaskStorage taskRepository,
            int expectedNewTasks = 1)
        {
            var taskCountBefore = taskRepository.Tasks.Count;
            action.Invoke();
            await WaitThrottleTime();
            Assert.Equal(taskCountBefore + expectedNewTasks, taskRepository.Tasks.Count);
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

        public static void ShouldHaveOnlyTitleChanged(ComparisonResult result, string oldTitle, string newTitle)
        {
            Assert.Single(result.Differences);
            Assert.Equal(nameof(TaskItem.Title), result.Differences[0].PropertyName);
            Assert.StartsWith($"\r\nBegin Differences (1 differences):\r\nTypes [String,String], Item Expected.Title != Actual.Title, Values ({oldTitle},{newTitle})", result.DifferencesString);
        }

        public static void ShouldContainOnlyDifference(ComparisonResult result, string propertyName)
        {
            Assert.Single(result.Differences, d =>
            {
                if (d == null) throw new ArgumentNullException(nameof(d));
                return d.PropertyName == propertyName;
            });
        }

        public static void AssertTaskExistsOnDisk(string folderPath, string taskId)
        {
            var path = Path.Combine(folderPath, taskId);
            Assert.True(File.Exists(path), $"Task file not found: {path}");
        }
    }
}
