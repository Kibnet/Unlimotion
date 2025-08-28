using ExCSS;
using FluentAssertions;
using KellermanSoftware.CompareNetObjects;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.ViewModel;
using Unlimotion.Views;
using Xunit;
using Unlimotion.Domain;

namespace Unlimotion.Test
{
    public class MainWindowViewModelTests : IDisposable
    {
        private readonly MainWindowViewModelFixture fixture;
        private readonly MainWindowViewModel mainWindowVM;
        private readonly CompareLogic compareLogic;
        private readonly ITaskStorage taskRepository;

        public MainWindowViewModelTests()
        {
            compareLogic = new CompareLogic();
            compareLogic.Config.MaxDifferences = 10;
            TaskItemViewModel.DefaultThrottleTime = TimeSpan.FromMilliseconds(10);
            fixture = new MainWindowViewModelFixture();
            mainWindowVM = fixture.MainWindowViewModelTest;
            taskRepository = mainWindowVM.taskRepository;
        }

        /// <summary>
        /// Очистка после тестов
        /// </summary>
        public void Dispose() => fixture.CleanTasks();

        /// <summary>
        /// Создание задачи в корне
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task CreateRootTask_Success()
        {
            var task = await TestHelpers.CreateAndReturnNewTaskItem(mainWindowVM.Create,
                taskRepository);

            task.Parents.Should().BeEmpty();
            TestHelpers.AssertTaskExistsOnDisk(fixture.DefaultTasksFolderPath, task.Id);
        }

        /// <summary>
        /// Переименование задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task RenameTask_Success()
        {
            var task = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.RootTask1Id);
            var before = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, task.Id);

            task.Title = "Changed task title";
            await TestHelpers.WaitThrottleTime();

