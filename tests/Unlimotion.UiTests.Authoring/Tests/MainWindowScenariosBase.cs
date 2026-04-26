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

        var input = WaitUntil(
            () => TryResolveDuringWait(() => Page.CurrentTaskParentsRelationAddInput),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Parents relation editor input did not open.")!;
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
            await Assert.That(input.AutomationId)
                .IsEqualTo("CurrentTaskParentsRelationAddInput");
            await Assert.That(cancelButton.AutomationId)
                .IsEqualTo("CurrentTaskParentsRelationAddCancelButton");
            await Assert.That(Page.CurrentItemParentsTree.AutomationId)
                .IsEqualTo("CurrentItemParentsTree");
        }

        cancelButton.Invoke();
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Card_blocking_relation_editor_can_be_opened_from_task_card()
    {
        await Assert.That(Page.MainTabs.AutomationId).IsEqualTo("MainTabs");

        Page.ClickButton(static page => page.CurrentTaskBlockingRelationAddButton);

        var input = WaitUntil(
            () => TryResolveDuringWait(() => Page.CurrentTaskBlockingRelationAddInput),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Blocking relation editor input did not open.")!;
        var cancelButton = WaitUntil(
            () => TryResolveDuringWait(() => Page.CurrentTaskBlockingRelationAddCancelButton),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Blocking relation editor did not open.")!;

        using (Assert.Multiple())
        {
            await Assert.That(input.AutomationId)
                .IsEqualTo("CurrentTaskBlockingRelationAddInput");
            await Assert.That(cancelButton.AutomationId)
                .IsEqualTo("CurrentTaskBlockingRelationAddCancelButton");
            await Assert.That(Page.CurrentItemBlockedByTree.AutomationId)
                .IsEqualTo("CurrentItemBlockedByTree");
        }

        cancelButton.Invoke();
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Card_containing_relation_editor_can_be_opened_from_task_card()
    {
        await Assert.That(Page.MainTabs.AutomationId).IsEqualTo("MainTabs");

        Page.ClickButton(static page => page.CurrentTaskContainingRelationAddButton);

        var input = WaitUntil(
            () => TryResolveDuringWait(() => Page.CurrentTaskContainingRelationAddInput),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Containing relation editor input did not open.")!;
        var cancelButton = WaitUntil(
            () => TryResolveDuringWait(() => Page.CurrentTaskContainingRelationAddCancelButton),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Containing relation editor did not open.")!;

        using (Assert.Multiple())
        {
            await Assert.That(input.AutomationId)
                .IsEqualTo("CurrentTaskContainingRelationAddInput");
            await Assert.That(cancelButton.AutomationId)
                .IsEqualTo("CurrentTaskContainingRelationAddCancelButton");
            await Assert.That(Page.CurrentItemContainsTree.AutomationId)
                .IsEqualTo("CurrentItemContainsTree");
        }

        cancelButton.Invoke();
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Card_blocked_relation_editor_can_be_opened_from_task_card()
    {
        await Assert.That(Page.MainTabs.AutomationId).IsEqualTo("MainTabs");

        Page.ClickButton(static page => page.CurrentTaskBlockedRelationAddButton);

        var input = WaitUntil(
            () => TryResolveDuringWait(() => Page.CurrentTaskBlockedRelationAddInput),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Blocked relation editor input did not open.")!;
        var cancelButton = WaitUntil(
            () => TryResolveDuringWait(() => Page.CurrentTaskBlockedRelationAddCancelButton),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Blocked relation editor did not open.")!;

        using (Assert.Multiple())
        {
            await Assert.That(input.AutomationId)
                .IsEqualTo("CurrentTaskBlockedRelationAddInput");
            await Assert.That(cancelButton.AutomationId)
                .IsEqualTo("CurrentTaskBlockedRelationAddCancelButton");
            await Assert.That(Page.CurrentItemBlocksTree.AutomationId)
                .IsEqualTo("CurrentItemBlocksTree");
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
