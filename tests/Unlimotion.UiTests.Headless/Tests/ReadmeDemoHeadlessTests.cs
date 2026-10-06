using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TUnit;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;
using Unlimotion.UiTests.Authoring.Tests;
using Unlimotion.UiTests.Headless.Infrastructure;
using Unlimotion.ViewModel;

namespace Unlimotion.UiTests.Headless.Tests;

[InheritsTests]
public abstract class ReadmeDemoHeadlessTestsBase
    : MainWindowScenariosBase<MainWindowHeadlessTests.HeadlessRuntimeSession>
{
    protected abstract string Language { get; }

    private MainWindowViewModel? _vm;

    protected override string ExpectedCurrentTaskTitle =>
        UnlimotionAppLaunchHost.GetCurrentTaskTitle(UnlimotionAutomationScenario.ReadmeDemo, Language);

    protected override MainWindowHeadlessTests.HeadlessRuntimeSession LaunchSession()
    {
        var session = new MainWindowHeadlessTests.HeadlessRuntimeSession(
            DesktopAppSession.Launch(
                UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
                    UnlimotionAutomationScenario.ReadmeDemo,
                    Language,
                    vm => _vm = vm,
                    viewModelFactoryDispatcher: factory => HeadlessRuntime.Dispatch(factory),
                    prepareViewModelDispatcher: HeadlessSessionHooks.PrepareAsync,
                    headlessWindowCleanup: HeadlessSessionHooks.CloseWindow)));
        HeadlessRuntime.Session.Dispatch<bool>(async () =>
        {
            session.Inner.MainWindow.Width = 1600;
            session.Inner.MainWindow.Height = 800;
            session.Inner.MainWindow.Show();
            Dispatcher.UIThread.RunJobs();
            session.Inner.MainWindow.UpdateLayout();
            await GetMainWindowViewModel().TryOpenTaskByIdAsync(
                UnlimotionAppLaunchHost.GetCurrentTaskId(UnlimotionAutomationScenario.ReadmeDemo, Language));
            return true;
        }, CancellationToken.None).GetAwaiter().GetResult();
        return session;
    }

    protected override MainWindowPage CreatePage(MainWindowHeadlessTests.HeadlessRuntimeSession session)
    {
        return new MainWindowPage(new HeadlessControlResolver(session.Inner.MainWindow));
    }

    protected override void PrepareMainTabSelection(string automationId) => HeadlessRuntime.Dispatch(() =>
    {
        Session.Inner.MainWindow.Width = 1600;
        Session.Inner.MainWindow.Show();
        Dispatcher.UIThread.RunJobs();
        Session.Inner.MainWindow.UpdateLayout();
    });

    protected override void ReopenFixtureTaskCard()
    {
        Page.WorkspaceRailAllTasksButton.Invoke();
        var taskId = UnlimotionAppLaunchHost.GetCurrentTaskId(UnlimotionAutomationScenario.ReadmeDemo, Language);
        var title = WaitUntil(() => HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants()
                .OfType<Control>().FirstOrDefault(control => control.IsEffectivelyVisible &&
                    AutomationProperties.GetAutomationId(control) == "TaskTitle_" + taskId)),
            control => control is not null, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "README fixture did not expose the task title to reopen.")!;
        HeadlessRuntime.Dispatch(() =>
        {
            title.BringIntoView();
            Session.Inner.MainWindow.UpdateLayout();
            var point = title.TranslatePoint(new Point(title.Bounds.Width / 2, title.Bounds.Height / 2), Session.Inner.MainWindow)!.Value;
            Session.Inner.MainWindow.MouseDown(point, MouseButton.Left);
            Session.Inner.MainWindow.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Readme_demo_uses_capture_presentation_state()
    {
        var vm = GetMainWindowViewModel();
        var expectedCurrentTaskId = UnlimotionAppLaunchHost.GetCurrentTaskId(
            UnlimotionAutomationScenario.ReadmeDemo,
            Language);
        WaitForExpandedTree(() =>
            FindExpandedWrapper(vm.CurrentAllTasksItems, expectedCurrentTaskId) is not null
            && AllParentNodesExpanded(vm.CurrentAllTasksItems));

        using (Assert.Multiple())
        {
            await Assert.That(vm.Title)
                .IsEqualTo(UnlimotionAppLaunchHost.GetWindowTitle(
                    UnlimotionAutomationScenario.ReadmeDemo,
                    Language));
            await Assert.That(AllParentNodesExpanded(vm.CurrentAllTasksItems)).IsTrue();
            await Assert.That(FindExpandedWrapper(vm.CurrentAllTasksItems, expectedCurrentTaskId) is not null).IsTrue();
        }

        Page.WorkspaceRailLastCreatedButton.Invoke();
        WaitForExpandedTree(() => vm.LastCreatedItems.Count > 0 && AllParentNodesExpanded(vm.LastCreatedItems));
        await Assert.That(AllParentNodesExpanded(vm.LastCreatedItems)).IsTrue();

        Page.WorkspaceRailLastUpdatedButton.Invoke();
        WaitForExpandedTree(() => vm.LastUpdatedItems.Count > 0 && AllParentNodesExpanded(vm.LastUpdatedItems));
        await Assert.That(AllParentNodesExpanded(vm.LastUpdatedItems)).IsTrue();

        Page.WorkspaceRailUnlockedButton.Invoke();
        WaitForExpandedTree(() => vm.UnlockedItems.Count > 0 && AllParentNodesExpanded(vm.UnlockedItems));
        await Assert.That(AllParentNodesExpanded(vm.UnlockedItems)).IsTrue();

        Page.WorkspaceRailLastOpenedButton.Invoke();
        WaitForExpandedTree(() => vm.LastOpenedItems.Count > 0 && AllParentNodesExpanded(vm.LastOpenedItems));
        await Assert.That(AllParentNodesExpanded(vm.LastOpenedItems)).IsTrue();
    }

    private static void WaitForExpandedTree(Func<bool> condition)
    {
        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < TimeSpan.FromSeconds(10))
        {
            if (condition())
            {
                return;
            }

            Thread.Sleep(100);
        }
    }

    private MainWindowViewModel GetMainWindowViewModel()
    {
        return _vm ?? throw new InvalidOperationException("Headless MainWindowViewModel was not available.");
    }

    private static bool AllParentNodesExpanded(IEnumerable<TaskWrapperViewModel> roots)
    {
        return roots.All(AllParentNodesExpanded);
    }

    private static bool AllParentNodesExpanded(TaskWrapperViewModel? wrapper)
    {
        if (wrapper is null)
        {
            return true;
        }

        var children = wrapper.SubTasks;
        return (children.Count == 0 || wrapper.IsExpanded)
            && children.All(AllParentNodesExpanded);
    }

    private static TaskWrapperViewModel? FindExpandedWrapper(
        IEnumerable<TaskWrapperViewModel> roots,
        string taskId)
    {
        foreach (var root in roots)
        {
            if (root.TaskItem.Id == taskId && root.IsExpanded)
            {
                return root;
            }

            var child = FindExpandedWrapper(root.SubTasks, taskId);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }
}

[InheritsTests]
public sealed class ReadmeDemoEnglishHeadlessTests : ReadmeDemoHeadlessTestsBase
{
    protected override string Language => "en";
}

[InheritsTests]
public sealed class ReadmeDemoRussianHeadlessTests : ReadmeDemoHeadlessTestsBase
{
    protected override string Language => "ru";
}
