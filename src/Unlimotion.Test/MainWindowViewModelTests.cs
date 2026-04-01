using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using KellermanSoftware.CompareNetObjects;
using Unlimotion.Domain;
using Unlimotion.ViewModel;

namespace Unlimotion.Test
{
    public class BaseModelTests : IDisposable
    {
        protected readonly MainWindowViewModelFixture fixture;
        protected readonly MainWindowViewModel mainWindowVM;
        protected readonly CompareLogic compareLogic;
        protected readonly ITaskStorage taskRepository;

        public BaseModelTests()
        {
            compareLogic = new CompareLogic();
            compareLogic.Config.MaxDifferences = 10;
            TaskItemViewModel.DefaultThrottleTime = TimeSpan.FromMilliseconds(10);
            fixture = new MainWindowViewModelFixture();
            mainWindowVM = fixture.MainWindowViewModelTest;
            mainWindowVM.Connect().GetAwaiter().GetResult();
            mainWindowVM.AllTasksMode = true;
            while (mainWindowVM.CurrentAllTasksItems.Count == 0){ }
            taskRepository = mainWindowVM.taskRepository!;
        }

        /// <summary>
        /// Очистка после тестов
        /// </summary>
        public void Dispose() => fixture.CleanTasks();

        protected TaskItemViewModel? GetTask(string taskId, bool dontAssertNull = false)
        {
            var result = mainWindowVM.taskRepository!.Tasks.Lookup(taskId);
            if (result.HasValue)
            {
                return result.Value;
            }

            if (!dontAssertNull)
                Assert.Fail("Задача не найдена");

            return null;
        }

