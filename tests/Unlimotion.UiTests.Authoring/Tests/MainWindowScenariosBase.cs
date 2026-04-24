using AppAutomation.Abstractions;
using AppAutomation.TUnit;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;

namespace Unlimotion.UiTests.Authoring.Tests;

public abstract partial class MainWindowScenariosBase<TSession> : UiTestBase<TSession, MainWindowPage>
    where TSession : class, IUiTestSession
{
    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Main_window_loads_current_task_on_launch()
    {
        using (Assert.Multiple())
        {
            await Assert.That(Page.MainTabs.AutomationId).IsEqualTo("MainTabs");
            await UiAssert.TextEqualsAsync(
                () => Page.CurrentTaskTitleTextBox.Text,
                UnlimotionAppLaunchHost.CurrentTaskTitle);
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Card_relation_picker_can_be_opened_from_task_card()
    {
        await Assert.That(Page.MainTabs.AutomationId).IsEqualTo("MainTabs");

        Page.ClickButton(static page => page.CurrentTaskParentsRelationAddButton);

        var cancelButton = WaitUntil(
            () => TryResolveDuringWait(() => Page.CurrentTaskParentsRelationAddCancelButton),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Parents relation picker did not open.")!;

        using (Assert.Multiple())
        {
            await UiAssert.TextEqualsAsync(
                () => Page.CurrentTaskTitleTextBox.Text,
                UnlimotionAppLaunchHost.CurrentTaskTitle);
            await Assert.That(cancelButton.AutomationId)
                .IsEqualTo("CurrentTaskParentsRelationAddCancelButton");
            await Assert.That(Page.CurrentItemParentsTree.AutomationId)
                .IsEqualTo("CurrentItemParentsTree");
        }

        cancelButton.Invoke();
    }

    private static TControl? TryResolveDuringWait<TControl>(Func<TControl> resolve)
        where TControl : class
    {
        try
        {
            return resolve();
        }
        catch
        {
            return null;
        }
    }
}
