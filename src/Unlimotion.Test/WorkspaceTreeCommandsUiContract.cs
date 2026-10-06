using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion.Test;

internal static class WorkspaceTreeCommandsUiContract
{
    public static async Task<WorkspaceTreeCommandsScenarioResult> ExecuteWorkspaceTreeCommandsScenarioAsync()
    {
        var result = new WorkspaceTreeCommandsScenarioResult();
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
                    ((NotificationManagerWrapperMock)vm.ManagerWrapper).AskResult = true;

                    var view = new MainControl { DataContext = vm };
                    window = CreateWindow(view);
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    result.MainControlOpened = true;

                    var allTasksTree = view.FindControl<TreeView>("AllTasksTree");
                    result.AllTasksTreeAvailable = allTasksTree is not null;
                    result.TreeCommandRouteBound = vm.ExecuteTreeCommandAction is not null;

                    var repository = vm.taskRepository
                        ?? throw new InvalidOperationException("Task repository was not initialized.");
                    var parent = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask1Id)
                        ?? throw new InvalidOperationException("Parent task was not found.");
                    var parentWrapper = await WaitForWrapperAsync(
                        vm.CurrentAllTasksItems,
                        MainWindowViewModelFixture.RootTask1Id);

                    SelectWrapper(allTasksTree!, vm, parentWrapper);
                    result.SelectionSynced = await WaitUntilAsync(() =>
                        vm.CurrentAllTasksItem?.TaskItem.Id == parent.Id &&
                        vm.CurrentTaskItem?.Id == parent.Id);

                    vm.CollapseAllNodes(vm.CurrentAllTasksItems);
                    Dispatcher.UIThread.RunJobs();
                    vm.ExpandAllTreeNodesCommand.Execute(null);
                    result.ExpandAllCommandWorked = await WaitUntilAsync(() =>
                        vm.CurrentAllTasksItems.All(IsExpandedRecursive));

                    vm.CollapseAllTreeNodesCommand.Execute(null);
                    result.CollapseAllCommandWorked = await WaitUntilAsync(() =>
                        vm.CurrentAllTasksItems.All(IsCollapsedRecursive));

                    var copyChild = await CreateChildWithTitleAsync(
                        repository,
                        parent,
                        "BDD tree command copy child");
                    _ = await CreateChildWithTitleAsync(
                        repository,
                        copyChild,
                        "BDD tree command copy grandchild");
                    var copyChildWrapper = await WaitForWrapperAsync(vm.CurrentAllTasksItems, copyChild.Id);

                    SelectWrapper(allTasksTree!, vm, copyChildWrapper);
                    vm.ExpandNodeAndDescendants(copyChildWrapper);
                    result.ExpandCurrentCommandWorked = await WaitUntilAsync(() => copyChildWrapper.IsExpanded);

                    vm.CollapseCurrentNestedCommand.Execute(null);
                    result.CollapseCurrentCommandWorked = await WaitUntilAsync(() =>
                        IsCollapsedRecursive(copyChildWrapper));

                    string? clipboardText = null;
                    vm.SetClipboardTextAsync = text =>
                    {
                        clipboardText = text;
                        return Task.CompletedTask;
                    };
                    SelectWrapper(allTasksTree!, vm, copyChildWrapper);
                    vm.CopyTaskOutlineTreeCommand.Execute(null);
                    result.CopyOutlineCommandWorked = await WaitUntilAsync(() =>
                        NormalizeNewLines(clipboardText) ==
                        "BDD tree command copy child\n\tBDD tree command copy grandchild");

                    const string pasteOutline =
                        "BDD tree command paste root\n" +
                        "\tBDD tree command paste child\n" +
                        "BDD tree command paste sibling";
                    var countBeforePaste = repository.Tasks.Count;
                    var clipboardReadCount = 0;
                    vm.GetClipboardTextAsync = () =>
                    {
                        clipboardReadCount++;
                        return Task.FromResult<string?>(pasteOutline);
                    };

