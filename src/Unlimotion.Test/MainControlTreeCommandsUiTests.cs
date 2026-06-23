using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion;
using Unlimotion.ViewModel;
using Unlimotion.Views;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class MainControlTreeCommandsUiTests
{
    private const int SearchExpansionWaitMilliseconds = 15000;

    [Test]
    public async Task TaskOutlinePastePreviewDialog_LargePreview_IsScrollableAndShowsLastTask()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            Window? window = null;

            try
            {
                var previewLines = Enumerable.Range(1, 100)
                    .Select(index => $"- Preview task {index:000}");
                var preview = new TaskOutlinePastePreview(
                    "Paste task outline?",
                    "Into: Root",
                    "Tasks to create: 100",
                    string.Join(Environment.NewLine, previewLines),
                    100);
                var view = new TaskOutlinePastePreviewControl
                {
                    DataContext = new TaskOutlinePastePreviewDialogViewModel(preview)
                };

                window = CreateWindow(view);
                window.Width = 760;
                window.Height = 420;
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var scrollViewer = FindControlByAutomationId<ScrollViewer>(
                    view,
                    "TaskOutlinePastePreviewScrollViewer");
                var previewText = FindControlByAutomationId<TextBlock>(
                    view,
                    "TaskOutlinePastePreviewText");

                await Assert.That(previewText.Text).Contains("Preview task 100");
                await Assert.That(scrollViewer.Bounds.Height).IsLessThan(previewText.Bounds.Height);

                scrollViewer.Offset = new Vector(0, scrollViewer.Extent.Height);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(scrollViewer.Offset.Y).IsGreaterThan(0);
            }
            finally
            {
                window?.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_Hotkey_UsesStickyActiveTabTreeAfterFocusMoves()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;
                ((NotificationManagerWrapperMock)vm.ManagerWrapper).AskResult = true;
                vm.CollapseAllNodes(vm.CurrentAllTasksItems);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                var toolbarFocusTarget = FindVisibleToolbarFocusTarget(view);

                await Assert.That(allTasksTree).IsNotNull();
                await ClickControlAsync(window, allTasksTree!);
                toolbarFocusTarget.Focus();

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
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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
    public async Task TreeCommandUi_CopyTaskOutline_HotkeyAndContextMenu_Work()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;
                vm.DetailsAreOpen = true;

                var parent = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask1Id);
                var child = await vm.taskRepository!.AddChild(parent!);
                await UpdateTaskForScenarioAsync(
                    vm.taskRepository,
                    child,
                    task => task.Title = "Outline UI copy child");
                var grandchild = await vm.taskRepository.AddChild(child);
                await UpdateTaskForScenarioAsync(
                    vm.taskRepository,
                    grandchild,
                    task => task.Title = "Outline UI copy grandchild");
                var childId = child.Id;
                var grandchildId = grandchild.Id;

                await Assert.That(await TestHelpers.WaitUntilAsync(
                        () =>
                        {
                            var currentParent = TestHelpers.GetTask(vm, parent!.Id);
                            var currentChild = TestHelpers.GetTask(vm, childId);
                            var currentGrandchild = TestHelpers.GetTask(vm, grandchildId);
                            return currentParent?.Contains.Contains(childId) == true &&
                                   currentChild?.Parents.Contains(parent.Id) == true &&
                                   currentChild.Contains.Contains(grandchildId) &&
                                   currentGrandchild?.Parents.Contains(childId) == true;
                        },
                        TimeSpan.FromSeconds(10)))
                    .IsTrue();

                TaskWrapperViewModel? parentWrapper = null;
                TaskWrapperViewModel? childWrapper = null;
                var wrappersReady = WaitFor(() =>
                {
                    parentWrapper = vm.FindTaskWrapperViewModel(parent!, vm.CurrentAllTasksItems);
                    childWrapper = vm.FindTaskWrapperViewModel(TestHelpers.GetTask(vm, childId)!, vm.CurrentAllTasksItems);
                    return parentWrapper != null && childWrapper != null;
                }, SearchExpansionWaitMilliseconds);
                await Assert.That(wrappersReady).IsTrue();
                parentWrapper!.IsExpanded = true;
                childWrapper!.IsExpanded = true;
                var childSubtreeReady = WaitFor(
                    () => childWrapper.SubTasks.Any(wrapper => wrapper.TaskItem.Id == grandchildId),
                    SearchExpansionWaitMilliseconds);
                await Assert.That(childSubtreeReady).IsTrue();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                string? clipboardText = null;
                vm.SetClipboardTextAsync = text =>
                {
                    clipboardText = text;
                    return Task.CompletedTask;
                };

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();

                allTasksTree!.SelectedItems!.Clear();
                allTasksTree.SelectedItems.Add(childWrapper);
                allTasksTree.SelectedItem = childWrapper;
                vm.CurrentAllTasksItem = childWrapper;
                allTasksTree.Focus();
                Dispatcher.UIThread.RunJobs();
                var childSelected = WaitFor(() =>
                    ReferenceEquals(vm.CurrentAllTasksItem, childWrapper) ||
                    vm.CurrentAllTasksItem?.TaskItem.Id == child.Id);
                await Assert.That(childSelected).IsTrue();

                PressHotkey(window, Key.C, PhysicalKey.C, RawInputModifiers.Control | RawInputModifiers.Shift);

                var copiedChild = WaitFor(() => clipboardText != null);
                await Assert.That(copiedChild).IsTrue();
                await Assert.That(NormalizeNewLines(clipboardText)).IsEqualTo(
                    $"Outline UI copy child\n\tOutline UI copy grandchild");

                clipboardText = null;
                var parentControl = FindWrapperControl(allTasksTree, parent!.Id);
                await ClickControlAsync(window, parentControl, MouseButton.Right);
                var copyMenuItem = FindContextMenuItem(allTasksTree.ContextMenu!, "CopyOutline");
                InvokeMenuItemClick(copyMenuItem);

                var copiedParent = WaitFor(() => clipboardText != null);
                await Assert.That(copiedParent).IsTrue();
                await Assert.That(NormalizeNewLines(clipboardText)).IsEqualTo(
                    $"{parent.Title}\n\tOutline UI copy child\n\t\tOutline UI copy grandchild");

                clipboardText = "unchanged";
                var titleTextBox = view.FindControl<TextBox>("CurrentTaskTitleTextBox");
                await Assert.That(titleTextBox).IsNotNull();
                titleTextBox!.Focus();
                PressHotkey(window, Key.C, PhysicalKey.C, RawInputModifiers.Control);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(clipboardText).IsEqualTo("unchanged");
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_CopyTaskOutline_UsesCurrentFiltersAndSort()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;
                vm.CurrentSortDefinition = vm.SortDefinitions.First(definition => definition.Id == "title-ascending");

                var parent = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask1Id);
                var zuluChild = await vm.taskRepository!.AddChild(parent!);
                await UpdateTaskForScenarioAsync(
                    vm.taskRepository,
                    zuluChild,
                    task => task.Title = "Zulu visible outline child");

                var hiddenChild = await vm.taskRepository.AddChild(parent);
                await UpdateTaskForScenarioAsync(
                    vm.taskRepository,
                    hiddenChild,
                    task => task.Title = "\u274C Alpha hidden excluded outline child");

                var alphaChild = await vm.taskRepository.AddChild(parent);
                await UpdateTaskForScenarioAsync(
                    vm.taskRepository,
                    alphaChild,
                    task => task.Title = "Alpha visible outline child");

                var excludeFilterReady = await TestHelpers.WaitUntilAsync(
                    () =>
                    {
                        Dispatcher.UIThread.RunJobs();
                        return vm.EmojiExcludeFilters.Any(filter => filter.Emoji == "\u274C");
                    },
                    TimeSpan.FromMilliseconds(SearchExpansionWaitMilliseconds));
                await Assert.That(excludeFilterReady).IsTrue();
                vm.EmojiExcludeFilters.First(filter => filter.Emoji == "\u274C").ShowTasks = true;

                var wrapperReady = await TestHelpers.WaitUntilAsync(
                    () =>
                    {
                        Dispatcher.UIThread.RunJobs();
                        var wrapper = vm.FindTaskWrapperViewModel(parent!, vm.CurrentAllTasksItems);
                        return wrapper?.SubTasks.Select(child => child.TaskItem.Title).SequenceEqual([
                            "Alpha visible outline child",
                            "Zulu visible outline child"
                        ]) == true;
                    },
                    TimeSpan.FromMilliseconds(SearchExpansionWaitMilliseconds));
                if (!wrapperReady)
                {
                    var wrapper = vm.FindTaskWrapperViewModel(parent!, vm.CurrentAllTasksItems);
                    throw new InvalidOperationException(
                        "Filtered task outline wrapper did not reach the expected visible sort order. " +
                        $"ExcludeFilters={string.Join("|", vm.EmojiExcludeFilters.Select(filter => $"{filter.Emoji}:{filter.ShowTasks}"))}; " +
                        $"SubTasks={string.Join("|", wrapper?.SubTasks.Select(child => child.TaskItem.Title) ?? [])}.");
                }

                await Assert.That(NormalizeNewLines(TaskOutlineClipboardService.BuildOutline(parent)))
                    .Contains("Alpha hidden excluded outline child");

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                string? clipboardText = null;
                vm.SetClipboardTextAsync = text =>
                {
                    clipboardText = text;
                    return Task.CompletedTask;
                };

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();

                var parentControl = FindWrapperControl(allTasksTree!, parent!.Id);
                await ClickControlAsync(window, parentControl);
                PressHotkey(window, Key.C, PhysicalKey.C, RawInputModifiers.Control | RawInputModifiers.Shift);

                var copied = await TestHelpers.WaitUntilAsync(
                    () =>
                    {
                        Dispatcher.UIThread.RunJobs();
                        return clipboardText != null;
                    },
                    TimeSpan.FromMilliseconds(SearchExpansionWaitMilliseconds));
                await Assert.That(copied).IsTrue();
                await Assert.That(NormalizeNewLines(clipboardText)).IsEqualTo(
                    $"{parent.Title}\n\tAlpha visible outline child\n\tZulu visible outline child");
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments("AllTasksTree")]
    [Arguments("LastCreatedTree")]
    [Arguments("LastUpdatedTree")]
    [Arguments("UnlockedTree")]
    [Arguments("CompletedTree")]
    [Arguments("ArchivedTree")]
    [Arguments("LastOpenedTree")]
    public async Task TreeSearch_ClearSearch_RestoresExpansionState(string treeName)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                var scenario = await CreateSearchExpansionScenarioAsync(vm, treeName);
                ActivateSearchExpansionTree(vm, treeName, scenario.Parent);
                await TestHelpers.WaitThrottleTime();
                Dispatcher.UIThread.RunJobs();

                var wrappersReady = WaitFor(() =>
                    FindWrapper(vm, treeName, scenario.Parent.Id) != null &&
                    FindWrapper(vm, treeName, scenario.Child.Id) != null,
                    SearchExpansionWaitMilliseconds);
                await Assert.That(wrappersReady).IsTrue();

                var parentWrapper = FindWrapper(vm, treeName, scenario.Parent.Id);
                var childWrapper = FindWrapper(vm, treeName, scenario.Child.Id);
                await Assert.That(parentWrapper).IsNotNull();
                await Assert.That(childWrapper).IsNotNull();

                var propertyChangedRaised = false;
                ((INotifyPropertyChanged)parentWrapper!).PropertyChanged += (_, e) =>
                {
                    propertyChangedRaised |= e.PropertyName == nameof(TaskWrapperViewModel.IsExpanded);
                };

                parentWrapper!.IsExpanded = true;
                childWrapper!.IsExpanded = false;
                Dispatcher.UIThread.RunJobs();
                await Assert.That(parentWrapper.IsExpanded).IsTrue();
                await Assert.That(childWrapper.IsExpanded).IsFalse();
                await Assert.That(propertyChangedRaised).IsTrue();

                await ApplySearchAsync(vm, $"search warmup {Guid.NewGuid():N}");
                await ApplySearchAsync(vm, scenario.SearchText);
                var parentFilteredOut = WaitFor(
                    () => FindWrapper(vm, treeName, scenario.Parent.Id) == null,
                    SearchExpansionWaitMilliseconds);
                await Assert.That(parentFilteredOut).IsTrue();

                await ApplySearchAsync(vm, string.Empty);
                TaskWrapperViewModel? restoredParentWrapper = null;
                TaskWrapperViewModel? restoredChildWrapper = null;
                var restored = WaitFor(
                    () =>
                    {
                        restoredParentWrapper = FindWrapper(vm, treeName, scenario.Parent.Id);
                        restoredChildWrapper = FindWrapper(vm, treeName, scenario.Child.Id);
                        return restoredParentWrapper != null && restoredChildWrapper != null;
                    },
                    SearchExpansionWaitMilliseconds);

                await Assert.That(restored).IsTrue();
                await Assert.That(restoredParentWrapper!.IsExpanded).IsTrue();
                await Assert.That(restoredChildWrapper!.IsExpanded).IsFalse();
            }
            finally
            {
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeSearch_AllTasksSearchEditor_FiltersVisibleTree()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;

                var repository = vm.taskRepository
                    ?? throw new InvalidOperationException("Task repository was not initialized.");
                var searchToken = Guid.NewGuid().ToString("N");
                var parent = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id)
                    ?? throw new InvalidOperationException("Search parent task was not found.");
                var selectedTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.SubTask22Id)
                    ?? throw new InvalidOperationException("Search target task was not found.");

                parent.Title = $"zzzz all tasks search parent {Guid.NewGuid():N}";
                selectedTask.Title = $"zzzz all tasks search target {searchToken}";
                selectedTask.IsCompleted = false;
                selectedTask.ArchiveDateTime = null;
                await repository.Update(parent);
                await repository.Update(selectedTask);

                await TestHelpers.WaitThrottleTime();
                Dispatcher.UIThread.RunJobs();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Width = 900;
                window.Height = 320;
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var searchEditor = view.GetVisualDescendants()
                    .OfType<TextBox>()
                    .FirstOrDefault(textBox => textBox.Name == "SearchEditor" && textBox.IsEffectivelyVisible);
                await Assert.That(searchEditor).IsNotNull();

                var searchTextPropertyChangedCount = 0;
                ((INotifyPropertyChanged)vm.Search).PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(SearchDefinition.SearchText))
                    {
                        searchTextPropertyChangedCount++;
                    }
                };

                searchEditor!.Text = searchToken;
                await TestHelpers.WaitThrottleTime();
                Dispatcher.UIThread.RunJobs();

                var filtered = WaitFor(() =>
                    vm.Search.SearchText == searchToken &&
                    vm.FindTaskWrapperViewModel(parent, vm.CurrentAllTasksItems) == null &&
                    vm.FindTaskWrapperViewModel(selectedTask, vm.CurrentAllTasksItems) != null,
                    SearchExpansionWaitMilliseconds);

                if (!filtered)
                {
                    throw new InvalidOperationException(
                        "AllTasks search editor did not filter the tree. " +
                        $"SearchEditorText={searchEditor.Text ?? "<null>"}; " +
                        $"VmSearchText={vm.Search.SearchText ?? "<null>"}; " +
                        $"SearchTextPropertyChangedCount={searchTextPropertyChangedCount}; " +
                        $"ParentVisible={vm.FindTaskWrapperViewModel(parent, vm.CurrentAllTasksItems) != null}; " +
                        $"TargetVisible={vm.FindTaskWrapperViewModel(selectedTask, vm.CurrentAllTasksItems) != null}; " +
                        $"RootCount={vm.CurrentAllTasksItems.Count}.");
                }

                await Assert.That(filtered).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeSearch_ClearSearch_ReselectsAndScrollsCurrentAllTasksItemWithClosedDetails()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;
                vm.DetailsAreOpen = false;
                vm.CurrentSortDefinition = vm.SortDefinitions.First(definition => definition.Id == "title-ascending");

                var repository = vm.taskRepository
                    ?? throw new InvalidOperationException("Task repository was not initialized.");
                var suffix = Guid.NewGuid().ToString("N");
                var searchToken = Guid.NewGuid().ToString("N");
                var parent = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id)
                    ?? throw new InvalidOperationException("Search selection parent task was not found.");
                var selectedTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.SubTask22Id)
                    ?? throw new InvalidOperationException("Search selection child task was not found.");

                for (var index = 0; index < 45; index++)
                {
                    var filler = await repository.Add();
                    filler.Title = $"zzzz search selection filler {index:D2} {suffix}";
                    await repository.Update(filler);
                }

                parent.Title = $"zzzz search selection parent {suffix}";
                parent.IsCompleted = false;
                parent.ArchiveDateTime = null;
                selectedTask.Title = $"zzzz search selection target {searchToken}";
                selectedTask.IsCompleted = false;
                selectedTask.ArchiveDateTime = null;
                await repository.Update(parent);
                await repository.Update(selectedTask);

                await Assert.That(await TestHelpers.WaitUntilAsync(
                        () => parent.Contains.Contains(selectedTask.Id) &&
                              selectedTask.Parents.Contains(parent.Id) &&
                              parent.ContainsTasks.Any(task => task.Id == selectedTask.Id),
                        TimeSpan.FromSeconds(10)))
                    .IsTrue();

                await TestHelpers.WaitThrottleTime();
                Dispatcher.UIThread.RunJobs();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Width = 900;
                window.Height = 320;
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();
                await Assert.That(allTasksTree!.Bounds.Height).IsGreaterThan(0);

                vm.CollapseAllNodes(vm.CurrentAllTasksItems);
                await ApplySearchAsync(vm, searchToken);

                var searchWrapper = WaitForWrapper(
                    () => vm.FindTaskWrapperViewModel(selectedTask, vm.CurrentAllTasksItems),
                    SearchExpansionWaitMilliseconds);
                await Assert.That(searchWrapper).IsNotNull();

                SelectTreeWrapper(allTasksTree, searchWrapper!);
                vm.CurrentAllTasksItem = searchWrapper;
                Dispatcher.UIThread.RunJobs();

                var selectedInSearch = WaitFor(() =>
                    vm.CurrentTaskItem?.Id == selectedTask.Id &&
                    ReferenceEquals(vm.CurrentAllTasksItem, searchWrapper) &&
                    ReferenceEquals(allTasksTree.SelectedItem, searchWrapper));
                await Assert.That(selectedInSearch).IsTrue();
                await Assert.That(vm.DetailsAreOpen).IsFalse();

                vm.CurrentTaskItem = null;
                Dispatcher.UIThread.RunJobs();
                await Assert.That(vm.CurrentTaskItem).IsNull();

                await ApplySearchAsync(vm, string.Empty);

                TaskWrapperViewModel? restoredParentWrapper = null;
                TaskWrapperViewModel? restoredSelectedWrapper = null;
                TreeViewItem? selectedTreeItem = null;
                var restoredSelectionVisible = WaitFor(() =>
                {
                    restoredParentWrapper = vm.FindTaskWrapperViewModel(parent, vm.CurrentAllTasksItems);
                    restoredSelectedWrapper = vm.FindTaskWrapperViewModel(selectedTask, vm.CurrentAllTasksItems);
                    selectedTreeItem = restoredSelectedWrapper == null
                        ? null
                        : FindWrapperTreeItemOrDefault(allTasksTree, restoredSelectedWrapper);

                    return restoredParentWrapper?.IsExpanded == true &&
                           restoredSelectedWrapper != null &&
                           !ReferenceEquals(restoredSelectedWrapper, searchWrapper) &&
                           ReferenceEquals(vm.CurrentAllTasksItem, restoredSelectedWrapper) &&
                           ReferenceEquals(allTasksTree.SelectedItem, restoredSelectedWrapper) &&
                           selectedTreeItem?.IsSelected == true &&
                           IntersectsVisibleBounds(allTasksTree, selectedTreeItem);
                }, SearchExpansionWaitMilliseconds);

                if (!restoredSelectionVisible)
                {
                    throw new InvalidOperationException(
                        "Selection was not restored after clearing search. " +
                        $"ParentExpanded={restoredParentWrapper?.IsExpanded}; " +
                        $"RestoredWrapper={(restoredSelectedWrapper == null ? "<null>" : restoredSelectedWrapper.TaskItem.Id)}; " +
                        $"CurrentAllTasksItem={vm.CurrentAllTasksItem?.TaskItem.Id ?? "<null>"}; " +
                        $"TreeSelectedItem={(allTasksTree.SelectedItem as TaskWrapperViewModel)?.TaskItem.Id ?? "<null>"}; " +
                        $"TreeItemFound={selectedTreeItem != null}; " +
                        $"TreeItemSelected={selectedTreeItem?.IsSelected}; " +
                        $"TreeItemVisible={(selectedTreeItem == null ? null : IntersectsVisibleBounds(allTasksTree, selectedTreeItem))}.");
                }

                await Assert.That(restoredSelectionVisible).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;
                var notificationManager = (NotificationManagerWrapperMock)vm.ManagerWrapper;
                notificationManager.AskResult = true;

                var parent = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask1Id);
                var parentWrapper = vm.FindTaskWrapperViewModel(parent!, vm.CurrentAllTasksItems);
                await Assert.That(parentWrapper).IsNotNull();
                parentWrapper!.IsExpanded = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                const string outline =
                    "Outline UI paste root\n" +
                    "\tOutline UI paste child\n" +
                    "\t\tOutline UI paste grandchild\n" +
                    "Outline UI paste sibling";
                var clipboardReadCount = 0;
                vm.GetClipboardTextAsync = () =>
                {
                    clipboardReadCount++;
                    return Task.FromResult<string?>(outline);
                };

                var countBefore = vm.taskRepository!.Tasks.Count;
                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();

                var titleTextBox = view.FindControl<TextBox>("CurrentTaskTitleTextBox");
                await Assert.That(titleTextBox).IsNotNull();
                titleTextBox!.Focus();
                PressHotkey(window, Key.V, PhysicalKey.V, RawInputModifiers.Control);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(clipboardReadCount).IsEqualTo(0);
                await Assert.That(vm.taskRepository.Tasks.Count).IsEqualTo(countBefore);

                var parentControl = FindWrapperControl(allTasksTree!, parent!.Id);
                await ClickControlAsync(window, parentControl);
                var parentSelected = WaitFor(
                    () => vm.CurrentAllTasksItem?.TaskItem.Id == parent.Id &&
                          allTasksTree!.SelectedItem is TaskWrapperViewModel selected &&
                          selected.TaskItem.Id == parent.Id,
                    SearchExpansionWaitMilliseconds);
                await Assert.That(parentSelected).IsTrue();
                SelectTreeWrapper(allTasksTree!, parentWrapper);
                allTasksTree!.Focus();
                Dispatcher.UIThread.RunJobs();

                PressHotkey(window, Key.V, PhysicalKey.V, RawInputModifiers.Control | RawInputModifiers.Shift);

                var pasteStarted = WaitFor(
                    () => clipboardReadCount == 1,
                    SearchExpansionWaitMilliseconds);
                await Assert.That(pasteStarted).IsTrue();

                var pasted = WaitFor(() =>
                    vm.taskRepository.Tasks.Count == countBefore + 4 &&
                    vm.taskRepository.Tasks.Items.Any(task => task.Title == "Outline UI paste root") &&
                    vm.taskRepository.Tasks.Items.Any(task => task.Title == "Outline UI paste child") &&
                    vm.taskRepository.Tasks.Items.Any(task => task.Title == "Outline UI paste grandchild") &&
                    vm.taskRepository.Tasks.Items.Any(task => task.Title == "Outline UI paste sibling"),
                    SearchExpansionWaitMilliseconds);
                if (!pasted)
                {
                    throw new InvalidOperationException(
                        "Task outline paste hotkey did not create expected tasks. " +
                        $"CountBefore={countBefore}; " +
                        $"Count={vm.taskRepository.Tasks.Count}; " +
                        $"ClipboardReadCount={clipboardReadCount}; " +
                        $"PreviewTaskCount={notificationManager.LastTaskOutlinePastePreview?.TaskCount.ToString() ?? "<null>"}; " +
                        $"LastError={notificationManager.LastErrorMessage ?? "<null>"}; " +
                        $"CurrentTask={vm.CurrentTaskItem?.Title ?? "<null>"}; " +
                        $"Titles={string.Join("|", vm.taskRepository.Tasks.Items.Select(task => task.Title))}.");
                }

                await Assert.That(clipboardReadCount).IsEqualTo(1);
                await Assert.That(notificationManager.LastTaskOutlinePastePreview).IsNotNull();
                await Assert.That(notificationManager.LastTaskOutlinePastePreview!.TaskCount).IsEqualTo(4);

                TaskItemViewModel? pastedRoot = null;
                TaskItemViewModel? pastedChild = null;
                TaskItemViewModel? pastedGrandchild = null;
                TaskItemViewModel? pastedSibling = null;
                var relationsReady = WaitFor(
                    () =>
                    {
                        pastedRoot = FindTaskByTitle(vm, "Outline UI paste root");
                        pastedChild = FindTaskByTitle(vm, "Outline UI paste child");
                        pastedGrandchild = FindTaskByTitle(vm, "Outline UI paste grandchild");
                        pastedSibling = FindTaskByTitle(vm, "Outline UI paste sibling");
                        var currentParent = TestHelpers.GetTask(vm, parent.Id);

                        return currentParent.Contains.Contains(pastedRoot.Id) &&
                               currentParent.Contains.Contains(pastedSibling.Id) &&
                               pastedRoot.Parents.Contains(parent.Id) &&
                               pastedRoot.Contains.Contains(pastedChild.Id) &&
                               pastedChild.Contains.Contains(pastedGrandchild.Id) &&
                               pastedGrandchild.Parents.Contains(pastedChild.Id);
                    },
                    SearchExpansionWaitMilliseconds);
                await Assert.That(relationsReady).IsTrue();

                await Assert.That(parent.Contains).Contains(pastedRoot!.Id);
                await Assert.That(parent.Contains).Contains(pastedSibling!.Id);
                await Assert.That(pastedRoot!.Parents).Contains(parent.Id);
                await Assert.That(pastedRoot.Contains).Contains(pastedChild!.Id);
                await Assert.That(pastedChild!.Contains).Contains(pastedGrandchild!.Id);
                await Assert.That(pastedGrandchild!.Parents).Contains(pastedChild.Id);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_InlineTitleEdit_CreatesEditorOnlyForF2OrRepeatedTitleClick()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;

                var currentTask = TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RootTask2Id);
                await Assert.That(currentTask).IsNotNull();
                currentTask!.Title = "Editable title";
                vm.SelectCurrentTask();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                await Assert.That(allTasksTree).IsNotNull();
                await Assert.That(WaitFor(() => IsVisibleInVisualTree(allTasksTree!))).IsTrue();

                var titleText = WaitForInlineTitleTextBlock(
                    view,
                    MainWindowViewModelFixture.RootTask2Id,
                    "AllTasksTree");
                await Assert.That(titleText.Bounds.Width).IsGreaterThan(0);
                await Assert.That(FindInlineTitleEditor(view, MainWindowViewModelFixture.RootTask2Id)).IsNull();

                allTasksTree!.Focus();
                Dispatcher.UIThread.RunJobs();

                await ClickPointAsync(window, GetPointRightOfControl(window, titleText, 48));
                await Assert.That(FindInlineTitleEditor(view, MainWindowViewModelFixture.RootTask2Id)).IsNull();

                await ClickControlAsync(window, titleText);
                await Assert.That(FindInlineTitleEditor(view, MainWindowViewModelFixture.RootTask2Id)).IsNull();

                await ClickControlAsync(window, titleText);
                await Assert.That(FindInlineTitleEditor(view, MainWindowViewModelFixture.RootTask2Id)).IsNull();

                await Task.Delay(650);
                await ClickControlAsync(window, titleText);
                await Assert.That(FindInlineTitleEditor(view, MainWindowViewModelFixture.RootTask2Id)).IsNull();

                await Task.Delay(650);
                await ClickControlAsync(window, titleText);

                var inlineEditor = WaitForInlineTitleEditor(view, MainWindowViewModelFixture.RootTask2Id);
                var clickFocused = WaitFor(() => IsFocused(window, inlineEditor));
                await Assert.That(clickFocused).IsTrue();
                await AssertInlineTitleEditorHasNoFrame(inlineEditor);

                inlineEditor.Text = "Renamed from title text";
                Dispatcher.UIThread.RunJobs();
                await Assert.That(currentTask.Title).IsEqualTo("Renamed from title text");

                allTasksTree.Focus();
                Dispatcher.UIThread.RunJobs();
                await Assert.That(WaitFor(() => FindInlineTitleEditor(view, MainWindowViewModelFixture.RootTask2Id) == null))
                    .IsTrue();

                PressHotkey(window, Key.F2, PhysicalKey.F2, RawInputModifiers.None);

                inlineEditor = WaitForInlineTitleEditor(view, MainWindowViewModelFixture.RootTask2Id);
                var hotkeyFocused = WaitFor(() => IsFocused(window, inlineEditor));
                await Assert.That(hotkeyFocused).IsTrue();
                await AssertInlineTitleEditorHasNoFrame(inlineEditor);
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
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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
                allTasksTree.ContextMenu?.Close();
                allTasksTree.Focus();
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
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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

                SelectTab(view, 3);

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
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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

                SelectTab(view, 3);

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
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                SetDateFilterAllTime(vm.LastCreatedDateFilter);
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
                var collapseAllMenuItem = FindContextMenuItem(lastCreatedTree.ContextMenu!, "CollapseAll");
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
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                SetDateFilterAllTime(vm.LastCreatedDateFilter);
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
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                SetDateFilterAllTime(vm.LastCreatedDateFilter);

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
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                SetDateFilterAllTime(vm.LastCreatedDateFilter);

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

                var childControl = FindWrapperControl(lastCreatedTree!, childTask.Id);
                var clickedChildWrapper = (TaskWrapperViewModel)childControl.DataContext!;
                var clickedGrandchildWrapper = clickedChildWrapper.SubTasks
                    .First(wrapper => wrapper.TaskItem.Id == grandchildTask.Id);
                clickedChildWrapper.IsExpanded = false;
                clickedGrandchildWrapper.IsExpanded = false;

                await ClickControlAsync(window, childControl);
                await Assert.That(vm.CurrentLastCreated?.TaskItem.Id).IsEqualTo(childTask.Id);

                PressHotkey(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Shift);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(clickedChildWrapper.IsExpanded).IsTrue();
                await Assert.That(clickedGrandchildWrapper.IsExpanded).IsTrue();

                clickedChildWrapper.IsExpanded = false;
                clickedGrandchildWrapper.IsExpanded = false;
                await ClickControlAsync(window, childControl, MouseButton.Right);
                var expandCurrentMenuItem = lastCreatedTree.ContextMenu!.Items.OfType<MenuItem>().First();
                InvokeMenuItemClick(expandCurrentMenuItem);

                await Assert.That(clickedChildWrapper.IsExpanded).IsTrue();
                await Assert.That(clickedGrandchildWrapper.IsExpanded).IsTrue();
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
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                SetDateFilterAllTime(vm.LastUpdatedDateFilter);
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
        var session = HeadlessUnitTestSession.StartNew(typeof(App));
        try
        {
            await session.DispatchAsync(async () =>
            {
                var fixture = new MainWindowViewModelFixture();
                Window? window = null;

                try
                {
                    var vm = fixture.MainWindowViewModelTest;
                    await vm.Connect();
                    SetDateFilterAllTime(vm.LastUpdatedDateFilter);

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
        finally
        {
            await session.DisposeIgnoringHeadlessTeardownNullReferenceAsync();
        }
    }

    [Test]
    public async Task TreeCommandUi_ShiftDelete_RemovesSelectedLastUpdatedTreeItem()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                SetDateFilterAllTime(vm.LastUpdatedDateFilter);
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
    [Arguments(3, "UnlockedTree", MainWindowViewModelFixture.RootTask2Id, MainWindowViewModelFixture.SubTask22Id)]
    [Arguments(5, "CompletedTree", MainWindowViewModelFixture.CompletedTaskId, MainWindowViewModelFixture.CompletedTaskId)]
    [Arguments(6, "ArchivedTree", MainWindowViewModelFixture.ArchivedTask1Id, MainWindowViewModelFixture.ArchivedTask11Id)]
    [Arguments(7, "LastOpenedTree", MainWindowViewModelFixture.RootTask2Id, MainWindowViewModelFixture.RootTask2Id)]
    public async Task TreeCommandUi_NonAllTasksTabs_CurrentAndAllCommands_Work(
        int tabIndex,
        string treeName,
        string rootTaskId,
        string childTaskId)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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
                var currentControl = FindWrapperControl(tree!, childWrapper);
                await ClickControlAsync(window, currentControl);
                var currentReady = WaitFor(() =>
                    GetCurrentWrapperForTree(vm, treeName)?.TaskItem.Id == currentTaskId,
                    5000);
                if (!currentReady)
                {
                    var selectedIds = string.Join(
                        ",",
                        GetSelectedWrappers(tree!).Select(wrapper => wrapper.TaskItem.Id));
                    throw new InvalidOperationException(
                        $"Current wrapper was not updated for {treeName}. " +
                        $"Expected={currentTaskId}; " +
                        $"Current={GetCurrentWrapperForTree(vm, treeName)?.TaskItem.Id ?? "<null>"}; " +
                        $"CurrentTask={vm.CurrentTaskItem?.Id ?? "<null>"}; " +
                        $"Selected=[{selectedIds}]");
                }

                SelectTreeWrapper(tree!, childWrapper);
                tree!.Focus();
                Dispatcher.UIThread.RunJobs();

                PressHotkey(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Shift);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(WaitFor(() => childWrapper.IsExpanded, 5000)).IsTrue();

                childWrapper.IsExpanded = false;
                Dispatcher.UIThread.RunJobs();

                await ClickControlAsync(window, currentControl, MouseButton.Right);
                var expandCurrentMenuItem = tree.ContextMenu!.Items.OfType<MenuItem>().First();
                InvokeMenuItemClick(expandCurrentMenuItem);

                await Assert.That(WaitFor(() => childWrapper.IsExpanded, 5000)).IsTrue();

                var roots = GetRootsForTree(vm, treeName).ToArray();
                vm.CollapseAllNodes(roots);
                Dispatcher.UIThread.RunJobs();

                await ClickControlAsync(window, tree);
                PressHotkey(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Alt);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(roots.All(IsExpandedRecursive)).IsTrue();

                await ClickControlAsync(window, tree, MouseButton.Right);
                var collapseAllMenuItem = FindContextMenuItem(tree.ContextMenu, "CollapseAll");
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
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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
                await Assert.That(relationTree.ContextMenu!.Items.OfType<MenuItem>().Count()).IsEqualTo(6);

                var childWrapper = vm.CurrentItemContains.SubTasks.First(wrapper => wrapper.TaskItem.Id == childTask.Id);
                var grandchildWrapper = childWrapper.SubTasks.First(wrapper => wrapper.TaskItem.Id == grandchildTask.Id);
                childWrapper.IsExpanded = false;
                grandchildWrapper.IsExpanded = false;

                var childControl = FindWrapperControl(relationTree, childWrapper);

                await ClickControlAsync(window, childControl, MouseButton.Right);
                relationTree.ContextMenu.PlacementTarget = childControl;
                titleTextBox!.Focus();
                var expandCurrentMenuItem = relationTree.ContextMenu.Items.OfType<MenuItem>().First();
                InvokeMenuItemClick(expandCurrentMenuItem);

                var expanded = WaitFor(
                    () => childWrapper.IsExpanded && grandchildWrapper.IsExpanded,
                    5000);
                await Assert.That(expanded).IsTrue();
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
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            ContextMenu? contextMenu = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                SelectTab(view, 3);
                var unlockedReady = WaitFor(() => vm.UnlockedItems.Any());
                await Assert.That(unlockedReady).IsTrue();

                var unlockedTree = view.FindControl<TreeView>("UnlockedTree");
                await Assert.That(unlockedTree).IsNotNull();
                await Assert.That(unlockedTree!.ContextMenu).IsNotNull();
                contextMenu = unlockedTree.ContextMenu!;

                vm.CollapseAllNodes(vm.UnlockedItems);
                Dispatcher.UIThread.RunJobs();

                contextMenu.PlacementTarget = unlockedTree;
                contextMenu.Open(unlockedTree);
                Dispatcher.UIThread.RunJobs();
                ClearStoredTreeCommandContext(view);
                var expandAllMenuItem = contextMenu.Items
                    .OfType<MenuItem>()
                    .First(item => string.Equals(item.Tag?.ToString(), "ExpandAll", StringComparison.Ordinal));
                InvokeMenuItemClick(expandAllMenuItem);
                contextMenu.Close();
                contextMenu.PlacementTarget = null;
                Dispatcher.UIThread.RunJobs();

                await Assert.That(vm.UnlockedItems.All(IsExpandedRecursive)).IsTrue();
            }
            finally
            {
                if (contextMenu != null)
                {
                    contextMenu.Close();
                    contextMenu.PlacementTarget = null;
                    Dispatcher.UIThread.RunJobs();
                }

                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_ContextMenu_DisplaysHotkeysForTreeCommands()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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
                await Assert.That(menuItems["CopyOutline"].InputGesture?.ToString()).IsEqualTo("Ctrl+Shift+C");
                await Assert.That(menuItems["PasteOutline"].InputGesture?.ToString()).IsEqualTo("Ctrl+Shift+V");
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_HotkeyHelpPanel_DisplaysEmbeddedShortcutReferenceFromF1()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Width = 680;
                window.Height = 520;
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var overlayHost = FindControlByAutomationId<Grid>(view, "HotkeyHelpOverlayHost");
                await Assert.That(overlayHost.IsVisible).IsFalse();
                await Assert.That(view.GetVisualDescendants()
                    .OfType<Button>()
                    .Where(control => AutomationProperties.GetAutomationId(control) == "HotkeyHelpButton")
                    .ToArray()).IsEmpty();

                var searchTextBox = view.GetVisualDescendants()
                    .OfType<TextBox>()
                    .First(textBox => string.Equals(textBox.Name, "SearchEditor", StringComparison.Ordinal));
                searchTextBox.Focus();
                PressHotkey(window, Key.F1, PhysicalKey.F1, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(view.IsHotkeyHelpVisible).IsTrue();
                await Assert.That(overlayHost.IsVisible).IsTrue();
                await AssertHotkeyHelpPanelContent(view);
                await Assert.That(FindControlByAutomationId<DropDownButton>(view, "GlobalTaskCreateMenuButton").Flyout)
                    .IsAssignableTo<MenuFlyout>();

                PressHotkey(window, Key.F1, PhysicalKey.F1, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                await Assert.That(view.IsHotkeyHelpVisible).IsFalse();
                await Assert.That(overlayHost.IsVisible).IsFalse();

                PressHotkey(window, Key.F1, PhysicalKey.F1, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                await Assert.That(view.IsHotkeyHelpVisible).IsTrue();

                PressHotkey(window, Key.Escape, PhysicalKey.Escape, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                await Assert.That(view.IsHotkeyHelpVisible).IsFalse();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task MainWindowUi_HotkeyHelpPanel_HandlesF1AtWindowLevel()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            MainWindow? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();

                window = new MainWindow
                {
                    DataContext = vm,
                    Width = 720,
                    Height = 560
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var view = window.GetVisualDescendants()
                    .OfType<MainControl>()
                    .Single();
                var overlayHost = FindControlByAutomationId<Grid>(view, "HotkeyHelpOverlayHost");
                await Assert.That(overlayHost.IsVisible).IsFalse();

                var searchTextBox = view.GetVisualDescendants()
                    .OfType<TextBox>()
                    .First(textBox => string.Equals(textBox.Name, "SearchEditor", StringComparison.Ordinal));
                searchTextBox.Focus();

                PressHotkey(window, Key.F1, PhysicalKey.F1, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(view.IsHotkeyHelpVisible).IsTrue();
                await Assert.That(overlayHost.IsVisible).IsTrue();

                PressHotkey(window, Key.Escape, PhysicalKey.Escape, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(view.IsHotkeyHelpVisible).IsFalse();
                await Assert.That(overlayHost.IsVisible).IsFalse();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TreeCommandUi_SettingsShowHotkeysButton_OpensEmbeddedShortcutReference()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Width = 720;
                window.Height = 560;
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var overlayHost = FindControlByAutomationId<Grid>(view, "HotkeyHelpOverlayHost");
                await Assert.That(overlayHost.IsVisible).IsFalse();

                var settingsTab = FindControlByAutomationId<TabItem>(view, "SettingsTabItem");
                settingsTab.IsSelected = true;
                Dispatcher.UIThread.RunJobs();

                var showHotkeysButton = FindControlByAutomationId<Button>(view, "SettingsShowHotkeysButton");
                await Assert.That(showHotkeysButton.Content?.ToString()).IsEqualTo(L10n.Get("ShowHotkeys"));

                InvokeButtonClick(showHotkeysButton);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(view.IsHotkeyHelpVisible).IsTrue();
                await Assert.That(overlayHost.IsVisible).IsTrue();
                await AssertHotkeyHelpPanelContent(view);

                var closeButton = FindControlByAutomationId<Button>(view, "HotkeyHelpCloseButton");
                InvokeButtonClick(closeButton);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(view.IsHotkeyHelpVisible).IsFalse();
                await Assert.That(overlayHost.IsVisible).IsFalse();
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
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                SetDateFilterAllTime(vm.LastCreatedDateFilter);

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

                var childControl = FindWrapperControl(lastCreatedTree, childTask.Id);
                var clickedChildWrapper = (TaskWrapperViewModel)childControl.DataContext!;
                var clickedGrandchildWrapper = clickedChildWrapper.SubTasks
                    .First(wrapper => wrapper.TaskItem.Id == grandchildTask.Id);
                clickedChildWrapper.IsExpanded = false;
                clickedGrandchildWrapper.IsExpanded = false;

                lastCreatedTree.SelectedItems?.Clear();
                lastCreatedTree.SelectedItem = null;
                vm.CurrentLastCreated = null!;

                lastCreatedTree.ContextMenu!.Open(lastCreatedTree);
                lastCreatedTree.ContextMenu.PlacementTarget = childControl;
                Dispatcher.UIThread.RunJobs();
                ClearStoredTreeCommandContext(view);
                var expandCurrentMenuItem = lastCreatedTree.ContextMenu.Items
                    .OfType<MenuItem>()
                    .First(item => string.Equals(item.Tag?.ToString(), "ExpandCurrentNested", StringComparison.Ordinal));
                InvokeMenuItemClick(expandCurrentMenuItem);
                lastCreatedTree.ContextMenu.Close();

                await Assert.That(clickedChildWrapper.IsExpanded).IsTrue();
                await Assert.That(clickedGrandchildWrapper.IsExpanded).IsTrue();
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
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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
                var toolbarFocusTarget = FindVisibleToolbarFocusTarget(view);
                await Assert.That(allTasksTree).IsNotNull();

                await ClickControlAsync(window, allTasksTree!);
                toolbarFocusTarget.Focus();
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
    public async Task TreeCommandUi_CtrlA_UsesFocusedRelationTree()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(App));
        try
        {
            await session.DispatchAsync(async () =>
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
                    await Assert.That(CountWrappers(vm.CurrentItemContains.SubTasks)).IsGreaterThan(0);

                    await ClickControlAsync(window, allTasksTree!);
                    var focused = relationTree!.Focus();
                    Dispatcher.UIThread.RunJobs();
                    await Assert.That(focused).IsTrue();

                    PressHotkey(window, Key.A, PhysicalKey.A, RawInputModifiers.Control);
                    Dispatcher.UIThread.RunJobs();

                    await Assert.That(GetSelectedWrappers(relationTree).Count)
                        .IsEqualTo(CountWrappers(vm.CurrentItemContains.SubTasks));
                    await Assert.That(GetSelectedWrappers(allTasksTree).Count)
                        .IsNotEqualTo(CountWrappers(vm.CurrentAllTasksItems));
                }
                finally
                {
                    window?.Close();
                    fixture.CleanTasks();
                }
            }, CancellationToken.None);
        }
        finally
        {
            await session.DisposeIgnoringHeadlessTeardownNullReferenceAsync();
        }
    }

    [Test]
    public async Task TreeCommandUi_CtrlA_SelectsTextWhenTextInputFocused()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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
                await Assert.That(currentTask).IsNotNull();
                currentTask!.Title = "Ctrl A text selection";

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                var titleTextBox = view.FindControl<TextBox>("CurrentTaskTitleTextBox");
                await Assert.That(allTasksTree).IsNotNull();
                await Assert.That(titleTextBox).IsNotNull();

                titleTextBox!.Focus();
                titleTextBox.CaretIndex = 5;
                titleTextBox.ClearSelection();
                PressHotkey(window, Key.A, PhysicalKey.A, RawInputModifiers.Control);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(titleTextBox.SelectedText).IsEqualTo(titleTextBox.Text);
                await Assert.That(GetSelectedWrappers(allTasksTree!).Count).IsNotEqualTo(CountWrappers(vm.CurrentAllTasksItems));
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
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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

                var root1Control = FindWrapperTreeItem(allTasksTree!, MainWindowViewModelFixture.RootTask1Id);
                var root4Control = FindWrapperTreeItem(allTasksTree, MainWindowViewModelFixture.RootTask4Id);

                await ClickControlAsync(window, root1Control);
                await ClickControlAsync(window, root4Control, modifiers: RawInputModifiers.Control);
                await Assert.That(GetSelectedWrappers(allTasksTree).Count).IsEqualTo(2);

                PressHotkey(window, Key.Delete, PhysicalKey.Delete, RawInputModifiers.Shift);
                await TestHelpers.WaitThrottleTime();

                await Assert.That(TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.RootTask1Id)).IsNull();
                await Assert.That(TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.RootTask4Id)).IsNull();
                await Assert.That(TestHelpers.GetStorageTaskItem(fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.SubTask41Id)).IsNull();
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
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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

                var childWrapper = (TaskWrapperViewModel)FindWrapperTreeItem(
                    relationTree!,
                    MainWindowViewModelFixture.SubTask22Id).DataContext!;
                relationTree.SelectedItems!.Clear();
                relationTree.SelectedItems.Add(childWrapper);
                relationTree.SelectedItem = childWrapper;
                relationTree.Focus();
                Dispatcher.UIThread.RunJobs();
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
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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

                var root1Control = FindWrapperTreeItem(allTasksTree!, MainWindowViewModelFixture.RootTask1Id);
                var root3Control = FindWrapperTreeItem(allTasksTree, MainWindowViewModelFixture.RootTask3Id);
                var root4Control = FindWrapperTreeItem(allTasksTree, MainWindowViewModelFixture.RootTask4Id);

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
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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

                var root1Control = FindWrapperTreeItem(allTasksTree!, MainWindowViewModelFixture.RootTask1Id);
                var root3Control = FindWrapperTreeItem(allTasksTree, MainWindowViewModelFixture.RootTask3Id);

                await ClickControlAsync(window, root1Control);
                await ClickControlAsync(window, root3Control, modifiers: RawInputModifiers.Control);

                var selectedBefore = GetSelectedWrappers(allTasksTree);
                await Assert.That(selectedBefore.Count).IsEqualTo(2);

                var root1Item = root1Control;
                var root3Item = root3Control;
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
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(point, button, modifiers);
        Dispatcher.UIThread.RunJobs();
        await Task.CompletedTask;
    }

    private static async Task ClickPointAsync(
        Window window,
        Point point,
        MouseButton button = MouseButton.Left,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.MouseDown(point, button, modifiers);
        Dispatcher.UIThread.RunJobs();
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

    private static Point GetPointRightOfControl(Visual relativeTo, Control control, double offset)
    {
        var point = control.TranslatePoint(
            new Point(control.Bounds.Width + offset, control.Bounds.Height / 2),
            relativeTo);

        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate point for control {control.GetType().Name}.");
        }

        return point.Value;
    }

    private static bool IsFocused(Window window, Control control)
    {
        return ReferenceEquals(window.FocusManager?.GetFocusedElement(), control) || control.IsFocused;
    }

    private static void PressHotkey(
        Window window,
        Key key,
        PhysicalKey physicalKey,
        RawInputModifiers modifiers,
        string? keySymbol = null)
    {
        window.KeyPress(key, modifiers, physicalKey, keySymbol);
        window.KeyRelease(key, modifiers, physicalKey, keySymbol);
    }

    private static void InvokeMenuItemClick(MenuItem menuItem)
    {
        menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, menuItem));
        Dispatcher.UIThread.RunJobs();
    }

    private static void InvokeButtonClick(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        Dispatcher.UIThread.RunJobs();
    }

    private static MenuItem FindContextMenuItem(ContextMenu contextMenu, string tag)
    {
        return contextMenu.Items
            .OfType<MenuItem>()
            .First(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal));
    }

    private static string? NormalizeNewLines(string? text)
    {
        return text?
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
    }

    private static TaskItemViewModel FindTaskByTitle(MainWindowViewModel vm, string title)
    {
        return vm.taskRepository!.Tasks.Items
            .First(task => string.Equals(task.Title, title, StringComparison.Ordinal));
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
        tabControl.GetVisualDescendants()
            .OfType<TabItem>()
            .FirstOrDefault(item => item.IsSelected)
            ?.Focus();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task ClickTabHeaderAsync(Window window, MainControl view, string header)
    {
        var tabItems = view.GetVisualDescendants()
            .OfType<TabItem>()
            .ToList();
        var tabItem = tabItems.FirstOrDefault(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal)) ??
                      tabItems.First(item => string.Equals(
                          AutomationProperties.GetAutomationId(item),
                          GetTabAutomationId(header),
                          StringComparison.Ordinal));

        await ClickControlAsync(window, tabItem);
        SelectTab(view, GetTabIndex(header));
    }

    private static string GetTabAutomationId(string header)
    {
        return header switch
        {
            "All Tasks" => "AllTasksTabItem",
            "Last Created" => "LastCreatedTabItem",
            "Last Updated" => "LastUpdatedTabItem",
            "Unlocked" => "UnlockedTabItem",
            "In Progress" => "InProgressTabItem",
            "Completed" => "CompletedTabItem",
            "Archived" => "ArchivedTabItem",
            "Last Opened" => "LastOpenedTabItem",
            _ => header
        };
    }

    private static int GetTabIndex(string header)
    {
        return header switch
        {
            "All Tasks" => 0,
            "Last Created" => 1,
            "Last Updated" => 2,
            "Unlocked" => 3,
            "In Progress" => 4,
            "Completed" => 5,
            "Archived" => 6,
            "Last Opened" => 7,
            _ => 0
        };
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

    private sealed record SearchExpansionScenario(
        TaskItemViewModel Parent,
        TaskItemViewModel Child,
        string SearchText);

    private static async Task UpdateTaskForScenarioAsync(
        ITaskStorage repository,
        TaskItemViewModel task,
        Action<TaskItemViewModel> configure)
    {
        var isInitializedProvider = task.IsInitializedProvider;
        task.IsInitializedProvider = () => false;
        try
        {
            configure(task);
            await repository.Update(task);
        }
        finally
        {
            task.IsInitializedProvider = isInitializedProvider;
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<SearchExpansionScenario> CreateSearchExpansionScenarioAsync(
        MainWindowViewModel vm,
        string treeName)
    {
        var repository = vm.taskRepository
            ?? throw new InvalidOperationException("Task repository was not initialized.");
        var searchToken = Guid.NewGuid().ToString("N");

        if (treeName == "UnlockedTree")
        {
            return await CreateUnlockedSearchExpansionScenarioAsync(vm, repository, searchToken);
        }

        if (treeName == "ArchivedTree")
        {
            return await CreateArchivedSearchExpansionScenarioAsync(vm, repository, searchToken);
        }

        if (treeName == "CompletedTree")
        {
            return await CreateCompletedSearchExpansionScenarioAsync(vm, repository, searchToken);
        }

        var parent = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id)
            ?? throw new InvalidOperationException("Search expansion parent task was not found.");
        var child = TestHelpers.GetTask(vm, MainWindowViewModelFixture.SubTask22Id)
            ?? throw new InvalidOperationException("Search expansion child task was not found.");
        await Assert.That(await TestHelpers.WaitUntilAsync(
                () => parent.Contains.Contains(child.Id) &&
                      child.Parents.Contains(parent.Id) &&
                      parent.ContainsTasks.Any(task => task.Id == child.Id),
                TimeSpan.FromSeconds(10)))
            .IsTrue();

        var searchTarget = await repository.Add();
        var searchText = searchToken;
        await UpdateTaskForScenarioAsync(
            repository,
            searchTarget,
            task => task.Title = $"Search expansion target {treeName} {searchToken}");

        await TestHelpers.WaitThrottleTime();
        Dispatcher.UIThread.RunJobs();

        return new SearchExpansionScenario(parent, child, searchText);
    }

    private static async Task<SearchExpansionScenario> CreateUnlockedSearchExpansionScenarioAsync(
        MainWindowViewModel vm,
        ITaskStorage repository,
        string searchToken)
    {
        var parent = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id)
            ?? throw new InvalidOperationException("Unlocked search parent task was not found.");
        var child = TestHelpers.GetTask(vm, MainWindowViewModelFixture.SubTask22Id)
            ?? throw new InvalidOperationException("Unlocked search child task was not found.");

        await UpdateTaskForScenarioAsync(
            repository,
            child,
            task =>
            {
                task.IsCompleted = true;
                task.CompletedDateTime ??= DateTimeOffset.UtcNow;
            });
        await Assert.That(await TestHelpers.WaitUntilAsync(
                () => parent.IsCanBeCompleted &&
                      parent.Contains.Contains(child.Id) &&
                      parent.ContainsTasks.Any(task => task.Id == child.Id),
                TimeSpan.FromSeconds(10)))
            .IsTrue();

        var searchTarget = await repository.Add();
        await UpdateTaskForScenarioAsync(
            repository,
            searchTarget,
            task => task.Title = $"Search expansion target UnlockedTree {searchToken}");

        await TestHelpers.WaitThrottleTime();
        Dispatcher.UIThread.RunJobs();

        return new SearchExpansionScenario(parent, child, searchToken);
    }

    private static async Task<SearchExpansionScenario> CreateCompletedSearchExpansionScenarioAsync(
        MainWindowViewModel vm,
        ITaskStorage repository,
        string searchToken)
    {
        var parent = TestHelpers.GetTask(vm, MainWindowViewModelFixture.CompletedTaskId)
            ?? throw new InvalidOperationException("Completed search parent task was not found.");
        var child = parent.ContainsTasks.FirstOrDefault();
        if (child == null)
        {
            child = await repository.AddChild(parent);
            await UpdateTaskForScenarioAsync(
                repository,
                child,
                task => task.Title = "Completed search expansion child");
        }

        await UpdateTaskForScenarioAsync(
            repository,
            parent,
            task =>
            {
                task.IsCompleted = true;
                task.CompletedDateTime ??= DateTimeOffset.UtcNow;
            });
        await UpdateTaskForScenarioAsync(
            repository,
            child,
            task =>
            {
                task.IsCompleted = true;
                task.CompletedDateTime ??= DateTimeOffset.UtcNow;
            });

        await Assert.That(await TestHelpers.WaitUntilAsync(
                () => parent.Contains.Contains(child.Id) &&
                      child.Parents.Contains(parent.Id) &&
                      parent.ContainsTasks.Any(task => task.Id == child.Id),
                TimeSpan.FromSeconds(10)))
            .IsTrue();

        var searchTarget = await repository.Add();
        await UpdateTaskForScenarioAsync(
            repository,
            searchTarget,
            task =>
            {
                task.Title = $"Search expansion target CompletedTree {searchToken}";
                task.IsCompleted = true;
                task.CompletedDateTime ??= DateTimeOffset.UtcNow;
            });

        await TestHelpers.WaitThrottleTime();
        Dispatcher.UIThread.RunJobs();

        return new SearchExpansionScenario(parent, child, searchToken);
    }

    private static async Task<SearchExpansionScenario> CreateArchivedSearchExpansionScenarioAsync(
        MainWindowViewModel vm,
        ITaskStorage repository,
        string searchToken)
    {
        var parent = TestHelpers.GetTask(vm, MainWindowViewModelFixture.ArchivedTask1Id)
            ?? throw new InvalidOperationException("Archived search parent task was not found.");
        var child = TestHelpers.GetTask(vm, MainWindowViewModelFixture.ArchivedTask11Id)
            ?? throw new InvalidOperationException("Archived search child task was not found.");

        await Assert.That(await TestHelpers.WaitUntilAsync(
                () => parent.Contains.Contains(child.Id) &&
                      child.Parents.Contains(parent.Id) &&
                      parent.ContainsTasks.Any(task => task.Id == child.Id),
                TimeSpan.FromSeconds(10)))
            .IsTrue();

        var searchTarget = await repository.Add();
        await UpdateTaskForScenarioAsync(
            repository,
            searchTarget,
            task =>
            {
                task.Title = $"Search expansion target ArchivedTree {searchToken}";
                task.IsCompleted = null;
                task.ArchiveDateTime ??= DateTimeOffset.UtcNow;
            });

        await TestHelpers.WaitThrottleTime();
        Dispatcher.UIThread.RunJobs();

        return new SearchExpansionScenario(parent, child, searchToken);
    }

    private static void ActivateSearchExpansionTree(
        MainWindowViewModel vm,
        string treeName,
        TaskItemViewModel parent)
    {
        switch (treeName)
        {
            case "AllTasksTree":
                vm.AllTasksMode = true;
                break;
            case "LastCreatedTree":
                SetDateFilterAllTime(vm.LastCreatedDateFilter);
                vm.LastCreatedMode = true;
                break;
            case "LastUpdatedTree":
                SetDateFilterAllTime(vm.LastUpdatedDateFilter);
                vm.LastUpdatedMode = true;
                break;
            case "UnlockedTree":
                vm.UnlockedMode = true;
                break;
            case "CompletedTree":
                EnsureStatusFilterSelected(vm, DomainTaskStatus.Completed);
                SetDateFilterAllTime(vm.CompletedDateFilter);
                vm.CompletedMode = true;
                break;
            case "ArchivedTree":
                EnsureStatusFilterSelected(vm, DomainTaskStatus.Archived);
                SetDateFilterAllTime(vm.ArchivedDateFilter);
                vm.ArchivedMode = true;
                break;
            case "LastOpenedTree":
                vm.DetailsAreOpen = true;
                vm.LastOpenedMode = true;
                Dispatcher.UIThread.RunJobs();
                vm.CurrentTaskItem = parent;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(treeName), treeName, "Unknown task tree.");
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static async Task ApplySearchAsync(MainWindowViewModel vm, string searchText)
    {
        vm.Search.SearchText = searchText;
        await Task.Delay(TimeSpan.FromMilliseconds(SearchDefinition.DefaultThrottleMs + 100));
        Dispatcher.UIThread.RunJobs();
    }

    private static void SetDateFilterAllTime(DateFilter filter)
    {
        filter.CurrentOption = DateFilterDefinition.AllTime;
        filter.SetDateTimes(DateFilterDefinition.AllTime);
    }

    private static T FindControlByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        return root.GetVisualDescendants()
            .OfType<T>()
            .First(control => AutomationProperties.GetAutomationId(control) == automationId);
    }

    private static T? FindControlInDetachedContent<T>(Control root, string automationId)
        where T : Control
    {
        if (root is T typedRoot && AutomationProperties.GetAutomationId(root) == automationId)
        {
            return typedRoot;
        }

        return root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId) ??
            root.GetLogicalDescendants()
                .OfType<T>()
                .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId);
    }

    private static async Task AssertHotkeyHelpPanelContent(MainControl view)
    {
        Dispatcher.UIThread.RunJobs();

        await Assert.That(view.IsHotkeyHelpVisible).IsTrue();
        var overlayHost = FindControlByAutomationId<Grid>(view, "HotkeyHelpOverlayHost");
        var panelFrame = FindControlByAutomationId<Border>(view, "HotkeyHelpOverlayPanelFrame");
        var panel = FindControlInDetachedContent<Border>(view, "HotkeyPanel") ??
                    throw new InvalidOperationException("Hotkey panel was not found.");
        var scrollViewer = FindControlInDetachedContent<ScrollViewer>(view, "HotkeyPanelScrollViewer") ??
                           throw new InvalidOperationException("Hotkey panel scroll viewer was not found.");

        await Assert.That(overlayHost.IsVisible).IsTrue();
        await Assert.That(panelFrame.MaxWidth).IsEqualTo(560);
        await Assert.That(panel.MaxWidth).IsEqualTo(560);
        await Assert.That(scrollViewer.VerticalScrollBarVisibility).IsEqualTo(ScrollBarVisibility.Auto);
        await Assert.That(scrollViewer.HorizontalScrollBarVisibility).IsEqualTo(ScrollBarVisibility.Disabled);
        await Assert.That(scrollViewer.Bounds.Height).IsLessThan(scrollViewer.Extent.Height);

        AssertText(view, "HotkeyPanelTitleText", L10n.Get("HotkeyPanelTitle"));
        AssertText(view, "HotkeyGeneralSectionTitle", L10n.Get("HotkeySectionGeneral"));
        AssertText(view, "HotkeyCurrentTaskSectionTitle", L10n.Get("HotkeySectionCurrentTask"));
        AssertText(view, "HotkeySelectionOutlineSectionTitle", L10n.Get("HotkeySectionSelectionOutline"));
        AssertText(view, "HotkeyTaskTreeSectionTitle", L10n.Get("HotkeySectionTaskTree"));
        AssertText(view, "HotkeyRelationsSectionTitle", L10n.Get("HotkeySectionRelations"));
        AssertText(view, "HotkeyDragDropSectionTitle", L10n.Get("HotkeySectionDragDrop"));
        AssertText(view, "HotkeyRoadmapSectionTitle", L10n.Get("HotkeySectionRoadmap"));
        AssertHotkeyRow(
            view,
            "HotkeyGeneralOpenHotkeyHelpRow",
            L10n.Get("HotkeyOpenHotkeyHelp"),
            HotkeyHints.OpenHotkeyHelp);
        AssertHotkeyRow(
            view,
            "HotkeyGeneralCloseHotkeyHelpRow",
            L10n.Get("HotkeyCloseHotkeyHelp"),
            HotkeyHints.CloseHotkeyHelp);
        AssertHotkeyRow(
            view,
            "HotkeyCurrentTaskRenameTaskRow",
            L10n.Get("HotkeyRenameTask"),
            HotkeyHints.RenameTask);
        AssertHotkeyRow(
            view,
            "HotkeyCurrentTaskCreateSiblingRow",
            L10n.Get("HotkeyCreateSibling"),
            HotkeyHints.CreateSibling);
        AssertHotkeyRow(
            view,
            "HotkeyCurrentTaskCreateBlockedSiblingRow",
            L10n.Get("HotkeyCreateBlockedSibling"),
            HotkeyHints.CreateBlockedSibling);
        AssertHotkeyRow(
            view,
            "HotkeyCurrentTaskCreateInnerRow",
            L10n.Get("HotkeyCreateInner"),
            HotkeyHints.CreateInner);
        AssertHotkeyRow(
            view,
            "HotkeyCurrentTaskCompleteCurrentTaskRow",
            L10n.Get("HotkeyCompleteCurrentTask"),
            HotkeyHints.CompleteCurrentTask);
        AssertHotkeyRow(
            view,
            "HotkeySelectionOutlineSelectAllRow",
            L10n.Get("HotkeySelectAll"),
            HotkeyHints.SelectAll);
        AssertHotkeyRow(
            view,
            "HotkeySelectionOutlineDeleteSelectionRow",
            L10n.Get("HotkeyDeleteSelection"),
            HotkeyHints.DeleteSelection);
        AssertHotkeyRow(
            view,
            "HotkeySelectionOutlineCopyOutlineRow",
            L10n.Get("CopyTaskOutline"),
            HotkeyHints.CopyOutline);
        AssertHotkeyRow(
            view,
            "HotkeySelectionOutlinePasteOutlineRow",
            L10n.Get("PasteTaskOutline"),
            HotkeyHints.PasteOutline);
        AssertHotkeyRow(
            view,
            "HotkeyTaskTreeExpandCurrentRow",
            L10n.Get("ExpandCurrentNested"),
            HotkeyHints.ExpandCurrent);
        AssertHotkeyRow(
            view,
            "HotkeyTaskTreeCollapseCurrentRow",
            L10n.Get("CollapseCurrentNested"),
            HotkeyHints.CollapseCurrent);
        AssertHotkeyRow(
            view,
            "HotkeyTaskTreeExpandAllRow",
            L10n.Get("ExpandAllNodes"),
            HotkeyHints.ExpandAll);
        AssertHotkeyRow(
            view,
            "HotkeyTaskTreeCollapseAllRow",
            L10n.Get("CollapseAllNodes"),
            HotkeyHints.CollapseAll);
        AssertHotkeyRow(
            view,
            "HotkeyRelationsConfirmRow",
            L10n.Get("HotkeyConfirmRelation"),
            HotkeyHints.ConfirmRelation);
        AssertHotkeyRow(
            view,
            "HotkeyRelationsCancelRow",
            L10n.Get("HotkeyCancelRelation"),
            HotkeyHints.CancelRelation);
        AssertHotkeyRow(
            view,
            "HotkeyDragCopyIntoRow",
            L10n.Get("HotkeyDragCopyInto"),
            HotkeyHints.DragCopyInto);
        AssertHotkeyRow(
            view,
            "HotkeyDragMoveIntoRow",
            L10n.Get("HotkeyDragMoveInto"),
            HotkeyHints.DragMoveInto);
        AssertHotkeyRow(
            view,
            "HotkeyDragCloneIntoRow",
            L10n.Get("HotkeyDragCloneInto"),
            HotkeyHints.DragCloneInto);
        AssertHotkeyRow(
            view,
            "HotkeyDragSourcesBlockTargetRow",
            L10n.Get("HotkeyDragSourcesBlockTarget"),
            HotkeyHints.DragSourcesBlockTarget);
        AssertHotkeyRow(
            view,
            "HotkeyDragTargetBlocksSourcesRow",
            L10n.Get("HotkeyDragTargetBlocksSources"),
            HotkeyHints.DragTargetBlocksSources);
        AssertHotkeyRow(
            view,
            "HotkeyRoadmapFitToScreenRow",
            L10n.Get("HotkeyRoadmapFitToScreen"),
            HotkeyHints.RoadmapFitToScreen);
        AssertHotkeyRow(
            view,
            "HotkeyRoadmapResetViewportRow",
            L10n.Get("HotkeyRoadmapResetViewport"),
            HotkeyHints.RoadmapResetViewport);
        AssertHotkeyRow(
            view,
            "HotkeyRoadmapToggleSelectionRow",
            L10n.Get("HotkeyRoadmapToggleSelection"),
            HotkeyHints.RoadmapToggleSelection);
        AssertHotkeyRow(
            view,
            "HotkeyRoadmapAddSelectionRow",
            L10n.Get("HotkeyRoadmapAddSelection"),
            HotkeyHints.RoadmapAddSelection);
        AssertHotkeyRow(
            view,
            "HotkeyRoadmapRemoveSelectionRow",
            L10n.Get("HotkeyRoadmapRemoveSelection"),
            HotkeyHints.RoadmapRemoveSelection);
        AssertHotkeyRow(
            view,
            "HotkeyRoadmapPanRow",
            L10n.Get("HotkeyRoadmapPan"),
            HotkeyHints.RoadmapPan);

        AssertHotkeyTextOccurrence(view, HotkeyHints.RenameTask, 1);
        AssertHotkeyTextOccurrence(view, HotkeyHints.CreateSibling, 1);
        AssertHotkeyTextOccurrence(view, HotkeyHints.CreateBlockedSibling, 1);
        AssertHotkeyTextOccurrence(view, HotkeyHints.CreateInner, 1);

        AssertHotkeyRowLabelDoesNotOverlapChip(view, "HotkeyTaskTreeExpandCurrentRow");
        AssertHotkeyRowLabelDoesNotOverlapChip(view, "HotkeyTaskTreeCollapseCurrentRow");
        AssertHotkeyRowLabelDoesNotOverlapChip(view, "HotkeySelectionOutlineCopyOutlineRow");
        AssertHotkeyRowLabelDoesNotOverlapChip(view, "HotkeyDragSourcesBlockTargetRow");
        AssertHotkeyRowLabelDoesNotOverlapChip(view, "HotkeyRoadmapToggleSelectionRow");

        scrollViewer.Offset = new Vector(0, scrollViewer.Extent.Height);
        Dispatcher.UIThread.RunJobs();

        await Assert.That(scrollViewer.Offset.Y).IsGreaterThan(0);
    }

    private static void AssertHotkeyTextOccurrence(Control root, string expectedText, int expectedCount)
    {
        var actualCount = root.GetVisualDescendants()
            .OfType<TextBlock>()
            .Count(textBlock => string.Equals(textBlock.Text, expectedText, StringComparison.Ordinal));

        if (actualCount != expectedCount)
        {
            throw new InvalidOperationException(
                $"Hotkey text '{expectedText}' appears {actualCount} times, expected {expectedCount}.");
        }
    }

    private static void AssertText(Control root, string automationId, string expectedText)
    {
        var textBlock = FindControlInDetachedContent<TextBlock>(root, automationId) ??
                        throw new InvalidOperationException($"Text block '{automationId}' was not found.");

        if (!string.Equals(textBlock.Text, expectedText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Text block '{automationId}' has text '{textBlock.Text}', expected '{expectedText}'.");
        }
    }

    private static void AssertHotkeyRow(Control root, string automationId, string expectedLabel, string expectedHotkey)
    {
        var row = FindControlInDetachedContent<Grid>(root, automationId) ??
                  throw new InvalidOperationException($"Hotkey row '{automationId}' was not found.");
        var texts = row.GetVisualDescendants()
            .OfType<TextBlock>()
            .Concat(row.GetLogicalDescendants().OfType<TextBlock>())
            .Select(textBlock => textBlock.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (!texts.Contains(expectedLabel, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Hotkey row '{automationId}' does not contain label '{expectedLabel}'. Actual: {string.Join(", ", texts)}.");
        }

        if (!texts.Contains(expectedHotkey, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Hotkey row '{automationId}' does not contain hotkey '{expectedHotkey}'. Actual: {string.Join(", ", texts)}.");
        }
    }

    private static void AssertHotkeyRowLabelDoesNotOverlapChip(Control root, string automationId)
    {
        var row = FindControlInDetachedContent<Grid>(root, automationId) ??
                  throw new InvalidOperationException($"Hotkey row '{automationId}' was not found.");
        var label = row.Children
            .OfType<TextBlock>()
            .FirstOrDefault(textBlock => textBlock.Classes.Contains("HotkeyRowLabel")) ??
                    throw new InvalidOperationException($"Hotkey row '{automationId}' label was not found.");
        var chip = row.Children
            .OfType<Border>()
            .FirstOrDefault(border => border.Classes.Contains("HotkeyChip")) ??
                   throw new InvalidOperationException($"Hotkey row '{automationId}' chip was not found.");

        if (label.TextWrapping != TextWrapping.Wrap)
        {
            throw new InvalidOperationException($"Hotkey row '{automationId}' label must wrap before reaching the chip.");
        }

        if (label.Margin.Right < 8)
        {
            throw new InvalidOperationException($"Hotkey row '{automationId}' label must keep spacing before the chip.");
        }

        var labelTopLeft = label.TranslatePoint(new Point(0, 0), row) ??
                           throw new InvalidOperationException($"Hotkey row '{automationId}' label bounds are unavailable.");
        var chipTopLeft = chip.TranslatePoint(new Point(0, 0), row) ??
                          throw new InvalidOperationException($"Hotkey row '{automationId}' chip bounds are unavailable.");
        var labelBounds = new Rect(labelTopLeft, label.Bounds.Size);
        var chipBounds = new Rect(chipTopLeft, chip.Bounds.Size);

        if (labelBounds.Intersects(chipBounds))
        {
            throw new InvalidOperationException(
                $"Hotkey row '{automationId}' label bounds overlap the hotkey chip. " +
                $"Label={labelBounds}; chip={chipBounds}.");
        }
    }

    private static Control FindVisibleToolbarFocusTarget(Control root)
    {
        return root.GetVisualDescendants()
            .OfType<Control>()
            .First(control =>
                AutomationProperties.GetAutomationId(control) == "AllTasksFiltersButton" &&
                control.IsAttachedToVisualTree() &&
                control.IsVisible &&
                control.IsEnabled);
    }

    private static Control FindWrapperControl(TreeView tree, string taskId)
    {
        var titleControl = tree.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(control =>
                string.Equals(
                    AutomationProperties.GetAutomationId(control),
                    "InlineTaskTitleTextBlock",
                    StringComparison.Ordinal) &&
                TryGetTaskItem(control.DataContext)?.Id == taskId &&
                control.IsAttachedToVisualTree() &&
                control.IsVisible &&
                control.IsEnabled);

        if (titleControl != null)
        {
            return titleControl;
        }

        return tree.GetVisualDescendants()
            .OfType<Control>()
            .First(control => control.DataContext is TaskWrapperViewModel wrapper && wrapper.TaskItem.Id == taskId);
    }

    private static Control FindWrapperControl(TreeView tree, TaskWrapperViewModel targetWrapper)
    {
        TreeViewItem? targetItem = null;
        var itemReady = WaitFor(() =>
        {
            targetItem = tree.GetVisualDescendants()
                .OfType<TreeViewItem>()
                .FirstOrDefault(item => ReferenceEquals(item.DataContext, targetWrapper));
            return targetItem != null;
        });

        if (!itemReady || targetItem == null)
        {
            throw new InvalidOperationException(
                $"Tree item for task wrapper '{targetWrapper.TaskItem.Id}' was not found.");
        }

        var titleControl = targetItem.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(control =>
                string.Equals(
                    AutomationProperties.GetAutomationId(control),
                    "InlineTaskTitleTextBlock",
                    StringComparison.Ordinal) &&
                control.IsAttachedToVisualTree() &&
                control.IsVisible &&
                control.IsEnabled);

        if (titleControl != null)
        {
            return titleControl;
        }

        return targetItem;
    }

    private static TextBlock WaitForInlineTitleTextBlock(
        Control root,
        string taskId,
        string treeAutomationId,
        int timeoutMilliseconds = 2000)
    {
        TextBlock? textBlock = null;
        var ready = WaitFor(() =>
        {
            textBlock = root.GetVisualDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(candidate =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(candidate),
                        "InlineTaskTitleTextBlock",
                        StringComparison.Ordinal) &&
                    TryGetTaskItem(candidate.DataContext)?.Id == taskId &&
                    HasVisualAncestorWithAutomationId(candidate, treeAutomationId) &&
                    candidate.IsAttachedToVisualTree() &&
                    candidate.IsVisible &&
                    candidate.IsEnabled);

            return textBlock != null;
        }, timeoutMilliseconds);

        if (!ready || textBlock == null)
        {
            throw new InvalidOperationException($"Inline title text for task '{taskId}' was not found.");
        }

        return textBlock;
    }

    private static TextBox WaitForInlineTitleEditor(Control root, string taskId, int timeoutMilliseconds = 2000)
    {
        TextBox? textBox = null;
        var ready = WaitFor(() =>
        {
            textBox = FindInlineTitleEditor(root, taskId);

            return textBox != null;
        }, timeoutMilliseconds);

        if (!ready || textBox == null)
        {
            throw new InvalidOperationException($"Inline title editor for task '{taskId}' was not found.");
        }

        return textBox;
    }

    private static TextBox? FindInlineTitleEditor(Control root, string taskId)
    {
        return root.GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(candidate =>
                string.Equals(
                    AutomationProperties.GetAutomationId(candidate),
                    "InlineTaskTitleTextBox",
                    StringComparison.Ordinal) &&
                TryGetTaskItem(candidate.DataContext)?.Id == taskId &&
                candidate.IsAttachedToVisualTree() &&
                candidate.IsVisible &&
                candidate.IsEnabled);
    }

    private static async Task AssertInlineTitleEditorHasNoFrame(TextBox inlineEditor)
    {
        await Assert.That(inlineEditor.BorderThickness).IsEqualTo(new Thickness(0));
        await Assert.That(inlineEditor.Padding).IsEqualTo(new Thickness(0));
        await Assert.That(IsTransparentBrush(inlineEditor.BorderBrush)).IsTrue();
        await Assert.That(IsTransparentBrush(inlineEditor.Background)).IsTrue();
        await Assert.That(inlineEditor.SelectedText).IsEqualTo(inlineEditor.Text);

        var templateBorder = FindInlineTitleEditorTemplateBorder(inlineEditor);
        if (templateBorder == null)
        {
            throw new InvalidOperationException("Inline title editor template border was not found.");
        }

        await Assert.That(templateBorder.BorderThickness).IsEqualTo(new Thickness(0));
        await Assert.That(IsTransparentBrush(templateBorder.BorderBrush)).IsTrue();
        await Assert.That(IsTransparentBrush(templateBorder.Background)).IsTrue();
    }

    private static Border? FindInlineTitleEditorTemplateBorder(TextBox inlineEditor)
    {
        return inlineEditor.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "PART_BorderElement", StringComparison.Ordinal));
    }

    private static bool IsTransparentBrush(IBrush? brush)
    {
        if (brush == null || brush.Opacity <= 0)
        {
            return true;
        }

        return brush is ISolidColorBrush solidColorBrush && solidColorBrush.Color.A == 0;
    }

    private static bool IsVisibleInVisualTree(Control control)
    {
        for (Control? current = control; current != null; current = current.GetVisualParent() as Control)
        {
            if (!current.IsVisible)
            {
                return false;
            }
        }

        return true;
    }

    private static TaskItemViewModel? TryGetTaskItem(object? source)
    {
        return source switch
        {
            TaskItemViewModel taskItem => taskItem,
            TaskWrapperViewModel wrapper => wrapper.TaskItem,
            _ => null
        };
    }

    private static TaskWrapperViewModel? TryGetWrapper(Control control)
    {
        return control.DataContext as TaskWrapperViewModel ??
               control.FindParentDataContext<TaskWrapperViewModel>();
    }

    private static bool HasVisualAncestorWithAutomationId(Control control, string automationId)
    {
        for (Control? current = control; current != null; current = current.GetVisualParent() as Control)
        {
            if (AutomationProperties.GetAutomationId(current) == automationId)
            {
                return true;
            }
        }

        return false;
    }

    private static TreeViewItem FindWrapperTreeItem(TreeView tree, string taskId)
    {
        return tree.GetVisualDescendants()
            .OfType<TreeViewItem>()
            .First(item => item.DataContext is TaskWrapperViewModel wrapper && wrapper.TaskItem.Id == taskId);
    }

    private static TreeViewItem? FindWrapperTreeItemOrDefault(TreeView tree, TaskWrapperViewModel wrapper)
    {
        return tree.GetVisualDescendants()
            .OfType<TreeViewItem>()
            .FirstOrDefault(item => ReferenceEquals(item.DataContext, wrapper));
    }

    private static bool IntersectsVisibleBounds(Control relativeTo, Control control)
    {
        var topLeft = control.TranslatePoint(new Point(0, 0), relativeTo);
        if (!topLeft.HasValue)
        {
            return false;
        }

        var controlBounds = new Rect(topLeft.Value, control.Bounds.Size);
        var visibleBounds = new Rect(0, 0, relativeTo.Bounds.Width, relativeTo.Bounds.Height);
        return controlBounds.Width > 0 &&
               controlBounds.Height > 0 &&
               visibleBounds.Intersects(controlBounds);
    }

    private static IReadOnlyList<TaskWrapperViewModel> GetSelectedWrappers(TreeView tree)
    {
        return tree.SelectedItems
            .OfType<TaskWrapperViewModel>()
            .ToList();
    }

    private static void SelectTreeWrapper(TreeView tree, TaskWrapperViewModel wrapper)
    {
        tree.SelectedItems?.Clear();
        tree.SelectedItems?.Add(wrapper);
        tree.SelectedItem = wrapper;
    }

    private static int CountWrappers(IEnumerable<TaskWrapperViewModel> roots)
    {
        return roots.Sum(wrapper => 1 + CountWrappers(wrapper.SubTasks));
    }

    private static IEnumerable<TaskWrapperViewModel> GetRootsForTree(MainWindowViewModel vm, string treeName)
    {
        return treeName switch
        {
            "AllTasksTree" => vm.CurrentAllTasksItems,
            "LastCreatedTree" => vm.LastCreatedItems,
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
            ? treeName == "CompletedTree"
                ? rootWrapper?.SubTasks.FirstOrDefault()
                : rootWrapper
            : rootWrapper?.SubTasks.FirstOrDefault(wrapper => wrapper.TaskItem.Id == childTaskId);
    }

    private static async Task PrepareTreeScenarioAsync(MainWindowViewModel vm, string treeName)
    {
        switch (treeName)
        {
            case "UnlockedTree":
            {
                var childTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.SubTask22Id);
                await Assert.That(childTask).IsNotNull();

                childTask!.IsCompleted = true;
                childTask.CompletedDateTime = DateTimeOffset.UtcNow;
                await vm.taskRepository!.Update(childTask);

                break;
            }
            case "CompletedTree":
            {
                EnsureStatusFilterSelected(vm, DomainTaskStatus.Completed);
                SetDateFilterAllTime(vm.CompletedDateFilter);

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
            case "ArchivedTree":
            {
                EnsureStatusFilterSelected(vm, DomainTaskStatus.Archived);
                SetDateFilterAllTime(vm.ArchivedDateFilter);
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

    private static void EnsureStatusFilterSelected(MainWindowViewModel vm, DomainTaskStatus status)
    {
        var filter = vm.StatusFilters.FirstOrDefault(item => item.Status == status)
                     ?? throw new InvalidOperationException($"Status filter for {status} was not found.");
        filter.ShowTasks = true;

        if (status == DomainTaskStatus.Completed)
        {
            vm.ShowCompleted = true;
        }
        else if (status == DomainTaskStatus.Archived)
        {
            vm.ShowArchived = true;
        }

        Dispatcher.UIThread.RunJobs();
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
