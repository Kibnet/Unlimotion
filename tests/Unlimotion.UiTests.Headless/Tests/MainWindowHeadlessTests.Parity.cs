using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AppAutomation.Avalonia.Headless.Session;
using TUnit.Core;
using TUnit.Assertions;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Feed;
using Unlimotion.ViewModel.Workspace;
using Unlimotion.Views;
using Unlimotion.Notes.Operations;
using WritableJsonConfiguration;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.UiTests.Headless.Tests;

public sealed partial class MainWindowHeadlessTests
{
    [Test, NotInParallel(DesktopUiConstraint)]
    public async Task UX14_ReviewKeepsSourceAndOtherTabs()
    {
        RequireRenderedStoryMode();
        Page.WorkspaceRailFeedButton.Invoke();
        WaitUntil(() => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.CanStartReview),
            timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Review fixture did not initialize.");
        var owner = HeadlessRuntime.Dispatch(GetHeadlessMainWindowViewModel);
        await RunParityUiAsync(() => owner.TryOpenTaskByIdAsync(UnlimotionAutomationScenarioData.FeedCurrentTaskId,
            WorkspaceOpenDisposition.NewTab));
        var tabsBefore = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.Panes.SelectMany(p => p.Tabs).Select(t => t.Id).ToArray());
        Page.GlobalReviewButton.Invoke();
        await CaptureStoryScreenshotAsync("parity-review-trigger.png");
        Console.WriteLine(HeadlessRuntime.Dispatch(() => $"Review: shell={owner.IsWorkspaceShellAttached}; active={owner.WorkspaceNavigation.ActiveTab.CurrentLocation}; busy={owner.Feed.IsBusy}; error={owner.Feed.ErrorMessage}"));
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.CurrentLocation?.Kind),
            kind => kind == WorkspaceLocationKind.Review, timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Review did not open beside source.");
        await Assert.That(HeadlessRuntime.Dispatch(() => tabsBefore.All(id => owner.WorkspaceNavigation.Panes.SelectMany(p => p.Tabs).Any(t => t.Id == id)))).IsTrue();
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.PrimaryPane.CurrentLocation?.Id))
            .IsEqualTo(HeadlessRuntime.Dispatch(() => owner.Feed.CurrentReview!.RelativePath));
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.IsLegacyReviewOverlayVisible)).IsFalse();
        await CaptureStoryScreenshotAsync("parity-review-wide.png");
        foreach (var width in new[] { 390, 520, 1000, 1400 })
        {
            HeadlessRuntime.Dispatch(() => { Session.Inner.MainWindow.Width = width; Session.Inner.MainWindow.Height = 800; Dispatcher.UIThread.RunJobs(); });
            await Task.Delay(150);
            await CaptureParityScreenshotAsync($"parity-review-{width}.png");
            if (width < 900)
            {
                var name = HeadlessRuntime.Dispatch(() => AutomationProperties.GetName(FindNativeControlByAutomationId<Button>("WorkspaceSecondaryPaneSelector")));
                await Assert.That(name).IsEqualTo(L10n.Get("GlobalReview"));
                Page.WorkspaceReviewSourceButton.Invoke();
                WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActivePane == owner.WorkspaceNavigation.PrimaryPane),
                    timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Review source button did not reveal the source pane.");
                await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActivePane == owner.WorkspaceNavigation.PrimaryPane)).IsTrue();
                HeadlessRuntime.Dispatch(() => FindNativeControlByAutomationId<Button>("WorkspaceSecondaryPaneSelector")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            }
        }
        var sourceTab = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.PrimaryPane.ActiveTab!);
        await RunParityUiAsync(() => owner.CloseWorkspaceTabAsync(owner.WorkspaceNavigation.PrimaryPane, sourceTab));
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.Panes.SelectMany(p => p.Tabs)
            .Any(t => t.CurrentLocation?.ObjectKey == sourceTab.CurrentLocation?.ObjectKey))).IsFalse();
        Page.WorkspaceReviewSourceButton.Invoke();
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.CurrentLocation?.Id),
            path => path == owner.Feed.CurrentReview!.RelativePath, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The review source action did not reopen the closed source tab.");
        await RunParityUiAsync(() => owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ReviewRoot));
        Page.FeedFinishReviewButton.Invoke();
        WaitUntil(() => HeadlessRuntime.Dispatch(() => !owner.Feed.IsReviewActive), timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Review did not finish.");
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.PrimaryPane.CurrentLocation!.Kind)).IsEqualTo(WorkspaceLocationKind.Feed);
    }

    [Test, NotInParallel(DesktopUiConstraint)]
    public async Task UX15_MoveAndMergePreserveTabsAndHistory()
    {
        RequireRenderedStoryMode();
        await CaptureStoryScreenshotAsync("parity-initial-workspace.png");
        var owner = HeadlessRuntime.Dispatch(GetHeadlessMainWindowViewModel);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.ResolveTaskById(UnlimotionAutomationScenarioData.FeedCurrentTaskId) is not null),
            timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Task fixture did not load.");
        await RunParityUiAsync(() => owner.OpenWorkspaceRootAsync(WorkspaceMode.Feed));
        var opened = await RunParityUiAsync(() => owner.TryOpenTaskByIdAsync(UnlimotionAutomationScenarioData.FeedCurrentTaskId, WorkspaceOpenDisposition.AdjacentPane));
        await Assert.That(opened).IsTrue();
        var movedId = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.Id);
        await CaptureStoryScreenshotAsync("parity-before-move.png");
        InvokeActiveTabMenu("WorkspaceMoveTab");
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.PrimaryPane.Tabs.Any(t => t.Id == movedId)),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Active tab did not move to the other pane.");
        await CaptureStoryScreenshotAsync("parity-after-move.png");
        InvokeActiveTabMenu("WorkspaceMoveTab");
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.HasSecondaryPane),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Move did not create an adjacent pane.");
        var allIds = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.Panes.SelectMany(p => p.Tabs).Select(t => t.Id).ToArray());
        await CaptureStoryScreenshotAsync("parity-before-merge.png");
        InvokeActiveTabMenu("WorkspaceMergePanes");
        WaitUntil(() => HeadlessRuntime.Dispatch(() => !owner.WorkspaceNavigation.HasSecondaryPane),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Merge did not close the empty pane.");
        await Assert.That(HeadlessRuntime.Dispatch(() => allIds.All(id => owner.WorkspaceNavigation.PrimaryPane.Tabs.Any(t => t.Id == id)))).IsTrue();
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.Id)).IsEqualTo(movedId);
        Page.WorkspaceGlobalBackButton.Invoke();
        await CaptureStoryScreenshotAsync("parity-merged-tabs.png");
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.Panes.SelectMany(p => p.Tabs).Select(t => t.CurrentLocation!.ObjectKey).Distinct().Count()))
            .IsEqualTo(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.Panes.Sum(p => p.Tabs.Count)));
    }

    [Test, NotInParallel(DesktopUiConstraint)]
    public async Task UX16_NotesFromTasksPinAndSingleClose()
    {
        RequireRenderedStoryMode();
        var owner = HeadlessRuntime.Dispatch(GetHeadlessMainWindowViewModel);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.Feed.IsVaultInitialized), timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Vault missing.");
        var path = "Projects/Parity note.md";
        Directory.CreateDirectory(Path.Combine(feedVaultPath!, "Projects"));
        await File.WriteAllTextAsync(Path.Combine(feedVaultPath!, path), "# A real note\n\nSource content\n");
        Page.WorkspaceRailNotesButton.Invoke();
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.Feed.FilesDrawer!.Files.Any(f => f.RelativePath == path)),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Global note browser did not list the file from Tasks.");
        await CaptureStoryScreenshotAsync("parity-note-browser.png");
        var fileButton = WaitUntil(() => HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.IsEffectivelyVisible && b.DataContext is FeedFileItemViewModel file && file.RelativePath == path)),
            button => button is not null, timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Note browser did not render the file button.")!;
        InvokeNativeButton(fileButton);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.CurrentLocation?.Id),
            id => id == path, timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Note browser did not open a workspace document.");
        await CaptureStoryScreenshotAsync("parity-open-note.png");
        InvokeActiveTabMenu("WorkspacePinNote");
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.PinnedNotes.Count), count => count == 1,
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Pin did not appear.");
        await Assert.That(HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants().OfType<Button>()
            .Any(b => b.IsEffectivelyVisible && AutomationProperties.GetAutomationId(b) == "FeedThematicFileCloseButton"))).IsFalse();
        await CaptureStoryScreenshotAsync("parity-pinned-note.png");
        await Assert.That(HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants().OfType<TextBlock>()
            .Any(text => text.IsEffectivelyVisible && AutomationProperties.GetAutomationId(text) == "FeedThematicTitle"))).IsFalse();
        var source = HeadlessRuntime.Dispatch(() => owner.Feed.TaskSourceIdentityProvider?.Invoke());
        var reloadedConfiguration = WritableJsonConfigurationFabric.Create(parityConfigurationPath!, reloadOnChange: false);
        var restarted = HeadlessRuntime.Dispatch(() => new MainWindowViewModel(null, owner.ManagerWrapper!, reloadedConfiguration));
        try
        {
            HeadlessRuntime.Dispatch(() => restarted.Feed.ConfigureTaskSourceParents(() => source,
                (_, _, _) => null, (_, _, _, _) => Task.CompletedTask));
            await RunParityUiAsync(() => restarted.Feed.InitializeVaultAsync(feedVaultPath));
            await Assert.That(HeadlessRuntime.Dispatch(() => restarted.PinnedNotes.Single().RelativePath)).IsEqualTo(path);
            HeadlessRuntime.Dispatch(() => restarted.Feed.ConfigureTaskSourceParents(
                () => new FeedTaskSourceIdentity("another-task-source", "another-binding"),
                (_, _, _) => null, (_, _, _, _) => Task.CompletedTask));
            HeadlessRuntime.Dispatch(restarted.ReloadPinnedNotes);
            await Assert.That(HeadlessRuntime.Dispatch(() => restarted.PinnedNotes.Count)).IsEqualTo(0);
        }
        finally { HeadlessRuntime.Dispatch(restarted.Dispose); (reloadedConfiguration as IDisposable)?.Dispose(); }
        await RunParityUiAsync(() => owner.Feed.InitializeVaultAsync(null));
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.PinnedNotes.Count)).IsEqualTo(0);
        await RunParityUiAsync(() => owner.Feed.InitializeVaultAsync(feedVaultPath));
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.PinnedNotes.Single().RelativePath)).IsEqualTo(path);
        HeadlessRuntime.Dispatch(() => owner.ToggleNotePin(path));
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.PinnedNotes.Count)).IsEqualTo(0);
    }

    [Test, NotInParallel(DesktopUiConstraint)]
    public async Task UX17_NextStepShowsParentAndSupportsCancel()
    {
        RequireRenderedStoryMode();
        await CaptureStoryScreenshotAsync("parity-next-step-initial.png");
        var owner = HeadlessRuntime.Dispatch(GetHeadlessMainWindowViewModel);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.Feed.IsVaultInitialized), timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Vault missing.");
        await RunParityUiAsync(() => owner.Feed.InitializeVaultAsync(null));
        await RunParityUiAsync(() => owner.TryOpenTaskByIdAsync(UnlimotionAutomationScenarioData.FeedCurrentTaskId));
        var parent = HeadlessRuntime.Dispatch(() => owner.CurrentTaskItem!);
        var status = parent.Status;
        var originalCount = HeadlessRuntime.Dispatch(() => owner.taskRepository!.Tasks.Count);
        Page.CurrentTaskNextStepButton.Invoke();
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.NextStepParents!.Parents.Single().Id)).IsEqualTo(parent.Id);
        Page.WorkspaceNextStepCancel.Invoke();
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.taskRepository!.Tasks.Count)).IsEqualTo(originalCount);
        Page.CurrentTaskNextStepButton.Invoke();
        Page.WorkspaceNextStepSave.Invoke();
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.NextStepError), error => !string.IsNullOrWhiteSpace(error),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Empty title did not show validation.");
        Page.WorkspaceNextStepTitle.Enter("Parity next step");
        Page.WorkspaceNextStepSave.Invoke();
        var child = WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.taskRepository!.Tasks.Items.FirstOrDefault(t => t.Title == "Parity next step")),
            task => task is not null, timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Next step was not persisted.")!;
        await Assert.That(child.ParentsTasks.Any(p => p.Id == parent.Id)).IsTrue();
        await Assert.That(parent.Status).IsEqualTo(status);
        await CaptureStoryScreenshotAsync("parity-next-step.png");
    }

    [Test, NotInParallel(DesktopUiConstraint)]
    public async Task UX18_MergeIsCancelledWhenEditorCannotSave()
    {
        RequireRenderedStoryMode();
        Page.WorkspaceRailFeedButton.Invoke();
        var owner = HeadlessRuntime.Dispatch(GetHeadlessMainWindowViewModel);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.Feed.Days.Count), count => count > 0,
            timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Feed missing.");
        await RunParityUiAsync(() => owner.TryOpenTaskByIdAsync(UnlimotionAutomationScenarioData.FeedCurrentTaskId, WorkspaceOpenDisposition.AdjacentPane));
        var before = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.Panes.SelectMany(p => p.Tabs).Select(t => t.Id).ToArray());
        HeadlessRuntime.Dispatch(() =>
        {
            var editor = owner.Feed.Days[0].MarkdownEditor;
            var block = editor.Blocks.First(b => b.IsEditable);
            editor.BeginEdit(block);
            block.EditorText += " unsaved marker";
            editor.CommitBlockAsync = (_, _) => Task.FromResult(MarkdownBlockCommitResult.Rejected("simulated save failure"));
        });
        var result = await RunParityUiAsync(() => owner.MergeWorkspacePanesAsync());
        await Assert.That(result).IsFalse();
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.HasSecondaryPane)).IsTrue();
        await Assert.That(HeadlessRuntime.Dispatch(() => before.SequenceEqual(owner.WorkspaceNavigation.Panes.SelectMany(p => p.Tabs).Select(t => t.Id)))).IsTrue();
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.Feed.Days[0].MarkdownEditor.ActiveBlock!.EditorText)).Contains("unsaved marker");
    }

    [Test, NotInParallel(DesktopUiConstraint)]
    public async Task UX19_TaskReturnsToVerifiedSourceAndHandlesMissingFile()
    {
        RequireRenderedStoryMode();
        var owner = HeadlessRuntime.Dispatch(GetHeadlessMainWindowViewModel);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.Feed.IsVaultInitialized),
            timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Vault missing.");
        var identity = new FeedTaskSourceIdentity("parity-source", "parity-storage");
        HeadlessRuntime.Dispatch(() =>
        {
            owner.Feed.TaskOwner = owner;
            owner.Feed.ConfigureTaskSourceParents(() => identity, (_, _, _) => null, (_, _, _, _) => Task.CompletedTask);
            owner.Feed.TaskCreationTarget = new TaskStorageFeedTaskCreationTarget(() => owner.taskRepository, () => identity);
        });
        Page.WorkspaceRailFeedButton.Invoke();
        const string marker = "Parity backlink source";
        CaptureViaHotkey(marker);
        Page.GlobalReviewButton.Invoke();
        await CaptureStoryScreenshotAsync("parity-backlink-review.png");
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.Feed.CurrentReview?.SelectedMarkdown == marker && !owner.Feed.IsBusy),
            timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Captured source did not enter review.");
        InvokeNativeButton(GetNativeControl<RadioButton>(Page.FeedReviewTaskActionButton));
        Page.FeedReviewConfirmButton.Invoke();
        var created = WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.taskRepository!.Tasks.Items.FirstOrDefault(t => t.Title == marker)),
            task => task is not null, timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Source task missing.")!;
        WaitUntil(() => HeadlessRuntime.Dispatch(() => !owner.Feed.IsBusy), timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Conversion not complete.");
        Page.FeedFinishReviewButton.Invoke();
        await RunParityUiAsync(() => owner.TryOpenTaskByIdAsync(created.Id));
        await CaptureStoryScreenshotAsync("parity-source-task.png");
        var locations = await RunParityUiAsync(() => owner.Feed.FindTaskSourceLocationsAsync(created.Id));
        await Assert.That(locations.Count).IsEqualTo(1);
        await Assert.That(locations[0].Anchor).IsNotNull();
        var sourceButton = WaitUntil(() => HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.IsEffectivelyVisible && AutomationProperties.GetAutomationId(b) == "CurrentTaskSourceButton")),
            button => button is not null, timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Verified backlink was not visible.")!;
        InvokeNativeButton(sourceButton);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.CurrentLocation?.Kind == WorkspaceLocationKind.Feed),
            timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Backlink did not open source Feed.");
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.CurrentLocation!.Anchor)).IsEqualTo(locations[0].Anchor);
        await CaptureStoryScreenshotAsync("parity-returned-source.png");
        Page.WorkspaceGlobalBackButton.Invoke();
        WaitUntil(() => HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.CurrentLocation?.Id == created.Id),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Back did not return to task.");
        File.Delete(Path.Combine(feedVaultPath!, locations[0].Id));
        await RunParityUiAsync(() => owner.Feed.OpenTaskSourceAsync(created.Id, locations[0]));
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.Feed.ErrorMessage)).IsEqualTo(L10n.Get("WorkspaceNoteUnavailable"));
        await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab.CurrentLocation!.Id)).IsEqualTo(created.Id);
        HeadlessRuntime.Dispatch(() => owner.Feed.ConfigureTaskSourceParents(
            () => new FeedTaskSourceIdentity("other", "other"), (_, _, _) => null, (_, _, _, _) => Task.CompletedTask));
        var mismatched = await RunParityUiAsync(() => owner.Feed.FindTaskSourceLocationsAsync(created.Id));
        await Assert.That(mismatched.Count).IsEqualTo(0);
    }

    private static Task RunParityUiAsync(Func<Task> action) =>
        RunParityUiAsync(async () => { await action(); return true; });

    // Headless completes its dispatch task on the UI thread. Complete on the default
    // scheduler so the test's next synchronous Dispatch cannot deadlock that thread.
    private static Task<T> RunParityUiAsync<T>(Func<Task<T>> action) =>
        HeadlessRuntime.Session.Dispatch(action, CancellationToken.None).ContinueWith(
            task => task.GetAwaiter().GetResult(), CancellationToken.None,
            TaskContinuationOptions.None, TaskScheduler.Default);

    private void InvokeActiveTabMenu(string resourceKey)
    {
        WaitUntil(() => HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants().OfType<Button>()
            .Any(b => AutomationProperties.GetAutomationId(b) == "WorkspaceTabActions-" + GetHeadlessMainWindowViewModel().WorkspaceNavigation.ActiveTab.Id.ToString("N"))),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Tab actions did not render.");
        HeadlessRuntime.Dispatch(() =>
        {
            var owner = GetHeadlessMainWindowViewModel();
            var action = FindNativeControlByAutomationId<Button>("WorkspaceTabActions-" + owner.WorkspaceNavigation.ActiveTab.Id.ToString("N"));
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var item = action.ContextMenu!.Items.OfType<MenuItem>().First(i => Equals(i.Header, L10n.Get(resourceKey)));
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            action.ContextMenu.Close();
        });
    }

    private async Task CaptureParityScreenshotAsync(string name)
    {
        var output = Path.Combine(Environment.GetEnvironmentVariable(WorkspaceScreenshotDirectoryVariable) ?? AppContext.BaseDirectory,
            "parity", Guid.NewGuid().ToString("N"), name);
        var path = await Task.Run(() => Session.Inner.CaptureScreenshot(output));
        Console.WriteLine($"Headless screenshot: {path}");
    }
}
