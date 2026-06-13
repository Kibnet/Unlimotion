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
    protected virtual string ExpectedCurrentTaskTitle => UnlimotionAppLaunchHost.CurrentTaskTitle;

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Main_window_loads_current_task_on_launch()
    {
        using (Assert.Multiple())
        {
            await Assert.That(Page.MainTabs.AutomationId).IsEqualTo("MainTabs");
            await UiAssert.TextEqualsAsync(
                () => Page.CurrentTaskTitleTextBox.Text,
                ExpectedCurrentTaskTitle);
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Current_task_card_exposes_redesigned_sections_on_launch()
    {
        await Assert.That(Page.MainTabs.AutomationId).IsEqualTo("MainTabs");

        var card = WaitUntil(
            () => TryResolveDuringWait(() => Page.CurrentTaskCard),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Current task card did not become available.")!;
        var header = WaitUntil(
            () => TryResolveDuringWait(() => Page.CurrentTaskHeader),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Current task header did not become available.")!;
        var commandBar = WaitUntil(
            () => TryResolveDuringWait(() => Page.CurrentTaskCommandBar),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Current task command bar did not become available.")!;

        using (Assert.Multiple())
        {
            await Assert.That(card.AutomationId).IsEqualTo("CurrentTaskCard");
            await Assert.That(header.AutomationId).IsEqualTo("CurrentTaskHeader");
            await Assert.That(commandBar.AutomationId).IsEqualTo("CurrentTaskCommandBar");
            await Assert.That(Page.CurrentTaskDescriptionSection.AutomationId)
                .IsEqualTo("CurrentTaskDescriptionSection");
            await Assert.That(Page.CurrentTaskPlanningSection.AutomationId)
                .IsEqualTo("CurrentTaskPlanningSection");
            await Assert.That(Page.CurrentTaskRepeaterSection.AutomationId)
                .IsEqualTo("CurrentTaskRepeaterSection");
            await Assert.That(Page.CurrentTaskRelationsSection.AutomationId)
                .IsEqualTo("CurrentTaskRelationsSection");
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Task_card_can_be_closed_and_reopened_from_details_toggle()
    {
        await Assert.That(Page.CurrentTaskDetailsScrollViewer.AutomationId)
            .IsEqualTo("CurrentTaskDetailsScrollViewer");
        await UiAssert.TextEqualsAsync(
            () => Page.CurrentTaskTitleTextBox.Text,
            ExpectedCurrentTaskTitle);
        await Assert.That(Page.DetailsPaneToggleButton.IsToggled).IsFalse();

        Page.DetailsPaneToggleButton.Toggle();
        WaitUntil(
            () => Page.DetailsPaneToggleButton.IsToggled,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Details pane toggle did not switch to the closed state.");

        Page.DetailsPaneToggleButton.Toggle();
        WaitUntil(
            () => !Page.DetailsPaneToggleButton.IsToggled,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Details pane toggle did not switch to the open state.");
        var detailsPane = WaitUntil(
            () => TryResolveDuringWait(() => Page.CurrentTaskDetailsScrollViewer),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Task details pane did not reappear after reopening.")!;

        using (Assert.Multiple())
        {
            await Assert.That(detailsPane.AutomationId).IsEqualTo("CurrentTaskDetailsScrollViewer");
            await UiAssert.TextEqualsAsync(
                () => Page.CurrentTaskTitleTextBox.Text,
                ExpectedCurrentTaskTitle);
        }
    }
    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Major_tabs_can_be_opened_from_main_window()
    {
        await Assert.That(Page.MainTabs.AutomationId).IsEqualTo("MainTabs");

        Page.SelectTabItem(static page => page.LastCreatedTabItem, timeoutMs: 10_000);
        await Assert.That(Page.LastCreatedTree.AutomationId).IsEqualTo("LastCreatedTree");

        Page.SelectTabItem(static page => page.LastUpdatedTabItem, timeoutMs: 10_000);
        await Assert.That(Page.LastUpdatedTree.AutomationId).IsEqualTo("LastUpdatedTree");

        Page.SelectTabItem(static page => page.UnlockedTabItem, timeoutMs: 10_000);
        await Assert.That(Page.UnlockedTree.AutomationId).IsEqualTo("UnlockedTree");

        Page.SelectTabItem(static page => page.InProgressTabItem, timeoutMs: 10_000);
        await Assert.That(Page.InProgressTree.AutomationId).IsEqualTo("InProgressTree");

        Page.SelectTabItem(static page => page.CompletedTabItem, timeoutMs: 10_000);
        await Assert.That(Page.CompletedTree.AutomationId).IsEqualTo("CompletedTree");

        Page.SelectTabItem(static page => page.ArchivedTabItem, timeoutMs: 10_000);
        await Assert.That(Page.ArchivedTree.AutomationId).IsEqualTo("ArchivedTree");

        Page.SelectTabItem(static page => page.LastOpenedTabItem, timeoutMs: 10_000);
        await Assert.That(Page.LastOpenedTree.AutomationId).IsEqualTo("LastOpenedTree");

        Page.SelectTabItem(static page => page.RoadmapTabItem, timeoutMs: 10_000);
        var roadmapRoot = WaitUntil(
            () => TryResolveDuringWait(() => Page.RoadmapRoot),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Roadmap root did not become available.")!;
        await Assert.That(roadmapRoot.AutomationId).IsEqualTo("RoadmapRoot");

        Page.SelectTabItem(static page => page.SettingsTabItem, timeoutMs: 10_000);
        var settingsRoot = WaitUntil(
            () => TryResolveDuringWait(() => Page.SettingsRoot),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Settings root did not become available.")!;
        await Assert.That(settingsRoot.AutomationId).IsEqualTo("SettingsRoot");
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Settings_ssh_backup_flow_shows_key_controls_for_ssh_remote()
    {
        await Assert.That(Page.MainTabs.AutomationId).IsEqualTo("MainTabs");

        Page.SelectTabItem(static page => page.SettingsTabItem, timeoutMs: 10_000);
        _ = WaitUntil(
            () => TryResolveDuringWait(() => Page.SettingsRoot),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Settings root did not become available.")!;

        Page.BackupAutoCheckBox.IsChecked = true;
        var remoteUrlInput = WaitUntil(
            () => TryResolveDuringWait(() => Page.GitRemoteUrlTextBox),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Git remote URL input did not become available.")!;
        remoteUrlInput.Enter("git@github.com:org/unlimotion-backup.git");
        await UiAssert.TextEqualsAsync(
            () => remoteUrlInput.Text,
            "git@github.com:org/unlimotion-backup.git");

        var sshSection = WaitUntil(
            () => TryResolveDuringWait(() => Page.SshKeysSection),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "SSH keys section did not become available for an SSH remote.")!;
        var connectButton = WaitUntil(
            () => TryResolveDuringWait(() => Page.ConnectRepositoryButton),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Connect repository button did not become available.")!;

        using (Assert.Multiple())
        {
            await Assert.That(Page.BackupAutoCheckBox.IsChecked).IsTrue();
            await Assert.That(remoteUrlInput.Text).IsEqualTo("git@github.com:org/unlimotion-backup.git");
            await Assert.That(sshSection.AutomationId).IsEqualTo("SshKeysSection");
            await Assert.That(Page.SelectedSshPublicKeyComboBox.AutomationId)
                .IsEqualTo("SelectedSshPublicKeyComboBox");
            await Assert.That(connectButton.AutomationId).IsEqualTo("ConnectRepositoryButton");
            await Assert.That(Page.SyncNowButton.AutomationId).IsEqualTo("SyncNowButton");
            await Assert.That(Page.PullNowButton.AutomationId).IsEqualTo("PullNowButton");
            await Assert.That(Page.PushNowButton.AutomationId).IsEqualTo("PushNowButton");
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
                ExpectedCurrentTaskTitle);
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
