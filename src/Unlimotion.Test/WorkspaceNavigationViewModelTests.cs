using System.Threading.Tasks;
using Unlimotion.ViewModel.Workspace;

namespace Unlimotion.Test;

public sealed class WorkspaceNavigationViewModelTests
{
    [Test]
    public async Task CurrentTabNavigation_BackAndForwardRestoreLocations_AndNewNavigationDropsForwardBranch()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot);
        var note = WorkspaceLocation.ForNote("Project/Plan.md", "Plan");
        var task = WorkspaceLocation.ForTask("task-1", "Task");

        await workspace.OpenAsync(note, WorkspaceOpenDisposition.CurrentTab);
        await workspace.OpenAsync(task, WorkspaceOpenDisposition.CurrentTab);

        await Assert.That(await workspace.GoBackAsync()).IsTrue();
        await Assert.That(workspace.ActiveTab.CurrentLocation).IsEqualTo(note);
        await Assert.That(await workspace.GoForwardAsync()).IsTrue();
        await Assert.That(workspace.ActiveTab.CurrentLocation).IsEqualTo(task);

        await workspace.GoBackAsync();
        var replacement = WorkspaceLocation.ForNote("Project/Decision.md", "Decision");
        await workspace.OpenAsync(replacement, WorkspaceOpenDisposition.CurrentTab);

        await Assert.That(workspace.ActiveTab.CurrentLocation).IsEqualTo(replacement);
        await Assert.That(workspace.CanGoForward).IsFalse();
        await Assert.That(workspace.History.Count).IsEqualTo(3);
    }

    [Test]
    public async Task NewTabKeepsSourceAndAddsToGlobalHistory()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.TasksRoot);
        var sourceTab = workspace.ActiveTab;
        var task = WorkspaceLocation.ForTask("task-1", "Task");

        await workspace.OpenAsync(task, WorkspaceOpenDisposition.NewTab);

        await Assert.That(workspace.PrimaryPane.Tabs.Count).IsEqualTo(2);
        await Assert.That(workspace.PrimaryPane.ActiveTab).IsNotSameReferenceAs(sourceTab);
        await Assert.That(sourceTab.CurrentLocation).IsEqualTo(WorkspaceLocation.TasksRoot);
        await Assert.That(workspace.PrimaryPane.ActiveTab!.CurrentLocation).IsEqualTo(task);
        await Assert.That(workspace.CanGoBack).IsTrue();
        await workspace.GoBackAsync();
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(sourceTab);
    }

    [Test]
    public async Task AdjacentPaneIsCreatedOnceThenReusedWithGlobalHistory()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot);
        var task = WorkspaceLocation.ForTask("task-1", "Task");
        var secondTask = WorkspaceLocation.ForTask("task-2", "Second task");

        await workspace.OpenAsync(task, WorkspaceOpenDisposition.AdjacentPane);
        var secondary = workspace.SecondaryPane!;
        var secondaryTab = secondary.ActiveTab!;
        workspace.ActivatePane(workspace.PrimaryPane);
        await workspace.OpenAsync(secondTask, WorkspaceOpenDisposition.AdjacentPane);

        await Assert.That(workspace.Panes.Length).IsEqualTo(2);
        await Assert.That(workspace.SecondaryPane).IsSameReferenceAs(secondary);
        await Assert.That(secondary.ActiveTab).IsSameReferenceAs(secondaryTab);
        await Assert.That(secondaryTab.CurrentLocation).IsEqualTo(secondTask);
        await Assert.That(workspace.CanGoBack).IsTrue();
        await Assert.That(workspace.PrimaryPane.ActiveTab!.CurrentLocation).IsEqualTo(WorkspaceLocation.FeedRoot);
    }

    [Test]
    public async Task ExistingTaskIsFocusedAcrossPanes_AndBackReturnsToFeed()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot);
        var task = WorkspaceLocation.ForTask("task-1", "Task");
        await workspace.OpenAsync(task, WorkspaceOpenDisposition.AdjacentPane);
        var taskTab = workspace.ActiveTab;
        workspace.ActivatePane(workspace.PrimaryPane);

        await workspace.OpenAsync(task, WorkspaceOpenDisposition.CurrentTab);

        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(taskTab);
        await Assert.That(workspace.SecondaryPane!.Tabs.Count).IsEqualTo(1);
        await Assert.That(workspace.PrimaryPane.ActiveTab!.CurrentLocation).IsEqualTo(WorkspaceLocation.FeedRoot);
        await Assert.That(await workspace.GoBackAsync()).IsTrue();
        await Assert.That(workspace.ActivePane).IsSameReferenceAs(workspace.PrimaryPane);
        await Assert.That(workspace.ActiveTab.CurrentLocation).IsEqualTo(WorkspaceLocation.FeedRoot);
    }

    [Test]
    public async Task OpenBesideMovesAlreadyOpenTabInsteadOfDuplicatingIt()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot);
        var task = WorkspaceLocation.ForTask("task-1", "Task");
        await workspace.OpenAsync(task, WorkspaceOpenDisposition.NewTab);
        var taskTab = workspace.ActiveTab;
        await workspace.SelectTabAsync(workspace.PrimaryPane, workspace.PrimaryPane.Tabs[0]);

        await Assert.That(await workspace.OpenAsync(task, WorkspaceOpenDisposition.AdjacentPane)).IsTrue();
        await Assert.That(workspace.PrimaryPane.Tabs.Count).IsEqualTo(1);
        await Assert.That(workspace.SecondaryPane!.Tabs.Count).IsEqualTo(1);
        await Assert.That(workspace.SecondaryPane.ActiveTab).IsSameReferenceAs(taskTab);
        await Assert.That(await workspace.OpenAsync(task, WorkspaceOpenDisposition.NewTab)).IsTrue();
        await Assert.That(workspace.SecondaryPane.Tabs.Count).IsEqualTo(1);
    }

    [Test]
    public async Task DistinctTaskListsHaveDistinctKeys_ButSameCategoryIsReused()
    {
        var all = WorkspaceLocation.TasksRoot with { StateKey = "tasktab:0" };
        var unlocked = WorkspaceLocation.TasksRoot with { StateKey = "tasktab:1" };
        var workspace = new WorkspaceNavigationViewModel(all);
        await workspace.OpenAsync(unlocked, WorkspaceOpenDisposition.AdjacentPane);
        workspace.ActivatePane(workspace.PrimaryPane);
        await workspace.OpenAsync(unlocked, WorkspaceOpenDisposition.NewTab);

        await Assert.That(workspace.PrimaryPane.Tabs.Count).IsEqualTo(1);
        await Assert.That(workspace.SecondaryPane!.Tabs.Count).IsEqualTo(1);
        await Assert.That(workspace.ActivePane).IsSameReferenceAs(workspace.SecondaryPane);
        await Assert.That(all.ObjectKey == unlocked.ObjectKey).IsFalse();
    }

    [Test]
    public async Task SameNotePathWithDifferentSlashAndCaseReusesItsOpenTab()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot);
        await workspace.OpenAsync(WorkspaceLocation.ForNote("Projects/Plan.md", "Plan"),
            WorkspaceOpenDisposition.NewTab);
        var noteTab = workspace.ActiveTab;
        await workspace.SelectTabAsync(workspace.PrimaryPane, workspace.PrimaryPane.Tabs[0]);

        var alternatePath = System.OperatingSystem.IsWindows()
            ? "projects\\plan.md"
            : "Projects\\Plan.md";
        await workspace.OpenAsync(WorkspaceLocation.ForNote(alternatePath, "Plan"),
            WorkspaceOpenDisposition.NewTab);

        await Assert.That(workspace.PrimaryPane.Tabs.Count).IsEqualTo(2);
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(noteTab);
        await Assert.That(await workspace.GoBackAsync()).IsTrue();
        await Assert.That(workspace.ActiveTab.CurrentLocation).IsEqualTo(WorkspaceLocation.FeedRoot);
    }

    [Test]
    public async Task FailedCommitLeavesActiveLocationAndPaneUnchanged()
    {
        var workspace = new WorkspaceNavigationViewModel(
            WorkspaceLocation.FeedRoot,
            _ => Task.FromResult(false));
        var tab = workspace.ActiveTab;
        var target = WorkspaceLocation.ForTask("task-1", "Task");

        var navigated = await workspace.OpenAsync(target, WorkspaceOpenDisposition.AdjacentPane);

        await Assert.That(navigated).IsFalse();
        await Assert.That(workspace.HasSecondaryPane).IsFalse();
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(tab);
        await Assert.That(workspace.ActiveTab.CurrentLocation).IsEqualTo(WorkspaceLocation.FeedRoot);
    }

    [Test]
    public async Task FailedCommitPreventsTabSelectionAndPaneClose()
    {
        var allowCommit = true;
        var workspace = new WorkspaceNavigationViewModel(
            WorkspaceLocation.FeedRoot,
            _ => Task.FromResult(allowCommit));
        var primaryTab = workspace.ActiveTab;
        await workspace.OpenAsync(
            WorkspaceLocation.ForTask("task-1", "Task"),
            WorkspaceOpenDisposition.NewTab);
        var secondTab = workspace.ActiveTab;
        await workspace.SelectTabAsync(workspace.PrimaryPane, primaryTab);

        allowCommit = false;
        await Assert.That(await workspace.SelectTabAsync(workspace.PrimaryPane, secondTab)).IsFalse();
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(primaryTab);
        allowCommit = true;
        await Assert.That(await workspace.OpenAsync(
            WorkspaceLocation.ForTask("task-2", "Task 2"),
            WorkspaceOpenDisposition.AdjacentPane)).IsTrue();
        // The adjacent open is accepted above only after the save gate is restored.
        await Assert.That(workspace.HasSecondaryPane).IsTrue();
        allowCommit = false;
        await Assert.That(await workspace.CloseSecondaryPaneAsync(WorkspaceLocation.TasksRoot)).IsFalse();
        await Assert.That(workspace.HasSecondaryPane).IsTrue();
    }

    [Test]
    public async Task ClosingLastTabReturnsToRootInsteadOfLeavingWorkspaceEmpty()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot);
        await workspace.OpenAsync(WorkspaceLocation.ForNote("A.md", "A"), WorkspaceOpenDisposition.CurrentTab);

        await workspace.CloseActiveTabAsync(WorkspaceLocation.FeedRoot);

        await Assert.That(workspace.PrimaryPane.Tabs.Count).IsEqualTo(1);
        await Assert.That(workspace.ActiveTab.CurrentLocation).IsEqualTo(WorkspaceLocation.FeedRoot);
    }
}