                    SelectWrapper(allTasksTree!, vm, parentWrapper);
                    vm.PasteTaskOutlineTreeCommand.Execute(null);
                    result.PasteOutlineCommandWorked = await WaitUntilAsync(() =>
                        clipboardReadCount == 1 &&
                        repository.Tasks.Count == countBeforePaste + 3 &&
                        repository.Tasks.Items.Any(task => task.Title == "BDD tree command paste root") &&
                        repository.Tasks.Items.Any(task => task.Title == "BDD tree command paste child") &&
                        repository.Tasks.Items.Any(task => task.Title == "BDD tree command paste sibling"));
                    if (!result.PasteOutlineCommandWorked)
                    {
                        // Capture the failed conjuncts at the original deadline, before
                        // the next command or teardown can obscure an async-void import.
                        var notifications = (NotificationManagerWrapperMock)vm.ManagerWrapper;
                        var cachedTasks = repository.Tasks.Items.ToArray();
                        var focused = window.FocusManager?.GetFocusedElement();
                        var persistedPasteTasks = new List<string>();
                        try
                        {
                            foreach (var file in Directory.EnumerateFiles(fixture.DefaultTasksFolderPath))
                            {
                                if (!Guid.TryParse(Path.GetFileName(file), out _)) continue;
                                try
                                {
                                    var stored = TestHelpers.GetStorageTaskItem(
                                        fixture.DefaultTasksFolderPath, Path.GetFileName(file));
                                    if (stored?.Title?.StartsWith("BDD tree command paste ", StringComparison.Ordinal) == true)
                                        persistedPasteTasks.Add($"{stored.Id}:{stored.Title}");
                                }
                                catch (Exception exception)
                                {
                                    persistedPasteTasks.Add($"{Path.GetFileName(file)}:<{exception.GetType().Name}>");
                                }
                            }
                        }
                        catch (Exception exception)
                        {
                            persistedPasteTasks.Add($"<directory:{exception.GetType().Name}>");
                        }
                        result.PasteOutlineDiagnostics = JsonSerializer.Serialize(new
                        {
                            ClipboardReadCount = clipboardReadCount,
                            PreviewTaskCount = notifications.LastTaskOutlinePastePreview?.TaskCount,
                            ConfirmationCount = notifications.TaskOutlinePasteConfirmationCount,
                            Errors = notifications.ErrorMessages.ToArray(),
                            CountBefore = countBeforePaste,
                            ExpectedCount = countBeforePaste + 3,
                            Count = cachedTasks.Length,
                            CachedTasks = cachedTasks.Select(task => new { task.Id, task.Title }).ToArray(),
                            PersistedPasteTasks = persistedPasteTasks.ToArray(),
                            Focused = focused is Control control ? $"{control.GetType().Name}#{control.Name}" : focused?.GetType().Name,
                            TreeHasFocus = allTasksTree!.IsKeyboardFocusWithin,
                            SelectedTask = (allTasksTree.SelectedItem as TaskWrapperViewModel)?.TaskItem.Id,
                            CurrentTask = vm.CurrentTaskItem?.Id,
                            CurrentTreeTask = vm.CurrentAllTasksItem?.TaskItem.Id,
                            ExpectedParent = parent.Id,
                            ParentWrapperStillInRootProjection = vm.CurrentAllTasksItems.Any(wrapper => ReferenceEquals(wrapper, parentWrapper))
                        });
                    }

