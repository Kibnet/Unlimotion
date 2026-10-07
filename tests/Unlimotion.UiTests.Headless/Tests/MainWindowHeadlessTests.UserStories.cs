using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TUnit;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion;
using Unlimotion.AppAutomation.TestHost;

namespace Unlimotion.UiTests.Headless.Tests;

public sealed partial class MainWindowHeadlessTests
{
    private const string CaptureThoughtMarker = "UX01 thought captured without losing task context";
    private const string HistoricalFactMarker = "UX05 chosen navigation variant: compact sidebar";
    private DateOnly historicalTargetDate;

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task UX01_CaptureWithoutLosingTask()
    {
        RequireRenderedStoryMode();
        if (feedVaultPath is null) throw new InvalidOperationException("UX01 did not start with the Feed vault fixture.");
        Page.ClickButton(static page => page.WorkspaceRailFeedButton);
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.VisibleDays.Count),
            count => count > 0,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The seeded daily note was not loaded for UX01.");
        PrepareFeedTaskReferenceSurface();
        WaitForHeadlessControl(
            () => Page.FeedSeededTaskTitleButton,
            "The source daily note did not expose the task used as the starting context.").Invoke();
        await UiAssert.TextEqualsAsync(
            () => Page.CurrentTaskTitleTextBox.Text,
            UnlimotionAutomationScenarioData.FeedCurrentTaskTitle,
            TimeSpan.FromSeconds(10));

