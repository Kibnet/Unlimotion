using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TUnit;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;
using Unlimotion.ViewModel;

namespace Unlimotion.UiTests.Headless.Tests;

public sealed class TaskSpacesStartupRecoveryHeadlessTests
    : UiTestBase<MainWindowHeadlessTests.HeadlessRuntimeSession, MainWindowPage>
{
    protected override MainWindowHeadlessTests.HeadlessRuntimeSession LaunchSession() =>
        new(
            DesktopAppSession.Launch(
                UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
                    UnlimotionAutomationScenario.TaskSpacesDuplicateCatalogRecovery)));

    protected override MainWindowPage CreatePage(MainWindowHeadlessTests.HeadlessRuntimeSession session) =>
        new(new HeadlessControlResolver(session.Inner.MainWindow));

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Duplicate_catalog_renders_blocking_recovery_state_with_identifier()
    {
        var vm = HeadlessRuntime.Dispatch(() =>
            Session.Inner.MainWindow.DataContext as MainWindowViewModel
            ?? throw new InvalidOperationException("Recovery window did not expose MainWindowViewModel."));

        await Assert.That(Page.TaskSpaceRecoveryOverlay.AutomationId)
            .IsEqualTo("TaskSpaceRecoveryOverlay");
        await Assert.That(vm.Settings.IsTaskSpaceRecoveryRequired).IsTrue();
        await Assert.That(vm.Settings.TaskSpaceRecoveryMessage).Contains("default");
        await Assert.That(vm.taskRepository).IsNull();
    }
}
