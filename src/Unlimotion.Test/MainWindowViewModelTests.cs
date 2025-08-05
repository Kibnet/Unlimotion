using FluentAssertions;
using KellermanSoftware.CompareNetObjects;
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
        CompareLogic compareLogic;
        ITaskRepository taskRepository;

        public MainWindowViewModelTests()
        {
            compareLogic = new CompareLogic();
            compareLogic.Config.MaxDifferences = 10;
            TaskItemViewModel.DefaultThrottleTime = TimeSpan.FromMilliseconds(10);
            this.fixture = new MainWindowViewModelFixture();
            taskRepository = fixture.MainWindowViewModelTest.taskRepository;
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
            //Запоминаем сколько задач было
            var taskCount = taskRepository.Tasks.Count;

            //Нажимаем кнопку создать задачу
            fixture.MainWindowViewModelTest.Create.Execute(null);

            //Assert
            //Проверяем что создалась ровно 1 задача
            Assert.Equal(taskCount + 1, taskRepository.Tasks.Count);

            //Находим вновь созданную задачу в репозитории
            var newTaskItemViewModel = fixture.MainWindowViewModelTest.CurrentTaskItem;
            Assert.NotNull(newTaskItemViewModel);
            Assert.True(newTaskItemViewModel.Parents.Count == 0);

            //Проверяем, что задача сохранена в файле
            var taskItem = GetStorageTaskItem(newTaskItemViewModel.Id);
            Assert.NotNull(taskItem);
        }

        /// <summary>
        /// Переименование задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task RenameTask_Success()
        {
            //Берем задачу из файла
            var rootTaskItemBeforeTest = GetStorageTaskItem(MainWindowViewModelFixture.RootTask1Id);
            var renameTask = GetTask(MainWindowViewModelFixture.RootTask1Id);
            renameTask.Title = "Changed task title";
            WaitThrottleTime();

            //Assert
            var rootTaskItemAfterTest = GetStorageTaskItem(renameTask.Id);
            Assert.Equal(renameTask.Title, rootTaskItemAfterTest.Title);
            //Сравниваем старую и новую версию корневой задачи
            var result = compareLogic.Compare(rootTaskItemBeforeTest, rootTaskItemAfterTest);
            //Должно быть только одно различие в названии
            Assert.StartsWith("\r\nBegin Differences (1 differences):\r\nTypes [String,String], Item Expected.Title != Actual.Title, Values (Root Task 1,Changed task title)",
                result.DifferencesString);
        }

        /// <summary>
        /// Создание вложенной задачи
        /// </summary>
        /// <returns></returns>
        [Theory]
        [InlineData(MainWindowViewModelFixture.RootTask1Id)]
        [InlineData(MainWindowViewModelFixture.RootTask2Id)]
        public Task CreateInnerTask_Success(string taskId)
        {
            // Берем задачу и делаем ее выбранной
            var taskViewModel = GetTask(taskId);
            fixture.MainWindowViewModelTest.CurrentTaskItem = taskViewModel;
            //Запоминаем сколько задач было
            var taskCount = taskRepository.Tasks.Count;
            var taskItemBeforeTest = GetStorageTaskItem(taskId);

            fixture.MainWindowViewModelTest.CreateInner.Execute(null);
            WaitThrottleTime();

            //Assert
            //Проверяем что создалась ровно 1 задача
            Assert.Equal(taskCount + 1, taskRepository.Tasks.Count);

            //Находим вновь созданную задачу в репозитории
            var newTaskItemViewModel = taskRepository.Tasks.Items.OrderBy(model => model.CreatedDateTime).Last();
            var taskItemAfterTest = GetStorageTaskItem(taskViewModel.Id);
            Assert.Contains(newTaskItemViewModel.Id, taskItemAfterTest.ContainsTasks);

            // Сравниваем старую и новую версию задачи
            var result = compareLogic.Compare(taskItemBeforeTest, taskItemAfterTest);

            //Должно быть различие в количестве ContainsTasks
            var containsTasksDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(taskItemAfterTest.ContainsTasks));
            Assert.NotNull(containsTasksDifference);
            Assert.Equal($"Types [List`1,List`1], Item Expected.ContainsTasks.Count != Actual.ContainsTasks.Count, Values ({taskItemBeforeTest.ContainsTasks.Count},{taskItemAfterTest.ContainsTasks.Count})",
                containsTasksDifference.ToString());

            //Должно быть различие UnlockedDateTime, если у задачи изначально не было внутренних подзадач
            if (!taskItemBeforeTest.ContainsTasks.Any())
            {
                var unlockedDateTimeDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(taskItemAfterTest.UnlockedDateTime));
                Assert.NotNull(unlockedDateTimeDifference);
                Assert.StartsWith("Types [DateTimeOffset,null], Item Expected.UnlockedDateTime != Actual.UnlockedDateTime",
                    unlockedDateTimeDifference.ToString());
            }

            //Теперь у целевой задачи есть невыполненные задачи внутри. Она заблокирована
            Assert.Null(taskItemAfterTest.UnlockedDateTime);
            //Должна добавиться одна задача
            Assert.Equal(taskItemBeforeTest.ContainsTasks.Count + 1, taskItemAfterTest.ContainsTasks.Count);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Создание вложенной задачи без выбранной текущей
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task CreateInnerTask_Fail()
        {
            // Берем корневую задачу и делаем ее выбранной
            var rootTaskViewModel = GetTask(MainWindowViewModelFixture.RootTask1Id);
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
        [Theory]
        [InlineData(MainWindowViewModelFixture.RootTask1Id)]
        [InlineData(MainWindowViewModelFixture.SubTask22Id)]
        public Task CreateSiblingTask_Success(string taskId)
        {
            // Берем задачу и делаем ее выбранной
            var taskViewModel = GetTask(taskId);
            fixture.MainWindowViewModelTest.CurrentTaskItem = taskViewModel;
            //Запоминаем сколько задач было
            var taskCount = taskRepository.Tasks.Count;

            fixture.MainWindowViewModelTest.CreateSibling.Execute(null);

            //Assert
            //Проверяем что создалась ровно 1 задача
            Assert.Equal(taskCount + 1, taskRepository.Tasks.Count);

            //Находим вновь созданную задачу в репозитории
            var newTaskItemViewModel = taskRepository.Tasks.Items.OrderBy(model => model.CreatedDateTime).Last();

            Assert.True(newTaskItemViewModel.Parents.Count <= taskViewModel.Parents.Count);
            //Если выбранная задача была подзадачей
            if (taskViewModel.Parents.Count > 0)
            {
                var newTaskParentId = newTaskItemViewModel.Parents.FirstOrDefault();
                Assert.NotNull(newTaskParentId);
                Assert.Contains(newTaskParentId, taskViewModel.Parents);
                //Берем задачу предка новой задачи из файла 
                var ParentTaskItem = GetStorageTaskItem(newTaskParentId);
                Assert.Contains(newTaskItemViewModel.Id, ParentTaskItem.ContainsTasks);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Создание зависимой соседней задачи
        /// </summary>
        /// <returns></returns>
        [Theory]
        [InlineData(MainWindowViewModelFixture.RootTask1Id)]
        [InlineData(MainWindowViewModelFixture.SubTask22Id)]
        public Task CreateBlockedSibling_Success(string taskId)
        {
            //Запоминаем сколько задач было
            var taskCount = taskRepository.Tasks.Count;

            // Берем корневую задачу и делаем ее выбранной
            var taskViewModel = GetTask(taskId);
            var taskItemBeforeTest = GetStorageTaskItem(taskViewModel.Id);
            fixture.MainWindowViewModelTest.CurrentTaskItem = taskViewModel;
            fixture.MainWindowViewModelTest.CreateBlockedSibling.Execute(null);
            //Assert
            //Проверяем что создалась ровно 1 задача
            Assert.Equal(taskCount + 1, taskRepository.Tasks.Count);

            //Находим вновь созданную задачу в репозитории
            var newTaskItemViewModel = taskRepository.Tasks.Items.OrderBy(model => model.CreatedDateTime).Last();

            Assert.True(newTaskItemViewModel.Parents.Count <= taskViewModel.Parents.Count);
            if (taskViewModel.Parents.Count > 0)
            {
                Assert.Contains(newTaskItemViewModel.Parents.FirstOrDefault(), taskViewModel.Parents);
            }
            //Загружаем новую задачу из файла
            var newTaskItem = GetStorageTaskItem(newTaskItemViewModel.Id);
            //Проверяем что файл с правильным ID
            Assert.Contains(newTaskItemViewModel.Id, newTaskItem.Id);
            //Загружаем корневую задачу из файла
            var taskItemAfterTest = GetStorageTaskItem(taskViewModel.Id);
            //Сравниваем старую и новую версию корневой задачи
            var result = compareLogic.Compare(taskItemBeforeTest, taskItemAfterTest);
            //Должно быть одно различие в количестве BlocksTasks
            Assert.StartsWith("\r\nBegin Differences (1 differences):\r\nTypes [List`1,List`1], Item Expected.BlocksTasks.Count != Actual.BlocksTasks.Count, Values (0,1)", result.DifferencesString);
            //Новая задача должна быть в Blocks во вьюмодели корневой задачи
            Assert.Contains(newTaskItemViewModel.Id, taskViewModel.Blocks);
            //Новая задача должна быть в BlocksTasks в файле корневой задачи
            Assert.Contains(newTaskItemViewModel.Id, taskItemAfterTest.BlocksTasks);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Перемещение задачи в другую родительскую задачу
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task MovingToRootTask_Success()
        {
            // Берем внутреннюю задачу
            var subTask22 = GetTask(MainWindowViewModelFixture.SubTask22Id);
            var rootTask2 = GetTask(MainWindowViewModelFixture.RootTask2Id);
            // Берем корневую задачу, куда перемещаем задачу
            var destinationRootTask = GetTask(MainWindowViewModelFixture.RootTask1Id);
            subTask22.MoveInto(destinationRootTask, rootTask2);

            //Assert
            var destinationTaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTask1Id);
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
            // Берем внутреннюю задачу
            var subTask22 = GetTask(MainWindowViewModelFixture.SubTask22Id);
            // Берем корневую задачу, куда добавляем ссылку на задачу
            var destinationRootTask = GetTask(MainWindowViewModelFixture.RootTask1Id);
            subTask22.CloneInto(destinationRootTask);
            var newTaskItemViewModel = taskRepository.Tasks.Items.Last();

            //Assert
            var destinationTaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTask1Id);
            var rootTask2TaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTask2Id);
            var newTaskItem = GetStorageTaskItem(newTaskItemViewModel.Id);

            Assert.NotNull(newTaskItem);

            Assert.NotEmpty(rootTask2TaskItem.ContainsTasks);
            Assert.Contains(subTask22.Id, rootTask2TaskItem.ContainsTasks);

            Assert.NotEmpty(destinationTaskItem.ContainsTasks);
            Assert.Contains(newTaskItem.Id, destinationTaskItem.ContainsTasks);

            destinationRootTask.Contains.Remove(newTaskItem.Id);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Перемещение ссылки в другую родительскую задачу
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task MovingTaskWithTwoParentsToRootTask_Success()
        {
            // Берем внутреннюю задачу
            var subTask22 = GetTask(MainWindowViewModelFixture.SubTask22Id);
            var rootTask3 = GetTask(MainWindowViewModelFixture.RootTask3Id);
            // Берем корневую задачу, куда перемещаем ссылку на задачу
            var destinationRootTask = GetTask(MainWindowViewModelFixture.RootTask1Id);
            subTask22.MoveInto(destinationRootTask, rootTask3);

            var newTaskItemViewModel = taskRepository.Tasks.Items.Last();

            //Assert
            var destinationTaskItem = GetStorageTaskItem(MainWindowViewModelFixture.RootTask1Id);
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
            // Берем задачу T 2.2 и делаем ее выбранной
            var subTask22ViewModel = GetTask(MainWindowViewModelFixture.SubTask22Id);
            var rootTask2ViewModel = GetTask(MainWindowViewModelFixture.RootTask2Id);

            fixture.MainWindowViewModelTest.CurrentTaskItem = subTask22ViewModel;
            var task2TaskWrapper = fixture.MainWindowViewModelTest.CurrentTaskItem.CurrentItemParents.SubTasks.Where(st => st.TaskItem.Id == MainWindowViewModelFixture.RootTask2Id).First();
            task2TaskWrapper.RemoveCommand.Execute(null);
            WaitThrottleTime();

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
            // Берем задачу T 2 и делаем ее выбранной
            var rootTask2ViewModel = GetTask(MainWindowViewModelFixture.RootTask2Id);

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
            // Берем заблокированную задачу задачей T 2 и делаем ее выбранной
            var blockedTask2ViewModel = GetTask(MainWindowViewModelFixture.BlockedTask2Id);

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
            // Берем задачу T 2 и делаем ее выбранной
            var rootTask2ViewModel = GetTask(MainWindowViewModelFixture.RootTask2Id);

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
            GetStorageTaskItem(MainWindowViewModelFixture.RootTask4Id).Should().BeNull();
            GetStorageTaskItem(MainWindowViewModelFixture.SubTask41Id).Should().NotBeNull();

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
            GetStorageTaskItem(MainWindowViewModelFixture.RootTask4Id).Should().NotBeNull();
            GetStorageTaskItem(MainWindowViewModelFixture.SubTask41Id).Should().NotBeNull();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Удаление задачи из карточки задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task CurrentTaskItemRemove_Success()
        {
            // Берем задачу T 4 и делаем ее выбранной
            var rootTask4ViewModel = GetTask(MainWindowViewModelFixture.RootTask4Id);
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
            var archiveTask11ViewModel = GetTask(MainWindowViewModelFixture.ArchiveTask11Id);
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
            var archiveTask1ViewModel = GetTask(MainWindowViewModelFixture.ArchiveTask1Id);
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
            var archivedTask11ViewModel = GetTask(MainWindowViewModelFixture.ArchivedTask11Id);
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
            var archivedTask1ViewModel = GetTask(MainWindowViewModelFixture.ArchivedTask1Id);
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
            var rootTaskViewModel = GetTask(MainWindowViewModelFixture.RootTask1Id);
            fixture.MainWindowViewModelTest.CurrentTaskItem = rootTaskViewModel;
            ((NotificationManagerWrapperMock)fixture.MainWindowViewModelTest.ManagerWrapper).AskResult = true;
            fixture.MainWindowViewModelTest.CurrentTaskItem.IsCompleted = true;
            WaitThrottleTime();

            // Assert
            var rootTask = GetStorageTaskItem(MainWindowViewModelFixture.RootTask1Id);
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
            var completedTaskViewModel = GetTask(MainWindowViewModelFixture.CompletedTaskId);
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
            var blockingTask5BeforeTest = GetStorageTaskItem(MainWindowViewModelFixture.RootTask5Id);
            var blockedTask5BeforeTest = GetStorageTaskItem(MainWindowViewModelFixture.BlockedTask5Id);

            //Берем задачу "task 5", которая блокирует задачу "blocked task 5" и делаем ее выполненной
            var blockingTaskViewModel = GetTask(MainWindowViewModelFixture.RootTask5Id);
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
            //Проверяем, что в блокирующем таске изменилось
            result = compareLogic.Compare(blockingTask5BeforeTest, rootTask5AfterTest);
            //Должно быть 2 различия поля IsCompleted и CompletedDateTime
            var isCompletedDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(rootTask5AfterTest.IsCompleted));
            var completedDateTimeDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(rootTask5AfterTest.CompletedDateTime));

            Assert.NotNull(isCompletedDifference);
            Assert.NotNull(completedDateTimeDifference);
            Assert.StartsWith("Types [Boolean,Boolean], Item Expected.IsCompleted != Actual.IsCompleted, Values (False,True)",
               isCompletedDifference.ToString());
            Assert.StartsWith("Types [null,DateTimeOffset], Item Expected.CompletedDateTime != Actual.CompletedDateTime",
               completedDateTimeDifference.ToString());
            Assert.True(rootTask5AfterTest.IsCompleted);
            Assert.NotNull(rootTask5AfterTest.CompletedDateTime);

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
            var destinationBeforeTest = GetStorageTaskItem(destinationId);
            var draggableBeforeTest = GetStorageTaskItem(draggableId);

            //Alt - Целевая задача блокирует перетаскиваемую задачу
            //Берем задачу "Blocked task 6" и с Alt перетаскиваем ее в "task 6"
            //либо берем задачу "deadlock task 6" и с Alt перетаскиваем ее в "deadlock blocked task 6"
            var draggableViewModel = GetTask(draggableId);
            var destinationTask6ViewModel = GetTask(destinationId);

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
            var draggableBeforeTest = GetStorageTaskItem(draggableId);
            var destinationBeforeTest = GetStorageTaskItem(destinationId);

            //Ctrl - Перетаскиваемая задача блокирует целевую задачу
            //Берем задачу "task 7" и с Ctrl перетаскиваем ее в "Blocked task 7"
            //или берем задачу "deadlock blocked task 7" и с Ctrl перетаскиваем ее в "deadlock task 7"
            var draggableViewModel = GetTask(draggableId);
            var destinationViewModel = GetTask(destinationId);

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

            return Task.CompletedTask;
        }

        /// <summary>
        /// Выполнение повторяемой задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task CopleteRepeatableTaskTask_Success()
        {
            //Запоминаем сколько задач было
            var taskCount = taskRepository.Tasks.Count;

            var repeateTask9BeforeTest = GetStorageTaskItem(MainWindowViewModelFixture.RepeateTask9Id);

            //Берем задачу "Repeate task 9" и делаем ее выполненной
            var repeateTask9ViewModel = GetTask(MainWindowViewModelFixture.RepeateTask9Id);
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

        /// <summary>
        /// Если у выбранной задачи нет имени, вложенные создавать нельзя
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task CreateTask_EmptyTitle_ShouldNotCreate()
        {
            var taskCountBefore = taskRepository.Tasks.Count;
            var task = new TaskItemViewModel(new TaskItem(), taskRepository); // Title is null
            fixture.MainWindowViewModelTest.CurrentTaskItem = task;
            fixture.MainWindowViewModelTest.CreateInner.Execute(null);

            taskRepository.Tasks.Count.Should().Be(taskCountBefore);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Перемещение без источника должно работать как копирование
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task MoveInto_NullSource_ShouldSucceed()
        {
            var subTask = GetTask(MainWindowViewModelFixture.SubTask22Id);
            var destination = GetTask(MainWindowViewModelFixture.RootTask1Id);
            var firstParent = subTask.ParentsTasks.First();

            subTask.MoveInto(destination, null);

            var stored = GetStorageTaskItem(destination.Id);
            stored.ContainsTasks.Should().Contain(subTask.Id);


            var stored2 = GetStorageTaskItem(firstParent.Id);
            stored2.ContainsTasks.Should().Contain(subTask.Id);
            return Task.CompletedTask;
        }

        [Fact]
        public Task RemoveTask_NoParents_ShouldDeleteFile()
        {
            var task = GetTask(MainWindowViewModelFixture.RootTask1Id);
            string path = Path.Combine(fixture.DefaultTasksFolderPath, task.Id);
            File.Exists(path).Should().BeTrue();

            task.RemoveFunc.Invoke(null);
            File.Exists(path).Should().BeFalse();
            return Task.CompletedTask;
        }

        [Fact]
        public Task RemoveTask_HasParents_ShouldNotDeleteFile()
        {
            var parent = GetTask(MainWindowViewModelFixture.RootTask2Id);
            var child = GetTask(MainWindowViewModelFixture.SubTask22Id);
            string path = Path.Combine(fixture.DefaultTasksFolderPath, child.Id);
            File.Exists(path).Should().BeTrue();

            child.RemoveFunc.Invoke(parent);
            File.Exists(path).Should().BeTrue();
            return Task.CompletedTask;
        }

        [Fact]
        public Task SelectCurrentTaskMode_SyncsCorrectly()
        {
            var task = GetTask(MainWindowViewModelFixture.RootTask1Id);
            fixture.MainWindowViewModelTest.CurrentTaskItem = task;

            fixture.MainWindowViewModelTest.AllTasksMode.Should().BeFalse();
            fixture.MainWindowViewModelTest.CurrentItem.Should().BeNull();
            fixture.MainWindowViewModelTest.AllTasksMode = true;
            fixture.MainWindowViewModelTest.CurrentItem.TaskItem.Should().Be(task);

            fixture.MainWindowViewModelTest.AllTasksMode = false;
            fixture.MainWindowViewModelTest.CurrentUnlockedItem.Should().BeNull();
            fixture.MainWindowViewModelTest.UnlockedMode = true;
            fixture.MainWindowViewModelTest.CurrentUnlockedItem.TaskItem.Should().Be(task);

            return Task.CompletedTask;
        }

        [Fact]
        public Task BlockedTask_CompletionIsPrevented()
        {
            var blocked = GetTask(MainWindowViewModelFixture.BlockedTask5Id);
            blocked.IsCanBeCompleted.Should().BeFalse();
            return Task.CompletedTask;
        }

        [Fact]
        public Task CloneInto_MultipleParents_ResultsCorrect()
        {
            var src = GetTask(MainWindowViewModelFixture.ClonedTask8Id);
            var dest1 = GetTask(MainWindowViewModelFixture.RootTask1Id);
            var dest2 = GetTask(MainWindowViewModelFixture.RootTask2Id);
            dest2.CopyInto(dest2);

            var clone = src.CloneInto(dest1);

            clone.Parents.Count.Should().Be(1);
            clone.Parents.Should().Contain(dest1.Id);
            clone.Parents.Should().NotContain(dest2.Id);
            return Task.CompletedTask;
        }

        private TaskItemViewModel? GetTask(string taskId, bool DontAssertNull = false)
        {
            var result = fixture.MainWindowViewModelTest.taskRepository.Tasks.Lookup(taskId);
            if (result.HasValue)
            {
                return result.Value;
            }

            if (!DontAssertNull)
                Assert.Fail("Задача не найдена");

            return null;
        }

        private TaskItem? GetStorageTaskItem(string taskId)
        {
            var path = Path.Combine(fixture.DefaultTasksFolderPath, taskId);
            if (!File.Exists(path))
                return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<TaskItem>(json);
        }

        private static void WaitThrottleTime()
        {
            var sleepTime = TaskItemViewModel.DefaultThrottleTime.Add(TimeSpan.FromSeconds(1));
            Thread.Sleep(sleepTime);
        }
    }
}