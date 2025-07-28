using DynamicData;
using FluentAssertions;
using Newtonsoft.Json.Linq;
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

        /// <summary>
        /// Создание задачи в корне
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task CreateRootTask()
        {
            //Нажимаем кнопку создать задачу
            fixture.MainWindowViewModelTest.Create.Execute(null);

            var newTaskItemViewModel = fixture.MainWindowViewModelTest.CurrentTaskItem;

            //Assert
            Assert.NotNull(newTaskItemViewModel);

            var taskItem = GetStorageTaskItem(newTaskItemViewModel.Id);
            Assert.NotNull(taskItem);

            DeleteTask(newTaskItemViewModel.Id);
        }

        /// <summary>
        /// Переименование задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task RenameTask_Success()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);

            var renameTask = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTaskId).Value;
            renameTask.PropertyChangedThrottleTimeSpanDefault = TimeSpan.FromSeconds(0.1);
            renameTask.Title = "Changed task title";

            Thread.Sleep(TimeSpan.FromSeconds(30));
            var taskItem = GetStorageTaskItem(renameTask.Id);
            Assert.Equal(renameTask.Title, taskItem.Title);
        }

        /// <summary>
        /// Создание вложенной задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task CreateInnerTask_Success()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);

            // Берем корневую задачу и делаем ее выбранной
            var rootTaskViewModel = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTaskId).Value;
            fixture.MainWindowViewModelTest.CurrentTaskItem = rootTaskViewModel;

            fixture.MainWindowViewModelTest.CreateInner.Execute(null);
            //Assert
            var newTaskItemViewModel = taskRepository.Tasks.Items.Last();
            var rootTaskItem = GetStorageTaskItem(rootTaskViewModel.Id);
            Assert.Contains(newTaskItemViewModel.Id, rootTaskItem.ContainsTasks);

            DeleteTask(newTaskItemViewModel.Id);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Создание вложенной задачи без выбранной текущей
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task CreateInnerTask_Fail()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);

            // Берем корневую задачу и делаем ее выбранной
            var rootTaskViewModel = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTaskId).Value;

            fixture.MainWindowViewModelTest.CreateInner.Execute(null);
            //Assert
            var rootTaskItem = GetStorageTaskItem(rootTaskViewModel.Id);
            Assert.Empty(rootTaskItem.ContainsTasks);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Создание соседней задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task CreateSiblingTask_Success()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);

            // Берем корневую задачу и делаем ее выбранной
            var rootTaskViewModel = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTaskId).Value;
            fixture.MainWindowViewModelTest.CurrentTaskItem = rootTaskViewModel;

            fixture.MainWindowViewModelTest.CreateSibling.Execute(null);

            //Assert
            var newTaskItemViewModel = taskRepository.Tasks.Items.Last();
            Assert.Empty(newTaskItemViewModel.Parents);
            Assert.Equal(rootTaskViewModel.Parents.Count, newTaskItemViewModel.Parents.Count);

            var count = fixture.MainWindowViewModelTest.CurrentItems.Count;
            Assert.Equal(2, count);
            Assert.Equal(2, taskRepository.Tasks.Count);

            DeleteTask(newTaskItemViewModel.Id);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Создание зависимой соседней задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task CreateBlockedSibling_Success()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);

            // Берем корневую задачу и делаем ее выбранной
            var rootTaskViewModel = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTaskId).Value;
            fixture.MainWindowViewModelTest.CurrentTaskItem = rootTaskViewModel;

            fixture.MainWindowViewModelTest.CreateBlockedSibling.Execute(null);

            //Assert
            var newTaskItemViewModel = taskRepository.Tasks.Items.Last();
            Assert.NotEmpty(rootTaskViewModel.Blocks);
            var newTaskItem = GetStorageTaskItem(newTaskItemViewModel.Id);
            Assert.NotEmpty(newTaskItem.BlocksTasks);

            Assert.Contains(newTaskItemViewModel.Id, rootTaskViewModel.Blocks);
            Assert.Contains(newTaskItemViewModel.Id, newTaskItem.BlocksTasks);

            DeleteTask(newTaskItemViewModel.Id);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Перемещение задачи в другую родительскую задачу
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task MovingToRootTask_Success()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);

            // Берем внутреннюю задачу
            var subTask22 = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.SubTask22Id).Value;
            var rootTask2 = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTask2Id).Value;
            // Берем корневую задачу, куда перемещаем задачу
            var distinationRootTask = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTaskId).Value;
            subTask22.MoveInto(distinationRootTask, rootTask2);

            //Assert
            var distinationTaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTaskId);
            var rootTask2TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTask2Id);
            Assert.Empty(rootTask2TaskItem.ContainsTasks);
            Assert.NotEmpty(distinationTaskItem.ContainsTasks);
            Assert.Contains(subTask22.Id, distinationTaskItem.ContainsTasks);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Создание ссылки на задачу в другой задаче
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task CloneToRootTask_Success()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);

            // Берем внутреннюю задачу
            var subTask22 = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.SubTask22Id).Value;
            // Берем корневую задачу, куда добавляем ссылку на задачу
            var distinationRootTask = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTaskId).Value;
            subTask22.CloneInto(distinationRootTask);
            var newTaskItemViewModel = taskRepository.Tasks.Items.Last();

            //Assert
            var distinationTaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTaskId);
            var rootTask2TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTask2Id);
            var newTaskItem = GetStorageTaskItem(newTaskItemViewModel.Id);

            Assert.NotNull(newTaskItem);

            Assert.NotEmpty(rootTask2TaskItem.ContainsTasks);
            Assert.Contains(subTask22.Id, rootTask2TaskItem.ContainsTasks);

            Assert.NotEmpty(distinationTaskItem.ContainsTasks);
            Assert.Contains(newTaskItem.Id, distinationTaskItem.ContainsTasks);

            distinationRootTask.Contains.Remove(newTaskItem.Id);
            DeleteTask(newTaskItem.Id);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Перемещение ссылки в другую родительскую задачу
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task MovingTaskWithTwoParentsToRootTask_Success()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);

            // Берем внутреннюю задачу
            var subTask22 = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.SubTask22Id).Value;
            var rootTask3 = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTask3Id).Value;
            // Берем корневую задачу, куда перемещаем ссылку на задачу
            var distinationRootTask = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTaskId).Value;
            subTask22.MoveInto(distinationRootTask, rootTask3);

            var newTaskItemViewModel = taskRepository.Tasks.Items.Last();

            //Assert
            var distinationTaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTaskId);
            var rootTask3TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTask3Id);
            var newTaskItem = GetStorageTaskItem(newTaskItemViewModel.Id);
            var rootTask2TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTask2Id);

            Assert.NotNull(newTaskItem);

            Assert.NotEmpty(distinationTaskItem.ContainsTasks);
            Assert.Contains(subTask22.Id, distinationTaskItem.ContainsTasks);

            //Проверка, что у задачи T 3 больше нет ссылки на T 2.2
            Assert.DoesNotContain(subTask22.Id, rootTask3TaskItem.ContainsTasks);

            //У задачи T 2 осталась ссылка на T 2.2
            Assert.NotEmpty(rootTask2TaskItem.ContainsTasks);
            Assert.Contains(subTask22.Id, rootTask2TaskItem.ContainsTasks);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Удаление родительской ссылки на задачу
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task CurrentItemParentsRemove_Success()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);

            // Берем задачу T 2.2 и делаем ее выбранной
            var subTask22ViewModel = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.SubTask22Id).Value;
            var rootTask2ViewModel = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTask2Id).Value;

            fixture.MainWindowViewModelTest.CurrentTaskItem = subTask22ViewModel;
            var task2TaskWrapper = fixture.MainWindowViewModelTest.CurrentTaskItem.CurrentItemParents.SubTasks.Where(st => st.TaskItem.Id == MainWindowViewModelFixture.RootTask2Id).First();
            task2TaskWrapper.RemoveCommand.Execute(null);

            // Assert
            var rootTask2TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTask2Id);
            Assert.DoesNotContain(subTask22ViewModel.Id, rootTask2TaskItem.ContainsTasks);
            var rootTask22TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.SubTask22Id);
            Assert.NotNull(rootTask22TaskItem);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Удаление дочерней ссылки на задачу
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task CurrentItemContainsRemove_Success()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);

            // Берем задачу T 2 и делаем ее выбранной
            var rootTask2ViewModel = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTask2Id).Value;

            fixture.MainWindowViewModelTest.CurrentTaskItem = rootTask2ViewModel;
            var task22TaskWrapper = fixture.MainWindowViewModelTest.CurrentTaskItem.CurrentItemContains.SubTasks.Where(st => st.TaskItem.Id == MainWindowViewModelFixture.SubTask22Id).First();
            task22TaskWrapper.RemoveCommand.Execute(null);

            // Assert
            var rootTask2TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTask2Id);
            var rootTask22TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.SubTask22Id);

            Assert.DoesNotContain(MainWindowViewModelFixture.SubTask22Id, rootTask2TaskItem.ContainsTasks);
            Assert.NotNull(rootTask22TaskItem);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Удаление блокирующей ссылки на задачу
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task CurrentItemBlockedByRemove_Success()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);

            // Берем заблокированную задачу задачей T 2 и делаем ее выбранной
            var blockedTask2ViewModel = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.BlockedTask2Id).Value;

            fixture.MainWindowViewModelTest.CurrentTaskItem = blockedTask2ViewModel;
            var blockedTask22TaskWrapper = fixture.MainWindowViewModelTest.CurrentTaskItem.CurrentItemBlockedBy.SubTasks.Where(st => st.TaskItem.Id == MainWindowViewModelFixture.RootTask2Id).First();
            blockedTask22TaskWrapper.RemoveCommand.Execute(null);

            // Assert
            var rootTask2TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTask2Id);
            var blockedTask2TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.BlockedTask2Id);

            Assert.DoesNotContain(MainWindowViewModelFixture.BlockedTask2Id, rootTask2TaskItem.BlocksTasks);
            Assert.NotNull(blockedTask2TaskItem);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Удаление блокируемой ссылки на задачу
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task CurrentItemBlocksRemove_Success()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);

            // Берем задачу T 2 и делаем ее выбранной
            var rootTask2ViewModel = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTask2Id).Value;

            fixture.MainWindowViewModelTest.CurrentTaskItem = rootTask2ViewModel;
            var blockedTask22TaskWrapper = fixture.MainWindowViewModelTest.CurrentTaskItem.CurrentItemBlocks.SubTasks.Where(st => st.TaskItem.Id == MainWindowViewModelFixture.BlockedTask2Id).First();
            blockedTask22TaskWrapper.RemoveCommand.Execute(null);

            // Assert
            var rootTask2TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTask2Id);
            var blockedTask2TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.BlockedTask2Id);

            Assert.DoesNotContain(MainWindowViewModelFixture.BlockedTask2Id, rootTask2TaskItem.BlocksTasks);
            Assert.NotNull(blockedTask2TaskItem);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Удаление ссылки из дерева задач
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task SubItemLinkRemoveCommand_Success()
        {
            var parentTask4 = fixture.MainWindowViewModelTest.CurrentItems.First(i => i.TaskItem.Id == MainWindowViewModelFixture.RootTask4Id);
            var removedSubTask41 = parentTask4.SubTasks.First(st => st.TaskItem.Id == MainWindowViewModelFixture.SubTask41Id);
            ((NotificationManagerWrapperMock)fixture.MainWindowViewModelTest.ManagerWrapper).AskResult = true;
            removedSubTask41.RemoveCommand.Execute(null);

            // Assert
            var subTask41TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.SubTask41Id);
            Assert.Null(subTask41TaskItem);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Отказ от удаления ссылки из дерева задач
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task SubItemRemoveCommand_Success()
        {
            var parentTask4 = fixture.MainWindowViewModelTest.CurrentItems.First(i => i.TaskItem.Id == MainWindowViewModelFixture.RootTask4Id);
            var removedSubTask41 = parentTask4.SubTasks.First(st => st.TaskItem.Id == MainWindowViewModelFixture.SubTask41Id);
            ((NotificationManagerWrapperMock)fixture.MainWindowViewModelTest.ManagerWrapper).AskResult = false;
            removedSubTask41.RemoveCommand.Execute(null);

            // Assert
            var task4TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTask4Id);
            Assert.NotNull(task4TaskItem);

            var subTask41TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.SubTask41Id);
            Assert.NotNull(subTask41TaskItem);
            Assert.Contains(MainWindowViewModelFixture.SubTask41Id, task4TaskItem.ContainsTasks);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Удаление задачи из дерева задач
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task ItemRemoveCommand_Success()
        {
            var parentTask4 = fixture.MainWindowViewModelTest.CurrentItems.First(i => i.TaskItem.Id == MainWindowViewModelFixture.RootTask4Id);
            ((NotificationManagerWrapperMock)fixture.MainWindowViewModelTest.ManagerWrapper).AskResult = true;
            parentTask4.RemoveCommand.Execute(null);

            // Assert
            var task4TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTask4Id);
            Assert.Null(task4TaskItem);
            var subTask41TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.SubTask41Id);
            Assert.NotNull(subTask41TaskItem);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Отказ от удаление задачи из дерева задач
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task CancelItemRemoveCommand_Success()
        {
            var parentTask4 = fixture.MainWindowViewModelTest.CurrentItems.First(i => i.TaskItem.Id == MainWindowViewModelFixture.RootTask4Id);
            ((NotificationManagerWrapperMock)fixture.MainWindowViewModelTest.ManagerWrapper).AskResult = false;
            parentTask4.RemoveCommand.Execute(null);

            // Assert
            var task4TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTask4Id);
            Assert.NotNull(task4TaskItem);
            var subTask41TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.SubTask41Id);
            Assert.NotNull(subTask41TaskItem);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Удаление задачи из карточки задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task CurrentTaskItemRemove_Success()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);

            // Берем задачу T 4 и делаем ее выбранной
            var rootTask4ViewModel = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTask4Id).Value;
            fixture.MainWindowViewModelTest.CurrentTaskItem = rootTask4ViewModel;
            ((NotificationManagerWrapperMock)fixture.MainWindowViewModelTest.ManagerWrapper).AskResult = true;
            fixture.MainWindowViewModelTest.Remove.Execute(null);

            // Assert
            var task4TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTask4Id);
            Assert.Null(task4TaskItem);
            var subTask41TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.SubTask41Id);
            Assert.NotNull(subTask41TaskItem);

            return Task.CompletedTask;
        }

        private TaskItem GetStorageTaskItem(string taskId)
        {
            var path = Path.Combine(fixture.DefaultTasksFolderPath, taskId);
            if (!File.Exists(path))
                return null;

            var taskItemString = File.ReadAllText(Path.Combine(fixture.DefaultTasksFolderPath, taskId));
            return JsonSerializer.Deserialize<TaskItem>(taskItemString);

        }

        private void DeleteTask(string id)
        {
            var taskPath = Path.Combine(fixture.DefaultTasksFolderPath, id);
            var fileInfo = new FileInfo(taskPath);
            fileInfo.Delete();
        }

        private void DeleteFilesFromTasksFolder()
        {
            DirectoryInfo tasksFolder = new DirectoryInfo(fixture.DefaultTasksFolderPath);
            foreach (FileInfo file in tasksFolder.GetFiles()) file.Delete();
            foreach (DirectoryInfo subDirectory in tasksFolder.GetDirectories()) subDirectory.Delete(true);
        }
    }
}