                    var deleteTarget = await CreateRootWithTitleAsync(
                        repository,
                        "BDD tree command delete target");
                    var deleteWrapper = await WaitForWrapperAsync(vm.CurrentAllTasksItems, deleteTarget.Id);
                    SelectWrapper(allTasksTree!, vm, deleteWrapper);
                    vm.DeleteSelectedTreeItemsCommand.Execute(null);
                    result.DeleteSelectionCommandWorked = await WaitUntilAsync(() =>
                        repository.Tasks.Items.All(task => task.Id != deleteTarget.Id));
                }
                finally
                {
                    window?.Close();
                    await fixture.CleanTasksAsync();
                }
            }, CancellationToken.None);
        }
        finally
        {
            await session.DisposeIgnoringHeadlessTeardownNullReferenceAsync();
        }

        return result;
    }

    public static async Task AssertWorkspaceTreeCommandsScenarioResultAsync(
        WorkspaceTreeCommandsScenarioResult result)
    {
        await Assert.That(result.MainControlOpened).IsTrue();
        await Assert.That(result.AllTasksTreeAvailable).IsTrue();
        await Assert.That(result.TreeCommandRouteBound).IsTrue();
        await Assert.That(result.SelectionSynced).IsTrue();
        await Assert.That(result.ExpandAllCommandWorked).IsTrue();
        await Assert.That(result.CollapseAllCommandWorked).IsTrue();
        await Assert.That(result.ExpandCurrentCommandWorked).IsTrue();
        await Assert.That(result.CollapseCurrentCommandWorked).IsTrue();
        await Assert.That(result.CopyOutlineCommandWorked).IsTrue();
        await Assert.That(result.PasteOutlineCommandWorked).IsTrue().Because(result.PasteOutlineDiagnostics);
        await Assert.That(result.DeleteSelectionCommandWorked).IsTrue();
    }

    private static Window CreateWindow(Control content)
    {
        return new Window
        {
            Width = 1800,
            Height = 1000,
            Content = content
        };
    }

    private static async Task<TaskItemViewModel> CreateChildWithTitleAsync(
        ITaskStorage repository,
        TaskItemViewModel parent,
        string title)
    {
        var task = await repository.AddChild(parent);
        return await UpdateTitleAsync(repository, task, title);
    }

    private static async Task<TaskItemViewModel> CreateRootWithTitleAsync(
        ITaskStorage repository,
        string title)
    {
        var task = await repository.Add();
        return await UpdateTitleAsync(repository, task, title);
    }

    private static async Task<TaskItemViewModel> UpdateTitleAsync(
        ITaskStorage repository,
        TaskItemViewModel task,
        string title)
    {
        task.Title = title;
        await repository.Update(task);
        Dispatcher.UIThread.RunJobs();
        var updated = repository.Tasks.Lookup(task.Id);
        return updated.HasValue ? updated.Value : task;
    }

    private static void SelectWrapper(
        TreeView tree,
        MainWindowViewModel vm,
        TaskWrapperViewModel wrapper)
    {
        tree.SelectedItems?.Clear();
        tree.SelectedItems?.Add(wrapper);
        tree.SelectedItem = wrapper;
        vm.CurrentAllTasksItem = wrapper;
        tree.Focus();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<TaskWrapperViewModel> WaitForWrapperAsync(
        IEnumerable<TaskWrapperViewModel> roots,
        string taskId)
    {
        TaskWrapperViewModel? wrapper = null;
        var found = await WaitUntilAsync(() =>
        {
            wrapper = FindWrapper(roots, taskId);
            return wrapper is not null;
        });

        await Assert.That(found).IsTrue();
        return wrapper!;
    }

    private static TaskWrapperViewModel? FindWrapper(
        IEnumerable<TaskWrapperViewModel> roots,
        string taskId)
    {
        foreach (var wrapper in roots)
        {
            if (string.Equals(wrapper.TaskItem.Id, taskId, StringComparison.Ordinal))
            {
                return wrapper;
            }

            var child = FindWrapper(wrapper.SubTasks, taskId);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static bool IsExpandedRecursive(TaskWrapperViewModel wrapper)
    {
        return (wrapper.SubTasks.Count == 0 || wrapper.IsExpanded) &&
               wrapper.SubTasks.All(IsExpandedRecursive);
    }

    private static bool IsCollapsedRecursive(TaskWrapperViewModel wrapper)
    {
        return (wrapper.SubTasks.Count == 0 || !wrapper.IsExpanded) &&
               wrapper.SubTasks.All(IsCollapsedRecursive);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate)
    {
        return await TestHelpers.WaitUntilAsync(
            () =>
            {
                Dispatcher.UIThread.RunJobs();
                return predicate();
            },
            TimeSpan.FromSeconds(5));
    }

    private static string? NormalizeNewLines(string? value)
    {
        return value?.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}

internal sealed class WorkspaceTreeCommandsScenarioResult
{
    public bool MainControlOpened { get; set; }

    public bool AllTasksTreeAvailable { get; set; }

    public bool TreeCommandRouteBound { get; set; }

    public bool SelectionSynced { get; set; }

    public bool ExpandAllCommandWorked { get; set; }

    public bool CollapseAllCommandWorked { get; set; }

    public bool ExpandCurrentCommandWorked { get; set; }

    public bool CollapseCurrentCommandWorked { get; set; }

    public bool CopyOutlineCommandWorked { get; set; }

    public bool PasteOutlineCommandWorked { get; set; }

    public string PasteOutlineDiagnostics { get; set; } = string.Empty;

    public bool DeleteSelectionCommandWorked { get; set; }
}