        protected TaskItem? GetStorageTaskItem(string taskId)
        {
            var path = Path.Combine(fixture.DefaultTasksFolderPath, taskId);
            if (!File.Exists(path))
                return null;

            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<TaskItem>(json);
                }
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(50);
                }
            }

            var finalJson = File.ReadAllText(path);
            return JsonSerializer.Deserialize<TaskItem>(finalJson);
        }
    }

    [NotInParallel]
    public class MainWindowViewModelTests : BaseModelTests
    {
        private NotificationManagerWrapperMock NotificationManager => (NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper;

        private static HashSet<string> CandidateIds(TaskRelationPickerViewModel picker)
        {
            return picker.Suggestions.Select(candidate => candidate.Task.Id).ToHashSet();
        }

        private async Task<(TaskWrapperViewModel RootWrapper, TaskWrapperViewModel ChildWrapper, TaskWrapperViewModel GrandchildWrapper)>
            CreateThreeLevelTreeCommandBranchAsync()
        {
            mainWindowVM.AllTasksMode = true;
            var childTask = GetTask(MainWindowViewModelFixture.SubTask22Id);
            var grandchildTask = await taskRepository.AddChild(childTask);
            grandchildTask.Title = "Tree command grandchild";
            await TestHelpers.WaitThrottleTime();

            var childWrapper = mainWindowVM.FindTaskWrapperViewModel(childTask, mainWindowVM.CurrentAllTasksItems);
            var grandchildWrapper = mainWindowVM.FindTaskWrapperViewModel(grandchildTask, mainWindowVM.CurrentAllTasksItems);

            await Assert.That(childWrapper).IsNotNull();
            await Assert.That(grandchildWrapper).IsNotNull();
            await Assert.That(childWrapper!.Parent).IsNotNull();
            await Assert.That(grandchildWrapper!.Parent).IsEqualTo(childWrapper);

            return (childWrapper.Parent, childWrapper, grandchildWrapper);
        }

        private static IReadOnlyList<TaskWrapperViewModel> FindWrappersByTaskId(
            IEnumerable<TaskWrapperViewModel> roots,
            string taskId)
        {
            return TraverseWrappers(roots)
                .Where(wrapper => string.Equals(wrapper.TaskItem.Id, taskId, StringComparison.Ordinal))
                .ToList();
        }

        private static IEnumerable<TaskWrapperViewModel> TraverseWrappers(IEnumerable<TaskWrapperViewModel> roots)
        {
            foreach (var wrapper in roots)
            {
                yield return wrapper;

                foreach (var child in TraverseWrappers(wrapper.SubTasks))
                {
                    yield return child;
                }
            }
        }

        /// <summary>
        /// Создание задачи в корне
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task CreateRootTask_Success()
        {
            var task = await TestHelpers.CreateAndReturnNewTaskItem(mainWindowVM.Create,
                taskRepository);

            await Assert.That(task.Parents).IsEmpty();
            await TestHelpers.AssertTaskExistsOnDisk(fixture.DefaultTasksFolderPath, task.Id);
        }

        [Test]
        public async Task CreateRootTask_ShouldRequestTitleFocusAndOpenDetails()
        {
            var focusVersionBefore = mainWindowVM.TitleFocusRequestVersion;
            mainWindowVM.DetailsAreOpen = false;

            _ = await TestHelpers.CreateAndReturnNewTaskItem(mainWindowVM.Create, taskRepository);

            await Assert.That(mainWindowVM.TitleFocusRequestVersion).IsEqualTo(focusVersionBefore + 1);
            await Assert.That(mainWindowVM.DetailsAreOpen).IsTrue();
        }

        /// <summary>
        /// Проверка что заголовок задачи не сбрасывается после сохранения файла
        /// Regression test for title reset bug
        /// </summary>
        [Test]
        public async Task NewTask_TitleNotResetAfterFileSave()
        {
            // Arrange: Create a new task
            var task = await TestHelpers.CreateAndReturnNewTaskItem(mainWindowVM.Create, taskRepository);
            
            // Act: Set title immediately after creation
            var expectedTitle = "Test Title That Should Not Reset";
            task.Title = expectedTitle;
            
            // Wait for file watcher to potentially trigger update (1 second throttle + buffer)
            await Task.Delay(TimeSpan.FromSeconds(2));
            
            // Assert: Title should still be what we set
            await Assert.That(task.Title).IsEqualTo(expectedTitle);

            // Also verify it eventually saves correctly
            await TestHelpers.WaitThrottleTime();
            var storedTask = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, task.Id);
            await Assert.That(storedTask?.Title).IsEqualTo(expectedTitle);
        }

        /// <summary>
        /// Переименование задачи
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task RenameTask_Success()
        {
            var task = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.RootTask1Id);
            var before = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, task!.Id);

            task.Title = "Changed task title";
            await TestHelpers.WaitThrottleTime();

            var after = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, task.Id);
            var result = TestHelpers.CompareStorageVersions(before!, after!);
            await TestHelpers.ShouldHaveOnlyTitleChanged(result, "Root Task 1", "Changed task title");
        }

        /// <summary>
        /// Создание вложенной задачи
        /// </summary>
        /// <returns></returns>
        [Test]
        [Arguments(MainWindowViewModelFixture.RootTask1Id)]
        [Arguments(MainWindowViewModelFixture.RootTask2Id)]
        public async Task CreateInnerTask_Success(string taskId)
        {
            TestHelpers.SetCurrentTask(mainWindowVM, taskId);
            var before = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, taskId);

            var newTask = await TestHelpers.CreateAndReturnNewTaskItem(mainWindowVM.CreateInner,
                taskRepository);

            var after = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, taskId);
            var result = TestHelpers.CompareStorageVersions(before!, after!);

            await TestHelpers.ShouldContainOnlyDifference(result, nameof(after.ContainsTasks));
            await Assert.That(after!.ContainsTasks).Contains(newTask.Id);
        }

        [Test]
        public async Task CreateInnerTask_ShouldRequestTitleFocusAndOpenDetails()
        {
            TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.RootTask1Id);
            var focusVersionBefore = mainWindowVM.TitleFocusRequestVersion;
            mainWindowVM.DetailsAreOpen = false;

            _ = await TestHelpers.CreateAndReturnNewTaskItem(mainWindowVM.CreateInner, taskRepository);

            await Assert.That(mainWindowVM.TitleFocusRequestVersion).IsEqualTo(focusVersionBefore + 1);
            await Assert.That(mainWindowVM.DetailsAreOpen).IsTrue();
        }

        /// <summary>
        /// Создание вложенной задачи без выбранной текущей
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task CreateInnerTask_Fail()
        {
            mainWindowVM.CurrentTaskItem = null;

            var countBefore = taskRepository.Tasks.Count;
            mainWindowVM.CreateInner.Execute(null);
            await TestHelpers.WaitThrottleTime();
            await Assert.That(taskRepository.Tasks.Count).IsEqualTo(countBefore);
        }

        [Test]
        public async Task CreateInnerTask_Fail_ShouldNotRequestTitleFocus()
        {
            mainWindowVM.CurrentTaskItem = null;
            mainWindowVM.DetailsAreOpen = false;
            var focusVersionBefore = mainWindowVM.TitleFocusRequestVersion;

            mainWindowVM.CreateInner.Execute(null);
            await TestHelpers.WaitThrottleTime();

            await Assert.That(mainWindowVM.TitleFocusRequestVersion).IsEqualTo(focusVersionBefore);
            await Assert.That(mainWindowVM.DetailsAreOpen).IsFalse();
        }

        /// <summary>
        /// Создание соседней задачи
        /// </summary>
        /// <returns></returns>
        [Test]
        [Arguments(MainWindowViewModelFixture.RootTask1Id)]
        [Arguments(MainWindowViewModelFixture.SubTask22Id)]
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
                await Assert.That(sharedParent).IsNotNull();
                await Assert.That(parent.Parents).Contains(sharedParent);
                await Assert.That(TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, sharedParent)
                    .ContainsTasks).Contains(sibling.Id);
            }
        }

        [Test]
        public async Task CreateSiblingTask_ShouldRequestTitleFocusAndOpenDetails()
        {
            mainWindowVM.AllTasksMode = true;
            TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.RootTask1Id);
            var focusVersionBefore = mainWindowVM.TitleFocusRequestVersion;
            mainWindowVM.DetailsAreOpen = false;

            _ = await TestHelpers.CreateAndReturnNewTaskItem(mainWindowVM.CreateSibling, taskRepository);

            await Assert.That(mainWindowVM.TitleFocusRequestVersion).IsEqualTo(focusVersionBefore + 1);
            await Assert.That(mainWindowVM.DetailsAreOpen).IsTrue();
        }

        [Test]
        public async Task CreateSiblingTask_WithEmptyCurrentTitle_ShouldNotRequestTitleFocus()
        {
            mainWindowVM.AllTasksMode = true;
            var emptyTask = await TestHelpers.CreateAndReturnNewTaskItem(mainWindowVM.Create, taskRepository);
            mainWindowVM.CurrentTaskItem = emptyTask;
            mainWindowVM.DetailsAreOpen = false;
            var focusVersionBefore = mainWindowVM.TitleFocusRequestVersion;

            mainWindowVM.CreateSibling.Execute(null);
            await TestHelpers.WaitThrottleTime();

            await Assert.That(mainWindowVM.TitleFocusRequestVersion).IsEqualTo(focusVersionBefore);
            await Assert.That(mainWindowVM.DetailsAreOpen).IsFalse();
        }
        
        /// <summary>
        /// Создание зависимой соседней задачи
        /// </summary>
        /// <returns></returns>
        [Test]
        [Arguments(MainWindowViewModelFixture.RootTask1Id)]
        [Arguments(MainWindowViewModelFixture.SubTask22Id)]
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

            await TestHelpers.ShouldContainOnlyDifference(result, nameof(after.BlocksTasks));
            await Assert.That(parent.Blocks).Contains(blocked.Id);
            await Assert.That(after.BlocksTasks).Contains(blocked.Id);
        }

        [Test]
        public async Task CreateBlockedSibling_ShouldRequestTitleFocusAndOpenDetails()
        {
            mainWindowVM.AllTasksMode = true;
            TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.RootTask1Id);
            var focusVersionBefore = mainWindowVM.TitleFocusRequestVersion;
            mainWindowVM.DetailsAreOpen = false;

            _ = await TestHelpers.CreateAndReturnNewTaskItem(mainWindowVM.CreateBlockedSibling, taskRepository);

            await Assert.That(mainWindowVM.TitleFocusRequestVersion).IsEqualTo(focusVersionBefore + 1);
            await Assert.That(mainWindowVM.DetailsAreOpen).IsTrue();
        }

        /// <summary>
        /// Regression: цепочка зависимых соседних задач на корневом уровне
        /// не должна самопроизвольно разблокировать промежуточную/последнюю задачу.
        /// </summary>
        [Test]
        public async Task CreateBlockedSiblingChain_RootLevel_ShouldStayUnavailable()
        {
            mainWindowVM.AllTasksMode = true;

            // 1) Создаём задачу 1 в корне.
            var task1 = await TestHelpers.CreateAndReturnNewTaskItem(mainWindowVM.Create, taskRepository);
            task1.Title = "Task 1";
            await TestHelpers.WaitThrottleTime();

            // 2) Создаём зависимую задачу 2 на том же уровне от задачи 1.
            mainWindowVM.CurrentTaskItem = task1;
            var task2 = await TestHelpers.CreateAndReturnNewTaskItem(mainWindowVM.CreateBlockedSibling, taskRepository);
            task2.Title = "Task 2";
            await TestHelpers.WaitThrottleTime();

            // 3) Задача 2 должна быть недоступной.
            await Assert.That(task2.IsCanBeCompleted).IsFalse();

            // 4) Создаём зависимую задачу 3 на том же уровне от задачи 2.
            mainWindowVM.CurrentTaskItem = task2;
            var task3 = await TestHelpers.CreateAndReturnNewTaskItem(mainWindowVM.CreateBlockedSibling, taskRepository);
            task3.Title = "Task 3";
            await TestHelpers.WaitThrottleTime();

            // 5-6) Ни сразу, ни через время задачи 2 и 3 не должны становиться доступными.
            await Task.Delay(TimeSpan.FromSeconds(3));

            var vmTask2 = TestHelpers.GetTask(mainWindowVM, task2.Id);
            var vmTask3 = TestHelpers.GetTask(mainWindowVM, task3.Id);
            await Assert.That(vmTask2!.IsCanBeCompleted).IsFalse();
            await Assert.That(vmTask3!.IsCanBeCompleted).IsFalse();

            var storedTask2 = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, task2.Id);
            var storedTask3 = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, task3.Id);
            await Assert.That(storedTask2).IsNotNull();
            await Assert.That(storedTask3).IsNotNull();
            await Assert.That(storedTask2!.IsCanBeCompleted).IsFalse();
            await Assert.That(storedTask3!.IsCanBeCompleted).IsFalse();
            await Assert.That(storedTask2.BlockedByTasks).Contains(task1.Id);
            await Assert.That(storedTask3.BlockedByTasks).Contains(task2.Id);
        }

        /// <summary>
        /// Перемещение задачи в другую родительскую задачу
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task MovingToRootTask_Success()
        {
            var subTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.SubTask22Id);
            var from = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.RootTask2Id);
            var to = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.RootTask1Id);

            await subTask.MoveInto(to, from);

            var storedTo = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, to.Id);
            var storedFrom = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, from.Id);

            await Assert.That(storedFrom.ContainsTasks).DoesNotContain(subTask.Id);
            await Assert.That(storedTo.ContainsTasks).Contains(subTask.Id);
        }

        /// <summary>
        /// Создание ссылки на задачу в другой задаче
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task CloneToRootTask_Success()
        {
            var subTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.SubTask22Id);
            var destination = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.RootTask1Id);
            
            var clone = await TestHelpers.CreateAndReturnNewTaskItemAsync(async () => await subTask.CloneInto(destination),
                taskRepository);

            await TestHelpers.AssertTaskExistsOnDisk(fixture.DefaultTasksFolderPath, clone.Id);

            var destItem = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, destination.Id);
            await Assert.That(destItem.ContainsTasks).Contains(clone.Id);

            await Assert.That(destination.Contains).Contains(clone.Id);
            await Assert.That(destination.Contains).DoesNotContain(subTask.Id); // так как Clone, не Move
        }
        
        /// <summary>
        /// Перемещение ссылки в другую родительскую задачу
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task MovingTaskWithTwoParentsToRootTask_Success()
        {
            var subTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.SubTask22Id);
            var from = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.RootTask3Id);
            var to = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.RootTask1Id);

            await TestHelpers.ActionNotCreateItemsAsync(() =>
                subTask.MoveInto(to, from), 
                taskRepository);

            var toStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, to.Id);
            var fromStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, from.Id);
            var otherStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.RootTask2Id);

            await Assert.That(toStored.ContainsTasks).Contains(subTask.Id);
            await Assert.That(fromStored.ContainsTasks).DoesNotContain(subTask.Id);
            await Assert.That(otherStored.ContainsTasks).Contains(subTask.Id);
        }

        /// <summary>
        /// Удаление родительской ссылки на задачу
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task CurrentItemParentsRemove_Success()
        {
            var rootTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.RootTask2Id);
            var subTask = TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.SubTask22Id);
            
            var rootTaskWrapper = mainWindowVM
                .CurrentItemParents
                .SubTasks
                .First(st => st.TaskItem.Id == rootTask.Id);
            await TestHelpers.ActionNotCreateItems(() => rootTaskWrapper.RemoveCommand.Execute(null), taskRepository);
            
            var stored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, rootTask.Id);
            await Assert.That(stored.ContainsTasks).DoesNotContain(subTask.Id);
        }

        /// <summary>
        /// Удаление дочерней ссылки на задачу
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task CurrentItemContainsRemove_Success()
        {
            var rootTask = TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.RootTask2Id);
            var subTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.SubTask22Id);

            var wrapper = mainWindowVM
                .CurrentItemContains
                .SubTasks
                .First(st => st.TaskItem.Id == subTask.Id);
            
            await TestHelpers.ActionNotCreateItems(() => wrapper.RemoveCommand.Execute(null), taskRepository);

            var stored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, rootTask.Id);
            await Assert.That(stored.ContainsTasks).DoesNotContain(subTask.Id);
        }


        /// <summary>
        /// Удаление блокирующей ссылки на задачу
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task CurrentItemBlockedByRemove_Success()
        {
            var rootTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.RootTask2Id);
            var blocked = TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.BlockedTask2Id);
            
            var wrapper = mainWindowVM
                .CurrentItemBlockedBy
                .SubTasks
                .First(st => st.TaskItem.Id == rootTask.Id);

            await TestHelpers.ActionNotCreateItems(() => wrapper.RemoveCommand.Execute(null), taskRepository);

            var blockerStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, rootTask.Id);
            await Assert.That(blockerStored.BlocksTasks).DoesNotContain(blocked.Id);
        }
        
        /// <summary>
        /// Удаление блокируемой ссылки на задачу
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task CurrentItemBlocksRemove_Success()
        {
            var rootTask = TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.RootTask2Id);
            var blocked = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.BlockedTask2Id);
            
            var wrapper = mainWindowVM
                .CurrentItemBlocks
                .SubTasks
                .First(st => st.TaskItem.Id == blocked.Id);

            await TestHelpers.ActionNotCreateItems(() => wrapper.RemoveCommand.Execute(null), taskRepository);

            var rootStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, rootTask.Id);
            await Assert.That(rootStored.BlocksTasks).DoesNotContain(blocked.Id);
        }

        [Test]
        public async Task CurrentItemParentsAdd_Success()
        {
            var currentTask = TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.BlockedTask7Id);
            var picker = mainWindowVM.CurrentItemParentsPicker!;

            picker.OpenCommand.Execute(null);
            picker.SelectedCandidate = picker.Suggestions.First(candidate => candidate.Task.Id == MainWindowViewModelFixture.RootTask1Id);

            await TestHelpers.ActionNotCreateItems(() => picker.ConfirmCommand.Execute(null), taskRepository);

            var currentStored = GetStorageTaskItem(MainWindowViewModelFixture.BlockedTask7Id);
            var parentStored = GetStorageTaskItem(MainWindowViewModelFixture.RootTask1Id);

            await Assert.That(currentStored).IsNotNull();
            await Assert.That(parentStored).IsNotNull();
            await Assert.That(currentStored.ParentTasks).Contains(MainWindowViewModelFixture.RootTask1Id);
            await Assert.That(parentStored.ContainsTasks).Contains(currentTask.Id);
        }

        [Test]
        public async Task CurrentItemContainsAdd_Success()
        {
            var currentTask = TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.RootTask1Id);
            var picker = mainWindowVM.CurrentItemContainsPicker!;

            picker.OpenCommand.Execute(null);
            picker.SelectedCandidate = picker.Suggestions.First(candidate => candidate.Task.Id == MainWindowViewModelFixture.BlockedTask7Id);

            await TestHelpers.ActionNotCreateItems(() => picker.ConfirmCommand.Execute(null), taskRepository);

            var currentStored = GetStorageTaskItem(MainWindowViewModelFixture.RootTask1Id);
            var childStored = GetStorageTaskItem(MainWindowViewModelFixture.BlockedTask7Id);

            await Assert.That(currentStored).IsNotNull();
            await Assert.That(childStored).IsNotNull();
            await Assert.That(currentStored.ContainsTasks).Contains(MainWindowViewModelFixture.BlockedTask7Id);
            await Assert.That(childStored.ParentTasks).Contains(currentTask.Id);
        }

        [Test]
        public async Task CurrentItemBlockedByAdd_Success()
        {
            TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.BlockedTask7Id);
            var picker = mainWindowVM.CurrentItemBlockedByPicker!;

            picker.OpenCommand.Execute(null);
            picker.SelectedCandidate = picker.Suggestions.First(candidate => candidate.Task.Id == MainWindowViewModelFixture.RootTask7Id);

            await TestHelpers.ActionNotCreateItems(() => picker.ConfirmCommand.Execute(null), taskRepository);

            var blockerStored = GetStorageTaskItem(MainWindowViewModelFixture.RootTask7Id);
            var blockedStored = GetStorageTaskItem(MainWindowViewModelFixture.BlockedTask7Id);
            var blockedVm = GetTask(MainWindowViewModelFixture.BlockedTask7Id);

            await Assert.That(blockerStored).IsNotNull();
            await Assert.That(blockedStored).IsNotNull();
            await Assert.That(blockerStored.BlocksTasks).Contains(MainWindowViewModelFixture.BlockedTask7Id);
            await Assert.That(blockedStored.BlockedByTasks).Contains(MainWindowViewModelFixture.RootTask7Id);
            await Assert.That(blockedVm.IsCanBeCompleted).IsFalse();
        }

        [Test]
        public async Task CurrentItemBlocksAdd_Success()
        {
            TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.RootTask7Id);
            var picker = mainWindowVM.CurrentItemBlocksPicker!;

            picker.OpenCommand.Execute(null);
            picker.SelectedCandidate = picker.Suggestions.First(candidate => candidate.Task.Id == MainWindowViewModelFixture.BlockedTask7Id);

            await TestHelpers.ActionNotCreateItems(() => picker.ConfirmCommand.Execute(null), taskRepository);

            var blockerStored = GetStorageTaskItem(MainWindowViewModelFixture.RootTask7Id);
            var blockedStored = GetStorageTaskItem(MainWindowViewModelFixture.BlockedTask7Id);
            var blockedVm = GetTask(MainWindowViewModelFixture.BlockedTask7Id);

            await Assert.That(blockerStored).IsNotNull();
            await Assert.That(blockedStored).IsNotNull();
            await Assert.That(blockerStored.BlocksTasks).Contains(MainWindowViewModelFixture.BlockedTask7Id);
            await Assert.That(blockedStored.BlockedByTasks).Contains(MainWindowViewModelFixture.RootTask7Id);
            await Assert.That(blockedVm.IsCanBeCompleted).IsFalse();
        }

        [Test]
        public async Task CurrentItemParentsPicker_ShouldNotContainSelfCandidate()
        {
            TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.RootTask1Id);
            var picker = mainWindowVM.CurrentItemParentsPicker!;

            picker.OpenCommand.Execute(null);

            await Assert.That(CandidateIds(picker)).DoesNotContain(MainWindowViewModelFixture.RootTask1Id);
        }

        [Test]
        public async Task CurrentItemBlockedByPicker_ShouldNotContainExistingRelationCandidate()
        {
            TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.BlockedTask2Id);
            var picker = mainWindowVM.CurrentItemBlockedByPicker!;

            picker.OpenCommand.Execute(null);

            await Assert.That(CandidateIds(picker)).DoesNotContain(MainWindowViewModelFixture.RootTask2Id);
        }

        [Test]
        public async Task CurrentItemParentsPicker_ShouldNotContainCandidateThatCreatesCycle()
        {
            TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.RootTask4Id);
            var picker = mainWindowVM.CurrentItemParentsPicker!;

            picker.OpenCommand.Execute(null);

            await Assert.That(CandidateIds(picker)).DoesNotContain(MainWindowViewModelFixture.SubTask41Id);
        }

        [Test]
        public async Task CurrentItemBlockedByPicker_InvalidForcedConfirm_ShouldShowToastAndKeepRelationsUnchanged()
        {
            TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.DeadlockTask6Id);
            var picker = mainWindowVM.CurrentItemBlockedByPicker!;
            var candidateTask = GetTask(MainWindowViewModelFixture.DeadlockBlockedTask6Id);
            NotificationManager.ClearMessages();

            picker.OpenCommand.Execute(null);

            await Assert.That(CandidateIds(picker)).DoesNotContain(MainWindowViewModelFixture.DeadlockBlockedTask6Id);

            picker.SelectedCandidate = new TaskRelationCandidateViewModel(
                candidateTask,
                candidateTask.Title,
                candidateTask.Id);

            await TestHelpers.ActionNotCreateItems(() => picker.ConfirmCommand.Execute(null), taskRepository);

            var currentStored = GetStorageTaskItem(MainWindowViewModelFixture.DeadlockTask6Id);
            var candidateStored = GetStorageTaskItem(MainWindowViewModelFixture.DeadlockBlockedTask6Id);

            await Assert.That(currentStored.BlockedByTasks).DoesNotContain(candidateTask.Id);
            await Assert.That(candidateStored.BlocksTasks).DoesNotContain(MainWindowViewModelFixture.DeadlockTask6Id);
            await Assert.That(NotificationManager.LastErrorMessage).IsNotNull();
        }

        /// <summary>
        /// Удаление ссылки из дерева задач
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task SubItemLinkRemoveCommand_Success()
        {
            var rootWrapper = mainWindowVM.CurrentAllTasksItems
                .First(i => i.TaskItem.Id == MainWindowViewModelFixture.RootTask4Id);

            var subWrapper = rootWrapper.SubTasks
                .First(st => st.TaskItem.Id == MainWindowViewModelFixture.SubTask41Id);

            ((NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper).AskResult = true;

            await TestHelpers.ActionNotCreateItems(() => subWrapper.RemoveCommand.Execute(null), taskRepository, -1);

            var subStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.SubTask41Id);
            await Assert.That(subStored).IsNull();
        }

        /// <summary>
        /// Отказ от удаления ссылки из дерева задач
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task SubItemRemoveCommand_Success()
        {
            var rootWrapper = mainWindowVM.CurrentAllTasksItems
                .First(i => i.TaskItem.Id == MainWindowViewModelFixture.RootTask4Id);

            var subWrapper = rootWrapper.SubTasks
                .First(st => st.TaskItem.Id == MainWindowViewModelFixture.SubTask41Id);

            ((NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper).AskResult = false;
            
            await TestHelpers.ActionNotCreateItems(() => subWrapper.RemoveCommand.Execute(null), taskRepository);
            
            var rootStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.RootTask4Id);
            var subStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.SubTask41Id);

            await Assert.That(rootStored).IsNotNull();
            await Assert.That(subStored).IsNotNull();
            await Assert.That(rootStored.ContainsTasks).Contains(subWrapper.TaskItem.Id);
        }

        /// <summary>
        /// Удаление задачи из дерева задач
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task ItemRemoveCommand_Success()
        {
            var parent = mainWindowVM.CurrentAllTasksItems
                .First(i => i.Id == MainWindowViewModelFixture.RootTask4Id);
            var subTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.SubTask22Id);

            ((NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper).AskResult = true;
            await TestHelpers.ActionNotCreateItems(() => parent.RemoveCommand.Execute(null), taskRepository, -1);
            
            await Assert.That(TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, parent.Id)).IsNull();
            await Assert.That(TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, subTask.Id)).IsNotNull();
        }

        [Test]
        public async Task RemoveSelectedWrappers_MultiParentSameTask_RemovesEachWrapperContext()
        {
            var wrappers = FindWrappersByTaskId(mainWindowVM.CurrentAllTasksItems, MainWindowViewModelFixture.SubTask22Id);
            await Assert.That(wrappers.Count).IsEqualTo(2);

            var root2Wrapper = wrappers.FirstOrDefault(wrapper => wrapper.Parent?.TaskItem.Id == MainWindowViewModelFixture.RootTask2Id);
            var root3Wrapper = wrappers.FirstOrDefault(wrapper => wrapper.Parent?.TaskItem.Id == MainWindowViewModelFixture.RootTask3Id);
            await Assert.That(root2Wrapper).IsNotNull();
            await Assert.That(root3Wrapper).IsNotNull();

            NotificationManager.AskResult = true;
            mainWindowVM.RemoveSelectedWrappers([root2Wrapper!, root3Wrapper!]);
            await TestHelpers.WaitThrottleTime();

            var taskFile = GetStorageTaskItem(MainWindowViewModelFixture.SubTask22Id);
            var root2Stored = GetStorageTaskItem(MainWindowViewModelFixture.RootTask2Id);
            var root3Stored = GetStorageTaskItem(MainWindowViewModelFixture.RootTask3Id);

            await Assert.That(taskFile).IsNull();
            await Assert.That(root2Stored!.ContainsTasks).DoesNotContain(MainWindowViewModelFixture.SubTask22Id);
            await Assert.That(root3Stored!.ContainsTasks).DoesNotContain(MainWindowViewModelFixture.SubTask22Id);
        }

        [Test]
        public async Task RemoveSelectedWrappers_CancelledBatch_KeepsAllSelectedEntries()
        {
            var wrappers = new[]
            {
                mainWindowVM.CurrentAllTasksItems.First(wrapper => wrapper.TaskItem.Id == MainWindowViewModelFixture.RootTask1Id),
                mainWindowVM.CurrentAllTasksItems.First(wrapper => wrapper.TaskItem.Id == MainWindowViewModelFixture.RootTask4Id)
            };

            NotificationManager.AskResult = false;
            mainWindowVM.RemoveSelectedWrappers(wrappers);
            await TestHelpers.WaitThrottleTime();

            var root1Stored = GetStorageTaskItem(MainWindowViewModelFixture.RootTask1Id);
            var root4Stored = GetStorageTaskItem(MainWindowViewModelFixture.RootTask4Id);

            await Assert.That(root1Stored).IsNotNull();
            await Assert.That(root4Stored).IsNotNull();
        }

        [Test]
        public async Task NormalizeForMoveBatch_RemovesDescendantsWhenAncestorSelected()
        {
            var rootWrapper = mainWindowVM.CurrentAllTasksItems
                .First(wrapper => wrapper.TaskItem.Id == MainWindowViewModelFixture.RootTask2Id);
            var childWrapper = rootWrapper.SubTasks
                .First(wrapper => wrapper.TaskItem.Id == MainWindowViewModelFixture.SubTask22Id);

            var normalized = new[] { rootWrapper, childWrapper }.NormalizeForMoveBatch();

            await Assert.That(normalized.Count).IsEqualTo(1);
            await Assert.That(normalized[0]).IsSameReferenceAs(rootWrapper);
        }

        [Test]
        public async Task NormalizeForNonMoveBatch_DeduplicatesSameTaskAcrossParents()
        {
            var wrappers = FindWrappersByTaskId(mainWindowVM.CurrentAllTasksItems, MainWindowViewModelFixture.SubTask22Id);
            await Assert.That(wrappers.Count).IsEqualTo(2);

            var normalized = wrappers.NormalizeForNonMoveBatch();

            await Assert.That(normalized.Count).IsEqualTo(1);
            await Assert.That(normalized[0].TaskItem.Id).IsEqualTo(MainWindowViewModelFixture.SubTask22Id);
        }

        /// <summary>
        /// Перемещение заблокированной задачи к новому родителю должно блокировать нового родителя
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task MoveBlockedTaskToNewParent_WithFileStorage_ShouldBlockNewParent()
        {
            // Arrange - Создаем задачу-ребенка, которая блокирует своего родителя
            var childTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.SubTask22Id);
            
            // Убедимся, что ребенок не завершен, чтобы он блокировал родителя
            if (childTask.IsCompleted == true)
            {
                childTask.IsCompleted = false;
                await TestHelpers.WaitThrottleTime();
            }
            
            // Получаем оригинального родителя
            var originalParent = childTask.ParentsTasks.First();
            
            // Создаем новую задачу, которая станет новым родителем
            var newParent = await TestHelpers.CreateAndReturnNewTaskItem(mainWindowVM.Create, taskRepository);
            
            // Проверяем начальное состояние - оригинальный родитель должен быть заблокирован из-за незавершенного ребенка
            var originalParentStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, originalParent.Id);
            await Assert.That(originalParentStored.IsCanBeCompleted).IsFalse();

            // Act - Перемещаем задачу с ребенком к новому родителю
            await childTask.MoveInto(newParent, originalParent );
            
            // Ждем сохранения
            await TestHelpers.WaitThrottleTime();
            
            // Reload tasks from file storage to get updated state
            var updatedOriginalParent = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, originalParent.Id);
            var updatedNewParent = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, newParent.Id);
            var updatedChild = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, childTask.Id);
            
            // Assert - Новый родитель должен быть заблокирован, потому что он теперь содержит задачу с незавершенным ребенком
            await Assert.That(updatedNewParent.IsCanBeCompleted).IsFalse();
            await Assert.That(updatedNewParent.UnlockedDateTime).IsNull();

            // Оригинальный родитель должен быть разблокирован, потому что у него больше нет детей
            await Assert.That(updatedOriginalParent.IsCanBeCompleted).IsTrue();
            await Assert.That(updatedOriginalParent.UnlockedDateTime).IsNotNull();

            // Отношения должны быть корректными
            await Assert.That(updatedNewParent.ContainsTasks).Contains(childTask.Id);
            await Assert.That(updatedChild.ParentTasks).Contains(newParent.Id);
            await Assert.That(updatedOriginalParent.ContainsTasks).DoesNotContain(childTask.Id);
            await Assert.That(updatedChild.ParentTasks).DoesNotContain(updatedOriginalParent.Id);
        }

        /// <summary>
        /// Копирование заблокированной задачи к новому родителю должно блокировать нового родителя
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task CopyBlockedTaskToNewParent_WithFileStorage_ShouldBlockNewParent()
        {
            // Arrange - Создаем задачу-ребенка, которая блокирует своего родителя
            var childTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.SubTask22Id);

            // Убедимся, что ребенок не завершен, чтобы он блокировал родителя
            if (childTask.IsCompleted == true)
            {
                childTask.IsCompleted = false;
                await TestHelpers.WaitThrottleTime();
            }

            // Получаем оригинального родителя
            var originalParent = childTask.ParentsTasks.First();

            // Создаем новую задачу, которая станет новым родителем
            var newParent = await TestHelpers.CreateAndReturnNewTaskItem(mainWindowVM.Create, taskRepository);
            await Assert.That(newParent.IsCanBeCompleted).IsTrue();

            // Проверяем начальное состояние - оригинальный родитель должен быть заблокирован из-за незавершенного ребенка
            var originalParentStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, originalParent.Id);
            await Assert.That(originalParentStored.IsCanBeCompleted).IsFalse();

            // Act - Копируем задачу с ребенком к новому родителю
            await childTask.CopyInto(newParent);

            // Ждем сохранения
            await TestHelpers.WaitThrottleTime();

            // Reload tasks from file storage to get updated state
            var updatedOriginalParent = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, originalParent.Id);
            var updatedNewParent = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, newParent.Id);

            // Assert - Новый родитель должен быть заблокирован, потому что он теперь содержит задачу с незавершенным ребенком
            await Assert.That(updatedNewParent.IsCanBeCompleted).IsFalse();
            await Assert.That(updatedNewParent.UnlockedDateTime).IsNull();

            // Оригинальный родитель должен оставаться заблокирован, потому что у него все еще есть дети
            await Assert.That(updatedOriginalParent.IsCanBeCompleted).IsFalse();
            await Assert.That(updatedOriginalParent.UnlockedDateTime).IsNull();

            // Отношения должны быть корректными - оба родителя содержат ребенка
            await Assert.That(updatedOriginalParent.ContainsTasks).Contains(childTask.Id);
            await Assert.That(updatedNewParent.ContainsTasks).Contains(childTask.Id);
        }

        /// <summary>
        /// Отказ от удаление задачи из дерева задач
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task CancelItemRemoveCommand_Success()
        {
            var parent = mainWindowVM.CurrentAllTasksItems
                .First(i => i.Id == MainWindowViewModelFixture.RootTask4Id);
            var subTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.SubTask22Id);

            ((NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper).AskResult = false;
            await TestHelpers.ActionNotCreateItems(() => parent.RemoveCommand.Execute(null), taskRepository);

            await Assert.That(TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, parent.Id)).IsNotNull();
            await Assert.That(TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, subTask.Id)).IsNotNull();
        }

        /// <summary>
        /// Удаление задачи из карточки задачи
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task CurrentTaskItemRemove_Success()
        {
            var task = TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.RootTask4Id);

            ((NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper).AskResult = true;
            
            await TestHelpers.ActionNotCreateItems(() => mainWindowVM.Remove.Execute(null), taskRepository, -1);
            
            await Assert.That(TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.RootTask4Id)).IsNull();
            await Assert.That(TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.SubTask41Id)).IsNotNull();
        }

        /// <summary>
        /// Архивация задачи
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task ArchiveCommandWithoutContainsTask_Success()
        {
            var task = TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.ArchiveTask11Id);
            ((NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper).AskResult = true;
            
            await TestHelpers.ActionNotCreateItems(() => mainWindowVM.CurrentTaskItem.ArchiveCommand.Execute(null), taskRepository);
            
            var archived = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, task.Id);
            await Assert.That(archived.ArchiveDateTime).IsNotNull();
            await Assert.That(archived.IsCompleted).IsNull();
        }

        /// <summary>
        /// Архивация задачи с подзадачами
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task ArchiveCommandWithContainsTask_Success()
        {
            var task = TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.ArchiveTask1Id);

            ((NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper).AskResult = true;
            await TestHelpers.ActionNotCreateItems(() => mainWindowVM.CurrentTaskItem.ArchiveCommand.Execute(null), taskRepository);

            var taskItem = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, task.Id);
            await Assert.That(taskItem.ArchiveDateTime).IsNotNull();
            await Assert.That(taskItem.IsCompleted).IsNull();

            var subItem = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath,
                MainWindowViewModelFixture.ArchiveTask11Id);
            await Assert.That(subItem.ArchiveDateTime).IsNotNull();
            await Assert.That(subItem.IsCompleted).IsNull();
        }

        /// <summary>
        /// Разархивация задачи
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task UnArchiveCommandWithoutContainsTask_Success()
        {
            var task = TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.ArchivedTask11Id);
            
            ((NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper).AskResult = true;
            await TestHelpers.ActionNotCreateItems(() => mainWindowVM.CurrentTaskItem.ArchiveCommand.Execute(null), taskRepository);

            var taskItem = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, task.Id);
            await Assert.That(taskItem.ArchiveDateTime).IsNull();
            await Assert.That(taskItem.IsCompleted).IsFalse();
        }

        /// <summary>
        /// Разархивация задачи с подзадачами
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task UnArchiveCommandWithContainsTask_Success()
        {
            var task = TestHelpers.SetCurrentTask(mainWindowVM, MainWindowViewModelFixture.ArchivedTask1Id);

            ((NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper).AskResult = true;
            await TestHelpers.ActionNotCreateItems(() => 
                mainWindowVM.CurrentTaskItem.ArchiveCommand.Execute(null), taskRepository);

            var taskItem = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, task.Id);
            await Assert.That(taskItem.ArchiveDateTime).IsNull();
            await Assert.That(taskItem.IsCompleted).IsFalse();

            var subitem = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.ArchivedTask11Id);
            await Assert.That(subitem.ArchiveDateTime).IsNull();
            await Assert.That(subitem.IsCompleted).IsFalse();
        }

        /// <summary>
        /// Выполнение задачи
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task IsCompletedTask_Success()
        {
            var rootTaskViewModel = GetTask(MainWindowViewModelFixture.RootTask1Id);
            mainWindowVM.CurrentTaskItem = rootTaskViewModel;
            ((NotificationManagerWrapperMock)mainWindowVM.ManagerWrapper).AskResult = true;
            mainWindowVM.CurrentTaskItem.IsCompleted = true;
            await TestHelpers.WaitThrottleTime();

            // Assert
            var rootTask = GetStorageTaskItem(MainWindowViewModelFixture.RootTask1Id);
            await Assert.That(rootTask.IsCompleted).IsEqualTo(true);
            await Assert.That(rootTask.CompletedDateTime).IsNotNull();
        }

        /// <summary>
        /// Отмена выполнения задачи
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task CancelCompletedTask_Success()
        {
            var completedTaskViewModel = GetTask(MainWindowViewModelFixture.CompletedTaskId);
            mainWindowVM.CurrentTaskItem = completedTaskViewModel;
            mainWindowVM.CurrentTaskItem.IsCompleted = false;
            await TestHelpers.WaitThrottleTime();

            // Assert
            var completedTask = GetStorageTaskItem(MainWindowViewModelFixture.CompletedTaskId);
            await Assert.That(completedTask.IsCompleted).IsEqualTo(false);
            await Assert.That(completedTask.CompletedDateTime).IsNull();
        }

        /// <summary>
        /// Выполнение зависимой задачи
        /// </summary>
        /// <returns></returns>
        [Test]
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
            await Assert.That(blockedTask5AfterTest.UnlockedDateTime).IsNotNull();
            var result = compareLogic.Compare(blockedTask5BeforeTest, blockedTask5AfterTest);
            //Должно быть два различия: IsCanBeCompleted и UnlockedDateTime
            await Assert.That(result.Differences.Count).IsEqualTo(2);
            var isCanBeCompletedDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(blockedTask5AfterTest.IsCanBeCompleted));
            var unlockedDateTimeDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(blockedTask5AfterTest.UnlockedDateTime));

            await Assert.That(isCanBeCompletedDifference).IsNotNull();
            await Assert.That(isCanBeCompletedDifference.Object1).IsEqualTo(false);
            await Assert.That(isCanBeCompletedDifference.Object2).IsEqualTo(true);
            await Assert.That(unlockedDateTimeDifference).IsNotNull();
            await Assert.That(unlockedDateTimeDifference.Object1).IsNull();
            await Assert.That(unlockedDateTimeDifference.Object2).IsNotNull();

            var blockedTask5ViewModel = taskRepository.Tasks.Items.First(i => i.Id == MainWindowViewModelFixture.BlockedTask5Id);
            await Assert.That(blockedTask5ViewModel).IsNotNull();
            //Теперь блокируемый таск можно выполнить
            await Assert.That(blockedTask5ViewModel.IsCanBeCompleted).IsTrue();

            var rootTask5AfterTest = GetStorageTaskItem(MainWindowViewModelFixture.RootTask5Id);
            //Проверяем, что в блокирующем таске изменилось
            result = compareLogic.Compare(blockingTask5BeforeTest, rootTask5AfterTest);
            //Должно быть 2 различия поля IsCompleted и CompletedDateTime
            await Assert.That(result.Differences.Count).IsEqualTo(2);
            var isCompletedDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(TaskItem.IsCompleted));
            var completedDateTimeDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(TaskItem.CompletedDateTime));

            await Assert.That(isCompletedDifference).IsNotNull();
            await Assert.That(completedDateTimeDifference).IsNotNull();
            await Assert.That(isCompletedDifference.Object1).IsEqualTo(false);
            await Assert.That(isCompletedDifference.Object2).IsEqualTo(true);
            await Assert.That(completedDateTimeDifference.Object1).IsNull();
            await Assert.That(completedDateTimeDifference.Object2).IsNotNull();
        }

        /// <summary>
        /// Создание зависимой связи
        /// </summary>
        /// <returns></returns>
        [Test]
        [Arguments(MainWindowViewModelFixture.BlockedTask6Id, MainWindowViewModelFixture.RootTask6Id)]
        [Arguments(MainWindowViewModelFixture.DeadlockTask6Id, MainWindowViewModelFixture.DeadlockBlockedTask6Id)]
        public async Task AddBlokedByLinkTask_Success(string draggableId, string destinationId)
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
                await Assert.That(result.Differences).HasSingleItem();
                // Проверяем, что количество блокируемых задач изменилось
                var blocksTasksDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(TaskItem.BlocksTasks));
                await Assert.That(blocksTasksDifference).IsNotNull();

                result = compareLogic.Compare(draggableBeforeTest, blockeddraggableAfterTest);
                //Должно быть 3 различия: BlockedByTasks.Count, IsCanBeCompleted и UnlockedDateTime
                await Assert.That(result.Differences.Count).IsEqualTo(3);
                var blockedByTasksDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(TaskItem.BlockedByTasks));
                var isCanBeCompletedDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(TaskItem.IsCanBeCompleted));
                var unlockedDateTimeDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(TaskItem.UnlockedDateTime));

                await Assert.That(blockedByTasksDifference).IsNotNull();
                await Assert.That(isCanBeCompletedDifference).IsNotNull();
                await Assert.That(unlockedDateTimeDifference).IsNotNull();

                await Assert.That(isCanBeCompletedDifference.Object1).IsEqualTo(true);
                await Assert.That(isCanBeCompletedDifference.Object2).IsEqualTo(false);
                await Assert.That(unlockedDateTimeDifference.Object1).IsNotNull();
                await Assert.That(unlockedDateTimeDifference.Object2).IsNull();

                await Assert.That(rootTaskAfterTest).IsNotNull();

                await Assert.That(rootTaskAfterTest.BlocksTasks).IsNotEmpty();
                await Assert.That(rootTaskAfterTest.BlocksTasks).Contains(blockeddraggableAfterTest.Id);
            }
            else
                await Assert.That(result.AreEqual).IsTrue();

        }

        /// <summary>
        /// Создание обратной зависимой связи
        /// </summary>
        /// <returns></returns>
        [Test]
        [Arguments(MainWindowViewModelFixture.RootTask7Id, MainWindowViewModelFixture.BlockedTask7Id)]
        [Arguments(MainWindowViewModelFixture.DeadlockBlockedTask7Id, MainWindowViewModelFixture.DeadlockTask7Id)]
        public async Task AddReverseBlokedByLinkTask_Success(string draggableId, string destinationId)
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
                var blocksTasksDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(TaskItem.BlocksTasks));
                await Assert.That(blocksTasksDifference).IsNotNull();

                result = compareLogic.Compare(destinationBeforeTest, destinationAfterTest);
                //Должно быть 3 различия: BlockedByTasks.Count, IsCanBeCompleted и UnlockedDateTime
                await Assert.That(result.Differences.Count).IsEqualTo(3);
                var blockedByTasksDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(TaskItem.BlockedByTasks));
                var isCanBeCompletedDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(TaskItem.IsCanBeCompleted));
                var unlockedDateTimeDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(TaskItem.UnlockedDateTime));

                await Assert.That(blockedByTasksDifference).IsNotNull();
                await Assert.That(isCanBeCompletedDifference).IsNotNull();
                await Assert.That(unlockedDateTimeDifference).IsNotNull();

                await Assert.That(isCanBeCompletedDifference.Object1).IsEqualTo(true);
                await Assert.That(isCanBeCompletedDifference.Object2).IsEqualTo(false);
                await Assert.That(unlockedDateTimeDifference.Object1).IsNotNull();
                await Assert.That(unlockedDateTimeDifference.Object2).IsNull();

                await Assert.That(draggableAfterTest).IsNotNull();

                await Assert.That(draggableAfterTest.BlocksTasks).IsNotEmpty();
                await Assert.That(draggableAfterTest.BlocksTasks).Contains(destinationAfterTest.Id);
            }
            else
                await Assert.That(result.AreEqual).IsTrue();

        }

        /// <summary>
        /// Клонирование задачи
        /// </summary>
        /// <returns></returns>
        [Test]
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
            var cloned = await clonedViewModel.CloneInto(destinationViewModel);
            await TestHelpers.WaitThrottleTime();

            //Assert
            //Проверяем что создалась ровно 1 задача
            await Assert.That(taskRepository.Tasks.Count).IsEqualTo(taskCount + 1);

            //Находим созданную склонированную задачу в репозитории
            var newTaskItemViewModel = taskRepository.Tasks.Lookup(cloned.Id).Value;
            await Assert.That(newTaskItemViewModel).IsNotNull();

            //Загружаем новую задачу из файла
            var newTaskItem = GetStorageTaskItem(newTaskItemViewModel.Id);
            //Загружаем целевую задачу из файла
            var destinationTask8ItemAfterTest = GetStorageTaskItem(MainWindowViewModelFixture.DestinationTask8Id);
            //Новая задача должна быть в массиве ContainsTasks целевой задачи из файла
            await Assert.That(destinationTask8ItemAfterTest.ContainsTasks).IsNotEmpty();
            await Assert.That(destinationTask8ItemAfterTest.ContainsTasks).Contains(newTaskItemViewModel.Id);
            //Теперь у целевой задачи есть невыполненные задачи внутри. Она заблокирована

            await Assert.That(destinationTask8ItemAfterTest.IsCanBeCompleted).IsFalse();
            await Assert.That(destinationTask8ItemAfterTest.UnlockedDateTime).IsNull();

            //Сравниваем старую и новую версию целевой задачи
            var result = compareLogic.Compare(destination8BeforeTest, destinationTask8ItemAfterTest);

            //Должно быть 2 различия в количестве ContainsTasks и UnlockedDateTime
            var unlockedDateTimeDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(TaskItem.UnlockedDateTime));
            var containsTasksDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(TaskItem.ContainsTasks));

            await Assert.That(unlockedDateTimeDifference).IsNotNull();
            await Assert.That(containsTasksDifference).IsNotNull();
            await Assert.That(unlockedDateTimeDifference.Object1).IsNotNull();
            await Assert.That(unlockedDateTimeDifference.Object2).IsNull();
            await Assert.That(((IList)containsTasksDifference.Object1).Count).IsEqualTo(0);
            await Assert.That(((IList)containsTasksDifference.Object2).Count).IsEqualTo(1);
            //Новая задача должна быть в Contains во вьюмодели целевой задачи
            await Assert.That(destinationViewModel.Contains).Contains(newTaskItemViewModel.Id);

            //Берем клонируюмую задачу из файла
            var clonedTask8ItemAfterTest = GetStorageTaskItem(clonedViewModel.Id);
            //Сравниваем клонируюмую задачу с новой созданной
            result = compareLogic.Compare(clonedTask8ItemAfterTest, newTaskItem);
            //Должны отличаться id, дата создания и кол-во родителей
            await Assert.That(result.Differences.Count).IsEqualTo(3);
            await Assert.That(result.Differences.Select(d => d.PropertyName)).Contains(nameof(TaskItem.Id));
            await Assert.That(result.Differences.Select(d => d.PropertyName)).Contains(nameof(TaskItem.CreatedDateTime));
            await Assert.That(result.Differences.Select(d => d.PropertyName)).Contains(nameof(TaskItem.ParentTasks));
            var parentTasksDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(TaskItem.ParentTasks));
            await Assert.That(((IList)parentTasksDifference.Object1).Count).IsEqualTo(0);
            await Assert.That(((IList)parentTasksDifference.Object2).Count).IsEqualTo(1);
            await Assert.That(newTaskItem.ContainsTasks).Contains(MainWindowViewModelFixture.ClonnedSubTask81Id);
        }

        /// <summary>
        /// Выполнение повторяемой задачи
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task CompleteRepeatableTaskTask_Success()
        {
            //Запоминаем сколько задач было
            var taskCount = taskRepository.Tasks.Count;

            var repeateTask9BeforeTest = GetStorageTaskItem(MainWindowViewModelFixture.RepeateTask9Id);

            //Берем задачу "Repeate task 9" и делаем ее выполненной
            var repeateTask9ViewModel = GetTask(MainWindowViewModelFixture.RepeateTask9Id);
            await Assert.That(repeateTask9ViewModel.Repeater).IsNotNull();
            repeateTask9ViewModel.IsCompleted = true;
            await TestHelpers.WaitThrottleTime();

            //Assert
            //Проверяем что создалась ровно 1 задача
            await Assert.That(taskRepository.Tasks.Count).IsEqualTo(taskCount + 1);


            //Берем задачу из файла
            var repeateTask9AfterTest = GetStorageTaskItem(MainWindowViewModelFixture.RepeateTask9Id);
            //Провереряем что исходная "Repeate task 9" задача выполнена
            await Assert.That(repeateTask9AfterTest.IsCompleted).IsEqualTo(true);
            await Assert.That(repeateTask9AfterTest.CompletedDateTime).IsNotNull();

            var result = compareLogic.Compare(repeateTask9BeforeTest, repeateTask9AfterTest);

            //Должно быть только 2 различия: IsCompleted и CompletedDateTime
            var isCompletedDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(repeateTask9AfterTest.IsCompleted));
            var completedDateTimeDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(repeateTask9AfterTest.CompletedDateTime));

            await Assert.That(isCompletedDifference).IsNotNull();
            await Assert.That(completedDateTimeDifference).IsNotNull();
            await Assert.That(isCompletedDifference.Object1).IsEqualTo(false);
            await Assert.That(isCompletedDifference.Object2).IsEqualTo(true);
            await Assert.That(completedDateTimeDifference.Object1).IsNull();
            await Assert.That(completedDateTimeDifference.Object2).IsNotNull();

            //Находим созданную склонированную повторяющейся "Repeate task 9" задачу в репозитории
            var newTaskItemViewModel = taskRepository.Tasks.Items.OrderBy(model => model.CreatedDateTime).Last();
            //Берем новую задачу из файла
            var newTask9 = GetStorageTaskItem(newTaskItemViewModel.Id);
            //Сравниваем ее с исходной до выполнения
            result = compareLogic.Compare(repeateTask9BeforeTest, newTask9);

            //Должно быть только 5 различия: Id, CreatedDateTime, UnlockedDateTime, PlannedBeginDateTime, PlannedEndDateTime
            await Assert.That(result.Differences.Count).IsEqualTo(5);
            var idDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(repeateTask9AfterTest.Id));
            var createdDateTimeDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(repeateTask9AfterTest.CreatedDateTime));
            var unlockedDateTimeDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(repeateTask9AfterTest.UnlockedDateTime));
            var plannedBeginDateTimeDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(repeateTask9AfterTest.PlannedBeginDateTime));
            var plannedEndDateTimeDifference = result.Differences.FirstOrDefault(d => d.PropertyName == nameof(repeateTask9AfterTest.PlannedEndDateTime));

            await Assert.That(idDifference).IsNotNull();
            await Assert.That(createdDateTimeDifference).IsNotNull();
            await Assert.That(unlockedDateTimeDifference).IsNotNull();
            await Assert.That(plannedBeginDateTimeDifference).IsNotNull();
            await Assert.That(plannedEndDateTimeDifference).IsNotNull();

            //У двух задач должны быть одни предки во вьюмоделе
            await Assert.That(repeateTask9ViewModel.Parents.Count >= newTaskItemViewModel.Parents.Count).IsTrue();
            if (repeateTask9ViewModel.Parents.Count > 0)
            {
                await Assert.That(repeateTask9ViewModel.Parents).Contains(newTaskItemViewModel.Parents.FirstOrDefault());
            }
        }

        /// <summary>
        /// Если у выбранной задачи нет имени, вложенные создавать нельзя
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task CreateTask_EmptyTitle_ShouldNotCreate()
        {
            var taskCountBefore = taskRepository.Tasks.Count;
            var task = new TaskItemViewModel(new TaskItem(), taskRepository); // Title is null
            mainWindowVM.CurrentTaskItem = task;
            mainWindowVM.CreateInner.Execute(null);

            await Assert.That(taskRepository.Tasks.Count).IsEqualTo(taskCountBefore);
        }

        /// <summary>
        /// Перемещение без источника должно работать как копирование
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task MoveInto_NullSource_ShouldSucceed()
        {
            var subTask = GetTask(MainWindowViewModelFixture.SubTask22Id);
            var destination = GetTask(MainWindowViewModelFixture.RootTask1Id);
            var firstParent = subTask.ParentsTasks.First();

            await subTask.MoveInto(destination, null);

            var stored = GetStorageTaskItem(destination.Id);
            await Assert.That(stored.ContainsTasks).Contains(subTask.Id);


            var stored2 = GetStorageTaskItem(firstParent.Id);
            await Assert.That(stored2.ContainsTasks).Contains(subTask.Id);
        }

        [Test]
        public async Task RemoveTask_NoParents_ShouldDeleteFile()
        {
            var task = GetTask(MainWindowViewModelFixture.RootTask1Id);
            string path = Path.Combine(fixture.DefaultTasksFolderPath, task.Id);
            await Assert.That(File.Exists(path)).IsTrue();

            await task.RemoveFunc.Invoke(null);
            await Assert.That(File.Exists(path)).IsFalse();
        }

        [Test]
        public async Task RemoveTask_HasParents_ShouldNotDeleteFile()
        {
            var parent = GetTask(MainWindowViewModelFixture.RootTask2Id);
            var child = GetTask(MainWindowViewModelFixture.SubTask22Id);
            string path = Path.Combine(fixture.DefaultTasksFolderPath, child.Id);
            await Assert.That(File.Exists(path)).IsTrue();

            await child.RemoveFunc.Invoke(parent);
            await Assert.That(File.Exists(path)).IsTrue();
        }

        [Test]
        public async Task SelectCurrentTaskMode_SyncsCorrectly()
        {
            var task = mainWindowVM.CurrentAllTasksItems.First().TaskItem;

            mainWindowVM.AllTasksMode = true;
            mainWindowVM.CurrentTaskItem = task;

            var allTasksSelected = SpinWait.SpinUntil(
                () => mainWindowVM.CurrentAllTasksItem?.TaskItem?.Id == task.Id,
                TimeSpan.FromSeconds(2));
            await Assert.That(allTasksSelected).IsTrue();

            mainWindowVM.AllTasksMode = false;
            mainWindowVM.UnlockedMode = true;

            var hasUnlockedItems = SpinWait.SpinUntil(
                () => mainWindowVM.UnlockedItems.Count > 0,
                TimeSpan.FromSeconds(2));
            await Assert.That(hasUnlockedItems).IsTrue();

            var unlockedTask = mainWindowVM.UnlockedItems.First().TaskItem;
            mainWindowVM.CurrentTaskItem = unlockedTask;

            var unlockedSelected = SpinWait.SpinUntil(
                () => mainWindowVM.CurrentUnlockedItem?.TaskItem?.Id == unlockedTask.Id,
                TimeSpan.FromSeconds(2));
            await Assert.That(unlockedSelected).IsTrue();

        }

        [Test]
        public async Task ParentTaskWithIncompleteChildAndBlocked_ShouldRemainNotAvailable()
        {
            // Arrange - Get a parent task with an incomplete child task
            var parentTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.RootTask2Id);
            var childTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.SubTask22Id);
            
            // Ensure the child task is not completed to block the parent
            if (childTask.IsCompleted == true)
            {
                childTask.IsCompleted = false;
                await TestHelpers.WaitThrottleTime();
            }
            
            // Verify initial state - parent should be blocked by incomplete child
            var parentStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, parentTask.Id);
            await Assert.That(parentStored.IsCanBeCompleted).IsFalse();
            await Assert.That(parentStored.UnlockedDateTime).IsNull();

            // Get a task to use as blocker
            var blockerTask = TestHelpers.GetTask(mainWindowVM, MainWindowViewModelFixture.RootTask1Id);

            // Act - Create blocking relation (blockerTask blocks parentTask)
            parentTask.BlockBy(blockerTask);
            await TestHelpers.WaitThrottleTime();

            // Assert - Parent task should remain not available
            var updatedParent = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, parentTask.Id);
            await Assert.That(updatedParent.IsCanBeCompleted).IsFalse();
            await Assert.That(updatedParent.UnlockedDateTime).IsNull();
        }

        [Test]
        public async Task BlockedTask_CompletionIsPrevented()
        {
            var blocked = GetTask(MainWindowViewModelFixture.BlockedTask5Id);
            await Assert.That(blocked.IsCanBeCompleted).IsFalse();
        }

        [Test]
        public async Task CloneInto_MultipleParents_ResultsCorrect()
        {
            var src = GetTask(MainWindowViewModelFixture.ClonedTask8Id);
            var dest1 = GetTask(MainWindowViewModelFixture.RootTask1Id);
            var dest2 = GetTask(MainWindowViewModelFixture.RootTask2Id);
            await dest1!.CopyInto(dest2!);

            var clone = await src!.CloneInto(dest1);

            await Assert.That(clone.Parents).HasSingleItem();
            await Assert.That(clone.Parents).Contains(dest1.Id);
            await Assert.That(clone.Parents).DoesNotContain(dest2!.Id);
        }

        [Test]
        public async Task TreeCommand_ExpandNodeAndDescendants_ExpandsWholeSubtree()
        {
            var (rootWrapper, childWrapper, grandchildWrapper) = await CreateThreeLevelTreeCommandBranchAsync();

            rootWrapper.IsExpanded = false;
            childWrapper.IsExpanded = false;
            grandchildWrapper.IsExpanded = false;

            mainWindowVM.ExpandNodeAndDescendants(rootWrapper);

            await Assert.That(rootWrapper.IsExpanded).IsTrue();
            await Assert.That(childWrapper.IsExpanded).IsTrue();
            await Assert.That(grandchildWrapper.IsExpanded).IsTrue();
        }

        [Test]
        public async Task TreeCommand_CollapseNodeDescendants_KeepsCurrentAndCollapsesChildren()
        {
            var (rootWrapper, childWrapper, grandchildWrapper) = await CreateThreeLevelTreeCommandBranchAsync();

            rootWrapper.IsExpanded = true;
            childWrapper.IsExpanded = true;
            grandchildWrapper.IsExpanded = true;

            mainWindowVM.CollapseNodeDescendants(rootWrapper);

            await Assert.That(rootWrapper.IsExpanded).IsFalse();
            await Assert.That(childWrapper.IsExpanded).IsFalse();
            await Assert.That(grandchildWrapper.IsExpanded).IsFalse();
        }

        [Test]
        public async Task TreeCommand_ExpandAllNodes_ExpandsAllRootsAndDescendants()
        {
            var (rootWrapper, childWrapper, grandchildWrapper) = await CreateThreeLevelTreeCommandBranchAsync();
            var siblingRoot = mainWindowVM.CurrentAllTasksItems.First(wrapper => wrapper != rootWrapper);

            rootWrapper.IsExpanded = false;
            childWrapper.IsExpanded = false;
            grandchildWrapper.IsExpanded = false;
            siblingRoot.IsExpanded = false;

            mainWindowVM.ExpandAllNodes(mainWindowVM.CurrentAllTasksItems);

            await Assert.That(rootWrapper.IsExpanded).IsTrue();
            await Assert.That(childWrapper.IsExpanded).IsTrue();
            await Assert.That(grandchildWrapper.IsExpanded).IsTrue();
            await Assert.That(siblingRoot.IsExpanded).IsTrue();
        }

        [Test]
        public async Task TreeCommand_CollapseAllNodes_CollapsesAllRootsAndDescendants()
        {
            var (rootWrapper, childWrapper, grandchildWrapper) = await CreateThreeLevelTreeCommandBranchAsync();
            var siblingRoot = mainWindowVM.CurrentAllTasksItems.First(wrapper => wrapper != rootWrapper);

            rootWrapper.IsExpanded = true;
            childWrapper.IsExpanded = true;
            grandchildWrapper.IsExpanded = true;
            siblingRoot.IsExpanded = true;

            mainWindowVM.CollapseAllNodes(mainWindowVM.CurrentAllTasksItems);

            await Assert.That(rootWrapper.IsExpanded).IsFalse();
            await Assert.That(childWrapper.IsExpanded).IsFalse();
            await Assert.That(grandchildWrapper.IsExpanded).IsFalse();
            await Assert.That(siblingRoot.IsExpanded).IsFalse();
        }

        [Test]
        public async Task TreeCommand_NullContext_IsNoOp()
        {
            var rootWrapper = mainWindowVM.CurrentAllTasksItems.First();
            rootWrapper.IsExpanded = false;

            mainWindowVM.ExpandNodeAndDescendants(null);
            mainWindowVM.CollapseNodeDescendants(null);
            mainWindowVM.ExpandAllNodes(null);
            mainWindowVM.CollapseAllNodes(null);

            await Assert.That(rootWrapper.IsExpanded).IsFalse();
        }
    }
}
