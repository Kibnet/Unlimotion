using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion.Test;

internal static class WorkspaceBreadcrumbsLastOpenedUiContract
{
    public static async Task<WorkspaceBreadcrumbsLastOpenedScenarioResult>
        ExecuteWorkspaceBreadcrumbsLastOpenedScenarioAsync()
    {
        var result = new WorkspaceBreadcrumbsLastOpenedScenarioResult();
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

                    var parentTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id)
                        ?? throw new InvalidOperationException("Parent task was not found.");
                    var childTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.SubTask22Id)
                        ?? throw new InvalidOperationException("Child task was not found.");

                    parentTask.Title = "📚 BDD Last Opened Parent";
                    childTask.Title = "🧪 BDD Last Opened Child";
                    var expectedBreadcrumbs = parentTask.Title + " / " + childTask.Title;

                    var view = new MainControl { DataContext = vm };
                    window = CreateWindow(view);
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    result.MainControlOpened = true;

                    var breadcrumbs = view.FindControl<EmojiTextBlock>("BreadcrumbsTextBlock");
                    var lastOpenedTree = view.FindControl<TreeView>("LastOpenedTree");
                    result.BreadcrumbsControlAvailable = breadcrumbs is not null;
                    result.LastOpenedTreeAvailable = lastOpenedTree is not null;

                    vm.DetailsAreOpen = true;
                    TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RootTask1Id);
                    Dispatcher.UIThread.RunJobs();
                    TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.SubTask22Id);
                    Dispatcher.UIThread.RunJobs();

                    result.BreadcrumbsShowCurrentPath = await WaitUntilAsync(() =>
                        GetBreadcrumbsText(breadcrumbs)
                            .Contains(expectedBreadcrumbs, StringComparison.Ordinal));

                    SelectTab(view, "LastOpenedTabItem");
                    result.LastOpenedProjectionAvailable = await WaitUntilAsync(() =>
                        ContainsWrapper(vm.LastOpenedItems, MainWindowViewModelFixture.RootTask1Id) &&
                        ContainsWrapper(vm.LastOpenedItems, MainWindowViewModelFixture.SubTask22Id));

                    var previousWrapper = FindWrapper(vm.LastOpenedItems, MainWindowViewModelFixture.RootTask1Id);
                    var childWrapper = FindWrapper(vm.LastOpenedItems, MainWindowViewModelFixture.SubTask22Id);
                    result.LastOpenedContainsRecentTasks = previousWrapper is not null && childWrapper is not null;

                    if (lastOpenedTree is not null && previousWrapper is not null)
                    {
                        lastOpenedTree.SelectedItem = previousWrapper;
                        Dispatcher.UIThread.RunJobs();
                    }

                    result.LastOpenedSelectionRestoresPreviousTask = await WaitUntilAsync(() =>
                        vm.CurrentLastOpenedItem?.TaskItem.Id == MainWindowViewModelFixture.RootTask1Id &&
                        vm.CurrentTaskItem?.Id == MainWindowViewModelFixture.RootTask1Id);

                    if (lastOpenedTree is not null && childWrapper is not null)
                    {
                        lastOpenedTree.SelectedItem = childWrapper;
                        Dispatcher.UIThread.RunJobs();
                    }

                    result.LastOpenedSelectionRestoresNestedTask = await WaitUntilAsync(() =>
                        vm.CurrentLastOpenedItem?.TaskItem.Id == MainWindowViewModelFixture.SubTask22Id &&
                        vm.CurrentTaskItem?.Id == MainWindowViewModelFixture.SubTask22Id);
                    result.BreadcrumbsReturnToNestedPath = await WaitUntilAsync(() =>
                        GetBreadcrumbsText(breadcrumbs)
                            .Contains(expectedBreadcrumbs, StringComparison.Ordinal));
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

    public static async Task AssertWorkspaceBreadcrumbsLastOpenedScenarioResultAsync(
        WorkspaceBreadcrumbsLastOpenedScenarioResult result)
    {
        await Assert.That(result.MainControlOpened).IsTrue();
        await Assert.That(result.BreadcrumbsControlAvailable).IsTrue();
        await Assert.That(result.LastOpenedTreeAvailable).IsTrue();
        await Assert.That(result.BreadcrumbsShowCurrentPath).IsTrue();
        await Assert.That(result.LastOpenedProjectionAvailable).IsTrue();
        await Assert.That(result.LastOpenedContainsRecentTasks).IsTrue();
        await Assert.That(result.LastOpenedSelectionRestoresPreviousTask).IsTrue();
        await Assert.That(result.LastOpenedSelectionRestoresNestedTask).IsTrue();
        await Assert.That(result.BreadcrumbsReturnToNestedPath).IsTrue();
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

    private static void SelectTab(MainControl view, string automationId)
    {
        var tabControl = view.GetVisualDescendants().OfType<TabControl>().First();
        tabControl.SelectedItem = FindControlByAutomationId<TabItem>(view, automationId);
        Dispatcher.UIThread.RunJobs();
    }

    private static T? FindControlByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        return root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(candidate =>
                string.Equals(
                    AutomationProperties.GetAutomationId(candidate),
                    automationId,
                    StringComparison.Ordinal));
    }

    private static string GetBreadcrumbsText(EmojiTextBlock? breadcrumbs)
    {
        return breadcrumbs?.Inlines is null
            ? string.Empty
            : string.Concat(breadcrumbs.Inlines.OfType<Run>().Select(run => run.Text));
    }

    private static bool ContainsWrapper(
        IEnumerable<TaskWrapperViewModel> roots,
        string taskId)
    {
        return FindWrapper(roots, taskId) is not null;
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

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate)
    {
        return await TestHelpers.WaitUntilAsync(
            () =>
            {
                Dispatcher.UIThread.RunJobs();
                return predicate();
            },
            TimeSpan.FromSeconds(2));
    }
}

internal sealed class WorkspaceBreadcrumbsLastOpenedScenarioResult
{
    public bool MainControlOpened { get; set; }

    public bool BreadcrumbsControlAvailable { get; set; }

    public bool LastOpenedTreeAvailable { get; set; }

    public bool BreadcrumbsShowCurrentPath { get; set; }

    public bool LastOpenedProjectionAvailable { get; set; }

    public bool LastOpenedContainsRecentTasks { get; set; }

    public bool LastOpenedSelectionRestoresPreviousTask { get; set; }

    public bool LastOpenedSelectionRestoresNestedTask { get; set; }

    public bool BreadcrumbsReturnToNestedPath { get; set; }
}
