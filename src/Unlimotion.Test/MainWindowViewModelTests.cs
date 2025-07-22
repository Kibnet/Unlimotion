using DynamicData;
using Splat;
using System;
using System.Linq;
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
        public Task CreateRootTask()
        {
            var taskRepositoryMock = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepositoryMock);

            // Добавляем корневую задачу
            var rootTaskViewModel = new TaskItemViewModel(MainWindowViewModelFixture.RootTask, taskRepositoryMock);
            rootTaskViewModel.SaveItemCommand.Execute();
            taskRepositoryMock.Tasks.AddOrUpdate(rootTaskViewModel);
            var task = fixture.MainWindowViewModelTest.CurrentItems.FirstOrDefault(t => t.TaskItem.Id == MainWindowViewModelFixture.RootTaskId);
            Assert.NotNull(task);
            Assert.Equivalent(rootTaskViewModel, task.TaskItem);
            return Task.CompletedTask;
        }

        [Fact]
        public async Task RenameTask_Success()
        {
            var taskRepositoryMock = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepositoryMock);

            // Добавляем корневую задачу
            var rootTaskViewModel = new TaskItemViewModel(MainWindowViewModelFixture.RootTask, taskRepositoryMock);
            rootTaskViewModel.SaveItemCommand.Execute();
            taskRepositoryMock.Tasks.AddOrUpdate(rootTaskViewModel);

            var renameTask = taskRepositoryMock.Tasks.Lookup(MainWindowViewModelFixture.RootTaskId).Value;
            renameTask.PropertyChangedThrottleTimeSpanDefault = TimeSpan.FromSeconds(0.1);
            renameTask.Title = "Changed task title";

            var newTask = taskRepositoryMock.Tasks.Items.FirstOrDefault(t => t.Id == MainWindowViewModelFixture.RootTaskId);
            Assert.Equal(newTask.Title, renameTask.Title);

            Thread.Sleep(TimeSpan.FromSeconds(10));
            var newStorageTask = await taskRepositoryMock.Load(MainWindowViewModelFixture.RootTaskId);
            Assert.Equal(renameTask.Title, newStorageTask.Title);
        }

        [Fact]
        public Task CreateSiblingTask_Success()
        {
            var taskRepositoryMock = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepositoryMock);

            // Добавляем корневую задачу
            var rootTaskViewModel = new TaskItemViewModel(MainWindowViewModelFixture.RootTask, taskRepositoryMock);
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
    }
}