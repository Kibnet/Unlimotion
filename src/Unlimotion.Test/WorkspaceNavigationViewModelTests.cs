using System;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.ViewModel.Workspace;

namespace Unlimotion.Test;

public sealed class WorkspaceNavigationViewModelTests
{
    [Test]
    public async Task TaskLists_HaveNineStableKeys_AndDecodeLegacyIndices()
    {
        var kinds = Enum.GetValues<TaskListKind>();
        await Assert.That(kinds.Length).IsEqualTo(9);
        await Assert.That(kinds.Select(kind => WorkspaceLocation.ForTaskList(kind).ObjectKey).Distinct().Count()).IsEqualTo(9);
        foreach (var kind in kinds)
        {
            var location = WorkspaceLocation.ForTaskList(kind, "Explicit caption");
            var legacy = new WorkspaceLocation(WorkspaceLocationKind.Tasks, "tasks", "Old", StateKey: $"tasktab:{(int)kind}");
            await Assert.That(location.ObjectKey).IsEqualTo(legacy.ObjectKey);
            await Assert.That(location.TaskListKind).IsEqualTo(kind);
            await Assert.That(location.Title).IsEqualTo("Explicit caption");
        }
    }

    [Test]
    public async Task Back_RestoresSnapshotInRequestedPaneWithoutChangingItsNeighbor()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.ForTaskList(TaskListKind.InProgress));
        var primary = workspace.PrimaryPane;
        var tab = workspace.ActiveTab;
        var snapshot = new ViewSnapshot("needle", 120);
        tab.CurrentEntry!.ViewState = snapshot;
        await workspace.OpenAsync(WorkspaceLocation.ForTask("a", "A"), WorkspaceOpenDisposition.CurrentTab);
        await workspace.OpenAsync(WorkspaceLocation.ForTaskList(TaskListKind.Unlocked), WorkspaceOpenDisposition.AdjacentPane);
        var neighbor = workspace.ActiveTab;
        object? restored = null;
        workspace.RestoreViewState = (target, entry) => restored = entry.ViewState;
        await Assert.That(await workspace.GoBackAsync(primary)).IsTrue();
        await Assert.That(restored).IsEqualTo(snapshot);
        await Assert.That(tab.CurrentLocation!.TaskListKind).IsEqualTo(TaskListKind.InProgress);
        await Assert.That(neighbor.CurrentLocation!.TaskListKind).IsEqualTo(TaskListKind.Unlocked);
        await Assert.That(neighbor.History.Count).IsEqualTo(1);
    }

    [Test]
    public async Task DuplicateHistoryTarget_FocusesOwnerWithoutChangingEitherHistoryCursorOrSnapshot()
    {
        var root = WorkspaceLocation.ForTaskList(TaskListKind.InProgress);
        var workspace = new WorkspaceNavigationViewModel(root);
        var original = workspace.ActiveTab;
        original.CurrentEntry!.ViewState = new ViewSnapshot("old filter", 10);
        await workspace.OpenAsync(WorkspaceLocation.ForTask("a", "A"), WorkspaceOpenDisposition.CurrentTab);
        await workspace.OpenAsync(root, WorkspaceOpenDisposition.AdjacentPane);
        var owner = workspace.ActiveTab;
        var ownerState = new ViewSnapshot("new filter", 70);
        owner.CurrentEntry!.ViewState = ownerState;
        var restoreCalls = 0;
        workspace.RestoreViewState = (_, _) => restoreCalls++;
        await Assert.That(await workspace.GoBackAsync(workspace.PrimaryPane)).IsTrue();
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(owner);
        await Assert.That(original.CurrentIndex).IsEqualTo(1);
        await Assert.That(original.CurrentLocation!.Id).IsEqualTo("a");
        await Assert.That(owner.CurrentIndex).IsEqualTo(0);
        await Assert.That(owner.History.Count).IsEqualTo(1);
        await Assert.That(owner.CurrentEntry.ViewState).IsEqualTo(ownerState);
        await Assert.That(restoreCalls).IsEqualTo(0);
        await Assert.That(workspace.LastNavigationNotice).IsEqualTo(WorkspaceNavigationNotice.ExistingHistoryDocumentFocused);
    }

    [Test]
    public async Task TargetLocator_UpdatesOnlyOwningHistory_AndBackRestoresPreviousDayAndFilterSnapshot()
    {
        var previous = WorkspaceLocation.ForFeedDay("Daily/A.md", "A", "block-a", "restrictive");
        var workspace = new WorkspaceNavigationViewModel(previous);
        var owningPane = workspace.PrimaryPane;
        var feed = workspace.ActiveTab;
        var originalState = new ViewSnapshot("restrictive", 45);
        feed.CurrentEntry!.ViewState = originalState;
        await workspace.OpenAsync(WorkspaceLocation.ForTask("a", "A"), WorkspaceOpenDisposition.AdjacentPane);
        var unrelated = workspace.ActiveTab;
        var requested = WorkspaceLocation.ForFeedDay("Daily/B.md", "B", "block-b");
        await workspace.OpenAsync(requested, WorkspaceOpenDisposition.NewTab);
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(feed);
        await Assert.That(feed.History.Count).IsEqualTo(2);
        await Assert.That(unrelated.History.Count).IsEqualTo(1);
        await Assert.That(workspace.PrimaryPane.Tabs.Count).IsEqualTo(1);
        await workspace.OpenAsync(requested with { ScrollOffset = 200, StateKey = "changed filter" }, WorkspaceOpenDisposition.AdjacentPane);
        await Assert.That(feed.History.Count).IsEqualTo(2);
        await Assert.That(await workspace.GoBackAsync(owningPane)).IsTrue();
        await Assert.That(feed.CurrentLocation).IsEqualTo(previous);
        await Assert.That(feed.CurrentEntry!.ViewState).IsEqualTo(originalState);
        await Assert.That(unrelated.CurrentLocation!.Id).IsEqualTo("a");
    }

    [Test]
    public async Task TargetLocator_InCurrentOwningTabAddsLocationWithoutDuplicatingTheObject()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.ForNote("A.md", "A", "first"));
        var owner = workspace.ActiveTab;
        await workspace.OpenAsync(WorkspaceLocation.ForNote("A.md", "A", "second"), WorkspaceOpenDisposition.NewTab);
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(owner);
        await Assert.That(owner.History.Count).IsEqualTo(2);
        await Assert.That(workspace.PrimaryPane.Tabs.Count).IsEqualTo(1);
        await workspace.GoBackAsync();
        await Assert.That(owner.CurrentLocation!.Anchor).IsEqualTo("first");
    }

    [Test]
    public async Task GuardFailure_DoesNotCaptureStateOrTruncateForwardBranch()
    {
        var allowed = true;
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot, _ => Task.FromResult(allowed));
        await workspace.OpenAsync(WorkspaceLocation.ForTask("a", "A"), WorkspaceOpenDisposition.CurrentTab);
        await workspace.GoBackAsync();
        var original = new ViewSnapshot("keep", 15);
        workspace.ActiveTab.CurrentEntry!.ViewState = original;
        var captures = 0;
        workspace.CaptureViewState = _ => { captures++; return new ViewSnapshot("replace", 20); };
        allowed = false;
        await Assert.That(await workspace.OpenAsync(WorkspaceLocation.ForTask("b", "B"), WorkspaceOpenDisposition.CurrentTab)).IsFalse();
        await Assert.That(captures).IsEqualTo(0);
        await Assert.That(workspace.ActiveTab.CurrentEntry.ViewState).IsEqualTo(original);
        await Assert.That(workspace.CanGoForward).IsTrue();
        await Assert.That(workspace.History.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ResetAsync_ProtectsInactiveEditorsBeforeClearingAnyDocument()
    {
        WorkspaceNavigationTabViewModel? refusing = null;
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot,
            tab => Task.FromResult(!ReferenceEquals(tab, refusing)));
        var inactive = workspace.ActiveTab;
        await workspace.OpenAsync(WorkspaceLocation.ForNote("A.md", "A"), WorkspaceOpenDisposition.NewTab);
        await workspace.OpenAsync(WorkspaceLocation.ForTask("b", "B"), WorkspaceOpenDisposition.AdjacentPane);
        var active = workspace.ActiveTab;
        refusing = inactive;
        await Assert.That(await workspace.ResetAsync(WorkspaceLocation.TasksRoot)).IsFalse();
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(active);
        await Assert.That(workspace.PrimaryPane.Tabs.Count).IsEqualTo(2);
        await Assert.That(workspace.SecondaryPane!.Tabs.Count).IsEqualTo(1);
        refusing = null;
        await Assert.That(await workspace.ResetAsync(WorkspaceLocation.TasksRoot)).IsTrue();
        await Assert.That(workspace.PrimaryPane.Tabs.Count).IsEqualTo(1);
        await Assert.That(workspace.HasSecondaryPane).IsFalse();
    }

    [Test]
    public async Task ConcurrentOpens_AreSerializedAndRetainTheirOwnTransitions()
    {
        var gate = new TaskCompletionSource<bool>();
        var calls = 0;
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.TasksRoot,
            _ => ++calls == 1 ? gate.Task : Task.FromResult(true));
        var first = workspace.OpenAsync(WorkspaceLocation.ForTask("a", "A"), WorkspaceOpenDisposition.CurrentTab);
        var second = workspace.OpenAsync(WorkspaceLocation.ForTask("b", "B"), WorkspaceOpenDisposition.CurrentTab);
        gate.SetResult(true);
        await Assert.That(await first).IsTrue();
        await Assert.That(await second).IsTrue();
        await Assert.That(workspace.History.Count).IsEqualTo(3);
        await Assert.That(workspace.ActiveTab.CurrentLocation!.Id).IsEqualTo("b");
        await workspace.GoBackAsync();
        await Assert.That(workspace.ActiveTab.CurrentLocation!.Id).IsEqualTo("a");
    }

    [Test]
    public async Task QueuedOpenFromOtherPane_PreservesItsInvocationOwner()
    {
        var gate = new TaskCompletionSource<bool>();
        var delayNextCommit = false;
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.TasksRoot, _ =>
        {
            if (!delayNextCommit) return Task.FromResult(true);
            delayNextCommit = false;
            return gate.Task;
        });
        await workspace.OpenAsync(WorkspaceLocation.ForTaskList(TaskListKind.Unlocked),
            WorkspaceOpenDisposition.AdjacentPane);
        var rightPane = workspace.SecondaryPane!;
        var rightTab = rightPane.ActiveTab!;
        var leftPane = workspace.PrimaryPane;
        var leftTab = leftPane.ActiveTab!;
        workspace.ActivatePane(leftPane);
        delayNextCommit = true;
        var leftOpen = workspace.OpenAsync(WorkspaceLocation.ForTask("a", "A"),
            WorkspaceOpenDisposition.CurrentTab);
        workspace.ActivatePane(rightPane);
        var rightOpen = workspace.OpenAsync(WorkspaceLocation.ForTask("b", "B"),
            WorkspaceOpenDisposition.CurrentTab);

        gate.SetResult(true);
        await Assert.That(await leftOpen).IsTrue();
        await Assert.That(await rightOpen).IsTrue();
        await Assert.That(leftTab.CurrentLocation!.Id).IsEqualTo("a");
        await Assert.That(rightTab.CurrentLocation!.Id).IsEqualTo("b");
        await Assert.That(leftTab.History.Count).IsEqualTo(2);
        await Assert.That(rightTab.History.Count).IsEqualTo(2);
        await Assert.That(workspace.ActivePane).IsSameReferenceAs(rightPane);
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(rightTab);
    }

    [Test]
    public async Task QueuedOpen_RejectsOwnerRemovedByAnEarlierTransition()
    {
        var gate = new TaskCompletionSource<bool>();
        var delayNextCommit = false;
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.TasksRoot, _ =>
        {
            if (!delayNextCommit) return Task.FromResult(true);
            delayNextCommit = false;
            return gate.Task;
        });
        await workspace.OpenAsync(WorkspaceLocation.ForTaskList(TaskListKind.Unlocked),
            WorkspaceOpenDisposition.AdjacentPane);
        var rightPane = workspace.SecondaryPane!;
        var rightTab = rightPane.ActiveTab!;
        workspace.ActivatePane(workspace.PrimaryPane);
        delayNextCommit = true;
        var first = workspace.OpenAsync(WorkspaceLocation.ForTask("a", "A"),
            WorkspaceOpenDisposition.CurrentTab);
        var close = workspace.CloseTabAsync(rightPane, rightTab, WorkspaceLocation.TasksRoot);
        workspace.ActivatePane(rightPane);
        var queued = workspace.OpenAsync(WorkspaceLocation.ForTask("b", "B"),
            WorkspaceOpenDisposition.CurrentTab);

        gate.SetResult(true);
        await Assert.That(await first).IsTrue();
        await Assert.That(await close).IsTrue();
        await Assert.That(await queued).IsFalse();
        await Assert.That(workspace.HasSecondaryPane).IsFalse();
        await Assert.That(workspace.ActiveTab.CurrentLocation!.Id).IsEqualTo("a");
        await Assert.That(rightTab.CurrentLocation!.TaskListKind).IsEqualTo(TaskListKind.Unlocked);
    }

    [Test]
    public async Task GuardRechecksEarlierEditorAfterLaterSave_AndRefusesWithoutMutationUntilRetry()
    {
        var storageGate = new TaskCompletionSource<bool>();
        var laterSaveStarted = new TaskCompletionSource<bool>();
        var delayLaterSave = false;
        var dirtyEarlierEditor = false;
        var refuseEarlierSave = false;
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.TasksRoot, async tab =>
        {
            if (tab.CurrentLocation?.Id == "a")
            {
                if (dirtyEarlierEditor && refuseEarlierSave) return false;
                dirtyEarlierEditor = false;
            }
            if (tab.CurrentLocation?.Id == "b" && delayLaterSave)
            {
                delayLaterSave = false;
                laterSaveStarted.SetResult(true);
                await storageGate.Task;
            }
            return true;
        });
        workspace.HasPendingEditorChanges = tab => tab.CurrentLocation?.Id == "a" && dirtyEarlierEditor;
        await workspace.OpenAsync(WorkspaceLocation.ForTask("a", "A"), WorkspaceOpenDisposition.CurrentTab);
        var primary = workspace.PrimaryPane;
        var earlierTab = workspace.ActiveTab;
        await workspace.OpenAsync(WorkspaceLocation.ForTask("b", "B"), WorkspaceOpenDisposition.AdjacentPane);
        var secondary = workspace.SecondaryPane!;
        var laterTab = workspace.ActiveTab;
        var captured = 0;
        workspace.CaptureViewState = _ => { captured++; return null; };
        var earlierHistoryCount = earlierTab.History.Count;
        var laterHistoryCount = laterTab.History.Count;
        dirtyEarlierEditor = true;
        delayLaterSave = true;
        var move = workspace.MoveTabAsync(primary, earlierTab);
        await laterSaveStarted.Task;
        dirtyEarlierEditor = true;
        refuseEarlierSave = true;
        storageGate.SetResult(true);

        await Assert.That(await move).IsFalse();
        await Assert.That(dirtyEarlierEditor).IsTrue();
        await Assert.That(captured).IsEqualTo(0);
        await Assert.That(workspace.PrimaryPane).IsSameReferenceAs(primary);
        await Assert.That(workspace.SecondaryPane).IsSameReferenceAs(secondary);
        await Assert.That(primary.ActiveTab).IsSameReferenceAs(earlierTab);
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(laterTab);
        await Assert.That(earlierTab.History.Count).IsEqualTo(earlierHistoryCount);
        await Assert.That(laterTab.History.Count).IsEqualTo(laterHistoryCount);

        refuseEarlierSave = false;
        await Assert.That(await workspace.MoveTabAsync(primary, earlierTab)).IsTrue();
        await Assert.That(dirtyEarlierEditor).IsFalse();
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(earlierTab);
        await Assert.That(earlierTab.History.Count).IsEqualTo(earlierHistoryCount);
        await Assert.That(laterTab.History.Count).IsEqualTo(laterHistoryCount);
    }

    [Test]
    public async Task ClosingInactiveTab_DoesNotChangeSelectedDocumentOrItsHistory()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot);
        var closing = workspace.ActiveTab;
        await workspace.OpenAsync(WorkspaceLocation.ForTask("a", "A"), WorkspaceOpenDisposition.NewTab);
        var selected = workspace.ActiveTab;
        await workspace.CloseTabAsync(workspace.PrimaryPane, closing, WorkspaceLocation.TasksRoot);
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(selected);
        await Assert.That(selected.History.Count).IsEqualTo(1);
        await Assert.That(selected.CurrentLocation!.Id).IsEqualTo("a");
    }

    [Test]
    public async Task ClosingInactiveTab_DoesNotRestoreStaleStateOfSelectedTab()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot);
        var closing = workspace.ActiveTab;
        await workspace.OpenAsync(WorkspaceLocation.ForTask("a", "A"), WorkspaceOpenDisposition.NewTab);
        var restored = 0;
        workspace.RestoreViewState = (_, _) => restored++;
        await workspace.CloseTabAsync(workspace.PrimaryPane, closing, WorkspaceLocation.TasksRoot);
        await Assert.That(restored).IsEqualTo(0);
    }

    [Test]
    public async Task ClosingMiddleSelectedTab_GuardsTheActualSuccessor()
    {
        WorkspaceNavigationTabViewModel? refusing = null;
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot,
            tab => Task.FromResult(!ReferenceEquals(tab, refusing)));
        await workspace.OpenAsync(WorkspaceLocation.ForTask("a", "A"), WorkspaceOpenDisposition.NewTab);
        var closing = workspace.ActiveTab;
        await workspace.OpenAsync(WorkspaceLocation.ForTask("b", "B"), WorkspaceOpenDisposition.NewTab);
        var successor = workspace.ActiveTab;
        await workspace.SelectTabAsync(workspace.PrimaryPane, closing);
        refusing = successor;
        await Assert.That(await workspace.CloseTabAsync(workspace.PrimaryPane, closing, WorkspaceLocation.TasksRoot)).IsFalse();
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(closing);
        await Assert.That(workspace.PrimaryPane.Tabs.Count).IsEqualTo(3);
    }

    [Test]
    public async Task FocusInactiveOwner_RestoresItsCurrentSnapshotNotIncomingLocation()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.ForNote("A.md", "A", "saved"));
        var owner = workspace.ActiveTab;
        var saved = new ViewSnapshot("keep", 120);
        owner.CurrentEntry!.ViewState = saved;
        await workspace.OpenAsync(WorkspaceLocation.ForTask("a", "A"), WorkspaceOpenDisposition.NewTab);
        object? restored = null;
        workspace.RestoreViewState = (_, entry) => restored = entry.ViewState;
        await workspace.OpenAsync(WorkspaceLocation.ForNote("A.md", "A"), WorkspaceOpenDisposition.AdjacentPane);
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(owner);
        await Assert.That(restored).IsEqualTo(saved);
        await Assert.That(owner.CurrentLocation!.Anchor).IsEqualTo("saved");
        await Assert.That(owner.History.Count).IsEqualTo(1);
    }

    private sealed record ViewSnapshot(string Search, int Scroll);

    [Test]
    public async Task ReviewPair_KeepsSecondarySourceOwnerAndAppendsOnlyItsLocatorHistory()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.TasksRoot);
        var unrelated = workspace.ActiveTab;
        var first = WorkspaceLocation.ForFeedDay("Daily/A.md", "A", "1");
        await workspace.OpenAsync(first, WorkspaceOpenDisposition.AdjacentPane);
        var sourcePane = workspace.SecondaryPane!;
        var sourceTab = workspace.ActiveTab;
        var snapshot = new ViewSnapshot("restrictive", 170);
        sourceTab.CurrentEntry!.ViewState = snapshot;
        var requested = WorkspaceLocation.ForFeedDay("Daily/B.md", "B", "2");
        await Assert.That(await workspace.ShowPairAsync(requested, WorkspaceLocation.ReviewRoot)).IsTrue();
        await Assert.That(workspace.SecondaryPane).IsSameReferenceAs(sourcePane);
        await Assert.That(sourcePane.ActiveTab).IsSameReferenceAs(sourceTab);
        await Assert.That(sourceTab.CurrentLocation).IsEqualTo(requested);
        await Assert.That(sourceTab.History.Count).IsEqualTo(2);
        await Assert.That(sourceTab.History[0].Location).IsEqualTo(first);
        await Assert.That(sourceTab.History[0].ViewState).IsEqualTo(snapshot);
        await Assert.That(unrelated.History.Count).IsEqualTo(1);
        await Assert.That(workspace.ActivePane).IsSameReferenceAs(workspace.PrimaryPane);
        var reviewTab = workspace.ActiveTab;
        await workspace.ShowPairAsync(requested, WorkspaceLocation.ReviewRoot);
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(reviewTab);
        await Assert.That(sourceTab.History.Count).IsEqualTo(2);
        await Assert.That(workspace.PrimaryPane.Tabs.Count).IsEqualTo(2);
        await Assert.That((workspace.SecondaryPane ?? throw new InvalidOperationException("Secondary pane is missing."))
            .Tabs.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ClosingSelectedTabInInactivePane_RestoresItsSuccessorWithoutChangingFocus()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot);
        var focused = workspace.ActiveTab;
        await workspace.OpenAsync(WorkspaceLocation.ForTask("a", "A"), WorkspaceOpenDisposition.AdjacentPane);
        var closing = workspace.ActiveTab;
        await workspace.OpenAsync(WorkspaceLocation.ForTask("b", "B"), WorkspaceOpenDisposition.NewTab);
        var successor = workspace.ActiveTab;
        successor.CurrentEntry!.ViewState = new ViewSnapshot("retain", 170);
        await workspace.SelectTabAsync(workspace.SecondaryPane!, closing);
        workspace.ActivatePane(workspace.PrimaryPane);
        WorkspaceNavigationTabViewModel? restoredTab = null;
        object? restoredState = null;
        workspace.RestoreViewState = (tab, entry) => { restoredTab = tab; restoredState = entry.ViewState; };
        await workspace.CloseTabAsync(workspace.SecondaryPane!, closing, WorkspaceLocation.TasksRoot);
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(focused);
        await Assert.That(restoredTab).IsSameReferenceAs(successor);
        await Assert.That(restoredState).IsEqualTo(successor.CurrentEntry.ViewState);
        await Assert.That(workspace.SecondaryPane!.ActiveTab).IsSameReferenceAs(successor);
    }

    [Test]
    public async Task ClosingLastTabInInactiveSecondaryPane_DoesNotRestoreUnchangedPrimary()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot);
        var focused = workspace.ActiveTab;
        await workspace.OpenAsync(WorkspaceLocation.ForTask("a", "A"), WorkspaceOpenDisposition.AdjacentPane);
        var closing = workspace.ActiveTab;
        var secondary = workspace.SecondaryPane!;
        workspace.ActivatePane(workspace.PrimaryPane);
        var restoreCalls = 0;
        workspace.RestoreViewState = (_, _) => restoreCalls++;
        await workspace.CloseTabAsync(secondary, closing, WorkspaceLocation.TasksRoot);
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(focused);
        await Assert.That(workspace.HasSecondaryPane).IsFalse();
        await Assert.That(restoreCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ReusingExistingDocument_AlsoHonorsTargetEditorGuard()
    {
        WorkspaceNavigationTabViewModel? refusing = null;
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot,
            tab => Task.FromResult(!ReferenceEquals(tab, refusing)));
        var source = workspace.ActiveTab;
        var task = WorkspaceLocation.ForTask("a", "A");
        await workspace.OpenAsync(task, WorkspaceOpenDisposition.NewTab);
        refusing = workspace.ActiveTab;
        // Selecting first while the target is still allowed.
        var target = refusing;
        refusing = null;
        await workspace.SelectTabAsync(workspace.PrimaryPane, source);
        refusing = target;
        await Assert.That(await workspace.OpenAsync(task, WorkspaceOpenDisposition.CurrentTab)).IsFalse();
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(source);
        await Assert.That(source.History.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReusingInactiveDocument_GuardsTheVisibleEditorInItsOwningPane(bool traverseHistory)
    {
        WorkspaceNavigationTabViewModel? refusing = null;
        var guardedTabs = new System.Collections.Generic.List<WorkspaceNavigationTabViewModel>();
        var taskB = WorkspaceLocation.ForTask("b", "B");
        var taskA = WorkspaceLocation.ForTask("a", "A");
        var workspace = new WorkspaceNavigationViewModel(traverseHistory ? taskB : taskA, tab =>
        {
            guardedTabs.Add(tab);
            return Task.FromResult(!ReferenceEquals(tab, refusing));
        });
        var sourcePane = workspace.PrimaryPane;
        var source = workspace.ActiveTab;
        if (traverseHistory)
            await workspace.OpenAsync(taskA, WorkspaceOpenDisposition.CurrentTab);
        await workspace.OpenAsync(taskB, WorkspaceOpenDisposition.AdjacentPane);
        var owningPane = workspace.ActivePane;
        var target = workspace.ActiveTab;
        await workspace.OpenAsync(WorkspaceLocation.ForNote("C.md", "C"), WorkspaceOpenDisposition.NewTab);
        var visibleEditor = workspace.ActiveTab;
        await workspace.SelectTabAsync(sourcePane, source);

        var editorState = new ViewSnapshot("unsaved C", 180);
        visibleEditor.CurrentEntry!.ViewState = editorState;
        var sourceIndex = source.CurrentIndex;
        var sourceHistory = source.History.Select(entry => entry.Location).ToArray();
        var captures = 0;
        var restores = 0;
        workspace.CaptureViewState = tab => { captures++; return tab.CurrentEntry?.ViewState; };
        workspace.RestoreViewState = (_, _) => restores++;
        refusing = visibleEditor;
        guardedTabs.Clear();

        var rejected = traverseHistory
            ? await workspace.GoBackAsync(sourcePane)
            : await workspace.OpenAsync(taskB, WorkspaceOpenDisposition.AdjacentPane);
        await Assert.That(rejected).IsFalse();
        await Assert.That(guardedTabs.Contains(visibleEditor)).IsTrue();
        await Assert.That(captures).IsEqualTo(0);
        await Assert.That(restores).IsEqualTo(0);
        await Assert.That(workspace.PrimaryPane).IsSameReferenceAs(sourcePane);
        await Assert.That(workspace.SecondaryPane).IsSameReferenceAs(owningPane);
        await Assert.That(workspace.ActivePane).IsSameReferenceAs(sourcePane);
        await Assert.That(sourcePane.ActiveTab).IsSameReferenceAs(source);
        await Assert.That(owningPane.ActiveTab).IsSameReferenceAs(visibleEditor);
        await Assert.That(sourcePane.Tabs.Count).IsEqualTo(1);
        await Assert.That(owningPane.Tabs.Count).IsEqualTo(2);
        await Assert.That(owningPane.Tabs[0]).IsSameReferenceAs(target);
        await Assert.That(owningPane.Tabs[1]).IsSameReferenceAs(visibleEditor);
        await Assert.That(source.CurrentIndex).IsEqualTo(sourceIndex);
        await Assert.That(source.History.Select(entry => entry.Location).SequenceEqual(sourceHistory)).IsTrue();
        await Assert.That(target.CurrentLocation).IsEqualTo(taskB);
        await Assert.That(target.CurrentIndex).IsEqualTo(0);
        await Assert.That(target.History.Count).IsEqualTo(1);
        await Assert.That(visibleEditor.CurrentLocation!.Id).IsEqualTo("C.md");
        await Assert.That(visibleEditor.CurrentIndex).IsEqualTo(0);
        await Assert.That(visibleEditor.History.Count).IsEqualTo(1);
        await Assert.That(visibleEditor.CurrentEntry.ViewState).IsSameReferenceAs(editorState);

        refusing = null;
        guardedTabs.Clear();
        var accepted = traverseHistory
            ? await workspace.GoBackAsync(sourcePane)
            : await workspace.OpenAsync(taskB, WorkspaceOpenDisposition.AdjacentPane);
        await Assert.That(accepted).IsTrue();
        await Assert.That(guardedTabs.Contains(visibleEditor)).IsTrue();
        await Assert.That(guardedTabs.Contains(source)).IsTrue();
        await Assert.That(guardedTabs.Contains(target)).IsTrue();
        await Assert.That(guardedTabs.Distinct().Count()).IsEqualTo(3);
        await Assert.That(captures).IsEqualTo(3);
        await Assert.That(restores).IsEqualTo(1);
        await Assert.That(workspace.ActivePane).IsSameReferenceAs(owningPane);
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(target);
        await Assert.That(sourcePane.ActiveTab).IsSameReferenceAs(source);
        await Assert.That(source.CurrentLocation).IsEqualTo(taskA);
        await Assert.That(source.CurrentIndex).IsEqualTo(sourceIndex);
        await Assert.That(source.History.Select(entry => entry.Location).SequenceEqual(sourceHistory)).IsTrue();
        await Assert.That(sourcePane.Tabs.Count).IsEqualTo(1);
        await Assert.That(owningPane.Tabs.Count).IsEqualTo(2);
        await Assert.That(target.CurrentIndex).IsEqualTo(0);
        await Assert.That(target.History.Count).IsEqualTo(1);
        await Assert.That(visibleEditor.CurrentEntry.ViewState).IsSameReferenceAs(editorState);
        if (traverseHistory)
            await Assert.That(workspace.LastNavigationNotice).IsEqualTo(WorkspaceNavigationNotice.ExistingHistoryDocumentFocused);
    }

    [Test]
    public async Task ScopeReset_InvalidatesPendingNavigationCompletion()
    {
        var completion = new TaskCompletionSource<bool>();
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot, _ => completion.Task);
        var pending = workspace.OpenAsync(WorkspaceLocation.ForTask("old-task", "Old"), WorkspaceOpenDisposition.CurrentTab);
        workspace.Reset(WorkspaceLocation.TasksRoot);
        completion.SetResult(true);
        await Assert.That(await pending).IsFalse();
        await Assert.That(workspace.ActiveTab.CurrentLocation).IsEqualTo(WorkspaceLocation.TasksRoot);
    }

    [Test]
    public async Task History_IsPerTab_AndFocusingAnotherDocumentDoesNotRecordTransitions()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.TasksRoot);
        var list = workspace.ActiveTab;
        await workspace.OpenAsync(WorkspaceLocation.ForTask("a", "A"), WorkspaceOpenDisposition.CurrentTab);
        await workspace.OpenAsync(WorkspaceLocation.ForNote("B.md", "B"), WorkspaceOpenDisposition.NewTab);
        var note = workspace.ActiveTab;
        await Assert.That(workspace.CanGoBack).IsFalse();
        await Assert.That(await workspace.GoBackAsync()).IsFalse();
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(note);
        await workspace.SelectTabAsync(workspace.PrimaryPane, list);
        await Assert.That(await workspace.GoBackAsync()).IsTrue();
        await Assert.That(list.CurrentLocation).IsEqualTo(WorkspaceLocation.TasksRoot);
        await Assert.That(note.History.Count).IsEqualTo(1);
    }

    [Test]
    public async Task FocusExistingObject_DoesNotMoveItsTabWhenRequestedBeside()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot);
        var feed = workspace.ActiveTab;
        await workspace.OpenAsync(WorkspaceLocation.ForTask("a", "A"), WorkspaceOpenDisposition.NewTab);
        var task = workspace.ActiveTab;
        await workspace.SelectTabAsync(workspace.PrimaryPane, feed);
        await workspace.OpenAsync(WorkspaceLocation.ForTask("a", "A"), WorkspaceOpenDisposition.AdjacentPane);
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(task);
        await Assert.That(workspace.HasSecondaryPane).IsFalse();
        await Assert.That(feed.History.Count).IsEqualTo(1);
        await Assert.That(task.History.Count).IsEqualTo(1);
    }

    [Test]
    public async Task LastPrimaryClose_PromotesEntireRemainingGroupWithoutLosingHistory()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot);
        var closing = workspace.ActiveTab;
        await workspace.OpenAsync(WorkspaceLocation.ForNote("A.md", "A"), WorkspaceOpenDisposition.AdjacentPane);
        var remainingGroup = workspace.SecondaryPane!;
        await workspace.OpenAsync(WorkspaceLocation.ForNote("B.md", "B"), WorkspaceOpenDisposition.NewTab);
        var selected = workspace.ActiveTab;
        await workspace.CloseTabAsync(workspace.PrimaryPane, closing, WorkspaceLocation.TasksRoot);
        await Assert.That(workspace.PrimaryPane).IsSameReferenceAs(remainingGroup);
        await Assert.That(workspace.PrimaryPane.Tabs.Count).IsEqualTo(2);
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(selected);
        await Assert.That(workspace.HasSecondaryPane).IsFalse();
    }

    [Test]
    public async Task MoveAndMergePreserveTabIdentityAndTabHistory()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot);
        var source = workspace.ActiveTab;
        await workspace.OpenAsync(WorkspaceLocation.ForNote("A.md", "A"), WorkspaceOpenDisposition.AdjacentPane);
        var note = workspace.ActiveTab;
        var originalHistory = note.History.Count;
        await Assert.That(await workspace.MoveTabAsync(workspace.SecondaryPane!, note)).IsTrue();
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(note);
        await Assert.That(note.History.Count).IsEqualTo(originalHistory);
        await Assert.That(workspace.HasSecondaryPane).IsFalse();
        await workspace.MoveTabAsync(workspace.PrimaryPane, note);
        await Assert.That(await workspace.MergePanesAsync()).IsTrue();
        await Assert.That(workspace.PrimaryPane.Tabs.Contains(source)).IsTrue();
        await Assert.That(workspace.PrimaryPane.Tabs.Contains(note)).IsTrue();
        await Assert.That(await workspace.GoBackAsync()).IsFalse();
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(note);
        await Assert.That(workspace.HasSecondaryPane).IsFalse();
    }

    [Test]
    public async Task MovingInactiveTabKeepsTheOtherSourceTabSelected()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot);
        var source = workspace.ActiveTab;
        await workspace.OpenAsync(WorkspaceLocation.ForNote("A.md", "A"), WorkspaceOpenDisposition.NewTab);
        var selected = workspace.ActiveTab;
        await workspace.MoveTabAsync(workspace.PrimaryPane, source);
        await Assert.That(workspace.PrimaryPane.ActiveTab).IsSameReferenceAs(selected);
    }

    [Test]
    public async Task ReviewPairReusesObjectsWithoutDiscardingOtherDocuments()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.TasksRoot);
        var taskList = workspace.ActiveTab;
        await workspace.OpenAsync(WorkspaceLocation.ForTask("task-a", "A"), WorkspaceOpenDisposition.AdjacentPane);
        var task = workspace.ActiveTab;
        await workspace.ShowPairAsync(WorkspaceLocation.ForFeedDay("Daily/A.md", "A", "3"), WorkspaceLocation.ReviewRoot);
        await Assert.That(workspace.PrimaryPane.Tabs.Contains(taskList)).IsTrue();
        await Assert.That(workspace.SecondaryPane!.Tabs.Contains(task)).IsTrue();
        var review = workspace.ActiveTab;
        await workspace.ShowPairAsync(WorkspaceLocation.ForFeedDay("Daily/B.md", "B", "4"), WorkspaceLocation.ReviewRoot);
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(review);
        await Assert.That(workspace.PrimaryPane.Tabs.Count).IsEqualTo(2);
        await Assert.That(workspace.SecondaryPane.Tabs.Count).IsEqualTo(2);
    }

    [Test]
    public async Task FailedCommitPreventsMoveMergeAndReviewPairAtomically()
    {
        var allow = true;
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot, _ => Task.FromResult(allow));
        await workspace.OpenAsync(WorkspaceLocation.ForNote("A.md", "A"), WorkspaceOpenDisposition.AdjacentPane);
        var primary = workspace.PrimaryPane.ActiveTab;
        var secondary = workspace.ActiveTab;
        allow = false;
        await Assert.That(await workspace.MoveTabAsync(workspace.SecondaryPane!, secondary)).IsFalse();
        await Assert.That(await workspace.MergePanesAsync()).IsFalse();
        await Assert.That(await workspace.ShowPairAsync(WorkspaceLocation.FeedRoot, WorkspaceLocation.ReviewRoot)).IsFalse();
        await Assert.That(workspace.PrimaryPane.ActiveTab).IsSameReferenceAs(primary);
        await Assert.That(workspace.SecondaryPane!.ActiveTab).IsSameReferenceAs(secondary);
        await Assert.That(workspace.PrimaryPane.Tabs.Count).IsEqualTo(1);
        await Assert.That(workspace.SecondaryPane.Tabs.Count).IsEqualTo(1);
    }

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
    public async Task NewTabKeepsSourceAndStartsAnIndependentHistory()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.TasksRoot);
        var sourceTab = workspace.ActiveTab;
        var task = WorkspaceLocation.ForTask("task-1", "Task");

        await workspace.OpenAsync(task, WorkspaceOpenDisposition.NewTab);

        await Assert.That(workspace.PrimaryPane.Tabs.Count).IsEqualTo(2);
        await Assert.That(workspace.PrimaryPane.ActiveTab).IsNotSameReferenceAs(sourceTab);
        await Assert.That(sourceTab.CurrentLocation).IsEqualTo(WorkspaceLocation.TasksRoot);
        await Assert.That(workspace.PrimaryPane.ActiveTab!.CurrentLocation).IsEqualTo(task);
        await Assert.That(workspace.CanGoBack).IsFalse();
        await Assert.That(await workspace.GoBackAsync()).IsFalse();
        await Assert.That(workspace.ActiveTab).IsNotSameReferenceAs(sourceTab);
        await Assert.That(sourceTab.History.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AdjacentPaneIsCreatedOnceThenReusedWithItsOwnHistory()
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
    public async Task ExistingTaskIsFocusedAcrossPanes_WithoutCrossPaneHistory()
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
        await Assert.That(await workspace.GoBackAsync()).IsFalse();
        await Assert.That(workspace.ActivePane).IsSameReferenceAs(workspace.SecondaryPane);
        await Assert.That(workspace.ActiveTab.CurrentLocation).IsEqualTo(task);
    }

    [Test]
    public async Task OpenBesideFocusesAlreadyOpenTabWithoutMovingOrDuplicatingIt()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot);
        var task = WorkspaceLocation.ForTask("task-1", "Task");
        await workspace.OpenAsync(task, WorkspaceOpenDisposition.NewTab);
        var taskTab = workspace.ActiveTab;
        await workspace.SelectTabAsync(workspace.PrimaryPane, workspace.PrimaryPane.Tabs[0]);

        await Assert.That(await workspace.OpenAsync(task, WorkspaceOpenDisposition.AdjacentPane)).IsTrue();
        await Assert.That(workspace.PrimaryPane.Tabs.Count).IsEqualTo(2);
        await Assert.That(workspace.HasSecondaryPane).IsFalse();
        await Assert.That(workspace.PrimaryPane.ActiveTab).IsSameReferenceAs(taskTab);
        await Assert.That(await workspace.OpenAsync(task, WorkspaceOpenDisposition.NewTab)).IsTrue();
        await Assert.That(workspace.PrimaryPane.Tabs.Count).IsEqualTo(2);
    }

    [Test]
    public async Task DistinctTaskListsHaveDistinctKeys_ButSameCategoryIsReused()
    {
        var all = WorkspaceLocation.ForTaskList(TaskListKind.AllTasks);
        var unlocked = WorkspaceLocation.ForTaskList(TaskListKind.Unlocked);
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
        await Assert.That(await workspace.GoBackAsync()).IsFalse();
        await Assert.That(workspace.ActiveTab).IsSameReferenceAs(noteTab);
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
    public async Task ClosingLastTabOpensAllTasksInsteadOfLeavingWorkspaceEmpty()
    {
        var workspace = new WorkspaceNavigationViewModel(WorkspaceLocation.FeedRoot);
        await workspace.OpenAsync(WorkspaceLocation.ForNote("A.md", "A"), WorkspaceOpenDisposition.CurrentTab);

        await workspace.CloseActiveTabAsync(WorkspaceLocation.FeedRoot);

        await Assert.That(workspace.PrimaryPane.Tabs.Count).IsEqualTo(1);
        await Assert.That(workspace.ActiveTab.CurrentLocation).IsEqualTo(WorkspaceLocation.TasksRoot);
    }
}
