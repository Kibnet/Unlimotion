using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
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
    public async Task TaskOutlinePastePreviewDialog_LargePreview_IsScrollableAndShowsLastTask()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
                var filterCheckBox = FindVisibleFilterCheckBox(view, vm);

                await Assert.That(allTasksTree).IsNotNull();
                await ClickControlAsync(window, allTasksTree!);
                filterCheckBox.Focus();

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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
                child.Title = "Outline UI copy child";
                await vm.taskRepository.Update(child);
                var grandchild = await vm.taskRepository.AddChild(child);
                grandchild.Title = "Outline UI copy grandchild";
                await vm.taskRepository.Update(grandchild);

                var wrappersReady = WaitFor(() =>
                    vm.FindTaskWrapperViewModel(parent!, vm.CurrentAllTasksItems) != null &&
                    vm.FindTaskWrapperViewModel(child, vm.CurrentAllTasksItems) != null);
                await Assert.That(wrappersReady).IsTrue();

                var parentWrapper = vm.FindTaskWrapperViewModel(parent!, vm.CurrentAllTasksItems);
                var childWrapper = vm.FindTaskWrapperViewModel(child, vm.CurrentAllTasksItems);
                await Assert.That(parentWrapper).IsNotNull();
                await Assert.That(childWrapper).IsNotNull();
                parentWrapper!.IsExpanded = true;
                childWrapper!.IsExpanded = true;

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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
                zuluChild.Title = "Zulu visible outline child";
                await vm.taskRepository.Update(zuluChild);

                var hiddenChild = await vm.taskRepository.AddChild(parent);
                hiddenChild.Title = "\u274C Alpha hidden excluded outline child";
                await vm.taskRepository.Update(hiddenChild);

                var alphaChild = await vm.taskRepository.AddChild(parent);
                alphaChild.Title = "Alpha visible outline child";
                await vm.taskRepository.Update(alphaChild);

                var excludeFilterReady = WaitFor(() => vm.EmojiExcludeFilters.Any(filter => filter.Emoji == "\u274C"));
                await Assert.That(excludeFilterReady).IsTrue();
                vm.EmojiExcludeFilters.First(filter => filter.Emoji == "\u274C").ShowTasks = true;

                var wrapperReady = WaitFor(() =>
                {
                    var wrapper = vm.FindTaskWrapperViewModel(parent!, vm.CurrentAllTasksItems);
                    return wrapper?.SubTasks.Select(child => child.TaskItem.Title).SequenceEqual([
                        "Alpha visible outline child",
                        "Zulu visible outline child"
                    ]) == true;
                });
                await Assert.That(wrapperReady).IsTrue();
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

                var copied = WaitFor(() => clipboardText != null);
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
    public async Task TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask()
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
                vm.AllTasksMode = true;
                ((NotificationManagerWrapperMock)vm.ManagerWrapper).AskResult = true;

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
                PressHotkey(window, Key.V, PhysicalKey.V, RawInputModifiers.Control | RawInputModifiers.Shift);

                var pasted = WaitFor(() =>
                    vm.taskRepository.Tasks.Count == countBefore + 4 &&
                    vm.taskRepository.Tasks.Items.Any(task => task.Title == "Outline UI paste root") &&
                    vm.taskRepository.Tasks.Items.Any(task => task.Title == "Outline UI paste child") &&
                    vm.taskRepository.Tasks.Items.Any(task => task.Title == "Outline UI paste grandchild") &&
                    vm.taskRepository.Tasks.Items.Any(task => task.Title == "Outline UI paste sibling"));
                await Assert.That(pasted).IsTrue();
                await Assert.That(clipboardReadCount).IsEqualTo(1);
                await Assert.That(((NotificationManagerWrapperMock)vm.ManagerWrapper).LastTaskOutlinePastePreview).IsNotNull();
                await Assert.That(((NotificationManagerWrapperMock)vm.ManagerWrapper).LastTaskOutlinePastePreview!.TaskCount).IsEqualTo(4);

                var pastedRoot = FindTaskByTitle(vm, "Outline UI paste root");
                var pastedChild = FindTaskByTitle(vm, "Outline UI paste child");
                var pastedGrandchild = FindTaskByTitle(vm, "Outline UI paste grandchild");
                var pastedSibling = FindTaskByTitle(vm, "Outline UI paste sibling");

                await Assert.That(parent.Contains).Contains(pastedRoot.Id);
                await Assert.That(parent.Contains).Contains(pastedSibling.Id);
                await Assert.That(pastedRoot.Parents).Contains(parent.Id);
                await Assert.That(pastedRoot.Contains).Contains(pastedChild.Id);
                await Assert.That(pastedChild.Contains).Contains(pastedGrandchild.Id);
                await Assert.That(pastedGrandchild.Parents).Contains(pastedChild.Id);
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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

    [Test]
    public async Task TreeCommandUi_ShiftDelete_RemovesSelectedLastUpdatedTreeItem()
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
    [Arguments(4, "CompletedTree", MainWindowViewModelFixture.CompletedTaskId, MainWindowViewModelFixture.CompletedTaskId)]
    [Arguments(5, "ArchivedTree", MainWindowViewModelFixture.ArchivedTask1Id, MainWindowViewModelFixture.ArchivedTask11Id)]
    [Arguments(6, "LastOpenedTree", MainWindowViewModelFixture.RootTask2Id, MainWindowViewModelFixture.RootTask2Id)]
    public async Task TreeCommandUi_NonAllTasksTabs_CurrentAndAllCommands_Work(
        int tabIndex,
        string treeName,
        string rootTaskId,
        string childTaskId)
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
                window.Show();
                Dispatcher.UIThread.RunJobs();

                SelectTab(view, 3);
                var unlockedReady = WaitFor(() => vm.UnlockedItems.Any());
                await Assert.That(unlockedReady).IsTrue();

                var unlockedTree = view.FindControl<TreeView>("UnlockedTree");
                await Assert.That(unlockedTree).IsNotNull();
                await Assert.That(unlockedTree!.ContextMenu).IsNotNull();

                vm.CollapseAllNodes(vm.UnlockedItems);
                Dispatcher.UIThread.RunJobs();

                unlockedTree.ContextMenu!.PlacementTarget = unlockedTree;
                unlockedTree.ContextMenu.Open(unlockedTree);
                Dispatcher.UIThread.RunJobs();
                ClearStoredTreeCommandContext(view);
                var expandAllMenuItem = unlockedTree.ContextMenu.Items
                    .OfType<MenuItem>()
                    .First(item => string.Equals(item.Tag?.ToString(), "ExpandAll", StringComparison.Ordinal));
                InvokeMenuItemClick(expandAllMenuItem);
                unlockedTree.ContextMenu.Close();

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
    public async Task TreeCommandUi_ContextMenu_UsesPlacementTargetItemWithoutStoredContextForCurrentCommand()
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
                var filterCheckBox = FindVisibleFilterCheckBox(view, vm);
                await Assert.That(allTasksTree).IsNotNull();

                await ClickControlAsync(window, allTasksTree!);
                filterCheckBox.Focus();
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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

    [Test]
    public async Task TreeCommandUi_CtrlA_SelectsTextWhenTextInputFocused()
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
            "Completed" => 4,
            "Archived" => 5,
            "Last Opened" => 6,
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

    private static CheckBox FindVisibleFilterCheckBox(Control root, MainWindowViewModel viewModel)
    {
        return root.GetVisualDescendants()
            .OfType<CheckBox>()
            .First(control =>
                ReferenceEquals(control.DataContext, viewModel) &&
                control.Content != null &&
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

    private static bool IsExpandedRecursive(TaskWrapperViewModel wrapper)
    {
        return wrapper.IsExpanded && wrapper.SubTasks.All(IsExpandedRecursive);
    }

    private static bool IsCollapsedRecursive(TaskWrapperViewModel wrapper)
    {
        return !wrapper.IsExpanded && wrapper.SubTasks.All(IsCollapsedRecursive);
    }
}
