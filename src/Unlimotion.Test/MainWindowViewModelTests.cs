using FluentAssertions;
using KellermanSoftware.CompareNetObjects;
using ServiceStack;
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
    public class MainWindowViewModelTests : IDisposable
    {
        MainWindowViewModelFixture fixture;
        public MainWindowViewModelTests()
        {
            TaskItemViewModel.DefaultThrottleTime = TimeSpan.FromMilliseconds(10);
            this.fixture = new MainWindowViewModelFixture();
        }

        /// <summary>
        /// Очистка после тестов
        /// </summary>
        public void Dispose()
        {
            fixture.CleanTasks();
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
            renameTask.Title = "Changed task title";

            WaitThrottleTime();
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
            var subTask22ViewModel = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.SubTask22Id).Value;
            fixture.MainWindowViewModelTest.CurrentTaskItem = subTask22ViewModel;

            fixture.MainWindowViewModelTest.CreateSibling.Execute(null);

            //Assert
            var newTaskItemViewModel = taskRepository.Tasks.Items.Last();

            var newTaskItem = GetStorageTaskItem(newTaskItemViewModel.Id);
            var rootTask2Item = GetStorageTaskItem(MainWindowViewModelFixture.RootTask2Id);

            Assert.Contains(newTaskItem.Id, rootTask2Item.ContainsTasks);

            var RootTask2ViewModel = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTask2Id).Value;

            RootTask2ViewModel.Contains.Remove(newTaskItem.Id);
            DeleteTask(newTaskItemViewModel.Id);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Создание зависимой соседней задачи
        /// </summary>
        /// <returns></returns>
        [Theory]
        [InlineData(MainWindowViewModelFixture.RootTaskId)]
        [InlineData(MainWindowViewModelFixture.SubTask22Id)]
        public Task CreateBlockedSibling_Success(string taskId)
        {
            CompareLogic compareLogic = new CompareLogic();

            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);
            //Запоминаем сколько задач было
            var taskCount = taskRepository.Tasks.Count;

            // Берем корневую задачу и делаем ее выбранной
            var rootTaskViewModel = taskRepository.Tasks.Lookup(taskId).Value;
            var rootTaskItemBeforeTest = GetStorageTaskItem(rootTaskViewModel.Id);
            fixture.MainWindowViewModelTest.CurrentTaskItem = rootTaskViewModel;
            fixture.MainWindowViewModelTest.CreateBlockedSibling.Execute(null);
            //Assert
            //Проверяем что создалась ровно 1 задача
            Assert.Equal(taskCount + 1, taskRepository.Tasks.Count);

            //Находим вновь созданную задачу в репозитории
            var newTaskItemViewModel = taskRepository.Tasks.Items.OrderBy(model => model.CreatedDateTime).Last();

            Assert.True(newTaskItemViewModel.Parents.Count <= rootTaskViewModel.Parents.Count);
            if (rootTaskViewModel.Parents.Count > 0)
            {
                Assert.Contains(newTaskItemViewModel.Parents.FirstOrDefault(), rootTaskViewModel.Parents);
            }
            //Загружаем новую задачу из файла
            var newTaskItem = GetStorageTaskItem(newTaskItemViewModel.Id);
            //Проверяем что файл с правильным ID
            Assert.Contains(newTaskItemViewModel.Id, newTaskItem.Id);
            //Загружаем корневую задачу из файла
            var rootTaskItemAfterTest = GetStorageTaskItem(rootTaskViewModel.Id);
            //Сравниваем старую и новую версию корневой задачи
            var result = compareLogic.Compare(rootTaskItemBeforeTest, rootTaskItemAfterTest);
            //Должно быть одно различие в количестве BlocksTasks
            Assert.Equal("\r\nBegin Differences (1 differences):\r\nTypes [List`1,List`1], Item Expected.BlocksTasks.Count != Actual.BlocksTasks.Count, Values (0,1)\r\nEnd Differences (Maximum of 1 differences shown).", result.DifferencesString);
            //Новая задача должна быть в Blocks во вьюмодели корневой задачи
            Assert.Contains(newTaskItemViewModel.Id, rootTaskViewModel.Blocks);
            //Новая задача должна быть в BlocksTasks в файле корневой задачи
            Assert.Contains(newTaskItemViewModel.Id, rootTaskItemAfterTest.BlocksTasks);

            //Удаление новой задачи
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
            var destinationRootTask = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTaskId).Value;
            subTask22.MoveInto(destinationRootTask, rootTask2);

            //Assert
            var destinationTaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTaskId);
            var rootTask2TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTask2Id);
            Assert.Empty(rootTask2TaskItem.ContainsTasks);
            Assert.NotEmpty(destinationTaskItem.ContainsTasks);
            Assert.Contains(subTask22.Id, destinationTaskItem.ContainsTasks);

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
            var destinationRootTask = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTaskId).Value;
            subTask22.CloneInto(destinationRootTask);
            var newTaskItemViewModel = taskRepository.Tasks.Items.Last();

            //Assert
            var destinationTaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTaskId);
            var rootTask2TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTask2Id);
            var newTaskItem = GetStorageTaskItem(newTaskItemViewModel.Id);

            Assert.NotNull(newTaskItem);

            Assert.NotEmpty(rootTask2TaskItem.ContainsTasks);
            Assert.Contains(subTask22.Id, rootTask2TaskItem.ContainsTasks);

            Assert.NotEmpty(destinationTaskItem.ContainsTasks);
            Assert.Contains(newTaskItem.Id, destinationTaskItem.ContainsTasks);

            destinationRootTask.Contains.Remove(newTaskItem.Id);
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
            var destinationRootTask = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTaskId).Value;
            subTask22.MoveInto(destinationRootTask, rootTask3);

            var newTaskItemViewModel = taskRepository.Tasks.Items.Last();

            //Assert
            var destinationTaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTaskId);
            var rootTask3TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTask3Id);
            var newTaskItem = GetStorageTaskItem(newTaskItemViewModel.Id);
            var rootTask2TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTask2Id);

            Assert.NotNull(newTaskItem);

            Assert.NotEmpty(destinationTaskItem.ContainsTasks);
            Assert.Contains(subTask22.Id, destinationTaskItem.ContainsTasks);

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

        /// <summary>
        /// Архивация задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task ArchiveCommandWithoutContainsTask_Success()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);

            var archiveTask11ViewModel = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.ArchiveTask11Id).Value;
            fixture.MainWindowViewModelTest.CurrentTaskItem = archiveTask11ViewModel;
            ((NotificationManagerWrapperMock)fixture.MainWindowViewModelTest.ManagerWrapper).AskResult = true;
            fixture.MainWindowViewModelTest.CurrentTaskItem.ArchiveCommand.Execute(null);
            WaitThrottleTime();

            // Assert
            var archiveTask11 = GetStorageTaskItem(MainWindowViewModelFixture.ArchiveTask11Id);
            Assert.NotNull(archiveTask11.ArchiveDateTime);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Архивация задачи с подзадачами
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task ArchiveCommandWithContainsTask_Success()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);

            var archiveTask1ViewModel = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.ArchiveTask1Id).Value;
            fixture.MainWindowViewModelTest.CurrentTaskItem = archiveTask1ViewModel;
            ((NotificationManagerWrapperMock)fixture.MainWindowViewModelTest.ManagerWrapper).AskResult = true;
            fixture.MainWindowViewModelTest.CurrentTaskItem.ArchiveCommand.Execute(null);
            WaitThrottleTime();

            // Assert
            var archiveTask1Item = GetStorageTaskItem(MainWindowViewModelFixture.ArchiveTask1Id);
            var archiveTask11Item = GetStorageTaskItem(MainWindowViewModelFixture.ArchiveTask11Id);
            Assert.NotNull(archiveTask1Item.ArchiveDateTime);
            Assert.NotNull(archiveTask11Item.ArchiveDateTime);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Разархивация задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task UnArchiveCommandWithoutContainsTask_Success()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);

            var archivedTask11ViewModel = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.ArchivedTask11Id).Value;
            fixture.MainWindowViewModelTest.CurrentTaskItem = archivedTask11ViewModel;
            ((NotificationManagerWrapperMock)fixture.MainWindowViewModelTest.ManagerWrapper).AskResult = true;
            fixture.MainWindowViewModelTest.CurrentTaskItem.ArchiveCommand.Execute(null);
            WaitThrottleTime();

            // Assert
            var unArchiveTask11 = GetStorageTaskItem(MainWindowViewModelFixture.ArchivedTask11Id);
            Assert.Null(unArchiveTask11.ArchiveDateTime);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Разархивация задачи с подзадачами
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task UnArchiveCommandWithContainsTask_Success()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);

            var archivedTask1ViewModel = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.ArchivedTask1Id).Value;
            fixture.MainWindowViewModelTest.CurrentTaskItem = archivedTask1ViewModel;
            ((NotificationManagerWrapperMock)fixture.MainWindowViewModelTest.ManagerWrapper).AskResult = true;
            fixture.MainWindowViewModelTest.CurrentTaskItem.ArchiveCommand.Execute(null);
            WaitThrottleTime();

            // Assert
            var archivedTask1Item = GetStorageTaskItem(MainWindowViewModelFixture.ArchivedTask1Id);
            var archivedTask11Item = GetStorageTaskItem(MainWindowViewModelFixture.ArchivedTask11Id);
            Assert.Null(archivedTask1Item.ArchiveDateTime);
            Assert.Null(archivedTask11Item.ArchiveDateTime);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Выполнение задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task IsCompletedTask_Success()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);

            var rootTaskViewModel = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTaskId).Value;
            fixture.MainWindowViewModelTest.CurrentTaskItem = rootTaskViewModel;
            ((NotificationManagerWrapperMock)fixture.MainWindowViewModelTest.ManagerWrapper).AskResult = true;
            fixture.MainWindowViewModelTest.CurrentTaskItem.IsCompleted = true;
            WaitThrottleTime();

            // Assert
            var rootTask = GetStorageTaskItem(MainWindowViewModelFixture.RootTaskId);
            Assert.Equal(true, rootTask.IsCompleted);
            Assert.NotNull(rootTask.CompletedDateTime);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Отмена выполнения задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task CancelCompletedTask_Success()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);

            var completedTaskViewModel = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.CompletedTaskId).Value;
            fixture.MainWindowViewModelTest.CurrentTaskItem = completedTaskViewModel;
            fixture.MainWindowViewModelTest.CurrentTaskItem.IsCompleted = false;
            WaitThrottleTime();

            // Assert
            var completedTask = GetStorageTaskItem(MainWindowViewModelFixture.CompletedTaskId);
            Assert.Equal(false, completedTask.IsCompleted);
            Assert.Null(completedTask.CompletedDateTime);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Выполнение зависимой задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task CompletingBlockingTask_Success()
        {
            CompareLogic compareLogic = new CompareLogic();

            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);
            var blockingTask5BeforeTest = GetStorageTaskItem(MainWindowViewModelFixture.RootTask5Id);
            var blockedTask5BeforeTest = GetStorageTaskItem(MainWindowViewModelFixture.BlockedTask5Id);

            //Берем задачу "task 5", которая блокирует задачу "blocked task 5" и делаем ее выполненной
            var blockingTaskViewModel = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTask5Id).Value;
            fixture.MainWindowViewModelTest.CurrentTaskItem = blockingTaskViewModel;
            fixture.MainWindowViewModelTest.CurrentTaskItem.IsCompleted = true;
            WaitThrottleTime();

            // Assert
            // Загружаем задачу "blocked task 5" из файла, которая была заблокированная
            var blockedTask5AfterTest = GetStorageTaskItem(MainWindowViewModelFixture.BlockedTask5Id);
            //У нее проставлено время разблокировки
            Assert.NotNull(blockedTask5AfterTest.UnlockedDateTime);
            var result = compareLogic.Compare(blockedTask5BeforeTest, blockedTask5AfterTest);
            //Должно быть одно различие: проставлена дата разблокировки
            Assert.StartsWith("\r\nBegin Differences (1 differences):\r\nTypes [null,DateTimeOffset], Item Expected.UnlockedDateTime != Actual.UnlockedDateTime, Values ((null)",
                result.DifferencesString);

            var blockedTask5ViewModel = taskRepository.Tasks.Items.First(i => i.Id == MainWindowViewModelFixture.BlockedTask5Id);
            Assert.NotNull(blockedTask5ViewModel);
            //Теперь блокируемый таск можно выполнить
            Assert.True(blockedTask5ViewModel.IsCanBeCompleted);

            var rootTask5AfterTest = GetStorageTaskItem(MainWindowViewModelFixture.RootTask5Id);
            //Проверяем, что в блокирующем таске изменилось только поле IsCompleted
            result = compareLogic.Compare(blockingTask5BeforeTest, rootTask5AfterTest);
            Assert.StartsWith("\r\nBegin Differences (1 differences):\r\nTypes [Boolean,Boolean], Item Expected.IsCompleted != Actual.IsCompleted",
               result.DifferencesString);
            Assert.True(rootTask5AfterTest.IsCompleted);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Создание зависимой связи
        /// </summary>
        /// <returns></returns>
        [Theory]
        [InlineData(MainWindowViewModelFixture.BlockedTask6Id, MainWindowViewModelFixture.RootTask6Id)]
        [InlineData(MainWindowViewModelFixture.DeadlockTask6Id, MainWindowViewModelFixture.DeadlockBlockedTask6Id)]
        public Task AddBlokedByLinkTask_Success(string draggableId, string destinationId)
        {
            CompareLogic compareLogic = new CompareLogic();

            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);
            var destinationBeforeTest = GetStorageTaskItem(destinationId);
            var draggableBeforeTest = GetStorageTaskItem(draggableId);

            //Alt - Целевая задача блокирует перетаскиваемую задачу
            //Берем задачу "Blocked task 6" и с Alt перетаскиваем ее в "task 6"
            //либо берем задачу "deadlock task 6" и с Alt перетаскиваем ее в "deadlock blocked task 6"
            var draggableViewModel = taskRepository.Tasks.Lookup(draggableId).Value;
            var destinationTask6ViewModel = taskRepository.Tasks.Lookup(destinationId).Value;

            //Попытка создание зависимой связи, когда перетаскиваемая задача уже заблокирована целевой
            //не даем создать взаимоблокировку
            var isdestinationNotBlockedByDraggable = !destinationTask6ViewModel.BlockedBy.Contains(draggableViewModel.Id);
            if (isdestinationNotBlockedByDraggable)
            {
                draggableViewModel.BlockBy(destinationTask6ViewModel);
            }

            // Assert
            var rootTaskAfterTest = GetStorageTaskItem(destinationId);
            var blockeddraggableAfterTest = GetStorageTaskItem(draggableId);

            var result = compareLogic.Compare(destinationBeforeTest, rootTaskAfterTest);
            //Должно быть одно различие: проставлен id блокируемой задачи "Blocked taask 6"
            if (isdestinationNotBlockedByDraggable)
            {
                Assert.StartsWith("\r\nBegin Differences (1 differences):\r\nTypes [List`1,List`1], Item Expected.BlocksTasks.Count != Actual.BlocksTasks.Count",
                result.DifferencesString);

                result = compareLogic.Compare(draggableBeforeTest, blockeddraggableAfterTest);
                Assert.True(result.AreEqual);

                Assert.NotNull(rootTaskAfterTest);

                Assert.NotEmpty(rootTaskAfterTest.BlocksTasks);
                Assert.Contains(blockeddraggableAfterTest.Id, rootTaskAfterTest.BlocksTasks);
            }
            else
                Assert.True(result.AreEqual);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Создание обратной зависимой связи
        /// </summary>
        /// <returns></returns>
        [Theory]
        [InlineData(MainWindowViewModelFixture.RootTask7Id, MainWindowViewModelFixture.BlockedTask7Id)]
        [InlineData(MainWindowViewModelFixture.DeadlockBlockedTask7Id, MainWindowViewModelFixture.DeadlockTask7Id)]
        public Task AddReverseBlokedByLinkTask_Success(string draggableId, string destinationId)
        {
            CompareLogic compareLogic = new CompareLogic();

            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);
            var draggableBeforeTest = GetStorageTaskItem(draggableId);
            var destinationBeforeTest = GetStorageTaskItem(destinationId);

            //Ctrl - Перетаскиваемая задача блокирует целевую задачу
            //Берем задачу "task 7" и с Ctrl перетаскиваем ее в "Blocked task 7"
            //или берем задачу "deadlock blocked task 7" и с Ctrl перетаскиваем ее в "deadlock task 7"
            var draggableViewModel = taskRepository.Tasks.Lookup(draggableId).Value;
            var destinationViewModel = taskRepository.Tasks.Lookup(destinationId).Value;

            //не даем создать взаимоблокировку
            var isDraggableNotBlockedBydestination = !draggableViewModel.BlockedBy.Contains(destinationViewModel.Id);
            if (isDraggableNotBlockedBydestination)
            {
                destinationViewModel.BlockBy(draggableViewModel);
            }

            // Assert
            var draggableAfterTest = GetStorageTaskItem(draggableId);
            var destinationAfterTest = GetStorageTaskItem(destinationId);

            var result = compareLogic.Compare(draggableBeforeTest, draggableAfterTest);
            if (isDraggableNotBlockedBydestination)
            {
                //Должно быть одно различие: проставлен id блокируемой задачи "Blocked taask 6"
                Assert.StartsWith("\r\nBegin Differences (1 differences):\r\nTypes [List`1,List`1], Item Expected.BlocksTasks.Count != Actual.BlocksTasks.Count",
                result.DifferencesString);
                result = compareLogic.Compare(destinationBeforeTest, destinationAfterTest);
                Assert.True(result.AreEqual);

                Assert.NotNull(draggableAfterTest);

                Assert.NotEmpty(draggableAfterTest.BlocksTasks);
                Assert.Contains(destinationAfterTest.Id, draggableAfterTest.BlocksTasks);
            }
            else
                Assert.True(result.AreEqual);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Клонирование задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task CloneTask_Success()
        {
            CompareLogic compareLogic = new CompareLogic();
            compareLogic.Config.MaxDifferences = 5;

            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);
            //Запоминаем сколько задач было
            var taskCount = taskRepository.Tasks.Count;

            var destination8BeforeTest = GetStorageTaskItem(MainWindowViewModelFixture.DestinationTask8Id);
            var clonedTask8BeforeTest = GetStorageTaskItem(MainWindowViewModelFixture.ClonedTask8Id);

            //Ctrl+Shift - Клонировать перетаскиваемую задачу в целевую как подзадачу
            //Берем задачу "cloned task 8" и с Ctrl+Shift перетаскиваем ее в подзадачу "destination task 8"
            //"cloned task 8" задача содержит "clonned sub task  8.1"
            var clonedViewModel = taskRepository.Tasks.Items.FirstOrDefault(m => m.Id == MainWindowViewModelFixture.ClonedTask8Id);
            var destinationViewModel = taskRepository.Tasks.Items.FirstOrDefault(m => m.Id == MainWindowViewModelFixture.DestinationTask8Id);
            clonedViewModel.CloneInto(destinationViewModel);
            WaitThrottleTime();

            //Assert
            //Проверяем что создалась ровно 1 задача
            Assert.Equal(taskCount + 1, taskRepository.Tasks.Count);

            //Находим созданную склонированную задачу в репозитории
            var newTaskItemViewModel = taskRepository.Tasks.Items.OrderBy(model => model.CreatedDateTime).Last();
            Assert.NotNull(newTaskItemViewModel);

            //Загружаем новую задачу из файла
            var newTaskItem = GetStorageTaskItem(newTaskItemViewModel.Id);
            //Загружаем целевую задачу из файла
            var destinationTask8ItemAfterTest = GetStorageTaskItem(MainWindowViewModelFixture.DestinationTask8Id);
            //Новая задача должна быть в массиве ContainsTasks целевой задачи из файла
            Assert.NotEmpty(destinationTask8ItemAfterTest.ContainsTasks);
            Assert.Contains(newTaskItemViewModel.Id, destinationTask8ItemAfterTest.ContainsTasks);
            //Теперь у целевой задачи есть невыполненные задачи внутри. Она заблокирована
            Assert.Null(destinationTask8ItemAfterTest.UnlockedDateTime);

            //Сравниваем старую и новую версию целевой задачи
            var result = compareLogic.Compare(destination8BeforeTest, destinationTask8ItemAfterTest);

            //Должно быть 2 различия в количестве ContainsTasks и UnlockedDateTime
            var unlockedDateTimeDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(destinationTask8ItemAfterTest.UnlockedDateTime));
            var containsTasksDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(destinationTask8ItemAfterTest.ContainsTasks));

            Assert.NotNull(unlockedDateTimeDifference);
            Assert.NotNull(containsTasksDifference);
            Assert.StartsWith("Types [DateTimeOffset,null], Item Expected.UnlockedDateTime != Actual.UnlockedDateTime",
                unlockedDateTimeDifference.ToString());
            Assert.Equal("Types [List`1,List`1], Item Expected.ContainsTasks.Count != Actual.ContainsTasks.Count, Values (0,1)",
                containsTasksDifference.ToString());
            //Новая задача должна быть в Contains во вьюмодели целевой задачи
            Assert.Contains(newTaskItemViewModel.Id, destinationViewModel.Contains);

            //Берем клонируюмую задачу из файла
            var clonedTask8ItemAfterTest = GetStorageTaskItem(clonedViewModel.Id);
            //Сравниваем клонируюмую задачу с новой созданной
            result = compareLogic.Compare(clonedTask8ItemAfterTest, newTaskItem);
            //Должны отличаться id и дата создания
            Assert.StartsWith($"\r\nBegin Differences (2 differences):\r\nTypes [String,String], Item Expected.Id != Actual.Id, Values ({MainWindowViewModelFixture.ClonedTask8Id},{newTaskItemViewModel.Id})\r\nTypes [DateTimeOffset,DateTimeOffset], Item Expected.CreatedDateTime != Actual.CreatedDateTime",
                result.DifferencesString);
            Assert.Contains(MainWindowViewModelFixture.ClonnedSubTask81Id, newTaskItem.ContainsTasks);

            //Удаление новой задачи
            DeleteTask(newTaskItemViewModel.Id);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Выполнение повторяемой задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task CopleteRepeatableTaskTask_Success()
        {
            CompareLogic compareLogic = new CompareLogic();
            compareLogic.Config.MaxDifferences = 10;

            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            Assert.NotNull(taskRepository);
            //Запоминаем сколько задач было
            var taskCount = taskRepository.Tasks.Count;

            var repeateTask9BeforeTest = GetStorageTaskItem(MainWindowViewModelFixture.RepeateTask9Id);

            //Берем задачу "Repeate task 9" и делаем ее выполненной
            var repeateTask9ViewModel = taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RepeateTask9Id).Value;
            repeateTask9ViewModel.IsCompleted = true;
            WaitThrottleTime();

            //Assert
            //Проверяем что создалась ровно 1 задача
            Assert.Equal(taskCount + 1, taskRepository.Tasks.Count);

    
            //Берем задачу из файла
            var repeateTask9AfterTest = GetStorageTaskItem(MainWindowViewModelFixture.RepeateTask9Id);
            //Провереряем что исходная "Repeate task 9" задача выполнена
            Assert.Equal(true, repeateTask9AfterTest.IsCompleted);
            Assert.NotNull(repeateTask9AfterTest.CompletedDateTime);

            //Сравниваем старую и новую версию задачи
            var result = compareLogic.Compare(repeateTask9BeforeTest, repeateTask9AfterTest);

            //Должно быть только 2 различия: IsCompleted и CompletedDateTime
            var isCompletedDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(repeateTask9AfterTest.IsCompleted));
            var completedDateTimeDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(repeateTask9AfterTest.CompletedDateTime));

            Assert.NotNull(isCompletedDifference);
            Assert.NotNull(completedDateTimeDifference);
            Assert.Equal("Types [Boolean,Boolean], Item Expected.IsCompleted != Actual.IsCompleted, Values (False,True)",
                isCompletedDifference.ToString());
            Assert.StartsWith("Types [null,DateTimeOffset], Item Expected.CompletedDateTime != Actual.CompletedDateTime",
                completedDateTimeDifference.ToString());

            //Находим созданную склонированную повторяющейся "Repeate task 9" задачу в репозитории
            var newTaskItemViewModel = taskRepository.Tasks.Items.OrderBy(model => model.CreatedDateTime).Last();
            //Берем новую задачу из файла
            var newTask9 = GetStorageTaskItem(newTaskItemViewModel.Id);
            //Сравниваем ее с исходной до выполнения
            result = compareLogic.Compare(repeateTask9BeforeTest, newTask9);

            //Должно быть только 5 различия: Id, CreatedDateTime, UnlockedDateTime, PlannedBeginDateTime, PlannedEndDateTime
            var idDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(repeateTask9AfterTest.Id));
            var createdDateTimeDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(repeateTask9AfterTest.CreatedDateTime));
            var unlockedDateTimeDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(repeateTask9AfterTest.UnlockedDateTime));
            var plannedBeginDateTimeDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(repeateTask9AfterTest.PlannedBeginDateTime));
            var plannedEndDateTimeDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(repeateTask9AfterTest.PlannedEndDateTime));

            Assert.NotNull(idDifference);
            Assert.NotNull(createdDateTimeDifference);
            Assert.NotNull(unlockedDateTimeDifference);
            Assert.NotNull(plannedBeginDateTimeDifference);
            Assert.NotNull(plannedEndDateTimeDifference);

            //У двух задач должны быть одни предки во вьюмоделе
            Assert.True(repeateTask9ViewModel.Parents.Count >= newTaskItemViewModel.Parents.Count);
            if (repeateTask9ViewModel.Parents.Count > 0)
            {
                Assert.Contains(newTaskItemViewModel.Parents.FirstOrDefault(), repeateTask9ViewModel.Parents);
            }

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
        private static void WaitThrottleTime()
        {
            var sleepTime = TaskItemViewModel.DefaultThrottleTime.Add(TimeSpan.FromSeconds(5));
            Thread.Sleep(sleepTime);
        }
    }
}