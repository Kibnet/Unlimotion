using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TUnit;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;
using Unlimotion.UiTests.Authoring.Tests;
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
        return new MainWindowHeadlessTests.HeadlessRuntimeSession(
            DesktopAppSession.Launch(
                UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
                    UnlimotionAutomationScenario.ReadmeDemo,
                    Language,
                    vm => _vm = vm)));
    }

    protected override MainWindowPage CreatePage(MainWindowHeadlessTests.HeadlessRuntimeSession session)
    {
        return new MainWindowPage(new HeadlessControlResolver(session.Inner.MainWindow));
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Readme_demo_uses_capture_presentation_state()
    {
        var vm = GetMainWindowViewModel();
        WaitForExpandedTree(() =>
            FindExpandedWrapper(vm.CurrentAllTasksItems, UnlimotionAutomationScenarioData.ReadmeDemoCurrentTaskId) is not null
            && AllParentNodesExpanded(vm.CurrentAllTasksItems));

        using (Assert.Multiple())
        {
            await Assert.That(vm.Title)
                .IsEqualTo(UnlimotionAppLaunchHost.GetWindowTitle(
                    UnlimotionAutomationScenario.ReadmeDemo,
                    Language));
            await Assert.That(AllParentNodesExpanded(vm.CurrentAllTasksItems)).IsTrue();
            await Assert.That(FindExpandedWrapper(vm.CurrentAllTasksItems, UnlimotionAutomationScenarioData.ReadmeDemoCurrentTaskId) is not null).IsTrue();
        }

        Page.SelectTabItem(static page => page.LastCreatedTabItem, timeoutMs: 10_000);
        WaitForExpandedTree(() => vm.LastCreatedItems.Count > 0 && AllParentNodesExpanded(vm.LastCreatedItems));
        await Assert.That(AllParentNodesExpanded(vm.LastCreatedItems)).IsTrue();

        Page.SelectTabItem(static page => page.LastUpdatedTabItem, timeoutMs: 10_000);
        WaitForExpandedTree(() => vm.LastUpdatedItems.Count > 0 && AllParentNodesExpanded(vm.LastUpdatedItems));
        await Assert.That(AllParentNodesExpanded(vm.LastUpdatedItems)).IsTrue();

        Page.SelectTabItem(static page => page.UnlockedTabItem, timeoutMs: 10_000);
        WaitForExpandedTree(() => vm.UnlockedItems.Count > 0 && AllParentNodesExpanded(vm.UnlockedItems));
        await Assert.That(AllParentNodesExpanded(vm.UnlockedItems)).IsTrue();

        Page.SelectTabItem(static page => page.LastOpenedTabItem, timeoutMs: 10_000);
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