        HeadlessRuntime.Dispatch(() =>
        {
            var window = Session.Inner.MainWindow;
            var shortcut = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Space,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
                Source = window
            };
            window.RaiseEvent(shortcut);
            if (!shortcut.Handled) throw new InvalidOperationException("The quick-capture shortcut was ignored.");
            Dispatcher.UIThread.RunJobs();
        });
        Page.FeedQuickCaptureTextBox.Enter(CaptureThoughtMarker);
        Page.FeedCaptureButton.Invoke();

        var todayPath = UnlimotionAutomationScenarioData.GetFeedDailyRelativePath(
            DateOnly.FromDateTime(DateTime.Now));
        WaitUntil(
            () => ReadFeedVaultText(todayPath),
            value => value.Contains(CaptureThoughtMarker, StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The thought was not saved in today's daily note.");
        await UiAssert.TextEqualsAsync(
            () => Page.CurrentTaskTitleTextBox.Text,
            UnlimotionAutomationScenarioData.FeedCurrentTaskTitle,
            TimeSpan.FromSeconds(10));

        Page.ClickButton(static page => page.WorkspaceRailFeedButton);
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.VisibleDays.Count),
            count => count > 0,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The new daily note could not be found in the Feed.");
        await Assert.That(ReadFeedVaultText(todayPath).Split(CaptureThoughtMarker).Length - 1).IsEqualTo(1);
        await CaptureStoryScreenshotAsync("ux01-captured-thought.png");
        var capturedBlock = HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.Days
            .First(day => day.Date == DateOnly.FromDateTime(DateTime.Now)).MarkdownEditor.Blocks
            .Single(block => block.Block.Raw.Contains(CaptureThoughtMarker, StringComparison.Ordinal)));
        HeadlessRuntime.Dispatch(() =>
        {
            var preview = FindNativeControlByAutomationId<Control>(capturedBlock.PreviewAutomationId);
            preview.Focus();
            preview.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
                Source = preview
            });
            Dispatcher.UIThread.RunJobs();
            var editor = FindNativeControlByAutomationId<TextBox>(capturedBlock.EditorAutomationId);
            if (!editor.IsVisible) throw new InvalidOperationException("The captured thought did not enter inline editing.");
            editor.Text = CaptureThoughtMarker + " — expanded in place";
            FindNativeControlByAutomationId<Button>("FeedRefreshButton").Focus();
            Dispatcher.UIThread.RunJobs();
        });
        WaitUntil(() => ReadFeedVaultText(todayPath),
            text => text.Contains(CaptureThoughtMarker + " — expanded in place", StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Editing the captured thought in the daily note did not persist.");
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task UX10_WorkWithSource()
    {
        RequireRenderedStoryMode();
        Page.ClickButton(static page => page.WorkspaceRailFeedButton);
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.VisibleDays.Count),
            count => count > 0,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The seeded daily note was not loaded for UX10.");
        PrepareFeedTaskReferenceSurface();
        var taskLink = WaitForHeadlessControl(
            () => Page.FeedSeededTaskTitleButton,
            "The daily entry did not display its linked task.");
        OpenStoryLinkBesidePhysically(HeadlessRuntime.Dispatch(() => GetNativeControl<Button>(taskLink)));
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() =>
            {
                var navigation = GetHeadlessMainWindowViewModel().WorkspaceNavigation;
                return navigation.PrimaryPane.ActiveTab?.CurrentLocation?.Kind == Unlimotion.ViewModel.Workspace.WorkspaceLocationKind.Feed &&
                    navigation.SecondaryPane?.ActiveTab?.CurrentLocation is
                        { Kind: Unlimotion.ViewModel.Workspace.WorkspaceLocationKind.Task } task && task.Id == UnlimotionAutomationScenarioData.FeedCurrentTaskId;
            }),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The task did not open next to its source note.");
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants()
                .OfType<TextBox>().FirstOrDefault(control => control.IsVisible &&
                    AutomationProperties.GetAutomationId(control) == "CurrentTaskTitleTextBox")?.Text),
            value => value == UnlimotionAutomationScenarioData.FeedCurrentTaskTitle,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The adjacent task card did not display the linked task title.");
        await Assert.That(Page.FeedRoot.AutomationId).IsEqualTo("FeedRoot");
        await CaptureStoryScreenshotAsync("ux10-source-and-task.png");

        var statusPicker = HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants()
            .OfType<TaskStatusPicker>().First(control => control.IsVisible &&
                AutomationProperties.GetAutomationId(control) == "CurrentTaskStatusButton"));
        InvokeNativeButton(statusPicker);
        HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            var option = (statusPicker.Flyout as MenuFlyout)?.Items.OfType<MenuItem>()
                .SingleOrDefault(item => AutomationProperties.GetAutomationId(item) == "TaskStatusOptionInProgress")
                ?? throw new InvalidOperationException("The task status menu has no InProgress option.");
            if (!option.IsEnabled) throw new InvalidOperationException("The task could not start from its card.");
            option.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, option));
            Dispatcher.UIThread.RunJobs();
        });
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items
                .Single(task => task.Id == UnlimotionAutomationScenarioData.FeedCurrentTaskId).Status),
            value => value == Unlimotion.Domain.TaskStatus.InProgress,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Changing task status from the adjacent card was not persisted.");
        var sourceStatus = GetNativeControl<TaskStatusPicker>(Page.FeedSeededTaskStatusPicker);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => sourceStatus.Task?.Status),
            status => status == Unlimotion.Domain.TaskStatus.InProgress,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The source-note task status did not update while its card was open beside it.");
        var independentOffsets = HeadlessRuntime.Dispatch(() =>
        {
            var window = Session.Inner.MainWindow;
            window.Height = 560;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var feedScroll = window.GetVisualDescendants().OfType<ScrollViewer>().First(control =>
                control.IsVisible && AutomationProperties.GetAutomationId(control) == "FeedChronologyList");
            var taskScroll = window.GetVisualDescendants().OfType<ScrollViewer>().First(control =>
                control.IsVisible && AutomationProperties.GetAutomationId(control) == "CurrentTaskDetailsScrollViewer");
            feedScroll.Offset = new Vector(0, 250);
            taskScroll.Offset = new Vector(0, 90);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            return (Feed: feedScroll.Offset.Y, Task: taskScroll.Offset.Y);
        });
        await Assert.That(independentOffsets.Feed).IsGreaterThan(0);
        await Assert.That(independentOffsets.Task).IsGreaterThan(0);

        HeadlessRuntime.Dispatch(() =>
        {
            var tab = Session.Inner.MainWindow.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(control => AutomationProperties.GetAutomationId(control)?.StartsWith(
                    "WorkspaceTab-", StringComparison.Ordinal) == true
                    && AutomationProperties.GetName(control) == UnlimotionAutomationScenarioData.FeedCurrentTaskTitle)
                ?? throw new InvalidOperationException("The adjacent task tab was not visible.");
            var closePane = tab.ContextMenu?.Items.OfType<MenuItem>().LastOrDefault()
                ?? throw new InvalidOperationException("The adjacent task tab has no close command.");
            closePane.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, closePane));
            Dispatcher.UIThread.RunJobs();
        });
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => !GetHeadlessMainWindowViewModel().WorkspaceNavigation.HasSecondaryPane),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Closing the adjacent task did not restore the single Feed pane.");
        await Assert.That(Page.FeedRoot.AutomationId).IsEqualTo("FeedRoot");
        await Assert.That(HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants()
            .OfType<ScrollViewer>().First(control => control.IsVisible &&
                AutomationProperties.GetAutomationId(control) == "FeedChronologyList").Offset.Y))
            .IsGreaterThan(0);
    }

    private void SeedLongSourceDay(string vaultPath)
    {
        var today = UnlimotionAutomationScenarioData.GetFeedDailyRelativePath(DateOnly.FromDateTime(DateTime.Now));
        var path = Path.Combine(vaultPath, today.Replace('/', Path.DirectorySeparatorChar));
        File.AppendAllText(path, "\n" + string.Join("\n", Enumerable.Range(1, 80)
            .Select(index => $"UX10 supporting observation {index:000}")) + "\n", Utf8WithoutBom);
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task UX02_LocalCheckbox()
    {
        RequireRenderedStoryMode();
        Page.ClickButton(static page => page.WorkspaceRailFeedButton);
        var marker = UnlimotionAutomationScenarioData.FeedNewestMarker;
        var todayPath = UnlimotionAutomationScenarioData.GetFeedDailyRelativePath(
            DateOnly.FromDateTime(DateTime.Now));
        var block = WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.Days
                .FirstOrDefault(day => day.Date == DateOnly.FromDateTime(DateTime.Now))?
                .MarkdownEditor.Blocks.FirstOrDefault(item => item.Block.Raw.Contains(marker, StringComparison.Ordinal))),
            value => value is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The plain daily-note block was not available for a local checklist item.")!;
        PrepareFeedTaskReferenceSurface();
        var originalTaskCount = HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items.Count());
        HeadlessRuntime.Dispatch(() =>
        {
            var row = FindNativeControlByAutomationId<Grid>(block.BlockAutomationId);
            row.RaiseEvent(new ContextRequestedEventArgs { Source = row });
            Dispatcher.UIThread.RunJobs();
            var command = row.ContextMenu?.Items.OfType<MenuItem>().SingleOrDefault(item =>
                Equals(item.Header, Unlimotion.ViewModel.Localization.Localization.Get("FeedBlockChecklist")))
                ?? throw new InvalidOperationException("The block has no checklist transformation command.");
            if (!command.IsEnabled) throw new InvalidOperationException("The checklist command is disabled.");
            command.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, command));
            row.ContextMenu!.Close();
        });
        WaitUntil(
            () => ReadFeedVaultText(todayPath),
            value => value.Contains("- [ ] " + marker, StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The checklist item was not persisted to the daily Markdown file.");
        var pendingChecklistText = ReadFeedVaultText(todayPath);
        ClickCurrentLocalCheckbox();
        WaitUntil(
            () => ReadFeedVaultText(todayPath),
            value => value.Contains("- [x] " + marker, StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Checking the local item was not saved.");
        await CaptureStoryScreenshotAsync("ux02-local-checkbox.png");
        ClickCurrentLocalCheckbox();
        WaitUntil(
            () => ReadFeedVaultText(todayPath),
            value => value.Contains("- [ ] " + marker, StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Returning the local item to pending was not saved.");
        await Assert.That(HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items.Count()))
            .IsEqualTo(originalTaskCount);
        await Assert.That(ReadFeedVaultText(todayPath)).IsEqualTo(pendingChecklistText);

        void ClickCurrentLocalCheckbox()
        {
            WaitUntil(() => HeadlessRuntime.Dispatch(() =>
            {
                Dispatcher.UIThread.RunJobs();
                Session.Inner.MainWindow.UpdateLayout();
                var currentBlock = GetHeadlessMainWindowViewModel().Feed.Days
                    .First(day => day.Date == DateOnly.FromDateTime(DateTime.Now)).MarkdownEditor.Blocks
                    .Single(item => item.Block.Raw.Contains(marker, StringComparison.Ordinal));
                var checkbox = TryFindNativeControlByAutomationId<CheckBox>(currentBlock.TaskCheckboxAutomationId);
                return checkbox is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true }
                    && checkbox.Bounds.Width > 0 && checkbox.Bounds.Height > 0
                    && ReferenceEquals(TopLevel.GetTopLevel(checkbox), Session.Inner.MainWindow);
            }), timeout: TimeSpan.FromSeconds(10), timeoutMessage: "The current local checklist checkbox did not attach.");
            HeadlessRuntime.Dispatch(() =>
            {
                var currentBlock = GetHeadlessMainWindowViewModel().Feed.Days
                    .First(day => day.Date == DateOnly.FromDateTime(DateTime.Now)).MarkdownEditor.Blocks
                    .Single(item => item.Block.Raw.Contains(marker, StringComparison.Ordinal));
                var checkbox = FindNativeControlByAutomationId<CheckBox>(currentBlock.TaskCheckboxAutomationId);
                var point = checkbox.TranslatePoint(new Point(checkbox.Bounds.Width / 2, checkbox.Bounds.Height / 2),
                    Session.Inner.MainWindow) ?? throw new InvalidOperationException("The local checkbox has no pointer position.");
                Session.Inner.MainWindow.MouseDown(point, MouseButton.Left);
                Session.Inner.MainWindow.MouseUp(point, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
            });
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task UX03_ReviewOutcomes()
    {
        RequireRenderedStoryMode();
        Page.ClickButton(static page => page.WorkspaceRailFeedButton);
        const string taskMarker = "UX03 draft the site outline";
        const string keepMarker = "UX03 useful observation to keep";
        CaptureViaHotkey(taskMarker);
        CaptureViaHotkey(keepMarker);
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.PendingReviewBlocks),
            count => count >= 3,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The daily entries were not available for review.");
        Page.FeedStartReviewButton.Invoke();
        var first = WaitUntil(
            ObserveUnifiedReviewState,
            state => state.SelectedMarkdown?.Contains(taskMarker,
                StringComparison.Ordinal) == true,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Review did not start with the topmost block of today.");
        var originalCount = HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items.Count());
        InvokeNativeButton(GetNativeControl<RadioButton>(Page.FeedReviewTaskActionButton));
        await Assert.That(HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items.Count()))
            .IsEqualTo(originalCount);
        InvokeNativeButton(GetNativeControl<RadioButton>(Page.FeedReviewLeaveButton));
        await Assert.That(ObserveUnifiedReviewState().SelectedMarkdown).IsEqualTo(first.SelectedMarkdown);
        await Assert.That(HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items.Count()))
            .IsEqualTo(originalCount);
        InvokeNativeButton(GetNativeControl<RadioButton>(Page.FeedReviewTaskActionButton));
        Page.FeedReviewConfirmButton.Invoke();
        var created = WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items
                .FirstOrDefault(task => task.Title == taskMarker)),
            value => value is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Confirming the task decision did not create the task.")!;
        var todayPath = UnlimotionAutomationScenarioData.GetFeedDailyRelativePath(DateOnly.FromDateTime(DateTime.Now));
        WaitUntil(
            () => ReadFeedVaultText(todayPath),
            text => text.Contains("unlimotion://task/" + created.Id, StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The source day did not retain a link to the created task.");
        var next = WaitUntil(
            ObserveUnifiedReviewState,
            state => state.SelectedMarkdown?.Contains(keepMarker,
                StringComparison.Ordinal) == true,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Review did not continue to the next block of the same day.");
        await Assert.That(next.SelectedMarkdown).IsNotEqualTo(first.SelectedMarkdown);
        await CaptureStoryScreenshotAsync("ux03-review-next-day.png");
        InvokeNativeButton(GetNativeControl<RadioButton>(Page.FeedReviewLeaveButton));
        Page.FeedReviewConfirmButton.Invoke();
        WaitUntil(
            ObserveUnifiedReviewState,
            state => state.SelectedMarkdown?.Contains(UnlimotionAutomationScenarioData.FeedPendingReviewMarker,
                StringComparison.Ordinal) == true,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Review did not proceed to the older day's unfinished item.");
        WaitUntil(() => HeadlessRuntime.Dispatch(() => !GetHeadlessMainWindowViewModel().Feed.IsBusy),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Review source navigation did not complete.");
        InvokeNativeButton(GetNativeControl<RadioButton>(Page.FeedReviewMoveTodayButton));
        await Assert.That(ObserveUnifiedReviewState().SelectedMarkdown).Contains(UnlimotionAutomationScenarioData.FeedPendingReviewMarker);
        var moveConfirmButton = GetNativeControl<Button>(Page.FeedReviewConfirmButton);
        WaitUntil(() => HeadlessRuntime.Dispatch(() =>
                GetHeadlessMainWindowViewModel().Feed.IsReviewMoveTodayStage
                && GetHeadlessMainWindowViewModel().Feed.CanConfirmReviewDecision
                && moveConfirmButton.IsEffectivelyEnabled),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Move to today was not ready for confirmation.");
        Console.WriteLine($"UX03 before confirmation: thread={Environment.CurrentManagedThreadId}; " +
            $"ui={Dispatcher.UIThread.CheckAccess()}; context={SynchronizationContext.Current?.GetType().FullName ?? "<null>"}");
        Page.FeedReviewConfirmButton.Invoke();
        try
        {
            WaitUntil(
                () => ReadFeedVaultText(todayPath),
                text => text.Contains(UnlimotionAutomationScenarioData.FeedPendingReviewMarker, StringComparison.Ordinal),
                timeout: TimeSpan.FromSeconds(10),
                timeoutMessage: "Confirming Move to today did not preserve the older unfinished item in today's note.");
        }
        catch (TimeoutException error)
        {
            var diagnostic = HeadlessRuntime.Dispatch(() =>
            {
                var feed = GetHeadlessMainWindowViewModel().Feed;
                return $"busy={feed.IsBusy}; stage={feed.ReviewDecisionStage}; error={feed.ErrorMessage}; today={feed.EffectiveToday}; current={feed.CurrentReview?.RelativePath}";
            });
            throw new InvalidOperationException($"{error.Message} {diagnostic}; destination={ReadFeedVaultText(todayPath)}", error);
        }
        await Assert.That(HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items.Count()))
            .IsEqualTo(originalCount + 1);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => !GetHeadlessMainWindowViewModel().Feed.IsBusy),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Review conversion was still busy at scenario end.");
        var reviewClose = HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants()
            .OfType<Button>().FirstOrDefault(control => control.IsVisible &&
                AutomationProperties.GetAutomationId(control) == "FeedFinishReviewButton"));
        if (reviewClose is not null)
        {
            InvokeNativeButton(reviewClose);
            WaitUntil(() => HeadlessRuntime.Dispatch(() => !GetHeadlessMainWindowViewModel().Feed.IsReviewActive),
                timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Review did not finish before the next scenario.");
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task UX04_ExtractNote()
    {
        RequireRenderedStoryMode();
        if (feedVaultPath is null) throw new InvalidOperationException("UX04 has no isolated vault.");
        Page.ClickButton(static page => page.WorkspaceRailFeedButton);
        const string opening = "UX04 meeting context";
        const string decision = "UX04 chosen page layout";
        const string reason = "UX04 reason: short task list";
        const string noteTitle = "UX04 Meeting decisions";
        CaptureViaHotkey(opening);
        CaptureViaHotkey(decision);
        CaptureViaHotkey(reason);
        Page.FeedStartReviewButton.Invoke();
        WaitUntil(
            ObserveUnifiedReviewState,
            state => state.SelectedMarkdown?.Contains(opening, StringComparison.Ordinal) == true,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Review did not select the beginning of the meeting fragment.");
        Page.FeedReviewExpandDownButton.Invoke();
        Page.FeedReviewExpandDownButton.Invoke();
        var selection = WaitUntil(
            ObserveUnifiedReviewState,
            state => state.SelectedMarkdown?.Contains(opening, StringComparison.Ordinal) == true
                && state.SelectedMarkdown.Contains(decision, StringComparison.Ordinal)
                && state.SelectedMarkdown.Contains(reason, StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The selected range did not include all three meeting blocks.");
        InvokeNativeButton(GetNativeControl<RadioButton>(Page.FeedReviewNoteActionButton));
        Page.FeedReviewNoteTitleBox.Enter(noteTitle);
        Page.FeedReviewNoteFolderBox.Enter("Projects");
        await Assert.That(Directory.GetFiles(feedVaultPath, noteTitle + ".md", SearchOption.AllDirectories)).IsEmpty();
        await CaptureStoryScreenshotAsync("ux04-extraction-preview.png");
        Console.WriteLine($"UX04 before confirmation: thread={Environment.CurrentManagedThreadId}; " +
            $"ui={Dispatcher.UIThread.CheckAccess()}; context={SynchronizationContext.Current?.GetType().FullName ?? "<null>"}");
        Page.FeedReviewConfirmButton.Invoke();
        var createdFile = WaitUntil(
            () => Directory.GetFiles(feedVaultPath, noteTitle + ".md", SearchOption.AllDirectories).SingleOrDefault(),
            value => value is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The thematic note was not created after confirmation.")!;
        var noteText = WaitUntil(() => ReadFeedVaultText(Path.GetRelativePath(feedVaultPath, createdFile)
                .Replace(Path.DirectorySeparatorChar, '/')),
            text => text.Contains(reason, StringComparison.Ordinal), timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The extracted note could not be read after creation.");
        using (Assert.Multiple())
        {
            await Assert.That(noteText).Contains(opening);
            await Assert.That(noteText).Contains(decision);
            await Assert.That(noteText).Contains(reason);
            await Assert.That(noteText.IndexOf(opening, StringComparison.Ordinal))
                .IsLessThan(noteText.IndexOf(decision, StringComparison.Ordinal));
            await Assert.That(noteText.IndexOf(decision, StringComparison.Ordinal))
                .IsLessThan(noteText.IndexOf(reason, StringComparison.Ordinal));
        }
        var todayPath = UnlimotionAutomationScenarioData.GetFeedDailyRelativePath(DateOnly.FromDateTime(DateTime.Now));
        var dayText = WaitUntil(() => ReadFeedVaultText(todayPath),
            text => text.Contains(noteTitle, StringComparison.Ordinal), timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The source day was not replaced with the extracted note link.");
        await Assert.That(dayText).Contains(noteTitle);
        await Assert.That(dayText).DoesNotContain(opening);
        await Assert.That(dayText).DoesNotContain(decision);
        await Assert.That(dayText).DoesNotContain(reason);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => !GetHeadlessMainWindowViewModel().Feed.IsBusy),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Extraction was still busy before finishing review.");
        Page.FeedFinishReviewButton.Invoke();
        WaitUntil(() => HeadlessRuntime.Dispatch(() => !GetHeadlessMainWindowViewModel().Feed.IsReviewActive),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Review did not close before opening the extracted note.");
        var relativeNotePath = Path.GetRelativePath(feedVaultPath, createdFile).Replace(Path.DirectorySeparatorChar, '/');
        var sourceLink = WaitUntil(
            () => HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants()
                .OfType<Button>().FirstOrDefault(control => control.IsEffectivelyVisible &&
                    (AutomationProperties.GetHelpText(control) ?? string.Empty).EndsWith(
                        noteTitle, StringComparison.OrdinalIgnoreCase))),
            value => value is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The source day did not show a clickable link to the extracted note.")!;
        string ObserveExtractionRoutes() => HeadlessRuntime.Dispatch(() =>
        {
            var owner = GetHeadlessMainWindowViewModel();
            return $"expected={relativeNotePath}; activePane={owner.WorkspaceNavigation.ActivePane.Id}; " +
                string.Join("; ", owner.WorkspaceNavigation.Panes.Select(pane =>
                    $"pane={pane.Id},active={pane.ActiveTab?.CurrentLocation?.Kind}:{pane.ActiveTab?.CurrentLocation?.Id}," +
                    $"tabs=[{string.Join(",", pane.Tabs.Select(tab => $"{tab.CurrentLocation?.Kind}:{tab.CurrentLocation?.Id}"))}]")) +
                $"; feedError={owner.Feed.ErrorMessage}; selectedDocument={owner.Feed.DocumentWorkspace.ActiveDocument?.RelativePath}; " +
                $"link={AutomationProperties.GetHelpText(sourceLink)}; source={dayText}";
        });
        Console.WriteLine("UX04 before physical context action: " + ObserveExtractionRoutes());
        HeadlessRuntime.Dispatch(() =>
        {
            sourceLink.BringIntoView();
            Session.Inner.MainWindow.UpdateLayout();
            var point = sourceLink.TranslatePoint(new Point(sourceLink.Bounds.Width / 2, sourceLink.Bounds.Height / 2),
                Session.Inner.MainWindow) ?? throw new InvalidOperationException("The source note link has no physical position.");
            Session.Inner.MainWindow.MouseDown(point, MouseButton.Right);
            Session.Inner.MainWindow.MouseUp(point, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
        });
        var adjacentCommand = WaitUntil(() => HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            if (sourceLink.ContextMenu is not { IsOpen: true } menu) return null;
            var command = menu.Items.OfType<MenuItem>().SingleOrDefault(item => Equals(item.Header,
                Unlimotion.ViewModel.Localization.Localization.Get("WorkspaceOpenBeside")));
            if (command is null) return null;
            TopLevel.GetTopLevel(command)?.UpdateLayout();
            return command is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } && command.Bounds.Width > 0
                ? command : null;
        }), command => command is not null, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Physical right-click did not show the note link Open beside command.")!;
        HeadlessRuntime.Dispatch(() =>
        {
            var popup = TopLevel.GetTopLevel(adjacentCommand)
                ?? throw new InvalidOperationException("The note opening menu has no rendered TopLevel.");
            var point = adjacentCommand.TranslatePoint(new Point(adjacentCommand.Bounds.Width / 2, adjacentCommand.Bounds.Height / 2), popup)
                ?? throw new InvalidOperationException("The Open beside menu item has no physical position.");
            popup.MouseDown(point, MouseButton.Left);
            popup.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        });
        Console.WriteLine("UX04 after physical context action: " + ObserveExtractionRoutes());
        try
        {
            WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().WorkspaceNavigation.SecondaryPane?
                .ActiveTab?.CurrentLocation is { Kind: Unlimotion.ViewModel.Workspace.WorkspaceLocationKind.Note } location &&
                location.Id.Replace('\\', '/') == relativeNotePath),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The adjacent pane did not select the exact extracted note document.");
        }
        catch (TimeoutException error)
        {
            var diagnostics = ObserveExtractionRoutes();
            Console.WriteLine("UX04 exact adjacent failure: " + diagnostics);
            await CaptureStoryScreenshotAsync("ux04-exact-adjacent-failure.png");
            throw new InvalidOperationException(error.Message + " " + diagnostics, error);
        }
        WaitUntil(() => HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            Session.Inner.MainWindow.UpdateLayout();
            var pane = FindNativeControlByAutomationId<Control>("WorkspaceSecondaryPane");
            var body = pane.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(control =>
                control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) == "FeedDocumentScrollViewer");
            var text = body is null ? string.Empty : string.Join("\n", body.GetVisualDescendants().OfType<TextBlock>()
                .Where(control => control.IsEffectivelyVisible).Select(control => control.Text));
            return text.Contains(opening, StringComparison.Ordinal) && text.Contains(decision, StringComparison.Ordinal) &&
                   text.Contains(reason, StringComparison.Ordinal);
        }), timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The extracted note body was not rendered in the adjacent document pane.");
        await Assert.That(HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().WorkspaceNavigation.PrimaryPane
            .ActiveTab?.CurrentLocation?.Kind)).IsEqualTo(Unlimotion.ViewModel.Workspace.WorkspaceLocationKind.Feed);
        await Assert.That(HeadlessRuntime.Dispatch(() => sourceLink.IsEffectivelyVisible && sourceLink.GetVisualAncestors()
            .OfType<Control>().Any(control => AutomationProperties.GetAutomationId(control) == "WorkspacePrimaryPane"))).IsTrue();
        await CaptureStoryScreenshotAsync("ux04-extracted-note-beside-source.png");
    }

    private void OpenStoryLinkBesidePhysically(Button sourceLink)
    {
        HeadlessRuntime.Dispatch(() =>
        {
            sourceLink.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            Session.Inner.MainWindow.UpdateLayout();
            if (!sourceLink.IsEffectivelyVisible || sourceLink.Bounds.Width <= 0 || sourceLink.Bounds.Height <= 0)
                throw new InvalidOperationException("The source link is not pointer-visible for Open beside.");
            var point = sourceLink.TranslatePoint(new Point(sourceLink.Bounds.Width / 2, sourceLink.Bounds.Height / 2),
                Session.Inner.MainWindow) ?? throw new InvalidOperationException("The source link has no physical position.");
            Session.Inner.MainWindow.MouseDown(point, MouseButton.Right);
            Session.Inner.MainWindow.MouseUp(point, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
        });
        var adjacent = WaitUntil(() => HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            if (sourceLink.ContextMenu is not { IsOpen: true } menu) return null;
            var item = menu.Items.OfType<MenuItem>().SingleOrDefault(command => Equals(command.Header,
                Unlimotion.ViewModel.Localization.Localization.Get("WorkspaceOpenBeside")));
            if (item is null) return null;
            TopLevel.GetTopLevel(item)?.UpdateLayout();
            return item.IsEffectivelyVisible && item.IsEffectivelyEnabled && item.Bounds.Width > 0 && item.Bounds.Height > 0
                ? item : null;
        }), item => item is not null, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Physical right-click did not render the named Open beside command.")!;
        HeadlessRuntime.Dispatch(() =>
        {
            var popup = TopLevel.GetTopLevel(adjacent)
                ?? throw new InvalidOperationException("The link opening menu has no rendered TopLevel.");
            var point = adjacent.TranslatePoint(new Point(adjacent.Bounds.Width / 2, adjacent.Bounds.Height / 2), popup)
                ?? throw new InvalidOperationException("The Open beside command has no physical position.");
            popup.MouseDown(point, MouseButton.Left);
            popup.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        });
    }

    private void CaptureViaHotkey(string text)
    {
        HeadlessRuntime.Dispatch(() =>
        {
            var window = Session.Inner.MainWindow;
            var shortcut = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Space,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
                Source = window
            };
            window.RaiseEvent(shortcut);
            if (!shortcut.Handled) throw new InvalidOperationException("The quick-capture shortcut was ignored.");
            Dispatcher.UIThread.RunJobs();
        });
        Page.FeedQuickCaptureTextBox.Enter(text);
        Page.FeedCaptureButton.Invoke();
        WaitUntil(
            () => ReadFeedVaultText(UnlimotionAutomationScenarioData.GetFeedDailyRelativePath(
                DateOnly.FromDateTime(DateTime.Now))),
            value => value.Contains(text, StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The preparatory review entry was not saved.");
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => !GetHeadlessMainWindowViewModel().IsQuickCaptureOpen),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Quick capture did not finish closing before the next entry.");
    }

    private void SeedHistoricalArchive(string vaultPath)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        historicalTargetDate = today.AddDays(-390);
        while (historicalTargetDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            historicalTargetDate = historicalTargetDate.AddDays(-1);
        var dailyDirectory = Path.Combine(vaultPath, "Ежедневные");
        Directory.CreateDirectory(dailyDirectory);
        for (var date = today.AddYears(-3); date < today.AddDays(-1); date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            var file = Path.Combine(dailyDirectory, $"{date:yyyy-MM-dd}.md");
            if (File.Exists(file)) continue;
            var body = date == historicalTargetDate
                ? string.Join("\n", Enumerable.Range(1, 200).Select(line => line == 137
                    ? HistoricalFactMarker + " — details in [Decision note](../Проекты/UX05 Decision note.md)"
                    : $"Archive line {line:000} for {date:yyyy-MM-dd}"))
                : $"Ordinary archived workday {date:yyyy-MM-dd}";
            File.WriteAllText(file,
                $"# {date:yyyy-MM-dd}\n\n## Unlimotion <!-- unlimotion-area:area-unlimotion -->\n{body}\n",
                Utf8WithoutBom);
        }
        var note = Path.Combine(vaultPath, "Проекты", "UX05 Decision note.md");
        Directory.CreateDirectory(Path.GetDirectoryName(note)!);
        File.WriteAllText(note, "# UX05 Decision note\n\nThe compact sidebar was chosen for navigation.\n", Utf8WithoutBom);
    }

    private void SeedComparisonNotes(string vaultPath)
    {
        var todayPath = Path.Combine(vaultPath, UnlimotionAutomationScenarioData.GetFeedDailyRelativePath(
            DateOnly.FromDateTime(DateTime.Now)).Replace('/', Path.DirectorySeparatorChar));
        File.AppendAllText(todayPath, "\n[UX12 First note](../Проекты/UX12 First note.md)\n", Utf8WithoutBom);
        var folder = Path.Combine(vaultPath, "Проекты");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "UX12 First note.md"),
            "# UX12 First note\n\n[UX12 Second note](UX12 Second note.md)\n\n" +
            "- [ ] UX12 confirm comparison\n\n" +
            string.Join("\n\n", Enumerable.Range(1, 90).Select(index => $"First note item {index:000}")) + "\n",
            Utf8WithoutBom);
        File.WriteAllText(Path.Combine(folder, "UX12 Second note.md"),
            "# UX12 Second note\n\n[UX12 Third note](UX12 Third note.md)\n\n" +
            string.Join("\n\n", Enumerable.Range(1, 90).Select(index => $"Second note item {index:000}")) + "\n",
            Utf8WithoutBom);
        File.WriteAllText(Path.Combine(folder, "UX12 Third note.md"),
            "# UX12 Third note\n\nThird note reference.\n", Utf8WithoutBom);
    }

    private void SeedPlanningNote(string vaultPath)
    {
        var todayPath = Path.Combine(vaultPath, UnlimotionAutomationScenarioData.GetFeedDailyRelativePath(
            DateOnly.FromDateTime(DateTime.Now)).Replace('/', Path.DirectorySeparatorChar));
        File.AppendAllText(todayPath, "\n[UX11 Meeting decisions](../Проекты/UX11 Meeting decisions.md)\n", Utf8WithoutBom);
        var folder = Path.Combine(vaultPath, "Проекты");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "UX11 Meeting decisions.md"),
            "# UX11 Meeting decisions\n\n" +
            "[UX07 Product description](unlimotion://task/ux07-shared)\n\n" +
            "UX11 Clarify the shared product description\n\n" +
            "UX11 Check the first screen against three examples\n\n" +
            "UX11 The obsolete launch announcement is no longer needed\n", Utf8WithoutBom);
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task UX05_FindHistoricalFact()
    {
        RequireRenderedStoryMode();
        if (historicalTargetDate == default) throw new InvalidOperationException("The three-year archive was not seeded.");
        Page.ClickButton(static page => page.WorkspaceRailFeedButton);
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.VisibleDays.Count),
            count => count > 0,
            timeout: TimeSpan.FromSeconds(30),
            timeoutMessage: "The archive did not show recent days.");
        Page.FeedSearchBox.Enter(HistoricalFactMarker);
        var result = WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.SearchResults
                .FirstOrDefault(item => item.Text.Contains(HistoricalFactMarker, StringComparison.Ordinal))),
            value => value is not null,
            timeout: TimeSpan.FromSeconds(45),
            timeoutMessage: "Search did not find the historical fact deep in the three-year archive.")!;
        await Assert.That(result.RelativePath).IsEqualTo(
            UnlimotionAutomationScenarioData.GetFeedDailyRelativePath(historicalTargetDate));
        Page.FeedSearchBox.Enter(string.Empty);
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.IsSearchActive),
            active => !active,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Clearing the global search did not restore the Feed chronology.");
        await Assert.That(HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.VisibleDays.Count))
            .IsGreaterThan(0);
        Page.FeedSearchBox.Enter(HistoricalFactMarker);
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.SearchResults.Any(item =>
                item.RelativePath == result.RelativePath)),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The historical result did not return after clearing and repeating the search.");
        PrepareFeedTaskReferenceSurface();
        await CaptureStoryScreenshotAsync("ux05-historical-search-result.png");
        InvokeNativeButton(HeadlessRuntime.Dispatch(() => FindNativeControlByAutomationId<Button>(result.AutomationId)));
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.SelectedDay?.Date),
            date => date == historicalTargetDate,
            timeout: TimeSpan.FromSeconds(30),
            timeoutMessage: "Opening the search result did not navigate to its source day.");
        await Assert.That(HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel()
            .WorkspaceNavigation.ActivePane.ActiveTab?.CurrentLocation?.Id))
            .IsEqualTo(result.RelativePath);
        (bool Visible, double MatchY, double Offset) MatchPosition() => HeadlessRuntime.Dispatch(() =>
            {
                var window = Session.Inner.MainWindow;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var scroller = FindNativeControlByAutomationId<ScrollViewer>("FeedChronologyList");
                var preview = window.GetVisualDescendants().OfType<Unlimotion.Views.MarkdownBlockPreviewControl>()
                    .FirstOrDefault(control => control.IsEffectivelyVisible
                        && control.Block?.Index == result.Entry.BlockIndex
                        && control.Block.PreviewText.Contains(HistoricalFactMarker, StringComparison.Ordinal));
                var line = preview?.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault();
                if (line is null) return (false, double.NaN, scroller.Offset.Y);
                var bounds = GetBounds(line, scroller);
                var matchPosition = preview!.Block!.PreviewText.IndexOf(HistoricalFactMarker, StringComparison.Ordinal);
                var matchY = bounds.Top + line.TextLayout.HitTestTextPosition(matchPosition).Y;
                return (matchY >= 0 && matchY < scroller.Viewport.Height, matchY, scroller.Offset.Y);
            });
        var match = WaitUntil(
            MatchPosition,
            position => position.Visible,
            timeout: TimeSpan.FromSeconds(3),
            timeoutMessage: $"The historical match was not scrolled into view (position: {MatchPosition()}).");
        await CaptureStoryScreenshotAsync("ux05-long-historical-day.png");
        var noteLink = WaitUntil(
            () => HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(control => control.IsVisible &&
                    (AutomationProperties.GetHelpText(control) ?? string.Empty).Contains("UX05 Decision note",
                        StringComparison.Ordinal))),
            value => value is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The historical source did not expose its supporting note link.")!;
        InvokeNativeButton(noteLink);
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel()
                .WorkspaceNavigation.ActivePane.ActiveTab?.CurrentLocation?.Kind),
            kind => kind == Unlimotion.ViewModel.Workspace.WorkspaceLocationKind.Note,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The supporting document did not open in the current tab.");
        InvokeWorkspaceBack();
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel()
                .WorkspaceNavigation.ActivePane.ActiveTab?.CurrentLocation?.Id),
            path => path == result.RelativePath,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Back did not restore the same historical day.");
        await Assert.That(match.Visible).IsTrue()
            .Because("Search navigated to the 200-line day but did not scroll the matching line into view.");

        const string noteFact = "compact sidebar was chosen for navigation";
        Page.FeedSearchBox.Enter(noteFact);
        var noteResult = WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.SearchResults
                .FirstOrDefault(item => item.Type == Unlimotion.Notes.Search.FeedSearchDocumentType.Note
                    && item.RelativePath == "Проекты/UX05 Decision note.md")),
            value => value is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Search did not find the supporting thematic note.")!;
        var initialActions = ReadSearchOpeningActions(noteResult.ActionsAutomationId, noteResult.AutomationId);
        await Assert.That(initialActions).IsEquivalentTo(new[]
        {
            Unlimotion.ViewModel.Localization.Localization.Get("WorkspaceOpenHere"),
            Unlimotion.ViewModel.Localization.Localization.Get("WorkspaceOpenInNewTab"),
            Unlimotion.ViewModel.Localization.Localization.Get("WorkspaceOpenBeside")
        });
        InvokeNativeButton(HeadlessRuntime.Dispatch(() => FindNativeControlByAutomationId<Button>(noteResult.AutomationId)));
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel()
                .WorkspaceNavigation.ActivePane.ActiveTab?.CurrentLocation?.Id),
            id => id == noteResult.RelativePath,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The thematic search result did not open its note.");
        await Assert.That(HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants()
            .OfType<TextBlock>().Any(control => control.IsEffectivelyVisible
                && control.Text?.Contains(noteFact, StringComparison.OrdinalIgnoreCase) == true))).IsTrue();
        Page.FeedSearchBox.Enter(noteFact);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.SearchResults.Any(
                item => item.AutomationId == noteResult.AutomationId)),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "The open note did not return as a search result.");
        await Assert.That(ReadSearchOpeningActions(noteResult.ActionsAutomationId, noteResult.AutomationId)).IsEquivalentTo(new[]
        {
            Unlimotion.ViewModel.Localization.Localization.Get("WorkspaceShowIndicatedPlace")
        });
    }

    private string[] ReadSearchOpeningActions(string automationId, string resultAutomationId)
    {
        var actions = HeadlessRuntime.Dispatch(() =>
        {
            var window = Session.Inner.MainWindow;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var popup = window.GetVisualDescendants().OfType<Popup>().SingleOrDefault(control =>
                control.IsOpen && AutomationProperties.GetAutomationId(control) == "GlobalSearchFlyout");
            Control surface;
            if (popup?.Child is { } globalSearch) surface = globalSearch;
            else
            {
                var owner = GetHeadlessMainWindowViewModel();
                var activePaneId = ReferenceEquals(owner.WorkspaceNavigation.ActivePane, owner.WorkspaceNavigation.PrimaryPane)
                    ? "WorkspacePrimaryPane" : "WorkspaceSecondaryPane";
                surface = window.GetVisualDescendants().OfType<Unlimotion.Views.WorkspacePaneView>()
                    .Single(control => control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) == activePaneId);
            }
            return surface.GetVisualDescendants().OfType<DropDownButton>().Single(control =>
                control.IsEffectivelyVisible && control.IsAttachedToVisualTree()
                && AutomationProperties.GetAutomationId(control) == automationId
                && control.DataContext is Unlimotion.ViewModel.Feed.FeedSearchResultViewModel result
                && result.AutomationId == resultAutomationId);
        });
        InvokeNativeButton(actions);
        return HeadlessRuntime.Dispatch(() =>
        {
            var flyout = (MenuFlyout)actions.Flyout!;
            if (!flyout.IsOpen) throw new InvalidOperationException("The visible search result's opening menu did not open.");
            try { return flyout.Items.OfType<MenuItem>().Select(item => item.Header!.ToString()!).ToArray(); }
            finally { flyout.Hide(); }
        });
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task UX06_UseExistingVault()
    {
        RequireRenderedStoryMode();
        if (feedVaultPath is null) throw new InvalidOperationException("UX06 has no isolated vault.");
        ConfigureDailyNoteFilenameFormatSettings();
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.IsVaultInitialized),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The existing vault did not initialize before settings were opened.");
        Page.ClickButton(static page => page.WorkspaceRailTasksButton);
        Page.ClickButton(static page => page.GlobalSettingsButton);
        _ = WaitForHeadlessControl(() => Page.SettingsRoot, "Settings were not accessible from Tasks.");
        var formatInput = Page.NoteDailyFileNameFormatTextBox;
        EnterDailyNoteFilenameFormat(formatInput, "yyyy.MM.dd");
        EnsureDailyNoteFilenameFormatDraft("yyyy.MM.dd");
        FlushDailyNoteFilenameFormatUi();
        WaitUntil(() => Page.ApplyNoteDailyFileNameFormatButton.IsEnabled,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The dotted daily filename format could not be applied.");
        await CaptureStoryScreenshotAsync("ux06-existing-vault-settings.png");
        Page.ApplyNoteDailyFileNameFormatButton.Invoke();
        Page.ConfirmNoteDailyFileNameFormatButton.Invoke();
        WaitUntil(
            () => GetAppliedDailyNoteFilenameFormat(),
            format => format == "yyyy.MM.dd",
            timeout: TimeSpan.FromSeconds(20),
            timeoutMessage: "The dotted filename format was not persisted.");
        Page.ClickButton(static page => page.GlobalSettingsCloseButton);
        Page.ClickButton(static page => page.WorkspaceRailFeedButton);
        const string marker = "UX06 edited the existing dotted daily note";
        HeadlessRuntime.Dispatch(() =>
        {
            var window = Session.Inner.MainWindow;
            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Space,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
                Source = window
            });
            Dispatcher.UIThread.RunJobs();
        });
        Page.FeedQuickCaptureTextBox.Enter(marker);
        Page.FeedCaptureButton.Invoke();
        var dottedPath = GetDottedDailyRelativePath(DateOnly.FromDateTime(DateTime.Now));
        WaitUntil(
            () => ReadFeedVaultText(dottedPath),
            text => text.Contains(marker, StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Capture did not edit the existing dotted daily Markdown file.");
        await Assert.That(Path.GetFullPath(Path.Combine(feedVaultPath,
            dottedPath.Replace('/', Path.DirectorySeparatorChar)))).StartsWith(feedVaultPath);
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task UX09_InAppTaskLink()
    {
        RequireRenderedStoryMode();
        Page.ClickButton(static page => page.WorkspaceRailFeedButton);
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.VisibleDays.Count),
            count => count > 0,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The source note was not visible.");
        PrepareFeedTaskReferenceSurface();
        var link = WaitForHeadlessControl(() => Page.FeedSeededTaskTitleButton,
            "The source note did not show its existing task link.");
        link.Invoke();
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel()
                .WorkspaceNavigation.ActivePane.ActiveTab?.CurrentLocation?.Id),
            id => id == UnlimotionAutomationScenarioData.FeedCurrentTaskId,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The link did not open the existing task in the current tab.");
        await Assert.That(HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel()
            .WorkspaceNavigation.PrimaryPane.Tabs.Count)).IsEqualTo(1);
        await CaptureStoryScreenshotAsync("ux09-existing-task-link.png");
        InvokeWorkspaceBack();
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel()
                .WorkspaceNavigation.ActivePane.ActiveTab?.CurrentLocation?.Kind),
            kind => kind == Unlimotion.ViewModel.Workspace.WorkspaceLocationKind.Feed,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Back did not return to the source Feed document.");
        WaitForHeadlessControl(() => Page.FeedSeededTaskTitleButton,
            "The task link disappeared after returning to its source.").Invoke();
        await Assert.That(HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel()
            .WorkspaceNavigation.PrimaryPane.Tabs.Count)).IsEqualTo(1);
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task UX12_CompareNotes()
    {
        RequireRenderedStoryMode();
        if (feedVaultPath is null) throw new InvalidOperationException("UX12 has no isolated vault.");
        Page.ClickButton(static page => page.WorkspaceRailFeedButton);
        PrepareFeedTaskReferenceSurface();
        var firstLink = FindStoryLink("UX12 First note");
        InvokeNativeButton(firstLink);
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel()
                .WorkspaceNavigation.PrimaryPane.ActiveTab?.CurrentLocation?.Id),
            path => path == "Проекты/UX12 First note.md",
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The first document did not replace the Feed in the current tab.");
        var secondLink = FindStoryLink("UX12 Second note");
        OpenStoryLinkBesidePhysically(secondLink);
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel()
                .WorkspaceNavigation.SecondaryPane?.ActiveTab?.CurrentLocation?.Id),
            path => path == "Проекты/UX12 Second note.md",
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The second document did not open beside the first.");
        await Assert.That(HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().WorkspaceNavigation.PrimaryPane
            .ActiveTab?.CurrentLocation is { Kind: Unlimotion.ViewModel.Workspace.WorkspaceLocationKind.Note } note &&
            note.Id == "Проекты/UX12 First note.md")).IsTrue();
        var firstNote = HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.DocumentWorkspace
            .Find("Проекты/UX12 First note.md"))
            ?? throw new InvalidOperationException("The first note was not loaded into the document workspace.");
        var checkboxId = HeadlessRuntime.Dispatch(() => firstNote.MarkdownEditor.Blocks
            .Single(block => block.Block.Raw.Contains("UX12 confirm comparison", StringComparison.Ordinal))
            .TaskCheckboxAutomationId);
        HeadlessRuntime.Dispatch(() =>
        {
            var checkbox = FindNativeControlByAutomationId<CheckBox>(checkboxId);
            checkbox.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, checkbox));
            Dispatcher.UIThread.RunJobs();
        });
        WaitUntil(
            () => ReadFeedVaultText("Проекты/UX12 First note.md"),
            text => text.Contains("- [x] UX12 confirm comparison", StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Editing the first note did not persist while the second was open beside it.");
        var offsets = HeadlessRuntime.Dispatch(() =>
        {
            var firstScroller = FindNoteScroller("Проекты/UX12 First note.md");
            var secondScroller = FindNoteScroller("Проекты/UX12 Second note.md");
            firstScroller.Offset = new Vector(0, 340);
            secondScroller.Offset = new Vector(0, 500);
            Dispatcher.UIThread.RunJobs();
            Session.Inner.MainWindow.UpdateLayout();
            return (First: firstScroller.Offset.Y, Second: secondScroller.Offset.Y);
        });
        await Assert.That(offsets.First).IsGreaterThan(0);
        await Assert.That(offsets.Second).IsGreaterThan(0);
        await Assert.That(HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants()
            .OfType<Border>().Count(control => control.IsEffectivelyVisible
                && AutomationProperties.GetAutomationId(control) == "FeedReviewBanner")))
            .IsEqualTo(0).Because("Two note panes must not repeat the Feed review reminder.");
        await CaptureStoryScreenshotAsync("ux12-two-notes.png");
        var visiblePaths = HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().WorkspaceNavigation
            .PrimaryPane.ActiveTab?.CurrentLocation?.Id + " | " + GetHeadlessMainWindowViewModel().WorkspaceNavigation
            .SecondaryPane?.ActiveTab?.CurrentLocation?.Id);
        await Assert.That(visiblePaths).Contains("UX12 First note.md | Проекты/UX12 Second note.md");
        var thirdLink = FindStoryLink("UX12 Third note");
        HeadlessRuntime.Dispatch(() =>
        {
            thirdLink.BringIntoView();
            Dispatcher.UIThread.RunJobs();
        });
        var beforeNavigationOffsets = HeadlessRuntime.Dispatch(() => (
            First: FindNoteScroller("Проекты/UX12 First note.md").Offset.Y,
            Second: FindNoteScroller("Проекты/UX12 Second note.md").Offset.Y));
        InvokeNativeButton(thirdLink);
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel()
                .WorkspaceNavigation.ActivePane.ActiveTab?.CurrentLocation?.Id),
            path => path == "Проекты/UX12 Third note.md",
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Following the second document's link did not open the third document.");
        InvokeWorkspaceBack();
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel()
                .WorkspaceNavigation.ActivePane.ActiveTab?.CurrentLocation?.Id),
            path => path == "Проекты/UX12 Second note.md",
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Back did not restore the second document beside the first.");
        var restoredOffsets = WaitUntil(() => HeadlessRuntime.Dispatch(() =>
            {
                Dispatcher.UIThread.RunJobs();
                Session.Inner.MainWindow.UpdateLayout();
                return (First: FindNoteScroller("Проекты/UX12 First note.md").Offset.Y,
                    Second: FindNoteScroller("Проекты/UX12 Second note.md").Offset.Y);
            }),
            offsets => Math.Abs(offsets.First - beforeNavigationOffsets.First) < 8
                && Math.Abs(offsets.Second - beforeNavigationOffsets.Second) < 8,
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Back did not restore both document viewports after layout.");
        await Assert.That(Math.Abs(restoredOffsets.First - beforeNavigationOffsets.First)).IsLessThan(8);
        await Assert.That(Math.Abs(restoredOffsets.Second - beforeNavigationOffsets.Second)).IsLessThan(8);
    }

    private ScrollViewer FindNoteScroller(string relativePath)
    {
        var feed = Session.Inner.MainWindow.GetVisualDescendants().OfType<Unlimotion.Views.FeedControl>()
            .FirstOrDefault(control => control.IsVisible &&
                control.DisplayedDocument?.RelativePath == relativePath)
            ?? throw new InvalidOperationException($"The note view for '{relativePath}' is not visible.");
        return feed.GetVisualDescendants().OfType<ScrollViewer>().First(control =>
            AutomationProperties.GetAutomationId(control) == "FeedDocumentScrollViewer");
    }

    private Button FindStoryLink(string title) => WaitUntil(
        () => HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(control => control.IsVisible &&
                (AutomationProperties.GetHelpText(control) ?? string.Empty).Contains(title,
                    StringComparison.Ordinal))),
        value => value is not null,
        timeout: TimeSpan.FromSeconds(10),
        timeoutMessage: $"The visible document did not expose the link to {title}.")!;

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task UX08_ChooseWork()
    {
        RequireRenderedStoryMode();
        Page.ClickButton(static page => page.WorkspaceRailTasksButton);
        PrepareFeedTaskReferenceSurface();
        OpenStoryTaskView("WorkspaceRailInProgressButton");
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().InProgressItems
                .SelectMany(item => FlattenStoryTask(item)).Select(item => item.Id).ToArray()),
            ids => ids.Contains("ux08-active") && ids.Contains("ux08-finished"),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The in-progress list did not show existing work to review first.");
        WaitUntil(
            () => Page.InProgressTree.Items.Count,
            count => count > 0,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The in-progress task tree did not render its loaded items.");
        SelectStoryTaskFromTree("InProgressTree", "ux08-finished");
        WaitForVisibleTaskCard("UX08 Already finished in reality");
        SelectVisibleTaskStatus(Unlimotion.Domain.TaskStatus.Completed);
        WaitForTaskStatus("ux08-finished", Unlimotion.Domain.TaskStatus.Completed);

        Page.ClickButton(static page => page.WorkspaceRailTasksButton);
        OpenStoryTaskView("WorkspaceRailUnlockedButton");
        var initialUnlocked = HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().UnlockedItems
            .SelectMany(item => FlattenStoryTask(item)).Select(item => item.Id).ToArray());
        await CaptureStoryScreenshotAsync("ux08-unlocked-before-filter.png");
        await Assert.That(initialUnlocked).Contains("ux08-short");
        await Assert.That(initialUnlocked).Contains("ux08-urgent");
        await Assert.That(initialUnlocked).Contains("ux08-overdue");
        await Assert.That(initialUnlocked).DoesNotContain("ux08-blocked");
        await Assert.That(initialUnlocked).Contains("ux08-finished")
            .Because("The isolated fixture explicitly persists ShowCompleted=true; the user's preference must be honored.");
        HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().UnlockedStatusFilters
            .Single(filter => filter.Status == Unlimotion.Domain.TaskStatus.Completed).ShowTasks = false);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().UnlockedItems
                .SelectMany(item => FlattenStoryTask(item)).All(item => item.Id != "ux08-finished")),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The unlocked list kept a completed task after its status filter was switched off.");

        var filterButton = GetNativeControl<DropDownButton>(Page.UnlockedFiltersButton);
        InvokeNativeButton(filterButton);
        var filterPanel = HeadlessRuntime.Dispatch(() => (filterButton.Flyout as Flyout)?.Content as Border)
            ?? throw new InvalidOperationException("The unlocked-filter button has no filter flyout content.");
        HeadlessRuntime.Dispatch(() =>
        {
            var combos = filterPanel.GetVisualDescendants().OfType<ComboBox>().ToArray();
            var timing = combos.First(combo => combo.ItemsSource is IEnumerable<Unlimotion.ViewModel.UnlockedTimeFilter>);
            var duration = combos.First(combo => combo.ItemsSource is IEnumerable<Unlimotion.ViewModel.DurationFilter>);
            timing.IsDropDownOpen = true;
            duration.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
        });
        HeadlessRuntime.Dispatch(() =>
        {
            var choices = Session.Inner.MainWindow.GetVisualDescendants().OfType<CheckBox>().ToArray();
            var today = choices.FirstOrDefault(item => item.DataContext is Unlimotion.ViewModel.UnlockedTimeFilter
                { ResourceKey: "UnlockedTimeFilterToday" });
            var shortDuration = choices.FirstOrDefault(item => item.DataContext is Unlimotion.ViewModel.DurationFilter
                { ResourceKey: "DurationFilter5m" });
            if (today is null || shortDuration is null)
                throw new InvalidOperationException("The unlocked filter flyout did not render Today and five-minute choices.");
            today.IsChecked = true;
            shortDuration.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
        });
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().UnlockedItems
                .SelectMany(item => FlattenStoryTask(item)).Select(item => item.Id).ToArray()),
            ids => ids.Contains("ux08-short") && !ids.Contains("ux08-urgent") && !ids.Contains("ux08-long"),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Today plus short-duration filters did not narrow the unlocked list.");
        await CaptureStoryScreenshotAsync("ux08-today-short-work.png");
        HeadlessRuntime.Dispatch(() => filterButton.Flyout!.Hide());
        SelectStoryTaskFromTree("UnlockedTree", "ux08-short");
        WaitForVisibleTaskCard("UX08 Five-minute action");
        SelectVisibleTaskStatus(Unlimotion.Domain.TaskStatus.Completed);
        WaitForTaskStatus("ux08-short", Unlimotion.Domain.TaskStatus.Completed);
        Page.ClickButton(static page => page.WorkspaceRailTasksButton);
        OpenStoryTaskView("WorkspaceRailUnlockedButton");
        var filteredFlyout = HeadlessRuntime.Dispatch(() => FindNativeControlByAutomationId<DropDownButton>(
            "UnlockedFiltersButton"));
        InvokeNativeButton(filteredFlyout);
        HeadlessRuntime.Dispatch(() =>
        {
            var panel = (filteredFlyout.Flyout as Flyout)?.Content as Border
                ?? throw new InvalidOperationException("The unlocked filters did not reopen.");
            var combos = panel.GetVisualDescendants().OfType<ComboBox>().ToArray();
            combos.First(combo => combo.ItemsSource is IEnumerable<Unlimotion.ViewModel.UnlockedTimeFilter>)
                .IsDropDownOpen = true;
            combos.First(combo => combo.ItemsSource is IEnumerable<Unlimotion.ViewModel.DurationFilter>)
                .IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            var choices = Session.Inner.MainWindow.GetVisualDescendants().OfType<CheckBox>().ToArray();
            choices.First(item => item.DataContext is Unlimotion.ViewModel.UnlockedTimeFilter
                { ResourceKey: "UnlockedTimeFilterToday" }).IsChecked = false;
            choices.First(item => item.DataContext is Unlimotion.ViewModel.DurationFilter
                { ResourceKey: "DurationFilter5m" }).IsChecked = false;
            choices.First(item => item.DataContext is Unlimotion.ViewModel.UnlockedTimeFilter
                { ResourceKey: "UnlockedTimeFilterUrgent" }).IsChecked = true;
            choices.First(item => item.DataContext is Unlimotion.ViewModel.DurationFilter
                { ResourceKey: "DurationFilter5mTo30m" }).IsChecked = true;
            Dispatcher.UIThread.RunJobs();
        });
        WaitUntil(() => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().UnlockedItems
                .SelectMany(item => FlattenStoryTask(item)).Select(task => task.Id).ToArray()),
            ids => ids.Contains("ux08-urgent") && !ids.Contains("ux08-overdue") && !ids.Contains("ux08-long"),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Urgent 5–30 minute work was not isolated.");
        HeadlessRuntime.Dispatch(() => filteredFlyout.Flyout!.Hide());
        SelectStoryTaskFromTree("UnlockedTree", "ux08-urgent");
        WaitForVisibleTaskCard("UX08 Due today");
        SelectVisibleTaskStatus(Unlimotion.Domain.TaskStatus.InProgress);
        WaitForTaskStatus("ux08-urgent", Unlimotion.Domain.TaskStatus.InProgress);
        SelectVisibleTaskStatus(Unlimotion.Domain.TaskStatus.Completed);
        WaitForTaskStatus("ux08-urgent", Unlimotion.Domain.TaskStatus.Completed);
        await CaptureStoryScreenshotAsync("ux08-urgent-completed.png");
        OpenStoryTaskView("WorkspaceRailUnlockedButton");
        filteredFlyout = HeadlessRuntime.Dispatch(() => FindNativeControlByAutomationId<DropDownButton>("UnlockedFiltersButton"));
        HeadlessRuntime.Dispatch(() =>
        {
            var flyout = filteredFlyout.Flyout as Flyout
                ?? throw new InvalidOperationException("The unlocked filters are unavailable after completing work.");
            flyout.ShowAt(filteredFlyout);
            Dispatcher.UIThread.RunJobs();
            var panel = flyout.Content as Border
                ?? throw new InvalidOperationException("The unlocked filter panel was not restored.");
            var combos = panel.GetVisualDescendants().OfType<ComboBox>().ToArray();
            combos.First(combo => combo.ItemsSource is IEnumerable<Unlimotion.ViewModel.UnlockedTimeFilter>)
                .IsDropDownOpen = true;
            combos.First(combo => combo.ItemsSource is IEnumerable<Unlimotion.ViewModel.DurationFilter>)
                .IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            var choices = Session.Inner.MainWindow.GetVisualDescendants().OfType<CheckBox>().ToArray();
            choices.First(item => item.DataContext is Unlimotion.ViewModel.UnlockedTimeFilter
                { ResourceKey: "UnlockedTimeFilterUrgent" }).IsChecked = false;
            choices.First(item => item.DataContext is Unlimotion.ViewModel.UnlockedTimeFilter
                { ResourceKey: "UnlockedTimeFilterOverdue" }).IsChecked = true;
            Dispatcher.UIThread.RunJobs();
        });
        WaitUntil(() => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().UnlockedItems
                .SelectMany(item => FlattenStoryTask(item)).Select(task => task.Id).ToArray()),
            ids => ids.Contains("ux08-overdue") && !ids.Contains("ux08-urgent"),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Overdue work was not separated from urgent work.");
        await CaptureStoryScreenshotAsync("ux08-overdue-work.png");
        HeadlessRuntime.Dispatch(() =>
        {
            var choices = Session.Inner.MainWindow.GetVisualDescendants().OfType<CheckBox>().ToArray();
            choices.First(item => item.DataContext is Unlimotion.ViewModel.UnlockedTimeFilter
                { ResourceKey: "UnlockedTimeFilterOverdue" }).IsChecked = false;
            choices.First(item => item.DataContext is Unlimotion.ViewModel.DurationFilter
                { ResourceKey: "DurationFilter5mTo30m" }).IsChecked = false;
            choices.First(item => item.DataContext is Unlimotion.ViewModel.DurationFilter
                { ResourceKey: "DurationFilter30mTo2h" }).IsChecked = true;
            Dispatcher.UIThread.RunJobs();
        });
        WaitUntil(() => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().UnlockedItems
                .SelectMany(item => FlattenStoryTask(item)).Select(task => task.Id).ToArray()),
            ids => ids.Contains("ux08-long") && !ids.Contains("ux08-overdue"),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "The longer-work filter did not expose the 55-minute task.");
        HeadlessRuntime.Dispatch(() => filteredFlyout.Flyout!.Hide());
        SelectStoryTaskFromTree("UnlockedTree", "ux08-long");
        WaitForVisibleTaskCard("UX08 Long available task");
        await CaptureStoryScreenshotAsync("ux08-longer-work.png");

        OpenStoryTaskView("WorkspaceRailCompletedButton");
        await Assert.That(HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items
            .Single(task => task.Id == "ux08-short").Status)).IsEqualTo(Unlimotion.Domain.TaskStatus.Completed);
        await Assert.That(HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items
            .Single(task => task.Id == "ux08-urgent").Status)).IsEqualTo(Unlimotion.Domain.TaskStatus.Completed);
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task UX07_LinkedPlan()
    {
        RequireRenderedStoryMode();
        Page.ClickButton(static page => page.WorkspaceRailTasksButton);
        OpenStoryTaskView("WorkspaceRailAllTasksButton");
        PrepareFeedTaskReferenceSurface();
        WaitUntil(() => Page.AllTasksTree.Items.Count, count => count > 0,
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "The project plan did not render.");

        SelectStoryTaskFromTree("AllTasksTree", "ux07-goal");
        WaitForVisibleTaskCard("UX07 Launch site");
        await Assert.That(HeadlessRuntime.Dispatch(() => TryFindNativeControlByAutomationId<Control>(
            "CurrentTaskGoalIndicator") is null)).IsTrue();
        var plan = HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items
            .Single(task => task.Id == "ux07-goal").ContainsTasks.Select(child => child.Id).ToArray());
        await Assert.That(plan).Contains("ux07-structure");
        await Assert.That(plan).Contains("ux07-prototype");

        SelectStoryTaskFromTree("AllTasksTree", "ux07-shared");
        WaitForVisibleTaskCard("UX07 Product description");
        AddStoryRelation("CurrentTaskParentsRelation", "UX07 Publish product", "ux07-second-goal");
        WaitUntil(() => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items
                .Single(task => task.Id == "ux07-shared").Parents.ToArray()),
            parents => parents.Contains("ux07-goal") && parents.Contains("ux07-second-goal"),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "The shared task did not gain a second parent.");

        SelectStoryTaskFromTree("AllTasksTree", "ux07-prototype");
        WaitForVisibleTaskCard("UX07 Build prototype");
        AddStoryRelation("CurrentTaskBlockingRelation", "UX07 Agree site structure", "ux07-structure");
        WaitUntil(() => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items
                .Single(task => task.Id == "ux07-prototype").BlockedBy.ToArray()),
            blockers => blockers.Contains("ux07-structure"), timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The prototype was not blocked by the prerequisite task.");
        await CaptureStoryScreenshotAsync("ux07-linked-project-plan.png");
    }

    private void AddStoryRelation(string prefix, string query, string expectedTaskId)
    {
        var addButton = HeadlessRuntime.Dispatch(() => FindNativeControlByAutomationId<Button>($"{prefix}AddButton"));
        InvokeNativeButton(addButton);
        var input = WaitUntil(() => HeadlessRuntime.Dispatch(() =>
                TryFindNativeControlByAutomationId<TextBox>($"{prefix}AddInput") is { IsVisible: true }
                    ? FindNativeControlByAutomationId<TextBox>($"{prefix}AddInput") : null),
            control => control is not null, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"The {prefix} relation search did not open.")!;
        HeadlessRuntime.Dispatch(() =>
        {
            input.Focus();
            input.Text = query;
            Dispatcher.UIThread.RunJobs();
        });
        var candidate = WaitUntil(() => HeadlessRuntime.Dispatch(() =>
                input.GetVisualAncestors().OfType<Unlimotion.Views.TaskCardView>().Single().CardContext!.RelationEditor.Suggestions
                    .FirstOrDefault(item => item.Task.Id == expectedTaskId)),
            value => value is not null, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"The {prefix} relation search did not find {expectedTaskId}.")!;
        var confirm = HeadlessRuntime.Dispatch(() =>
        {
            var suggestions = FindNativeControlByAutomationId<ListBox>($"{prefix}Suggestions");
            suggestions.SelectedItem = candidate;
            Dispatcher.UIThread.RunJobs();
            var confirm = FindNativeControlByAutomationId<Button>($"{prefix}AddConfirmButton");
            if (!confirm.IsEnabled) throw new InvalidOperationException($"The {prefix} relation could not be confirmed.");
            return confirm;
        });
        InvokeNativeButton(confirm);
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task UX13_MetaWork()
    {
        RequireRenderedStoryMode();
        Page.ClickButton(static page => page.WorkspaceRailTasksButton);
        OpenStoryTaskView("WorkspaceRailAllTasksButton");
        PrepareFeedTaskReferenceSurface();
        WaitUntil(() => Page.AllTasksTree.Items.Count, count => count > 0,
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "The planning tasks did not load.");

        SelectStoryTaskFromTree("AllTasksTree", "ux13-unprepared");
        WaitForVisibleTaskCard("UX13 Needs planning");
        SelectStoryQuickPlanningValue("CurrentTaskSetDurationButton", 3); // twenty minutes
        SelectStoryQuickPlanningValue("CurrentTaskSetBeginButton", 0); // today
        SelectStoryQuickPlanningValue("CurrentTaskSetEndButton", 0); // today
        Page.CurrentTaskDescriptionTextBox.Enter(
            "Review the prepared brief and deliver a concrete next-step decision.");
        var titleBox = GetNativeControl<TextBox>(Page.CurrentTaskTitleTextBox);
        HeadlessRuntime.Dispatch(() => titleBox.Focus());
        WaitUntil(() => HeadlessRuntime.Dispatch(() =>
                GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items.Single(task => task.Id == "ux13-unprepared")),
            task => task.PlannedDuration == TimeSpan.FromMinutes(20)
                && task.PlannedBeginDateTime is not null && task.PlannedEndDateTime is not null
                && task.Description.Contains("concrete next-step", StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Duration, dates or execution context were not saved from the card.");
        SelectVisibleTaskStatus(Unlimotion.Domain.TaskStatus.Prepared);
        WaitForTaskStatus("ux13-unprepared", Unlimotion.Domain.TaskStatus.Prepared);
        await CaptureStoryScreenshotAsync("ux13-prepared-task.png");

        SelectStoryTaskFromTree("AllTasksTree", "ux13-stale");
        WaitForVisibleTaskCard("UX13 Obsolete task");
        SelectVisibleTaskStatus(Unlimotion.Domain.TaskStatus.Archived);
        WaitForTaskStatus("ux13-stale", Unlimotion.Domain.TaskStatus.Archived);
        await Assert.That(HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items
            .Single(task => task.Id == "ux13-stale").Status)).IsNotEqualTo(Unlimotion.Domain.TaskStatus.Completed);
        await CaptureStoryScreenshotAsync("ux13-archived-obsolete.png");

        SelectStoryTaskFromTree("AllTasksTree", "ux08-finished");
        WaitForVisibleTaskCard("UX08 Already finished in reality");
        SelectVisibleTaskStatus(Unlimotion.Domain.TaskStatus.Completed);
        WaitForTaskStatus("ux08-finished", Unlimotion.Domain.TaskStatus.Completed);

        SelectStoryTaskFromTree("AllTasksTree", "ux07-prototype");
        WaitForVisibleTaskCard("UX07 Build prototype");
        var originalChildren = HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items
            .Single(task => task.Id == "ux07-prototype").Contains.ToArray());
        HeadlessRuntime.Dispatch(() =>
        {
            var plus = FindNativeControlByAutomationId<Button>("GlobalCreateMenuButton");
            var menu = plus.Flyout as MenuFlyout
                ?? throw new InvalidOperationException("The global create button has no task menu.");
            menu.ShowAt(plus);
            Dispatcher.UIThread.RunJobs();
            var createInner = menu.Items.OfType<MenuItem>().First(item =>
                AutomationProperties.GetAutomationId(item) == "GlobalTaskCreateInnerMenuItem");
            if (!createInner.IsEnabled) throw new InvalidOperationException("New child task is unavailable.");
            createInner.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, createInner));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();
        });
        var nextStep = WaitUntil(() => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().CurrentTaskItem),
            task => task is not null && task.Id != "ux07-prototype" && task.Parents.Contains("ux07-prototype"),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "The long task did not gain a new child step.")!;
        HeadlessRuntime.Dispatch(() =>
        {
            var title = FindVisibleStoryTaskTextBox("CurrentTaskTitleTextBox");
            title.Focus();
            title.Text = "UX13 Verify prototype on a small screen";
            FindVisibleStoryTaskTextBox("CurrentTaskDescriptionTextBox").Focus();
            Dispatcher.UIThread.RunJobs();
        });
        WaitUntil(() => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items
                .Single(task => task.Id == nextStep.Id).Title),
            title => title == "UX13 Verify prototype on a small screen", timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The decomposed next step was not saved.");
        SelectVisibleTaskStatus(Unlimotion.Domain.TaskStatus.Prepared);
        WaitForTaskStatus(nextStep.Id, Unlimotion.Domain.TaskStatus.Prepared);
        await Assert.That(HeadlessRuntime.Dispatch(() => nextStep.IsCanBeCompleted)).IsTrue()
            .Because("A new child without blockers must be available after preparation.");
        Page.ClickButton(static page => page.WorkspaceRailTasksButton);
        OpenStoryTaskView("WorkspaceRailUnlockedButton");
        WaitUntil(() => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().UnlockedItems
                .SelectMany(item => FlattenStoryTask(item)).Any(item => item.Id == nextStep.Id)),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The prepared, unblocked next step did not enter the unlocked work queue.");
        await Assert.That(HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items
            .Single(task => task.Id == "ux07-prototype").Contains.Count)).IsEqualTo(originalChildren.Length + 1);
        await CaptureStoryScreenshotAsync("ux13-new-next-step.png");
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task UX11_UpdatePlanFromNotes()
    {
        RequireRenderedStoryMode();
        if (feedVaultPath is null) throw new InvalidOperationException("UX11 has no isolated note vault.");
        Page.ClickButton(static page => page.WorkspaceRailFeedButton);
        PrepareFeedTaskReferenceSurface();
        InvokeNativeButton(FindStoryLink("UX11 Meeting decisions"));
        const string relativeNote = "Проекты/UX11 Meeting decisions.md";
        WaitUntil(() => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel()
                .WorkspaceNavigation.ActivePane.ActiveTab?.CurrentLocation?.Id),
            id => id == relativeNote, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The meeting note did not open for planning.");
        var taskLink = FindStoryLink("ux07-shared");
        OpenStoryLinkBesidePhysically(taskLink);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel()
                .WorkspaceNavigation.SecondaryPane?.ActiveTab?.CurrentLocation is
                    { Kind: Unlimotion.ViewModel.Workspace.WorkspaceLocationKind.Task } task && task.Id == "ux07-shared"), timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The existing plan task did not open beside the meeting note.");
        await Assert.That(HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().WorkspaceNavigation.PrimaryPane
            .ActiveTab?.CurrentLocation is { Kind: Unlimotion.ViewModel.Workspace.WorkspaceLocationKind.Note } note &&
            note.Id == relativeNote)).IsTrue();
        WaitForVisibleTaskCard("UX07 Product description");
        var originalCount = HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items.Count());
        HeadlessRuntime.Dispatch(() =>
        {
            var description = FindVisibleStoryTaskTextBox("CurrentTaskDescriptionTextBox");
            description.Focus();
            description.Text = "Shared product description, updated after UX11 meeting decisions.";
            FindVisibleStoryTaskTextBox("CurrentTaskTitleTextBox").Focus();
            Dispatcher.UIThread.RunJobs();
        });
        WaitUntil(() => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items
                .Single(task => task.Id == "ux07-shared").Description),
            description => description.Contains("UX11 meeting decisions", StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "Updating the existing task from the note did not persist.");
        await Assert.That(HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items.Count()))
            .IsEqualTo(originalCount);
        await CaptureStoryScreenshotAsync("ux11-note-beside-plan-task.png");

        var note = HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.DocumentWorkspace.Find(relativeNote))
            ?? throw new InvalidOperationException("The meeting note editor is unavailable.");
        var newStep = HeadlessRuntime.Dispatch(() => note.MarkdownEditor.Blocks.Single(block =>
            block.Block.Raw.Contains("UX11 Check the first screen", StringComparison.Ordinal)));
        HeadlessRuntime.Dispatch(() =>
        {
            var row = FindNativeControlByAutomationId<Grid>(newStep.BlockAutomationId);
            row.RaiseEvent(new ContextRequestedEventArgs { Source = row });
            Dispatcher.UIThread.RunJobs();
            var create = row.ContextMenu?.Items.OfType<MenuItem>().SingleOrDefault(item =>
                Equals(item.Header, Unlimotion.ViewModel.Localization.Localization.Get("FeedToolbarTask")))
                ?? throw new InvalidOperationException("The note block has no create-task command.");
            if (!create.IsEnabled) throw new InvalidOperationException("The note block create-task command is disabled.");
            create.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, create));
            row.ContextMenu!.Close();
            Dispatcher.UIThread.RunJobs();
        });
        WaitUntil(ObserveUnifiedReviewState,
            state => state.SelectedMarkdown?.Contains("UX11 Check the first screen", StringComparison.Ordinal) == true,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Creating a task from the meeting note did not enter review for the selected step.");
        var reviewArea = WaitUntil(() => HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            Session.Inner.MainWindow.UpdateLayout();
            if (!GetHeadlessMainWindowViewModel().Feed.IsReviewTaskStage) return null;
            return Session.Inner.MainWindow.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault(control =>
                control.IsEffectivelyVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0 &&
                control.DataContext is Unlimotion.ViewModel.Feed.FeedTaskAreaOptionViewModel option &&
                option.Area.StableAreaId == "area-unlimotion");
        }), control => control is not null, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The review task editor did not render the Unlimotion area checkbox.")!;
        if (HeadlessRuntime.Dispatch(() => reviewArea.IsChecked != true)) InvokeNativeButton(reviewArea);
        await Assert.That(HeadlessRuntime.Dispatch(() => reviewArea.IsChecked == true &&
            reviewArea.DataContext is Unlimotion.ViewModel.Feed.FeedTaskAreaOptionViewModel option &&
            option.Area.StableAreaId == "area-unlimotion" && option.IsSelected)).IsTrue();
        AddStoryReviewDraftParent("UX07 Launch site", "ux07-goal");
        Page.FeedReviewConfirmButton.Invoke();
        var created = WaitUntil(() => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items
                .FirstOrDefault(task => task.Title.Contains("UX11 Check the first screen", StringComparison.Ordinal))),
            task => task is not null, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The new plan step was not created from the meeting note.")!;
        await Assert.That(HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items.Count()))
            .IsEqualTo(originalCount + 1);
        var sourceResult = WaitUntil(() => (
                Text: ReadFeedVaultText(relativeNote),
                Error: HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.ErrorMessage)),
            state => state.Text.Contains("unlimotion://task/" + created.Id, StringComparison.Ordinal)
                || !string.IsNullOrWhiteSpace(state.Error),
            timeout: TimeSpan.FromSeconds(20),
            timeoutMessage: "The meeting note did not link to its new task and review reported no error.");
        if (!sourceResult.Text.Contains("unlimotion://task/" + created.Id, StringComparison.Ordinal))
            throw new InvalidOperationException($"The task was created, but its source was not linked: {sourceResult.Error}");
        await Assert.That(HeadlessRuntime.Dispatch(() => created.Parents.ToArray())).Contains("ux07-goal");
        await Assert.That(HeadlessRuntime.Dispatch(() => created.AreaIds.ToArray())).Contains("area-unlimotion");
        WaitUntil(ObserveUnifiedReviewState,
            state => state.SelectedMarkdown?.Contains(UnlimotionAutomationScenarioData.FeedPendingReviewMarker,
                StringComparison.Ordinal) == true,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Review did not finish advancing after the new task was created.");
        InvokeNativeButton(HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants()
            .OfType<Button>().First(control => control.IsVisible &&
                AutomationProperties.GetAutomationId(control) == "FeedFinishReviewButton")));
        WaitUntil(() => HeadlessRuntime.Dispatch(() => !GetHeadlessMainWindowViewModel().Feed.IsReviewActive),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "The planning review overlay did not close.");
        await CaptureStoryScreenshotAsync("ux11-new-step-linked-to-source.png");
    }

    private void SelectStoryQuickPlanningValue(string buttonId, int optionIndex)
    {
        var button = HeadlessRuntime.Dispatch(() => FindNativeControlByAutomationId<DropDownButton>(buttonId));
        InvokeNativeButton(button);
        ClickRenderedStoryMenuItem(() => (button.Flyout as MenuFlyout)?.Items.OfType<MenuItem>().ElementAtOrDefault(optionIndex),
            $"Planning option {buttonId}/{optionIndex}");
        WaitUntil(() => HeadlessRuntime.Dispatch(() => !button.Flyout!.IsOpen), timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"Planning menu {buttonId} did not close after choosing an option.");
    }

    private void ClickRenderedStoryMenuItem(Func<MenuItem?> resolve, string description)
    {
        WaitUntil(() => HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            var current = resolve();
            var popup = TopLevel.GetTopLevel(current);
            popup?.UpdateLayout();
            return current is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true }
                && popup is not null && current.Bounds.Width > 0 && current.Bounds.Height > 0
                && current.TranslatePoint(new Point(current.Bounds.Width / 2, current.Bounds.Height / 2), popup) is not null;
        }), timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"{description} did not attach as an enabled menu item to a rendered popup.");
        HeadlessRuntime.Dispatch(() =>
        {
            // A live status refresh can rebuild MenuFlyout.Items between readiness and input.
            // Never keep a detached menu item from the previous dispatcher turn.
            var item = resolve() ?? throw new InvalidOperationException($"{description} is no longer available.");
            var popup = TopLevel.GetTopLevel(item)
                ?? throw new InvalidOperationException($"{description} has no rendered popup.");
            var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), popup)
                ?? throw new InvalidOperationException($"{description} has no pointer position.");
            popup.MouseDown(point, MouseButton.Left);
            popup.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        });
    }

    private void AddStoryReviewDraftParent(string query, string expectedTaskId)
    {
        const string prefix = "FeedReviewDraftParents";
        var add = HeadlessRuntime.Dispatch(() => FindNativeControlByAutomationId<Button>($"{prefix}AddButton"));
        InvokeNativeButton(add);
        var input = WaitUntil(() => HeadlessRuntime.Dispatch(() =>
                TryFindNativeControlByAutomationId<TextBox>($"{prefix}AddInput") is { IsVisible: true }
                    ? FindNativeControlByAutomationId<TextBox>($"{prefix}AddInput") : null),
            control => control is not null, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The review parent picker did not open.")!;
        HeadlessRuntime.Dispatch(() =>
        {
            input.Focus();
            input.Text = query;
            Dispatcher.UIThread.RunJobs();
        });
        var candidate = WaitUntil(() => HeadlessRuntime.Dispatch(() =>
                GetHeadlessMainWindowViewModel().Feed.ReviewParents?.Editor.Suggestions
                    .FirstOrDefault(item => item.Task.Id == expectedTaskId)),
            value => value is not null, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The review parent picker did not find the site project.")!;
        var confirm = HeadlessRuntime.Dispatch(() =>
        {
            var suggestions = FindNativeControlByAutomationId<ListBox>($"{prefix}Suggestions");
            suggestions.SelectedItem = candidate;
            Dispatcher.UIThread.RunJobs();
            return FindNativeControlByAutomationId<Button>($"{prefix}AddConfirmButton");
        });
        InvokeNativeButton(confirm);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.ReviewParents?.Parents
                .Any(parent => parent.Id == expectedTaskId) == true),
            timeout: TimeSpan.FromSeconds(10), timeoutMessage: "The project parent was not added to the task draft.");
    }

    private TextBox FindVisibleStoryTaskTextBox(string automationId) =>
        Session.Inner.MainWindow.GetVisualDescendants().OfType<TextBox>().First(control =>
            control.IsVisible && AutomationProperties.GetAutomationId(control) == automationId);

    private static IEnumerable<Unlimotion.ViewModel.TaskItemViewModel> FlattenStoryTask(
        Unlimotion.ViewModel.TaskWrapperViewModel wrapper)
    {
        yield return wrapper.TaskItem;
        foreach (var child in wrapper.SubTasks)
        foreach (var descendant in FlattenStoryTask(child))
            yield return descendant;
    }

    private void SelectStoryTaskFromTree(string automationId, string taskId)
    {
        var railId = automationId switch
        {
            "AllTasksTree" => "WorkspaceRailAllTasksButton",
            "UnlockedTree" => "WorkspaceRailUnlockedButton",
            "InProgressTree" => "WorkspaceRailInProgressButton",
            "CompletedTree" => "WorkspaceRailCompletedButton",
            "ArchivedTree" => "WorkspaceRailArchivedButton",
            _ => throw new InvalidOperationException($"No task-view rail entry for {automationId}.")
        };
        OpenStoryTaskView(railId);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants()
                .OfType<TreeView>().Any(tree => tree.IsEffectivelyVisible &&
                    AutomationProperties.GetAutomationId(tree) == automationId)),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"The {automationId} document did not become visible.");
        var title = WaitUntil(() => HeadlessRuntime.Dispatch(() =>
            {
                Dispatcher.UIThread.RunJobs();
                Session.Inner.MainWindow.UpdateLayout();
                var tree = Session.Inner.MainWindow.GetVisualDescendants().OfType<TreeView>().Single(control =>
                    control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) == automationId);
                // Expand the model path even when an ancestor's container is not
                // realized yet. Do not select or open another row to materialize it.
                foreach (var wrapper in tree.Items.OfType<Unlimotion.ViewModel.TaskWrapperViewModel>()
                             .SelectMany(FlattenStoryWrapper))
                    if (wrapper.TaskItem.Id != taskId && FlattenStoryWrapper(wrapper).Any(item => item.TaskItem.Id == taskId))
                        wrapper.IsExpanded = true;
                Session.Inner.MainWindow.UpdateLayout();
                var row = tree.GetVisualDescendants().OfType<TreeViewItem>().FirstOrDefault(control =>
                    control.DataContext is Unlimotion.ViewModel.TaskWrapperViewModel wrapper && wrapper.TaskItem.Id == taskId);
                row?.BringIntoView();
                Dispatcher.UIThread.RunJobs();
                Session.Inner.MainWindow.UpdateLayout();
                var candidate = tree.GetVisualDescendants().OfType<Control>().FirstOrDefault(control =>
                    control.IsEffectivelyVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0 &&
                    (AutomationProperties.GetAutomationId(control) == $"TaskTitle_{taskId}" ||
                     AutomationProperties.GetAutomationId(control) == "InlineTaskTitleTextBlock") &&
                    (control.DataContext is Unlimotion.ViewModel.TaskWrapperViewModel wrapper && wrapper.TaskItem.Id == taskId ||
                     control.DataContext is Unlimotion.ViewModel.TaskItemViewModel task && task.Id == taskId));
                candidate?.BringIntoView();
                Dispatcher.UIThread.RunJobs();
                Session.Inner.MainWindow.UpdateLayout();
                var center = candidate?.TranslatePoint(new Point(candidate.Bounds.Width / 2, candidate.Bounds.Height / 2),
                    Session.Inner.MainWindow);
                return center is { } point && point.X >= 0 && point.Y >= 0 &&
                    point.X < Session.Inner.MainWindow.Bounds.Width && point.Y < Session.Inner.MainWindow.Bounds.Height
                    ? candidate : null;
            }),
            control => control is not null, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"The visible {automationId} row did not expose the task title {taskId}.")!;
        HeadlessRuntime.Dispatch(() =>
        {
            title.BringIntoView();
            Session.Inner.MainWindow.UpdateLayout();
            var position = title.TranslatePoint(new Avalonia.Point(title.Bounds.Width / 2, title.Bounds.Height / 2),
                Session.Inner.MainWindow) ?? throw new InvalidOperationException("Task title has no window position.");
            Session.Inner.MainWindow.MouseDown(position, MouseButton.Left);
            Session.Inner.MainWindow.MouseUp(position, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        });
    }

    private static IEnumerable<Unlimotion.ViewModel.TaskWrapperViewModel> FlattenStoryWrapper(
        Unlimotion.ViewModel.TaskWrapperViewModel wrapper)
    {
        yield return wrapper;
        foreach (var child in wrapper.SubTasks)
        foreach (var descendant in FlattenStoryWrapper(child))
            yield return descendant;
    }

    private void OpenStoryTaskView(string railId)
    {
        RunParityUiAsync(async () =>
        {
            Session.Inner.MainWindow.Width = 1600;
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            Session.Inner.MainWindow.UpdateLayout();
        }).GetAwaiter().GetResult();
        var rail = WaitUntil(() => HeadlessRuntime.Dispatch(() => TryFindNativeControlByAutomationId<Button>(railId)),
            button => button is { IsEffectivelyVisible: true }, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"Expanded navigation did not expose {railId}.")!;
        InvokeNativeButton(rail);
    }

    private void WaitForVisibleTaskCard(string title) => WaitUntil(
        () => HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants().OfType<TextBox>()
            .FirstOrDefault(control => control.IsVisible &&
                AutomationProperties.GetAutomationId(control) == "CurrentTaskTitleTextBox")?.Text),
        value => value == title,
        timeout: TimeSpan.FromSeconds(10),
        timeoutMessage: $"The selected task card did not show '{title}'.");

    private void SelectVisibleTaskStatus(Unlimotion.Domain.TaskStatus status)
    {
        var picker = WaitUntil(() => HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            Session.Inner.MainWindow.UpdateLayout();
            var activeTaskId = GetHeadlessMainWindowViewModel().WorkspaceNavigation.ActivePane.ActiveTab?.CurrentLocation?.Id;
            return Session.Inner.MainWindow.GetVisualDescendants().OfType<TaskStatusPicker>().SingleOrDefault(control =>
                control.IsEffectivelyVisible && control.IsEffectivelyEnabled
                && control.Bounds.Width > 0 && control.Bounds.Height > 0
                && ReferenceEquals(TopLevel.GetTopLevel(control), Session.Inner.MainWindow)
                && control.GetVisualAncestors().OfType<Unlimotion.Views.TaskCardView>().Any(card => card.IsEffectivelyVisible)
                && (control.Task ?? control.DataContext as Unlimotion.ViewModel.TaskItemViewModel)?.Id == activeTaskId
                && AutomationProperties.GetAutomationId(control) == "CurrentTaskStatusButton");
        }), control => control is not null, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The active standalone card's status picker did not become ready.")!;
        HeadlessRuntime.Dispatch(() =>
        {
            var point = picker.TranslatePoint(new Point(picker.Bounds.Width / 2, picker.Bounds.Height / 2),
                Session.Inner.MainWindow) ?? throw new InvalidOperationException("The active status picker has no pointer position.");
            Session.Inner.MainWindow.MouseDown(point, MouseButton.Left);
            Session.Inner.MainWindow.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        });
        ClickRenderedStoryMenuItem(() => (picker.Flyout as MenuFlyout)?.Items.OfType<MenuItem>().SingleOrDefault(item =>
            AutomationProperties.GetAutomationId(item) == $"TaskStatusOption{status}"), $"Task status {status}");
        WaitUntil(() => HeadlessRuntime.Dispatch(() => !picker.Flyout!.IsOpen), timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The task status menu did not close after choosing a status.");
    }

    private void WaitForTaskStatus(string id, Unlimotion.Domain.TaskStatus status) => WaitUntil(
        () => HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().taskRepository!.Tasks.Items
            .Single(task => task.Id == id).Status),
        value => value == status,
        timeout: TimeSpan.FromSeconds(10),
        timeoutMessage: $"Task {id} did not persist status {status}.");

    private static void RequireRenderedStoryMode()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("UNLIMOTION_RENDERED_HEADLESS_SCREENSHOTS"),
                "1", StringComparison.Ordinal))
        {
            throw new TUnit.Core.Exceptions.SkipTestException(
                "Workspace user stories require UNLIMOTION_RENDERED_HEADLESS_SCREENSHOTS=1.");
        }
    }

    private async Task CaptureStoryScreenshotAsync(string fileName, double width = 1280, double height = 800)
    {
        await HeadlessRuntime.Session.Dispatch<bool>(async () =>
        {
            var window = Session.Inner.MainWindow;
            window.Width = width;
            window.Height = height;
            window.Show();
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            return true;
        }, CancellationToken.None).ConfigureAwait(false);
        var output = Path.Combine(
            Environment.GetEnvironmentVariable(WorkspaceScreenshotDirectoryVariable)
                ?? Path.Combine(AppContext.BaseDirectory, "artifacts", "headless-screenshots"),
            "workspace-user-stories", Guid.NewGuid().ToString("N"), fileName);
        var path = await Task.Run(() => Session.Inner.CaptureScreenshot(output)).ConfigureAwait(false);
        Console.WriteLine($"Headless screenshot: {path}");
        Console.WriteLine($"Screenshot continuation: thread={Environment.CurrentManagedThreadId}; " +
            $"ui={Dispatcher.UIThread.CheckAccess()}; context={SynchronizationContext.Current?.GetType().FullName ?? "<null>"}");
    }
}