            var after = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, task.Id);
            var result = TestHelpers.CompareStorageVersions(before, after);
            TestHelpers.ShouldHaveOnlyTitleChanged(result, "Root Task 1", "Changed task title");
        }

        /// <summary>
        /// Создание вложенной задачи
        /// </summary>
        /// <returns></returns>
        [Theory]
        [InlineData(MainWindowViewModelFixture.RootTask1Id)]
        [InlineData(MainWindowViewModelFixture.RootTask2Id)]
        public async Task CreateInnerTask_Success(string taskId)
        {
            TestHelpers.SetCurrentTask(mainWindowVM, taskId);
            var before = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, taskId);

            var newTask = await TestHelpers.CreateAndReturnNewTaskItem(mainWindowVM.CreateInner,
                taskRepository);

            var after = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, taskId);
            var result = TestHelpers.CompareStorageVersions(before, after);

            TestHelpers.ShouldContainOnlyDifference(result, nameof(after.ContainsTasks));
            after.ContainsTasks.Should().Contain(newTask.Id);
        }

        /// <summary>
        /// Создание вложенной задачи без выбранной текущей
        /// </summary>
        /// <returns></returns>
        [Fact]
        public Task CreateInnerTask_Fail()
        {
            mainWindowVM.CurrentTaskItem = null;

            var countBefore = taskRepository.Tasks.Count;
            mainWindowVM.CreateInner.Execute(null);
            TestHelpers.WaitThrottleTime();
            taskRepository.Tasks.Count.Should().Be(countBefore);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Создание соседней задачи
        /// </summary>
        /// <returns></returns>
        [Theory]
        [InlineData(MainWindowViewModelFixture.RootTask1Id)]
        [InlineData(MainWindowViewModelFixture.SubTask22Id)]
        public async Task CreateSiblingTask_Success(string taskId)
        {
            mainWindowVM.AllTasksMode = true;
            TestHelpers.SetCurrentTask(mainWindowVM, taskId);
            var parent = TestHelpers.GetTask(mainWindowVM, taskId);

            var sibling = await TestHelpers.CreateAndReturnNewTaskItem(mainWindowVM.CreateSibling,
                taskRepository);

            if (parent.Parents.Count > 0)
            {
                var sharedParent = sibling.Parents.FirstOrDefault();
                sharedParent.Should().NotBeNull();
                parent.Parents.Should().Contain(sharedParent);
                TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, sharedParent)
                    .ContainsTasks.Should().Contain(sibling.Id);
            }
        }
        
        /// <summary>
        /// Создание зависимой соседней задачи
        /// </summary>
        /// <returns></returns>
        [Theory]
        [InlineData(MainWindowViewModelFixture.RootTask1Id)]
        [InlineData(MainWindowViewModelFixture.SubTask22Id)]
        public async Task CreateBlockedSibling_Success(string taskId)
        {
            mainWindowVM.AllTasksMode = true;
            TestHelpers.SetCurrentTask(mainWindowVM, taskId);
            var parent = TestHelpers.GetTask(mainWindowVM, taskId);
            var before = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, parent.Id);

            var blocked = await TestHelpers.CreateAndReturnNewTaskItem(mainWindowVM.CreateBlockedSibling,
                taskRepository);

            var after = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, parent.Id);
            var result = TestHelpers.CompareStorageVersions(before, after);

            TestHelpers.ShouldContainOnlyDifference(result, nameof(after.BlocksTasks));
            parent.Blocks.Should().Contain(blocked.Id);
            after.BlocksTasks.Should().Contain(blocked.Id);
        }

        /// <summary>
        /// Перемещение задачи в другую родительскую задачу
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task MovingToRootTask_Success()
        {
            var subTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.SubTask22Id);
            var from = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.RootTask2Id);
            var to = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.RootTask1Id);

            await subTask.MoveInto(to, from);

            var storedTo = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, to.Id);
            var storedFrom = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, from.Id);

            storedFrom.ContainsTasks.Should().NotContain(subTask.Id);
            storedTo.ContainsTasks.Should().Contain(subTask.Id);
        }

        /// <summary>
        /// Создание ссылки на задачу в другой задаче
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task CloneToRootTask_Success()
        {
            var subTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.SubTask22Id);
            var destination = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.RootTask1Id);
            
            var clone = await TestHelpers.CreateAndReturnNewTaskItem(async () => await subTask.CloneInto(destination),
                taskRepository);

            TestHelpers.AssertTaskExistsOnDisk(fixture.DefaultTasksFolderPath, clone.Id);

            var destItem = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, destination.Id);
            destItem.ContainsTasks.Should().Contain(clone.Id);

            destination.Contains.Should().Contain(clone.Id);
            destination.Contains.Should().NotContain(subTask.Id); // так как Clone, не Move
        }
        
        /// <summary>
        /// Перемещение ссылки в другую родительскую задачу
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task MovingTaskWithTwoParentsToRootTask_Success()
        {
            var subTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.SubTask22Id);
            var from = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.RootTask3Id);
            var to = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.RootTask1Id);

            await TestHelpers.ActionNotCreateItems(() =>
                subTask.MoveInto(to, from), 
                taskRepository);

            var toStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, to.Id);
            var fromStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, from.Id);
            var otherStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.RootTask2Id);

            toStored.ContainsTasks.Should().Contain(subTask.Id);
            fromStored.ContainsTasks.Should().NotContain(subTask.Id);
            otherStored.ContainsTasks.Should().Contain(subTask.Id);
        }

        /// <summary>
        /// Удаление родительской ссылки на задачу
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task CurrentItemParentsRemove_Success()
        {
            var rootTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.RootTask2Id);
            var subTask = TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.SubTask22Id);
            
            var rootTaskWrapper = mainWindowVM.CurrentTaskItem
                .CurrentItemParents
                .SubTasks
                .First(st => st.TaskItem.Id == rootTask.Id);
            await TestHelpers.ActionNotCreateItems(() => rootTaskWrapper.RemoveCommand.Execute(null), taskRepository);
            
            var stored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, rootTask.Id);
            stored.ContainsTasks.Should().NotContain(subTask.Id);
        }

        /// <summary>
        /// Удаление дочерней ссылки на задачу
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task CurrentItemContainsRemove_Success()
        {
            var rootTask = TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.RootTask2Id);
            var subTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.SubTask22Id);

            var wrapper = mainWindowVM.CurrentTaskItem
                .CurrentItemContains
                .SubTasks
                .First(st => st.TaskItem.Id == subTask.Id);
            
            await TestHelpers.ActionNotCreateItems(() => wrapper.RemoveCommand.Execute(null), taskRepository);

            var stored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, rootTask.Id);
            stored.ContainsTasks.Should().NotContain(subTask.Id);
        }


        /// <summary>
        /// Удаление блокирующей ссылки на задачу
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task CurrentItemBlockedByRemove_Success()
        {
            var rootTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.RootTask2Id);
            var blocked = TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.BlockedTask2Id);
            
            var wrapper = mainWindowVM.CurrentTaskItem
                .CurrentItemBlockedBy
                .SubTasks
                .First(st => st.TaskItem.Id == rootTask.Id);

            await TestHelpers.ActionNotCreateItems(() => wrapper.RemoveCommand.Execute(null), taskRepository);

            var blockerStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, rootTask.Id);
            blockerStored.BlocksTasks.Should().NotContain(blocked.Id);
        }
        
        /// <summary>
        /// Удаление блокируемой ссылки на задачу
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task CurrentItemBlocksRemove_Success()
        {
            var rootTask = TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.RootTask2Id);
            var blocked = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.BlockedTask2Id);
            
            var wrapper = mainWindowVM.CurrentTaskItem
                .CurrentItemBlocks
                .SubTasks
                .First(st => st.TaskItem.Id == blocked.Id);

            await TestHelpers.ActionNotCreateItems(() => wrapper.RemoveCommand.Execute(null), taskRepository);

            var rootStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, rootTask.Id);
            rootStored.BlocksTasks.Should().NotContain(blocked.Id);
        }

        /// <summary>
        /// Удаление ссылки из дерева задач
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task SubItemLinkRemoveCommand_Success()
        {
            var rootWrapper = mainWindowVM.CurrentItems
                .First(i => i.TaskItem.Id == MainWindowViewModelFixture.RootTask4Id);

            var subWrapper = rootWrapper.SubTasks
                .First(st => st.TaskItem.Id == MainWindowViewModelFixture.SubTask41Id);

            ((NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper).AskResult = true;

            await TestHelpers.ActionNotCreateItems(() => subWrapper.RemoveCommand.Execute(null), taskRepository, -1);

            var subStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.SubTask41Id);
            subStored.Should().BeNull();
        }

        /// <summary>
        /// Отказ от удаления ссылки из дерева задач
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task SubItemRemoveCommand_Success()
        {
            var rootWrapper = mainWindowVM.CurrentItems
                .First(i => i.TaskItem.Id == MainWindowViewModelFixture.RootTask4Id);

            var subWrapper = rootWrapper.SubTasks
                .First(st => st.TaskItem.Id == MainWindowViewModelFixture.SubTask41Id);

            ((NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper).AskResult = false;
            
            await TestHelpers.ActionNotCreateItems(() => subWrapper.RemoveCommand.Execute(null), taskRepository, 0);
            
            var rootStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.RootTask4Id);
            var subStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.SubTask41Id);

            rootStored.Should().NotBeNull();
            subStored.Should().NotBeNull();
            rootStored.ContainsTasks.Should().Contain(subWrapper.TaskItem.Id);
        }

        /// <summary>
        /// Удаление задачи из дерева задач
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task ItemRemoveCommand_Success()
        {
            var parent = mainWindowVM.CurrentItems
                .First(i => i.Id == MainWindowViewModelFixture.RootTask4Id);
            var subTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.SubTask22Id);

            ((NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper).AskResult = true;
            await TestHelpers.ActionNotCreateItems(() => parent.RemoveCommand.Execute(null), taskRepository, -1);
            
            TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, parent.Id).Should().BeNull();
            TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, subTask.Id).Should().NotBeNull();
        }

        /// <summary>
        /// Отказ от удаление задачи из дерева задач
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task CancelItemRemoveCommand_Success()
        {
            var parent = mainWindowVM.CurrentItems
                .First(i => i.Id == MainWindowViewModelFixture.RootTask4Id);
            var subTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.SubTask22Id);

            ((NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper).AskResult = false;
            await TestHelpers.ActionNotCreateItems(() => parent.RemoveCommand.Execute(null), taskRepository, 0);

            TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, parent.Id).Should().NotBeNull();
            TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, subTask.Id).Should().NotBeNull();
        }

        /// <summary>
        /// Удаление задачи из карточки задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task CurrentTaskItemRemove_Success()
        {
            var task = TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.RootTask4Id);

            ((NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper).AskResult = true;
            
            await TestHelpers.ActionNotCreateItems(() => mainWindowVM.Remove.Execute(null), taskRepository, -1);
            
            TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.RootTask4Id).Should().BeNull();
            TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.SubTask41Id).Should().NotBeNull();
        }

        /// <summary>
        /// Архивация задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task ArchiveCommandWithoutContainsTask_Success()
        {
            var task = TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.ArchiveTask11Id);
            ((NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper).AskResult = true;
            
            await TestHelpers.ActionNotCreateItems(() => mainWindowVM.CurrentTaskItem.ArchiveCommand.Execute(null), taskRepository);
            
            var archived = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, task.Id);
            archived.ArchiveDateTime.Should().NotBeNull();
            archived.IsCompleted.Should().BeNull();
        }

        /// <summary>
        /// Архивация задачи с подзадачами
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task ArchiveCommandWithContainsTask_Success()
        {
            var task = TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.ArchiveTask1Id);

            ((NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper).AskResult = true;
            await TestHelpers.ActionNotCreateItems(() => mainWindowVM.CurrentTaskItem.ArchiveCommand.Execute(null), taskRepository);

            var taskItem = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, task.Id);
            taskItem.ArchiveDateTime.Should().NotBeNull();
            taskItem.IsCompleted.Should().BeNull();

            var subItem = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath,
                MainWindowViewModelFixture.ArchiveTask11Id);
            subItem.ArchiveDateTime.Should().NotBeNull();
            subItem.IsCompleted.Should().BeNull();
        }

        /// <summary>
        /// Разархивация задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task UnArchiveCommandWithoutContainsTask_Success()
        {
            var task = TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.ArchivedTask11Id);
            
            ((NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper).AskResult = true;
            await TestHelpers.ActionNotCreateItems(() => mainWindowVM.CurrentTaskItem.ArchiveCommand.Execute(null), taskRepository);

            var taskItem = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, task.Id);
            taskItem.ArchiveDateTime.Should().BeNull();
            taskItem.IsCompleted.Should().BeFalse();
        }

        /// <summary>
        /// Разархивация задачи с подзадачами
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task UnArchiveCommandWithContainsTask_Success()
        {
            var task = TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.ArchivedTask1Id);

            ((NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper).AskResult = true;
            await TestHelpers.ActionNotCreateItems(() => 
                mainWindowVM.CurrentTaskItem.ArchiveCommand.Execute(null), taskRepository);

            var taskItem = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, task.Id);
            taskItem.ArchiveDateTime.Should().BeNull();
            taskItem.IsCompleted.Should().BeFalse();

            var subitem = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.ArchivedTask11Id);
            subitem.ArchiveDateTime.Should().BeNull();
            subitem.IsCompleted.Should().BeFalse();
        }

        /// <summary>
        /// Выполнение задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task IsCompletedTask_Success()
        {
            var rootTaskViewModel = GetTask(MainWindowViewModelFixture.RootTask1Id);
            mainWindowVM.CurrentTaskItem = rootTaskViewModel;
            ((NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper).AskResult = true;
            mainWindowVM.CurrentTaskItem.IsCompleted = true;
            await TestHelpers.WaitThrottleTime();

            // Assert
            var rootTask = GetStorageTaskItem(MainWindowViewModelFixture.RootTask1Id);
            Assert.Equal(true, rootTask.IsCompleted);
            Assert.NotNull(rootTask.CompletedDateTime);
        }

        /// <summary>
        /// Отмена выполнения задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task CancelCompletedTask_Success()
        {
            var completedTaskViewModel = GetTask(MainWindowViewModelFixture.CompletedTaskId);
            mainWindowVM.CurrentTaskItem = completedTaskViewModel;
            mainWindowVM.CurrentTaskItem.IsCompleted = false;
            await TestHelpers.WaitThrottleTime();

            // Assert
            var completedTask = GetStorageTaskItem(MainWindowViewModelFixture.CompletedTaskId);
            Assert.Equal(false, completedTask.IsCompleted);
            Assert.Null(completedTask.CompletedDateTime);
        }

        /// <summary>
        /// Выполнение зависимой задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task CompletingBlockingTask_Success()
        {
            var blockingTask5BeforeTest = GetStorageTaskItem(MainWindowViewModelFixture.RootTask5Id);
            var blockedTask5BeforeTest = GetStorageTaskItem(MainWindowViewModelFixture.BlockedTask5Id);

            //Берем задачу "task 5", которая блокирует задачу "blocked task 5" и делаем ее выполненной
            var blockingTaskViewModel = GetTask(MainWindowViewModelFixture.RootTask5Id);
            mainWindowVM.CurrentTaskItem = blockingTaskViewModel;
            mainWindowVM.CurrentTaskItem.IsCompleted = true;
            await TestHelpers.WaitThrottleTime();

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
            //Должно быть одно различие: проставлен id блокируемой задачи "Blocked task 6"
            if (isdestinationNotBlockedByDraggable)
            {
                Assert.StartsWith("\r\nBegin Differences (2 differences):\r\nTypes [List`1,List`1], Item Expected.BlocksTasks.Count != Actual.BlocksTasks.Count",
                result.DifferencesString);
                Assert.Contains("Item Expected.SortOrder != Actual.SortOrder",
                    result.DifferencesString);

                result = compareLogic.Compare(draggableBeforeTest, blockeddraggableAfterTest);
                //Должно быть 2 различия: id задач которые блокируют и sortOrder
                Assert.StartsWith("\r\nBegin Differences (2 differences):",
                    result.DifferencesString);
                Assert.Contains("Item Expected.BlockedByTasks.Count != Actual.BlockedByTasks.Count",
                    result.DifferencesString);
                Assert.Contains("Item Expected.SortOrder != Actual.SortOrder",
                    result.DifferencesString);

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
                //Должно быть три различия: проставлен id блокируемой задачи "Blocked taask 6"
                Assert.Contains("Item Expected.BlocksTasks.Count != Actual.BlocksTasks.Count",
                result.DifferencesString);

                result = compareLogic.Compare(destinationBeforeTest, destinationAfterTest);
                //Должно быть 2 различия: id задач которые блокируют и sortOrder
                Assert.StartsWith("\r\nBegin Differences (2 differences):",
                    result.DifferencesString);
                Assert.Contains("Item Expected.BlockedByTasks.Count != Actual.BlockedByTasks.Count",
                    result.DifferencesString);
                Assert.Contains("Item Expected.SortOrder != Actual.SortOrder",
                    result.DifferencesString);

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
        public async Task CloneTask_Success()
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
            await TestHelpers.WaitThrottleTime();

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
            //Должны отличаться id, дата создания, кол-во родителей и sortOrder
            Assert.StartsWith($"\r\nBegin Differences (4 differences):\r\nTypes [String,String], Item Expected.Id != Actual.Id, Values ({MainWindowViewModelFixture.ClonedTask8Id},{newTaskItemViewModel.Id})",
                result.DifferencesString);
            Assert.Contains("Types [DateTimeOffset,DateTimeOffset], Item Expected.CreatedDateTime != Actual.CreatedDateTime", result.DifferencesString);
            Assert.Contains("Types [List`1,List`1], Item Expected.ParentTasks.Count != Actual.ParentTasks.Count",
                result.DifferencesString);
            Assert.Contains("Item Expected.SortOrder != Actual.SortOrder",
                result.DifferencesString);
            Assert.Contains(MainWindowViewModelFixture.ClonnedSubTask81Id, newTaskItem.ContainsTasks);
        }

        /// <summary>
        /// Выполнение повторяемой задачи
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task CopleteRepeatableTaskTask_Success()
        {
            //Запоминаем сколько задач было
            var taskCount = taskRepository.Tasks.Count;

            var repeateTask9BeforeTest = GetStorageTaskItem(MainWindowViewModelFixture.RepeateTask9Id);

            //Берем задачу "Repeate task 9" и делаем ее выполненной
            var repeateTask9ViewModel = GetTask(MainWindowViewModelFixture.RepeateTask9Id);
            repeateTask9ViewModel.IsCompleted = true;
            await TestHelpers.WaitThrottleTime();

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
            mainWindowVM.CurrentTaskItem = task;
            mainWindowVM.CreateInner.Execute(null);

            taskRepository.Tasks.Count.Should().Be(taskCountBefore);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Перемещение без источника должно работать как копирование
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task MoveInto_NullSource_ShouldSucceed()
        {
            var subTask = GetTask(MainWindowViewModelFixture.SubTask22Id);
            var destination = GetTask(MainWindowViewModelFixture.RootTask1Id);
            var firstParent = subTask.ParentsTasks.First();

            await subTask.MoveInto(destination, null);

            var stored = GetStorageTaskItem(destination.Id);
            stored.ContainsTasks.Should().Contain(subTask.Id);


            var stored2 = GetStorageTaskItem(firstParent.Id);
            stored2.ContainsTasks.Should().Contain(subTask.Id);
        }

        [Fact]
        public Task RemoveTask_NoParents_ShouldDeleteFile()
        {
            var task = GetTask(MainWindowViewModelFixture.RootTask1Id);
            string path = Path.Combine(fixture.DefaultTasksFolderPath, task.Id);
            File.Exists(path).Should().BeTrue();

            task.RemoveFunc.Invoke();
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

            child.RemoveFunc.Invoke();
            File.Exists(path).Should().BeTrue();
            return Task.CompletedTask;
        }

        [Fact]
        public Task SelectCurrentTaskMode_SyncsCorrectly()
        {
            var task = GetTask(MainWindowViewModelFixture.RootTask1Id);
            mainWindowVM.CurrentTaskItem = task;

            mainWindowVM.AllTasksMode.Should().BeFalse();
            mainWindowVM.CurrentItem.Should().BeNull();
            mainWindowVM.AllTasksMode = true;
            mainWindowVM.CurrentItem.TaskItem.Should().Be(task);

            mainWindowVM.AllTasksMode = false;
            mainWindowVM.CurrentUnlockedItem.Should().BeNull();
            mainWindowVM.UnlockedMode = true;
            mainWindowVM.CurrentUnlockedItem.TaskItem.Should().Be(task);

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
        public async Task CloneInto_MultipleParents_ResultsCorrect()
        {
            var src = GetTask(MainWindowViewModelFixture.ClonedTask8Id);
            var dest1 = GetTask(MainWindowViewModelFixture.RootTask1Id);
            var dest2 = GetTask(MainWindowViewModelFixture.RootTask2Id);
            await dest2.CopyInto(dest2);

            var clone = await src.CloneInto(dest1);

            clone.Parents.Count.Should().Be(1);
            clone.Parents.Should().Contain(dest1.Id);
            clone.Parents.Should().NotContain(dest2.Id);
        }

        private TaskItemViewModel? GetTask(string taskId, bool DontAssertNull = false)
        {
            var result = mainWindowVM.taskRepository.Tasks.Lookup(taskId);
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
    }
}