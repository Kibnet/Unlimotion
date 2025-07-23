using DynamicData;
using Splat;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.ViewModel;
using Xunit;

namespace Unlimotion.Test
{
    public class MainWindowViewModelTests : IClassFixture<MainWindowViewModelFixture>
    {
        MainWindowViewModelFixture fixture;
        public MainWindowViewModelTests(MainWindowViewModelFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public async Task CreateRootTask()
        {
            //Нажимаем кнопку создать задачу
            fixture.MainWindowViewModelTest.Create.Execute(null);

            var newTaskItemViewModel = fixture.MainWindowViewModelTest.CurrentTaskItem;
            newTaskItemViewModel.PropertyChangedThrottleTimeSpanDefault = TimeSpan.FromSeconds(0.1);
            newTaskItemViewModel.Title = fixture.RootTask.Title;
            newTaskItemViewModel.Description = fixture.RootTask.Description;
            Thread.Sleep(TimeSpan.FromSeconds(30));

            var taskItem = GetStorageTaskItem(newTaskItemViewModel.Id);

            Assert.Multiple(
            () =>
            {
                Assert.NotNull(taskItem);
                Assert.Equal(taskItem.Title, fixture.RootTask.Title);
                Assert.Equal(taskItem.Description, fixture.RootTask.Description);
                Assert.Equal(taskItem.Id, newTaskItemViewModel.Id);
            }
            );

            DeleteFilesFromTasksFolder();
        }

        [Fact]
        public async Task RenameTask_Success()
        {
            var taskRepositoryMock = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepositoryMock);

            // Добавляем корневую задачу
            var rootTaskViewModel = new TaskItemViewModel(fixture.RootTask, taskRepositoryMock);
            rootTaskViewModel.SaveItemCommand.Execute();
            taskRepositoryMock.Tasks.AddOrUpdate(rootTaskViewModel);

            var renameTask = taskRepositoryMock.Tasks.Lookup(MainWindowViewModelFixture.RootTaskId).Value;
            renameTask.PropertyChangedThrottleTimeSpanDefault = TimeSpan.FromSeconds(0.1);
            renameTask.Title = "Changed task title";

            var newTask = taskRepositoryMock.Tasks.Items.FirstOrDefault(t => t.Id == MainWindowViewModelFixture.RootTaskId);
            Assert.Equal(newTask.Title, renameTask.Title);

            Thread.Sleep(TimeSpan.FromSeconds(1));
            var newStorageTask = await taskRepositoryMock.Load(MainWindowViewModelFixture.RootTaskId);
            Assert.Equal(renameTask.Title, newStorageTask.Title);
        }

        [Fact]
        public Task CreateSiblingTask_Success()
        {
            var taskRepositoryMock = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepositoryMock);

            // Добавляем корневую задачу
            var rootTaskViewModel = new TaskItemViewModel(fixture.RootTask, taskRepositoryMock);
            rootTaskViewModel.SaveItemCommand.Execute();
            taskRepositoryMock.Tasks.AddOrUpdate(rootTaskViewModel);

            //Делаем ее выбранной
            fixture.MainWindowViewModelTest.CurrentTaskItem = rootTaskViewModel;

            fixture.MainWindowViewModelTest.CreateSibling.Execute(null);
            var newTaskItemViewModel = taskRepositoryMock.Tasks.Items.Last();
            Assert.NotNull(newTaskItemViewModel.Parents);
            Assert.Equal(0, newTaskItemViewModel.Parents.Count);

            var count = fixture.MainWindowViewModelTest.CurrentItems.Count;
            Assert.Equal(2, count);
            Assert.Equal(2, taskRepositoryMock.Tasks.Count);
            return Task.CompletedTask;
        }

        private TaskItem GetStorageTaskItem(string taskId)
        {
            var taskItemString = File.ReadAllText(Path.Combine(fixture.DefaultTasksFolderPath, taskId));
            return JsonSerializer.Deserialize<TaskItem>(taskItemString);
        }

        private void DeleteFilesFromTasksFolder()
        {
            DirectoryInfo tasksFolder = new DirectoryInfo(fixture.DefaultTasksFolderPath);
            foreach (FileInfo file in tasksFolder.GetFiles()) file.Delete();
            foreach (DirectoryInfo subDirectory in tasksFolder.GetDirectories()) subDirectory.Delete(true);
        }
    }
}