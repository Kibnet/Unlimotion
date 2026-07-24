using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TUnit;
using Avalonia.Threading;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;
using Unlimotion.ViewModel;

namespace Unlimotion.UiTests.Headless.Tests;

public sealed class TaskSpacesHeadlessTests
    : UiTestBase<MainWindowHeadlessTests.HeadlessRuntimeSession, MainWindowPage>
{
    protected override MainWindowHeadlessTests.HeadlessRuntimeSession LaunchSession() =>
        new(
            DesktopAppSession.Launch(
                UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
                    UnlimotionAutomationScenario.TaskSpaces)));

    protected override MainWindowPage CreatePage(MainWindowHeadlessTests.HeadlessRuntimeSession session) =>
        new(new HeadlessControlResolver(session.Inner.MainWindow));

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Spaces_render_selector_and_settings_management_controls()
    {
        await Assert.That(Page.TaskSpaceSelector.AutomationId).IsEqualTo("TaskSpaceSelector");
        var vm = GetViewModel();
        await Assert.That(vm.Settings.TaskSpaces.Select(space => space.DisplayName))
            .IsEquivalentTo(["Space A", "Space B"]);
        await Assert.That(GetOnlyTaskTitle(vm)).IsEqualTo(
            UnlimotionAutomationScenarioData.TaskSpacesSpaceATitle);

        HeadlessRuntime.Dispatch(() =>
        {
            vm.SettingsMode = true;
            Dispatcher.UIThread.RunJobs();
        });
        await Assert.That(Page.TaskSpacesSection.AutomationId).IsEqualTo("TaskSpacesSection");
        await Assert.That(Page.TaskSpacesList.AutomationId).IsEqualTo("TaskSpacesList");
        await Assert.That(Page.AddTaskSpaceButton.AutomationId).IsEqualTo("AddTaskSpaceButton");
        await Assert.That(Page.RenameTaskSpaceButton.AutomationId).IsEqualTo("RenameTaskSpaceButton");
        await Assert.That(Page.RemoveTaskSpaceButton.AutomationId).IsEqualTo("RemoveTaskSpaceButton");
        await Assert.That(Page.RemoveTaskSpaceButton.IsEnabled).IsTrue();
        await Assert.That(vm.Settings.SwitchTaskSpaceCommand).IsNotNull();
        await Assert.That(vm.Settings.AddTaskSpaceCommand).IsNotNull();
        await Assert.That(vm.Settings.RenameTaskSpaceCommand).IsNotNull();
        await Assert.That(vm.Settings.RemoveTaskSpaceCommand).IsNotNull();
    }

    private MainWindowViewModel GetViewModel() =>
        HeadlessRuntime.Dispatch(() =>
            Session.Inner.MainWindow.DataContext as MainWindowViewModel
            ?? throw new InvalidOperationException("Task-spaces window did not expose MainWindowViewModel."));

    private static string GetOnlyTaskTitle(MainWindowViewModel vm) =>
        HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return vm.taskRepository?.Tasks.Items.Single().Title
                ?? throw new InvalidOperationException("The active task space did not contain exactly one task.");
        });

}
