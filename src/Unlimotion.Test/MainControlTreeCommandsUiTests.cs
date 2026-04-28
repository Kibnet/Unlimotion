using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion.Test;

[ParallelLimiter<SharedUiStateParallelLimit>]
public class MainControlTreeCommandsUiTests
{
    [Test]
    public async Task TreeCommandUi_Hotkey_UsesStickyActiveTabTreeAfterFocusMoves()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;
                vm.CollapseAllNodes(vm.CurrentAllTasksItems);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                var completedCheckBox = view.GetVisualDescendants()
                    .OfType<CheckBox>()
                    .First(control => string.Equals(control.Content?.ToString(), "Completed", StringComparison.Ordinal));

                await Assert.That(allTasksTree).IsNotNull();
                await ClickControlAsync(window, allTasksTree!);
                completedCheckBox.Focus();

                PressHotkey(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Alt);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(vm.CurrentAllTasksItems.All(IsExpandedRecursive)).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_Hotkey_IsIgnoredWhileTextInputFocused()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;
                vm.DetailsAreOpen = true;
                TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RootTask2Id);
                vm.CollapseAllNodes(vm.CurrentAllTasksItems);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                var titleTextBox = view.FindControl<TextBox>("CurrentTaskTitleTextBox");

                await Assert.That(allTasksTree).IsNotNull();
                await Assert.That(titleTextBox).IsNotNull();
                await ClickControlAsync(window, allTasksTree!);
                titleTextBox!.Focus();

                PressHotkey(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Alt);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(vm.CurrentAllTasksItems.All(IsCollapsedRecursive)).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_Hotkey_UsesSelectedItem_NotLastClickedWrapper()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;

                var rootTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
                var childTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.SubTask22Id);
                var grandchildTask = await vm.taskRepository!.AddChild(childTask);
                grandchildTask.Title = "Hotkey selected item grandchild";
                await TestHelpers.WaitThrottleTime();

                var rootWrapper = vm.FindTaskWrapperViewModel(rootTask!, vm.CurrentAllTasksItems);
                var childWrapper = vm.FindTaskWrapperViewModel(childTask!, vm.CurrentAllTasksItems);
                var grandchildWrapper = vm.FindTaskWrapperViewModel(grandchildTask, vm.CurrentAllTasksItems);

                await Assert.That(rootWrapper).IsNotNull();
                await Assert.That(childWrapper).IsNotNull();
                await Assert.That(grandchildWrapper).IsNotNull();

                rootWrapper!.IsExpanded = true;
                childWrapper!.IsExpanded = true;
                grandchildWrapper!.IsExpanded = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();

                var rootControl = allTasksTree!.GetVisualDescendants()
                    .OfType<Control>()
                    .First(control => control.DataContext is TaskWrapperViewModel wrapper && wrapper.TaskItem.Id == rootTask.Id);

                await ClickControlAsync(window, rootControl, MouseButton.Right);
                vm.CurrentAllTasksItem = childWrapper;
                Dispatcher.UIThread.RunJobs();

                PressHotkey(window, Key.Left, PhysicalKey.ArrowLeft, RawInputModifiers.Control | RawInputModifiers.Shift);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(rootWrapper.IsExpanded).IsTrue();
                await Assert.That(childWrapper.IsExpanded).IsFalse();
                await Assert.That(grandchildWrapper.IsExpanded).IsFalse();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_Hotkey_UsesVisibleTabTreeAfterSwitchWithoutAdditionalTreeClick()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.CollapseAllNodes(vm.CurrentAllTasksItems);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();
                await ClickControlAsync(window, allTasksTree!);

                SelectTab(view, 2);

                var unlockedReady = WaitFor(() => vm.UnlockedItems.Any());
                await Assert.That(unlockedReady).IsTrue();
                vm.CollapseAllNodes(vm.UnlockedItems);
                Dispatcher.UIThread.RunJobs();

                PressHotkey(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Alt);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(vm.UnlockedItems.All(IsExpandedRecursive)).IsTrue();
                await Assert.That(vm.CurrentAllTasksItems.All(IsCollapsedRecursive)).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_Hotkey_UsesVisibleTabTreeWithoutPriorTreeActivation()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                SelectTab(view, 2);

                var unlockedReady = WaitFor(() => vm.UnlockedItems.Any());
                await Assert.That(unlockedReady).IsTrue();
                vm.CollapseAllNodes(vm.UnlockedItems);
                Dispatcher.UIThread.RunJobs();

                PressHotkey(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Alt);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(vm.UnlockedItems.All(IsExpandedRecursive)).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_LastCreatedTab_HotkeyAndContextMenu_Work()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.CollapseAllNodes(vm.LastCreatedItems);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var tabControl = view.GetVisualDescendants().OfType<TabControl>().First();
                tabControl.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();

                var lastCreatedTree = view.FindControl<TreeView>("LastCreatedTree");
                await Assert.That(lastCreatedTree).IsNotNull();

                await ClickControlAsync(window, lastCreatedTree!);
                PressHotkey(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Alt);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(vm.LastCreatedItems.All(IsExpandedRecursive)).IsTrue();

                await ClickControlAsync(window, lastCreatedTree, MouseButton.Right);
                var collapseAllMenuItem = lastCreatedTree.ContextMenu!.Items.OfType<MenuItem>().Last();
                InvokeMenuItemClick(collapseAllMenuItem);

                await Assert.That(vm.LastCreatedItems.All(IsCollapsedRecursive)).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_LastCreatedTab_Hotkey_WorksAfterSwitchFromAllTasksHeaderClick()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.CollapseAllNodes(vm.CurrentAllTasksItems);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();
                await ClickControlAsync(window, allTasksTree!);

                PressHotkey(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Alt);
                Dispatcher.UIThread.RunJobs();
                PressHotkey(window, Key.Left, PhysicalKey.ArrowLeft, RawInputModifiers.Control | RawInputModifiers.Alt);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(vm.CurrentAllTasksItems.All(IsCollapsedRecursive)).IsTrue();

                await ClickTabHeaderAsync(window, view, "Last Created");
                var lastCreatedReady = WaitFor(() => vm.LastCreatedItems.Any());
                await Assert.That(lastCreatedReady).IsTrue();
                vm.CollapseAllNodes(vm.LastCreatedItems);
                Dispatcher.UIThread.RunJobs();

                PressHotkey(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Alt);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(vm.LastCreatedItems.All(IsExpandedRecursive)).IsTrue();
                await Assert.That(vm.CurrentAllTasksItems.All(IsCollapsedRecursive)).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_Hotkey_WorksOnFirstPressImmediatelyAfterTabHeaderClick()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();
                await ClickControlAsync(window, allTasksTree!);

                await ClickTabHeaderAsync(window, view, "Last Created");
                var lastCreatedReady = WaitFor(() => vm.LastCreatedItems.Any());
                await Assert.That(lastCreatedReady).IsTrue();

                await ClickTabHeaderAsync(window, view, "All Tasks");
                Dispatcher.UIThread.RunJobs();

                vm.CollapseAllNodes(vm.CurrentAllTasksItems);
                vm.CollapseAllNodes(vm.LastCreatedItems);
                Dispatcher.UIThread.RunJobs();

                await ClickTabHeaderAsync(window, view, "Last Created");
                PressHotkey(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Alt);

                var expandedLastCreated = WaitFor(() => vm.LastCreatedItems.Any() && vm.LastCreatedItems.All(IsExpandedRecursive));
                await Assert.That(expandedLastCreated).IsTrue();
                await Assert.That(vm.CurrentAllTasksItems.All(IsCollapsedRecursive)).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_LastCreatedTab_CurrentCommands_WorkOnClickedItem()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();

                var rootTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
                var childTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.SubTask22Id);
                var grandchildTask = await vm.taskRepository!.AddChild(childTask);
                grandchildTask.Title = "LastCreated current command grandchild";
                await TestHelpers.WaitThrottleTime();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var tabControl = view.GetVisualDescendants().OfType<TabControl>().First();
                tabControl.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();

                var lastCreatedTree = view.FindControl<TreeView>("LastCreatedTree");
                await Assert.That(lastCreatedTree).IsNotNull();

                var rootWrapper = vm.FindTaskWrapperViewModel(rootTask!, vm.LastCreatedItems);
                var childWrapper = vm.FindTaskWrapperViewModel(childTask!, vm.LastCreatedItems);
                var grandchildWrapper = vm.FindTaskWrapperViewModel(grandchildTask, vm.LastCreatedItems);

                await Assert.That(rootWrapper).IsNotNull();
                await Assert.That(childWrapper).IsNotNull();
                await Assert.That(grandchildWrapper).IsNotNull();

                rootWrapper!.IsExpanded = true;
                childWrapper!.IsExpanded = false;
                grandchildWrapper!.IsExpanded = false;
                Dispatcher.UIThread.RunJobs();

                var childControl = lastCreatedTree!.GetVisualDescendants()
                    .OfType<Control>()
                    .First(control => control.DataContext is TaskWrapperViewModel wrapper && wrapper.TaskItem.Id == childTask.Id);

                await ClickControlAsync(window, childControl);
                await Assert.That(vm.CurrentLastCreated?.TaskItem.Id).IsEqualTo(childTask.Id);

                PressHotkey(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Shift);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(childWrapper.IsExpanded).IsTrue();
                await Assert.That(grandchildWrapper.IsExpanded).IsTrue();

                childWrapper.IsExpanded = false;
                grandchildWrapper.IsExpanded = false;
                await ClickControlAsync(window, childControl, MouseButton.Right);
                var expandCurrentMenuItem = lastCreatedTree.ContextMenu!.Items.OfType<MenuItem>().First();
                InvokeMenuItemClick(expandCurrentMenuItem);

                await Assert.That(childWrapper.IsExpanded).IsTrue();
                await Assert.That(grandchildWrapper.IsExpanded).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_LastUpdatedTab_Hotkey_WorksAfterSwitchFromAllTasksHeaderClick()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.CollapseAllNodes(vm.CurrentAllTasksItems);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();
                await ClickControlAsync(window, allTasksTree!);

                await ClickTabHeaderAsync(window, view, "All Tasks");
                PressHotkey(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Alt);
                Dispatcher.UIThread.RunJobs();
                PressHotkey(window, Key.Left, PhysicalKey.ArrowLeft, RawInputModifiers.Control | RawInputModifiers.Alt);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(vm.CurrentAllTasksItems.All(IsCollapsedRecursive)).IsTrue();

                await ClickTabHeaderAsync(window, view, "Last Updated");
                var lastUpdatedReady = WaitFor(() => vm.LastUpdatedItems.Any());
                await Assert.That(lastUpdatedReady).IsTrue();
                vm.CollapseAllNodes(vm.LastUpdatedItems);
                Dispatcher.UIThread.RunJobs();

                PressHotkey(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Alt);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(vm.LastUpdatedItems.All(IsExpandedRecursive)).IsTrue();
                await Assert.That(vm.CurrentAllTasksItems.All(IsCollapsedRecursive)).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CreateTaskUi_CtrlEnter_CreatesSiblingForSelectedTaskInLastUpdatedTab()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();

                var selectedTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
                await Assert.That(selectedTask).IsNotNull();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                await ClickTabHeaderAsync(window, view, "Last Updated");
                var lastUpdatedReady = WaitFor(() => vm.LastUpdatedItems.Any());
                await Assert.That(lastUpdatedReady).IsTrue();

                var lastUpdatedTree = view.FindControl<TreeView>("LastUpdatedTree");
                await Assert.That(lastUpdatedTree).IsNotNull();

                vm.ExpandAllNodes(vm.LastUpdatedItems);
                Dispatcher.UIThread.RunJobs();

                var selectedControl = FindWrapperControl(lastUpdatedTree!, selectedTask!.Id);
                await ClickControlAsync(window, selectedControl);

                await Assert.That(vm.CurrentLastUpdated?.TaskItem.Id).IsEqualTo(selectedTask.Id);
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(selectedTask.Id);

                vm.AllTasksMode = false;
                vm.LastCreatedMode = false;
                vm.LastUpdatedMode = true;
                vm.UnlockedMode = false;
                vm.CompletedMode = false;
                vm.ArchivedMode = false;
                vm.LastOpenedMode = false;

                var taskCountBefore = vm.taskRepository!.Tasks.Count;
                PressHotkey(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.Control);

                var created = WaitFor(() =>
                    vm.taskRepository.Tasks.Count == taskCountBefore + 1 &&
                    vm.CurrentTaskItem != null &&
                    vm.CurrentTaskItem.Id != selectedTask.Id);
                await Assert.That(created).IsTrue();
                await Assert.That(vm.CurrentTaskItem!.Parents).IsEquivalentTo(selectedTask.Parents);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_ShiftDelete_RemovesSelectedLastUpdatedTreeItem()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                ((NotificationManagerWrapperMock)vm.ManagerWrapper).AskResult = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                await ClickTabHeaderAsync(window, view, "Last Updated");
                var lastUpdatedReady = WaitFor(() => vm.LastUpdatedItems.Any());
                await Assert.That(lastUpdatedReady).IsTrue();

                var lastUpdatedTree = view.FindControl<TreeView>("LastUpdatedTree");
                await Assert.That(lastUpdatedTree).IsNotNull();

                var root4Control = FindWrapperControl(lastUpdatedTree!, MainWindowViewModelFixture.RootTask4Id);
                await ClickControlAsync(window, root4Control);
                await Assert.That(vm.CurrentLastUpdated?.TaskItem.Id).IsEqualTo(MainWindowViewModelFixture.RootTask4Id);

                PressHotkey(window, Key.Delete, PhysicalKey.Delete, RawInputModifiers.Shift);
                await TestHelpers.WaitThrottleTime();

                await Assert.That(TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.RootTask4Id)).IsNull();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments(2, "UnlockedTree", MainWindowViewModelFixture.RootTask2Id, MainWindowViewModelFixture.SubTask22Id)]
    [Arguments(3, "CompletedTree", MainWindowViewModelFixture.CompletedTaskId, MainWindowViewModelFixture.CompletedTaskId)]
    [Arguments(4, "ArchivedTree", MainWindowViewModelFixture.ArchivedTask1Id, MainWindowViewModelFixture.ArchivedTask11Id)]
    [Arguments(5, "LastOpenedTree", MainWindowViewModelFixture.RootTask2Id, MainWindowViewModelFixture.SubTask22Id)]
    public async Task TreeCommandUi_NonAllTasksTabs_CurrentAndAllCommands_Work(
        int tabIndex,
        string treeName,
        string rootTaskId,
        string childTaskId)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                await PrepareTreeScenarioAsync(vm, treeName);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                SelectTab(view, tabIndex);

                var tree = view.FindControl<TreeView>(treeName);
                await Assert.That(tree).IsNotNull();

                var rootsReady = WaitFor(() => GetRootsForTree(vm, treeName).Any());
                await Assert.That(rootsReady).IsTrue();

                var rootWrapper = WaitForWrapper(() => FindWrapper(vm, treeName, rootTaskId));
                var childWrapper = WaitForWrapper(() => FindDescendantWrapper(vm, treeName, rootTaskId, childTaskId));

                await Assert.That(rootWrapper).IsNotNull();
                await Assert.That(childWrapper).IsNotNull();

                rootWrapper!.IsExpanded = true;
                childWrapper!.IsExpanded = false;
                Dispatcher.UIThread.RunJobs();

                var currentTaskId = childWrapper.TaskItem.Id;
                var currentControl = FindWrapperControl(tree!, currentTaskId);
                await ClickControlAsync(window, currentControl);
                await Assert.That(GetCurrentWrapperForTree(vm, treeName)?.TaskItem.Id).IsEqualTo(currentTaskId);

                PressHotkey(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Shift);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(childWrapper.IsExpanded).IsTrue();

                childWrapper.IsExpanded = false;
                Dispatcher.UIThread.RunJobs();

                await ClickControlAsync(window, currentControl, MouseButton.Right);
                var expandCurrentMenuItem = tree.ContextMenu!.Items.OfType<MenuItem>().First();
                InvokeMenuItemClick(expandCurrentMenuItem);

                await Assert.That(childWrapper.IsExpanded).IsTrue();

                var roots = GetRootsForTree(vm, treeName).ToArray();
                vm.CollapseAllNodes(roots);
                Dispatcher.UIThread.RunJobs();

                await ClickControlAsync(window, tree);
                PressHotkey(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Alt);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(roots.All(IsExpandedRecursive)).IsTrue();

                await ClickControlAsync(window, tree, MouseButton.Right);
                var collapseAllMenuItem = tree.ContextMenu.Items.OfType<MenuItem>().Last();
                InvokeMenuItemClick(collapseAllMenuItem);

                await Assert.That(roots.All(IsCollapsedRecursive)).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_ContextMenuClick_UsesClickedRelationItemEvenWhenTextInputFocused()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;
                vm.DetailsAreOpen = true;

                var currentTask = TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RootTask2Id);
                var childTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.SubTask22Id);
                var grandchildTask = await vm.taskRepository!.AddChild(childTask);
                grandchildTask.Title = "Relation tree grandchild";
                await TestHelpers.WaitThrottleTime();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var relationTree = view.FindControl<TreeView>("CurrentItemContainsTree");
                var titleTextBox = view.FindControl<TextBox>("CurrentTaskTitleTextBox");
                await Assert.That(relationTree).IsNotNull();
                await Assert.That(titleTextBox).IsNotNull();
                await Assert.That(relationTree!.ContextMenu).IsNotNull();
                await Assert.That(relationTree.ContextMenu!.Items.OfType<MenuItem>().Count()).IsEqualTo(4);

                var childWrapper = vm.CurrentItemContains.SubTasks.First(wrapper => wrapper.TaskItem.Id == childTask.Id);
                var grandchildWrapper = childWrapper.SubTasks.First(wrapper => wrapper.TaskItem.Id == grandchildTask.Id);
                childWrapper.IsExpanded = false;
                grandchildWrapper.IsExpanded = false;

                var childControl = relationTree.GetVisualDescendants()
                    .OfType<Control>()
                    .First(control => control.DataContext is TaskWrapperViewModel wrapper && wrapper.TaskItem.Id == childTask.Id);

                await ClickControlAsync(window, childControl, MouseButton.Right);
                titleTextBox!.Focus();
                var expandCurrentMenuItem = relationTree.ContextMenu.Items.OfType<MenuItem>().First();
                InvokeMenuItemClick(expandCurrentMenuItem);

                await Assert.That(childWrapper.IsExpanded).IsTrue();
                await Assert.That(grandchildWrapper.IsExpanded).IsTrue();
                await Assert.That(vm.CurrentTaskItem?.Id).IsEqualTo(currentTask!.Id);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_Hotkey_UsesFocusedRelationTreeWithoutPointerActivation()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;
                vm.DetailsAreOpen = true;

                var currentTask = TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RootTask2Id);
                var childTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.SubTask22Id);
                var grandchildTask = await vm.taskRepository!.AddChild(childTask);
                grandchildTask.Title = "Focused relation tree grandchild";
                await TestHelpers.WaitThrottleTime();

                var mainRootWrapper = vm.FindTaskWrapperViewModel(currentTask!, vm.CurrentAllTasksItems);
                var mainChildWrapper = vm.FindTaskWrapperViewModel(childTask!, vm.CurrentAllTasksItems);
                await Assert.That(mainRootWrapper).IsNotNull();
                await Assert.That(mainChildWrapper).IsNotNull();

                mainRootWrapper!.IsExpanded = false;
                mainChildWrapper!.IsExpanded = false;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                var relationTree = view.FindControl<TreeView>("CurrentItemContainsTree");
                await Assert.That(allTasksTree).IsNotNull();
                await Assert.That(relationTree).IsNotNull();

                var relationChildWrapper = vm.CurrentItemContains.SubTasks.First(wrapper => wrapper.TaskItem.Id == childTask.Id);
                var relationGrandchildWrapper = relationChildWrapper.SubTasks.First(wrapper => wrapper.TaskItem.Id == grandchildTask.Id);
                relationChildWrapper.IsExpanded = false;
                relationGrandchildWrapper.IsExpanded = false;
                Dispatcher.UIThread.RunJobs();

                Dispatcher.UIThread.RunJobs();

                var focused = relationTree.Focus();
                Dispatcher.UIThread.RunJobs();
                await Assert.That(focused).IsTrue();

                var activeTaskTreeField = typeof(MainControl).GetField("_activeTaskTree", BindingFlags.Instance | BindingFlags.NonPublic);
                await Assert.That(activeTaskTreeField).IsNotNull();
                activeTaskTreeField!.SetValue(view, allTasksTree);

                PressHotkey(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Alt);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(relationChildWrapper.IsExpanded).IsTrue();
                await Assert.That(relationGrandchildWrapper.IsExpanded).IsTrue();
                await Assert.That(mainRootWrapper.IsExpanded).IsFalse();
                await Assert.That(mainChildWrapper.IsExpanded).IsFalse();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_HotkeyRouting_PrefersFocusedRelationTreeOverStaleActiveTree()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;
                vm.DetailsAreOpen = true;
                TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RootTask2Id);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                var relationTree = view.FindControl<TreeView>("CurrentItemContainsTree");
                await Assert.That(allTasksTree).IsNotNull();
                await Assert.That(relationTree).IsNotNull();

                var focused = relationTree!.Focus();
                Dispatcher.UIThread.RunJobs();
                await Assert.That(focused).IsTrue();

                var activeTaskTreeField = typeof(MainControl).GetField("_activeTaskTree", BindingFlags.Instance | BindingFlags.NonPublic);
                var tryGetHotkeyTreeMethod = typeof(MainControl).GetMethod("TryGetHotkeyTree", BindingFlags.Instance | BindingFlags.NonPublic);
                await Assert.That(activeTaskTreeField).IsNotNull();
                await Assert.That(tryGetHotkeyTreeMethod).IsNotNull();

                activeTaskTreeField!.SetValue(view, allTasksTree);
                var args = new object?[] { null };
                var resolved = (bool)tryGetHotkeyTreeMethod!.Invoke(view, args)!;

                await Assert.That(resolved).IsTrue();
                await Assert.That(args[0]).IsSameReferenceAs(relationTree);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_ContextMenu_UsesPlacementTargetWithoutStoredTreeContext()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                SelectTab(view, 2);
                var unlockedReady = WaitFor(() => vm.UnlockedItems.Any());
                await Assert.That(unlockedReady).IsTrue();

                var unlockedTree = view.FindControl<TreeView>("UnlockedTree");
                await Assert.That(unlockedTree).IsNotNull();
                await Assert.That(unlockedTree!.ContextMenu).IsNotNull();

                vm.CollapseAllNodes(vm.UnlockedItems);
                Dispatcher.UIThread.RunJobs();

                ClearStoredTreeCommandContext(view);
                unlockedTree.ContextMenu!.PlacementTarget = unlockedTree;
                var expandAllMenuItem = unlockedTree.ContextMenu.Items
                    .OfType<MenuItem>()
                    .First(item => string.Equals(item.Tag?.ToString(), "ExpandAll", StringComparison.Ordinal));
                InvokeMenuItemClick(expandAllMenuItem);

                await Assert.That(vm.UnlockedItems.All(IsExpandedRecursive)).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_ContextMenu_DisplaysHotkeysForTreeCommands()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();
                await Assert.That(allTasksTree!.ContextMenu).IsNotNull();

                var menuItems = allTasksTree.ContextMenu!.Items
                    .OfType<MenuItem>()
                    .ToDictionary(item => item.Tag?.ToString() ?? string.Empty, item => item);

                await Assert.That(menuItems["ExpandCurrentNested"].InputGesture?.ToString()).IsEqualTo("Ctrl+Shift+Right");
                await Assert.That(menuItems["CollapseCurrentNested"].InputGesture?.ToString()).IsEqualTo("Ctrl+Shift+Left");
                await Assert.That(menuItems["ExpandAll"].InputGesture?.ToString()).IsEqualTo("Ctrl+Alt+Right");
                await Assert.That(menuItems["CollapseAll"].InputGesture?.ToString()).IsEqualTo("Ctrl+Alt+Left");
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_ContextMenu_UsesPlacementTargetItemWithoutStoredContextForCurrentCommand()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();

                var rootTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
                var childTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.SubTask22Id);
                var grandchildTask = await vm.taskRepository!.AddChild(childTask);
                grandchildTask.Title = "PlacementTarget current command grandchild";
                await TestHelpers.WaitThrottleTime();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                SelectTab(view, 1);

                var lastCreatedTree = view.FindControl<TreeView>("LastCreatedTree");
                await Assert.That(lastCreatedTree).IsNotNull();
                await Assert.That(lastCreatedTree!.ContextMenu).IsNotNull();

                var rootWrapper = vm.FindTaskWrapperViewModel(rootTask!, vm.LastCreatedItems);
                var childWrapper = vm.FindTaskWrapperViewModel(childTask!, vm.LastCreatedItems);
                var grandchildWrapper = vm.FindTaskWrapperViewModel(grandchildTask, vm.LastCreatedItems);

                await Assert.That(rootWrapper).IsNotNull();
                await Assert.That(childWrapper).IsNotNull();
                await Assert.That(grandchildWrapper).IsNotNull();

                rootWrapper!.IsExpanded = true;
                childWrapper!.IsExpanded = false;
                grandchildWrapper!.IsExpanded = false;
                Dispatcher.UIThread.RunJobs();

                var childControl = lastCreatedTree.GetVisualDescendants()
                    .OfType<Control>()
                    .First(control => control.DataContext is TaskWrapperViewModel wrapper && wrapper.TaskItem.Id == childTask.Id);

                lastCreatedTree.SelectedItems?.Clear();
                lastCreatedTree.SelectedItem = null;
                vm.CurrentLastCreated = null!;

                ClearStoredTreeCommandContext(view);
                lastCreatedTree.ContextMenu!.PlacementTarget = childControl;
                var expandCurrentMenuItem = lastCreatedTree.ContextMenu.Items
                    .OfType<MenuItem>()
                    .First(item => string.Equals(item.Tag?.ToString(), "ExpandCurrentNested", StringComparison.Ordinal));
                InvokeMenuItemClick(expandCurrentMenuItem);

                await Assert.That(childWrapper.IsExpanded).IsTrue();
                await Assert.That(grandchildWrapper.IsExpanded).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_CtrlA_SelectsAllItemsInActiveTree()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;
                vm.ExpandAllNodes(vm.CurrentAllTasksItems);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();

                await ClickControlAsync(window, allTasksTree!);
                PressHotkey(window, Key.A, PhysicalKey.A, RawInputModifiers.Control);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(GetSelectedWrappers(allTasksTree).Count).IsEqualTo(CountWrappers(vm.CurrentAllTasksItems));
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_ShiftDelete_RemovesSelectedMainTreeItems()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                ((NotificationManagerWrapperMock)vm.ManagerWrapper).AskResult = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();

                var root1Control = FindWrapperControl(allTasksTree!, MainWindowViewModelFixture.RootTask1Id);
                var root4Control = FindWrapperControl(allTasksTree, MainWindowViewModelFixture.RootTask4Id);

                await ClickControlAsync(window, root1Control);
                await ClickControlAsync(window, root4Control, modifiers: RawInputModifiers.Control);
                await Assert.That(GetSelectedWrappers(allTasksTree).Count).IsEqualTo(2);

                PressHotkey(window, Key.Delete, PhysicalKey.Delete, RawInputModifiers.Shift);
                await TestHelpers.WaitThrottleTime();

                await Assert.That(TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.RootTask1Id)).IsNull();
                await Assert.That(TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.RootTask4Id)).IsNull();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_ShiftDelete_IgnoresRelationTree()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;
                vm.DetailsAreOpen = true;
                TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RootTask2Id);
                ((NotificationManagerWrapperMock)vm.ManagerWrapper).AskResult = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var relationTree = view.FindControl<TreeView>("CurrentItemContainsTree");
                await Assert.That(relationTree).IsNotNull();

                var childControl = FindWrapperControl(relationTree!, MainWindowViewModelFixture.SubTask22Id);
                await ClickControlAsync(window, childControl);
                await Assert.That(GetSelectedWrappers(relationTree).Count).IsEqualTo(1);

                PressHotkey(window, Key.Delete, PhysicalKey.Delete, RawInputModifiers.Shift);
                await TestHelpers.WaitThrottleTime();

                var rootStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.RootTask2Id);
                var childStored = TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.SubTask22Id);
                await Assert.That(rootStored!.ContainsTasks).Contains(MainWindowViewModelFixture.SubTask22Id);
                await Assert.That(childStored).IsNotNull();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_RightClick_PreservesSelectedBatch_AndCollapsesOnUnselectedItem()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();

                var root1Control = FindWrapperControl(allTasksTree!, MainWindowViewModelFixture.RootTask1Id);
                var root3Control = FindWrapperControl(allTasksTree, MainWindowViewModelFixture.RootTask3Id);
                var root4Control = FindWrapperControl(allTasksTree, MainWindowViewModelFixture.RootTask4Id);

                await ClickControlAsync(window, root1Control);
                await ClickControlAsync(window, root3Control, modifiers: RawInputModifiers.Control);
                var selectedIds = GetSelectedWrappers(allTasksTree).Select(wrapper => wrapper.TaskItem.Id).ToHashSet();
                await Assert.That(selectedIds.Count).IsEqualTo(2);
                await Assert.That(selectedIds).Contains(MainWindowViewModelFixture.RootTask1Id);
                await Assert.That(selectedIds).Contains(MainWindowViewModelFixture.RootTask3Id);

                await ClickControlAsync(window, root1Control, MouseButton.Right);
                selectedIds = GetSelectedWrappers(allTasksTree).Select(wrapper => wrapper.TaskItem.Id).ToHashSet();
                await Assert.That(selectedIds.Count).IsEqualTo(2);
                await Assert.That(selectedIds).Contains(MainWindowViewModelFixture.RootTask1Id);
                await Assert.That(selectedIds).Contains(MainWindowViewModelFixture.RootTask3Id);

                await ClickControlAsync(window, root4Control, MouseButton.Right);
                var selectedAfterCollapse = GetSelectedWrappers(allTasksTree);
                await Assert.That(selectedAfterCollapse.Count).IsEqualTo(1);
                await Assert.That(selectedAfterCollapse[0].TaskItem.Id).IsEqualTo(MainWindowViewModelFixture.RootTask4Id);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeDragUi_DragPreparation_PreservesExistingMultiSelectionVisualState()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();

                var root1Control = FindWrapperControl(allTasksTree!, MainWindowViewModelFixture.RootTask1Id);
                var root3Control = FindWrapperControl(allTasksTree, MainWindowViewModelFixture.RootTask3Id);

                await ClickControlAsync(window, root1Control);
                await ClickControlAsync(window, root3Control, modifiers: RawInputModifiers.Control);

                var selectedBefore = GetSelectedWrappers(allTasksTree);
                await Assert.That(selectedBefore.Count).IsEqualTo(2);

                var root1Item = FindWrapperTreeItem(allTasksTree, MainWindowViewModelFixture.RootTask1Id);
                var root3Item = FindWrapperTreeItem(allTasksTree, MainWindowViewModelFixture.RootTask3Id);
                await Assert.That(root1Item.IsSelected).IsTrue();
                await Assert.That(root3Item.IsSelected).IsTrue();

                var collectionChangeCount = 0;
                var mouseIsDown = false;
                var selectedItemsNotifier = allTasksTree.SelectedItems as INotifyCollectionChanged;
                await Assert.That(selectedItemsNotifier).IsNotNull();
                NotifyCollectionChangedEventHandler handler = (_, _) => collectionChangeCount++;
                selectedItemsNotifier!.CollectionChanged += handler;
                try
                {
                    var startPoint = GetControlCenterPoint(window, root1Control);
                    var movePoint = new Point(startPoint.X + 12, startPoint.Y + 12);

                    window.MouseDown(startPoint, MouseButton.Left, RawInputModifiers.None);
                    mouseIsDown = true;
                    Dispatcher.UIThread.RunJobs();

                    await Assert.That(GetSelectedWrappers(allTasksTree).Count).IsEqualTo(2);
                    await Assert.That(root1Item.IsSelected).IsTrue();
                    await Assert.That(root3Item.IsSelected).IsTrue();

                    window.MouseMove(movePoint, RawInputModifiers.LeftMouseButton);
                    Dispatcher.UIThread.RunJobs();

                    await Assert.That(GetSelectedWrappers(allTasksTree).Count).IsEqualTo(2);
                    await Assert.That(root1Item.IsSelected).IsTrue();
                    await Assert.That(root3Item.IsSelected).IsTrue();

                    window.MouseUp(movePoint, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                    mouseIsDown = false;
                    Dispatcher.UIThread.RunJobs();
                }
                finally
                {
                    if (mouseIsDown && window is { } topLevel)
                    {
                        var releasePoint = GetControlCenterPoint(topLevel, root1Control);
                        topLevel.MouseUp(releasePoint, MouseButton.Left, RawInputModifiers.None);
                        Dispatcher.UIThread.RunJobs();
                    }

                    selectedItemsNotifier.CollectionChanged -= handler;
                }

                await Assert.That(collectionChangeCount).IsEqualTo(0);
                await Assert.That(GetSelectedWrappers(allTasksTree).Count).IsEqualTo(2);
                await Assert.That(root1Item.IsSelected).IsTrue();
                await Assert.That(root3Item.IsSelected).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    private static Window CreateWindow(Control content)
    {
        return new Window
        {
            Width = 1400,
            Height = 900,
            Content = content
        };
    }

    private static async Task ClickControlAsync(
        Window window,
        Control control,
        MouseButton button = MouseButton.Left,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = GetControlCenterPoint(window, control);
        window.MouseDown(point, button, modifiers);
        window.MouseUp(point, button, modifiers);
        Dispatcher.UIThread.RunJobs();
        await Task.CompletedTask;
    }

    private static Point GetControlCenterPoint(Visual relativeTo, Control control)
    {
        var point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            relativeTo);

        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate point for control {control.GetType().Name}.");
        }

        return point.Value;
    }

    private static void PressHotkey(Window window, Key key, PhysicalKey physicalKey, RawInputModifiers modifiers)
    {
        window.KeyPress(key, modifiers, physicalKey, null);
        window.KeyRelease(key, modifiers, physicalKey, null);
    }

    private static void InvokeMenuItemClick(MenuItem menuItem)
    {
        menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, menuItem));
        Dispatcher.UIThread.RunJobs();
    }

    private static void ClearStoredTreeCommandContext(MainControl view)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MainControl);

        var activeTaskTreeField = type.GetField("_activeTaskTree", flags)
            ?? throw new InvalidOperationException("Cannot find _activeTaskTree field.");
        var contextMenuTreeField = type.GetField("_contextMenuTree", flags)
            ?? throw new InvalidOperationException("Cannot find _contextMenuTree field.");
        var contextMenuWrapperField = type.GetField("_contextMenuWrapper", flags)
            ?? throw new InvalidOperationException("Cannot find _contextMenuWrapper field.");

        activeTaskTreeField.SetValue(view, null);
        contextMenuTreeField.SetValue(view, null);
        contextMenuWrapperField.SetValue(view, null);
    }

    private static void SelectTab(MainControl view, int index)
    {
        var tabControl = view.GetVisualDescendants().OfType<TabControl>().First();
        tabControl.SelectedIndex = index;
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task ClickTabHeaderAsync(Window window, MainControl view, string header)
    {
        var tabItem = view.GetVisualDescendants()
            .OfType<TabItem>()
            .First(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));

        await ClickControlAsync(window, tabItem);
    }

    private static bool WaitFor(Func<bool> predicate, int timeoutMilliseconds = 2000)
    {
        return SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return predicate();
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));
    }

    private static TaskWrapperViewModel? WaitForWrapper(Func<TaskWrapperViewModel?> getter, int timeoutMilliseconds = 2000)
    {
        TaskWrapperViewModel? wrapper = null;
        var ready = WaitFor(() =>
        {
            wrapper = getter();
            return wrapper != null;
        }, timeoutMilliseconds);

        return ready ? wrapper : null;
    }

    private static Control FindWrapperControl(TreeView tree, string taskId)
    {
        return tree.GetVisualDescendants()
            .OfType<Control>()
            .First(control => control.DataContext is TaskWrapperViewModel wrapper && wrapper.TaskItem.Id == taskId);
    }

    private static TreeViewItem FindWrapperTreeItem(TreeView tree, string taskId)
    {
        return tree.GetVisualDescendants()
            .OfType<TreeViewItem>()
            .First(item => item.DataContext is TaskWrapperViewModel wrapper && wrapper.TaskItem.Id == taskId);
    }

    private static IReadOnlyList<TaskWrapperViewModel> GetSelectedWrappers(TreeView tree)
    {
        return tree.SelectedItems
            .OfType<TaskWrapperViewModel>()
            .ToList();
    }

    private static int CountWrappers(IEnumerable<TaskWrapperViewModel> roots)
    {
        return roots.Sum(wrapper => 1 + CountWrappers(wrapper.SubTasks));
    }

    private static IEnumerable<TaskWrapperViewModel> GetRootsForTree(MainWindowViewModel vm, string treeName)
    {
        return treeName switch
        {
            "LastUpdatedTree" => vm.LastUpdatedItems,
            "UnlockedTree" => vm.UnlockedItems,
            "CompletedTree" => vm.CompletedItems,
            "ArchivedTree" => vm.ArchivedItems,
            "LastOpenedTree" => vm.LastOpenedItems,
            _ => Array.Empty<TaskWrapperViewModel>()
        };
    }

    private static TaskWrapperViewModel? GetCurrentWrapperForTree(MainWindowViewModel vm, string treeName)
    {
        return treeName switch
        {
            "LastUpdatedTree" => vm.CurrentLastUpdated,
            "UnlockedTree" => vm.CurrentUnlockedItem,
            "CompletedTree" => vm.CurrentCompletedItem,
            "ArchivedTree" => vm.CurrentArchivedItem,
            "LastOpenedTree" => vm.CurrentLastOpenedItem,
            _ => null
        };
    }

    private static TaskWrapperViewModel? FindWrapper(MainWindowViewModel vm, string treeName, string taskId)
    {
        return FindWrapper(GetRootsForTree(vm, treeName), taskId);
    }

    private static TaskWrapperViewModel? FindDescendantWrapper(
        MainWindowViewModel vm,
        string treeName,
        string rootTaskId,
        string childTaskId)
    {
        var rootWrapper = FindWrapper(vm, treeName, rootTaskId);
        return childTaskId == rootTaskId
            ? rootWrapper?.SubTasks.FirstOrDefault()
            : rootWrapper?.SubTasks.FirstOrDefault(wrapper => wrapper.TaskItem.Id == childTaskId);
    }

    private static async Task PrepareTreeScenarioAsync(MainWindowViewModel vm, string treeName)
    {
        switch (treeName)
        {
            case "CompletedTree":
            {
                var completedTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.CompletedTaskId);
                var existingChild = completedTask?.ContainsTasks.FirstOrDefault();
                if (completedTask != null && existingChild == null)
                {
                    var completedChild = await vm.taskRepository!.AddChild(completedTask);
                    completedChild.Title = "Completed tree child";
                    completedChild.IsCompleted = true;
                    await TestHelpers.WaitThrottleTime();
                }

                break;
            }
            case "LastOpenedTree":
            {
                vm.DetailsAreOpen = true;
                TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RootTask2Id);
                await TestHelpers.WaitThrottleTime();
                break;
            }
        }
    }

    private static TaskWrapperViewModel? FindWrapper(IEnumerable<TaskWrapperViewModel> roots, string taskId)
    {
        foreach (var wrapper in roots)
        {
            if (wrapper.TaskItem.Id == taskId)
            {
                return wrapper;
            }

            var nested = FindWrapper(wrapper.SubTasks, taskId);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private static bool IsExpandedRecursive(TaskWrapperViewModel wrapper)
    {
        return wrapper.IsExpanded && wrapper.SubTasks.All(IsExpandedRecursive);
    }

    private static bool IsCollapsedRecursive(TaskWrapperViewModel wrapper)
    {
        return !wrapper.IsExpanded && wrapper.SubTasks.All(IsCollapsedRecursive);
    }
}
