using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TUnit;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI;
using System.Reactive;
using System.Reactive.Threading.Tasks;
using System.Reflection;
using System.Text;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;
using Unlimotion.UiTests.Authoring.Tests;
using Unlimotion.UiTests.Headless.Infrastructure;

namespace Unlimotion.UiTests.Headless.Tests;

[InheritsTests]
public sealed class MainWindowHeadlessTests
    : FeedScenariosBase<MainWindowHeadlessTests.HeadlessRuntimeSession>
{
    private const string UnifiedEditorUseEditorMarker = "Unified editor version chosen by UseEditor";
    private const string UnifiedDiskUseEditorMarker = "Unified disk version rejected by UseEditor";
    private const string UnifiedEditorUseDiskMarker = "Unified editor version rejected by UseDisk";
    private const string UnifiedDiskUseDiskMarker = "Unified disk version chosen by UseDisk";
    private const string UnifiedEditorSaveBothMarker = "Unified editor version preserved by SaveBoth";
    private const string UnifiedDiskSaveBothMarker = "Unified disk version preserved by SaveBoth";
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private string? feedVaultPath;

    protected override HeadlessRuntimeSession LaunchSession()
    {
        var isStatusContract = IsStatusContractScenarioTest;
        var isFeed = IsFeedScenarioTest;
        var headlessSession = HeadlessRuntime.Session;
        var sessionThreadId = HeadlessRuntime.Dispatch(static () => Environment.CurrentManagedThreadId);
        var inner = DesktopAppSession.Launch(
                UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
                    isStatusContract
                        ? UnlimotionAutomationScenario.StatusContract
                        : isFeed
                            ? UnlimotionAutomationScenario.Feed
                        : UnlimotionAutomationScenario.Smoke,
                    language: isStatusContract ? StatusContractLanguage : null,
                    currentTaskId: isStatusContract ? StatusContractCurrentTaskId : null,
                    theme: isStatusContract ? StatusContractTheme : null,
                    feedVaultPrepared: path =>
                    {
                        feedVaultPath = path;
                        if (IsUnifiedFeedScenarioTest)
                        {
                            CompleteSeededPendingReview(path);
                        }
                    },
                    beforeViewModelInitialized: viewModel =>
                    {
                        if (!isFeed)
                        {
                            return;
                        }

                        viewModel.Feed.TaskCreationTarget =
                            new Unlimotion.ViewModel.Feed.TaskStorageFeedTaskCreationTarget(
                                () => viewModel.taskRepository);
                        viewModel.Feed.SetNotificationDispatcher(action =>
                        {
                            if (Environment.CurrentManagedThreadId == sessionThreadId)
                            {
                                action();
                                return;
                            }

                            _ = headlessSession.Dispatch(action, CancellationToken.None);
                        });
                    },
                    viewModelFactoryDispatcher: factory => HeadlessRuntime.Dispatch(factory),
                    headlessWindowCleanup: HeadlessSessionHooks.CloseWindow));
        return new HeadlessRuntimeSession(inner);
    }

    protected override MainWindowPage CreatePage(HeadlessRuntimeSession session)
    {
        return new MainWindowPage(new HeadlessControlResolver(session.Inner.MainWindow));
    }

    protected override StatusContractWindowSnapshot GetStatusContractWindowSnapshot()
    {
        return HeadlessRuntime.Dispatch(() =>
        {
            var window = Session.Inner.MainWindow;
            return new StatusContractWindowSnapshot(
                Environment.ProcessId,
                window.Title ?? string.Empty,
                window.Position.X,
                window.Position.Y,
                window.Width,
                window.Height);
        });
    }

    protected override bool SupportsStatusContractScreenshotCapture => false;

    protected override string ReadFeedVaultText(string relativePath)
    {
        var root = feedVaultPath
            ?? throw new InvalidOperationException("Feed automation vault was not captured.");
        try
        {
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (IOException)
        {
            // The production vault deliberately commits an existing file under FileShare.None.
            // Returning an empty observation lets UiWait retry after that very short commit window.
            return string.Empty;
        }
    }

    protected override FeedTaskGeometrySnapshot GetFeedTaskGeometrySnapshot()
    {
        var status = GetNativeControl<Control>(Page.FeedSeededTaskStatusPicker);
        var title = GetNativeControl<Control>(Page.FeedSeededTaskTitleButton);
        return HeadlessRuntime.Dispatch(() =>
        {
            var window = Session.Inner.MainWindow;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            return new FeedTaskGeometrySnapshot(
                GetBounds(status, window),
                GetBounds(title, window));
        });
    }

    protected override void PrepareFeedTaskReferenceSurface()
    {
        HeadlessRuntime.Dispatch(() =>
        {
            var window = Session.Inner.MainWindow;
            if (!window.IsVisible)
            {
                window.Show();
            }

            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        });
    }

    protected override FeedNarrowLayoutSnapshot GetFeedNarrowLayoutSnapshot()
    {
        var feedRoot = GetNativeControl<Control>(Page.FeedRoot);
        var feedModeButton = GetNativeControl<Control>(Page.FeedModeButton);
        var tasksModeButton = GetNativeControl<Control>(Page.TasksModeButton);
        var quickCapture = GetNativeControl<Control>(Page.FeedQuickCaptureTextBox);
        var reviewAction = GetNativeControl<Control>(Page.FeedStartReviewButton);
        return HeadlessRuntime.Dispatch(() =>
        {
            var window = Session.Inner.MainWindow;
            window.Width = 720;
            window.Height = 800;
            if (!window.IsVisible)
            {
                window.Show();
            }

            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var viewport = new FeedElementBounds(0, 0, window.Bounds.Width, window.Bounds.Height);
            var visibleFeedControls = feedRoot.GetVisualDescendants()
                .OfType<Control>()
                .Where(static control => control.IsVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0)
                .Select(control => GetBounds(control, window))
                .ToArray();
            var hasHorizontalOverflow = visibleFeedControls.Any(bounds =>
                bounds.Left < viewport.Left - 1 || bounds.Right > viewport.Right + 1);

            return new FeedNarrowLayoutSnapshot(
                viewport,
                GetBounds(feedModeButton, window),
                GetBounds(tasksModeButton, window),
                GetBounds(quickCapture, window),
                GetBounds(reviewAction, window),
                hasHorizontalOverflow);
        });
    }

    private static FeedElementBounds GetBounds(Control control, Visual relativeTo)
    {
        var transform = control.TransformToVisual(relativeTo)
            ?? throw new InvalidOperationException(
                $"Control '{AutomationProperties.GetAutomationId(control)}' was not connected to the Feed visual tree.");
        var corners = new[]
        {
            transform.Transform(default),
            transform.Transform(new Point(control.Bounds.Width, 0)),
            transform.Transform(new Point(0, control.Bounds.Height)),
            transform.Transform(new Point(control.Bounds.Width, control.Bounds.Height))
        };
        var left = corners.Min(static point => point.X);
        var top = corners.Min(static point => point.Y);
        var right = corners.Max(static point => point.X);
        var bottom = corners.Max(static point => point.Y);
        return new FeedElementBounds(left, top, right - left, bottom - top);
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Feed_unified_capture_review_task_parent_status_navigation_search_and_conflicts()
    {
        var todayPath = UnlimotionAutomationScenarioData.GetFeedDailyRelativePath(
            DateOnly.FromDateTime(DateTime.Now));

        OpenFeedForUnifiedScenario();
        Page.FeedQuickCaptureTextBox.Enter(UnlimotionAutomationScenarioData.FeedQuickCaptureMarker);
        Page.FeedCaptureButton.Invoke();
        WaitUntil(
            () => ReadFeedVaultText(todayPath),
            text => text.Contains(UnlimotionAutomationScenarioData.FeedQuickCaptureMarker, StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Unified Feed quick capture was not written to today's Markdown file.");
        WaitUntil(
            () => Page.FeedQuickCaptureTextBox.Text,
            string.IsNullOrEmpty,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Unified Feed quick capture buffer was not cleared after refresh.");
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() =>
            {
                var feed = GetHeadlessMainWindowViewModel().Feed;
                return feed.PendingReviewBlocks > 0 && feed.CanStartReview;
            }),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Unified Feed review queue did not include the just-captured plain block.");

        var startReview = WaitForHeadlessControl(
            () => Page.FeedStartReviewButton,
            "Unified Feed flow did not expose the review action after capture.");
        WaitUntil(
            () => startReview.IsEnabled,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Unified Feed review action stayed disabled after queue refresh.");
        startReview.Invoke();
        try
        {
            WaitUntil(
                ObserveUnifiedReviewState,
                static state => state.IsSelectionVisible,
                timeout: TimeSpan.FromSeconds(10),
                timeoutMessage: "Unified Feed review command did not activate a selection.");
        }
        catch (TimeoutException exception)
        {
            var state = ObserveUnifiedReviewState();
            throw new TimeoutException(
                $"Unified Feed review command state: {state}.",
                exception);
        }

        _ = WaitForHeadlessControl(
            () => Page.FeedReviewPanel,
            "Unified Feed flow did not expose its inline review panel.");
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() =>
                GetHeadlessMainWindowViewModel().Feed.Days
                    .SelectMany(static day => day.MarkdownEditor.Blocks)
                    .Any(block => block.IsReviewAnchor
                        && block.Block.Raw.Contains(
                            UnlimotionAutomationScenarioData.FeedQuickCaptureMarker,
                            StringComparison.Ordinal))),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Unified Feed review did not highlight the just-captured Live Preview block.");

        var createTask = WaitForHeadlessControl(
            () => Page.FeedReviewCreateTaskButton,
            "Unified Feed review did not expose task conversion.");
        WaitUntil(
            () => createTask.IsEnabled,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Unified Feed task conversion stayed disabled.");
        createTask.Invoke();
        _ = WaitForHeadlessControl(
            () => Page.FeedTaskInlineSurface,
            "Unified Feed task conversion did not expose the inline task surface.");
        Unlimotion.ViewModel.TaskItemViewModel createdTask;
        try
        {
            createdTask = WaitUntil(
                ObserveUnifiedCreatedTask,
                static task => task is not null,
                timeout: TimeSpan.FromSeconds(10),
                timeoutMessage: "Unified Feed task conversion did not create a repository task.")!;
        }
        catch (TimeoutException exception)
        {
            var state = HeadlessRuntime.Dispatch(() =>
            {
                var owner = GetHeadlessMainWindowViewModel();
                var reference = owner.Feed.CreatedTaskReference;
                var tasks = owner.taskRepository?.Tasks.Items
                    .Select(static task => $"{task.Id}:{task.Title}")
                    .OrderBy(static value => value, StringComparer.Ordinal)
                    .ToArray() ?? [];
                return $"busy={owner.Feed.IsBusy}; error={owner.Feed.ErrorMessage}; " +
                       $"hasCreated={owner.Feed.HasCreatedTask}; reference={reference?.TaskId}:{reference?.FallbackTitle}; " +
                       $"resolved={reference?.IsResolved}; tasks=[{string.Join(" | ", tasks)}]";
            });
            throw new TimeoutException($"Unified Feed task conversion state: {state}.", exception);
        }

        WaitUntil(
            () => HeadlessRuntime.Dispatch(() =>
            {
                var feed = GetHeadlessMainWindowViewModel().Feed;
                return feed.HasCreatedTask && !feed.IsBusy;
            }),
            static isTerminal => isTerminal,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Unified Feed task conversion did not reach its terminal linked-task state.");

        var terminalMarkdownBefore = ReadFeedVaultText(todayPath);
        var terminalTaskState = await ExerciseUnifiedTerminalTaskEditingGuardsAsync(createdTask);

        var relationControl = AssignUnifiedTaskParentThroughFeedControl(createdTask.Id);
        ChangeUnifiedTaskStatusThroughFeedPicker(Unlimotion.Domain.TaskStatus.Prepared);

        using (Assert.Multiple())
        {
            await Assert.That(createdTask.Title)
                .IsEqualTo(UnlimotionAutomationScenarioData.FeedQuickCaptureMarker);
            await Assert.That(createdTask.Parents)
                .Contains(UnlimotionAutomationScenarioData.FeedCurrentTaskId);
            await Assert.That(createdTask.Status).IsEqualTo(Unlimotion.Domain.TaskStatus.Prepared);
            await Assert.That(IsUnifiedParentRendered(relationControl)).IsTrue();
            await Assert.That(terminalTaskState.HasCreatedTask).IsTrue();
            await Assert.That(terminalTaskState.CanModifyReviewSource).IsFalse();
            await Assert.That(terminalTaskState.LegacyActionsVisible).IsFalse();
            await Assert.That(terminalTaskState.DraftGoalVisible).IsFalse();
            await Assert.That(terminalTaskState.CreatedGoalVisible).IsTrue();
            await Assert.That(terminalTaskState.CreatedAreasVisible).IsTrue();
            await Assert.That(terminalTaskState.AreaMutationApplied).IsTrue();
            await Assert.That(terminalTaskState.TaskCount).IsEqualTo(2);
            await Assert.That(ReadFeedVaultText(todayPath)).IsEqualTo(terminalMarkdownBefore);
            await Assert.That(ReadFeedVaultText("Проекты/terminal-link-guard.md")).IsEqualTo(string.Empty);
        }

        RaiseNativeClick(GetNativeControl<Button>(Page.FeedTaskTitleButton));
        WaitUntil(
            () => Page.TasksModeButton.IsChecked == true,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Unified Feed task title did not navigate to Tasks.");
        await UiAssert.TextEqualsAsync(
            () => Page.CurrentTaskTitleTextBox.Text,
            UnlimotionAutomationScenarioData.FeedQuickCaptureMarker,
            TimeSpan.FromSeconds(10));
        OpenFeedForUnifiedScenario();
        Page.FeedSearchBox.Enter(UnlimotionAutomationScenarioData.FeedQuickCaptureMarker);
        var searchResults = WaitUntil(
            () => TryResolveHeadless(() => Page.FeedSearchResultsList.Items) ?? [],
            items => items.Any(item => item.Text?.Contains(
                UnlimotionAutomationScenarioData.FeedQuickCaptureMarker,
                StringComparison.Ordinal) == true),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Unified Feed search did not find the converted capture.");
        Page.FeedSearchBox.Enter(string.Empty);
        WaitUntil(
            () => TryResolveHeadless(() => Page.FeedChronologyList.Items)?.Count ?? 0,
            count => count >= 2,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Unified Feed search clear did not restore chronology.");

        ResolveUnifiedDirtyConflict(
            todayPath,
            UnlimotionAutomationScenarioData.FeedNewestMarker,
            UnifiedEditorUseEditorMarker,
            UnifiedDiskUseEditorMarker,
            UnifiedConflictAction.UseEditor);
        ResolveUnifiedDirtyConflict(
            todayPath,
            UnifiedEditorUseEditorMarker,
            UnifiedEditorUseDiskMarker,
            UnifiedDiskUseDiskMarker,
            UnifiedConflictAction.UseDisk);
        var conflictCopyText = ResolveUnifiedDirtyConflict(
            todayPath,
            UnifiedDiskUseDiskMarker,
            UnifiedEditorSaveBothMarker,
            UnifiedDiskSaveBothMarker,
            UnifiedConflictAction.SaveBoth);
        using (Assert.Multiple())
        {
            await Assert.That(searchResults.Count).IsGreaterThanOrEqualTo(1);
            await Assert.That(ReadFeedVaultText(todayPath)).Contains(UnifiedDiskSaveBothMarker);
            await Assert.That(ReadFeedVaultText(todayPath)).DoesNotContain(UnifiedEditorSaveBothMarker);
            await Assert.That(conflictCopyText).Contains(UnifiedEditorSaveBothMarker);
            await Assert.That(conflictCopyText).DoesNotContain(UnifiedDiskSaveBothMarker);
        }
    }

    private void OpenFeedForUnifiedScenario()
    {
        Page.FeedModeButton.IsChecked = true;
        WaitUntil(
            () => Page.FeedModeButton.IsChecked == true,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Unified Feed workspace did not become selected.");
        _ = WaitForHeadlessControl(
            () => Page.FeedRoot,
            "Unified Feed root did not become available.");
    }

    private Unlimotion.ViewModel.TaskItemViewModel? ObserveUnifiedCreatedTask()
    {
        return HeadlessRuntime.Dispatch(() =>
        {
            var viewModel = GetHeadlessMainWindowViewModel();
            return viewModel.taskRepository?.Tasks.Items.FirstOrDefault(task => string.Equals(
                task.Title,
                UnlimotionAutomationScenarioData.FeedQuickCaptureMarker,
                StringComparison.Ordinal));
        });
    }

    private async Task<UnifiedTerminalTaskState> ExerciseUnifiedTerminalTaskEditingGuardsAsync(
        Unlimotion.ViewModel.TaskItemViewModel createdTask)
    {
        var guardedCommands = HeadlessRuntime.Dispatch(() =>
        {
            var feed = GetHeadlessMainWindowViewModel().Feed;
            feed.ReviewNoteTitle = "terminal-link-guard";
            feed.ReviewNoteFolder = "Проекты";
            if (!feed.HasCreatedTask)
            {
                throw new InvalidOperationException("The created-task terminal state was lost before guarded commands ran.");
            }

            return new (string Name, ReactiveCommand<Unit, Unit> Command)[]
            {
                (nameof(feed.LeaveReviewCommand), RequireReactiveCommand(feed.LeaveReviewCommand)),
                (nameof(feed.SkipReviewCommand), RequireReactiveCommand(feed.SkipReviewCommand)),
                (nameof(feed.AssignReviewAreaCommand), RequireReactiveCommand(feed.AssignReviewAreaCommand)),
                (nameof(feed.CreateTaskCommand), RequireReactiveCommand(feed.CreateTaskCommand)),
                (nameof(feed.CreateNoteCommand), RequireReactiveCommand(feed.CreateNoteCommand)),
                (nameof(feed.MoveToTodayCommand), RequireReactiveCommand(feed.MoveToTodayCommand))
            };
        });

        foreach (var (name, command) in guardedCommands)
        {
            await command.Execute().ToTask().WaitAsync(TimeSpan.FromSeconds(10));
            var stillTerminal = HeadlessRuntime.Dispatch(() => GetHeadlessMainWindowViewModel().Feed.HasCreatedTask);
            if (!stillTerminal)
            {
                throw new InvalidOperationException($"The created-task terminal state was lost after {name}.");
            }
        }

        PrepareFeedTaskReferenceSurface();
        return HeadlessRuntime.Dispatch(() =>
        {
            var feed = GetHeadlessMainWindowViewModel().Feed;
            feed.ExpandSelectionUpCommand.Execute(null);
            feed.ExpandSelectionDownCommand.Execute(null);
            feed.ShrinkSelectionUpCommand.Execute(null);
            feed.ShrinkSelectionDownCommand.Execute(null);
            if (!feed.HasCreatedTask)
            {
                throw new InvalidOperationException("The created-task terminal state was lost after selection commands.");
            }
            var area = feed.ReviewTaskAreas.First();
            var selected = !area.IsSelected;
            area.IsSelected = selected;
            Dispatcher.UIThread.RunJobs();
            Session.Inner.MainWindow.UpdateLayout();

            bool IsVisible(string automationId) =>
                TryFindNativeControlByAutomationId<Control>(automationId)?.IsEffectivelyVisible == true;
            bool IsDeclaredVisible(string automationId) =>
                TryFindNativeControlByAutomationId<Control>(automationId)?.IsVisible == true;
            var createdGoal = TryFindNativeControlByAutomationId<Control>("FeedCreatedTaskGoalToggle");
            var createdAreas = TryFindNativeControlByAutomationId<Control>("FeedCreatedTaskAreas");
            if (createdGoal is null || createdAreas is null)
            {
                var owner = GetHeadlessMainWindowViewModel();
                var descendants = Session.Inner.MainWindow
                    .GetVisualDescendants()
                    .OfType<Control>()
                    .ToArray();
                var feedIds = descendants
                    .Select(AutomationProperties.GetAutomationId)
                    .Where(static id => id?.StartsWith("Feed", StringComparison.Ordinal) == true)
                    .OrderBy(static id => id, StringComparer.Ordinal)
                    .ToArray();
                throw new InvalidOperationException(
                    $"Created-task controls were missing from the visual tree. " +
                    $"mode={owner.SelectedWorkspaceMode}; windowVisible={Session.Inner.MainWindow.IsVisible}; " +
                    $"descendants={descendants.Length}; Feed IDs: {string.Join(", ", feedIds)}");
            }
            var legacyActionsVisible = new[]
            {
                "FeedReviewExpandUpButton",
                "FeedReviewExpandDownButton",
                "FeedReviewShrinkUpButton",
                "FeedReviewShrinkDownButton",
                "FeedReviewAreaPicker",
                "FeedReviewAssignAreaButton",
                "FeedReviewLeaveButton",
                "FeedReviewSkipButton",
                "FeedReviewMoveTodayButton",
                "FeedReviewCreateTaskButton",
                "FeedReviewNoteTitleBox",
                "FeedReviewNoteFolderBox",
                "FeedReviewCreateNoteButton"
            }.Any(IsVisible);
            var taskCount = GetHeadlessMainWindowViewModel().taskRepository?.Tasks.Items.Count() ?? 0;
            return new UnifiedTerminalTaskState(
                feed.HasCreatedTask,
                feed.CanModifyReviewSource,
                legacyActionsVisible,
                IsVisible("FeedReviewTaskGoalToggle"),
                IsDeclaredVisible("FeedCreatedTaskGoalToggle"),
                IsDeclaredVisible("FeedCreatedTaskAreas"),
                createdTask.AreaIds.Contains(area.Area.Identity) == selected,
                taskCount);
        });

        static ReactiveCommand<Unit, Unit> RequireReactiveCommand(System.Windows.Input.ICommand command) =>
            command as ReactiveCommand<Unit, Unit>
            ?? throw new InvalidOperationException("A guarded Feed command was not a ReactiveCommand.");
    }

    private UnifiedReviewState ObserveUnifiedReviewState()
    {
        return HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            var feed = GetHeadlessMainWindowViewModel().Feed;
            return new UnifiedReviewState(
                feed.IsReviewActive,
                feed.IsReviewSelectionVisible,
                feed.IsBusy,
                feed.PendingReviewBlocks,
                feed.CanStartReview,
                feed.CurrentReview?.SelectedMarkdown,
                feed.ErrorMessage);
        });
    }

    private Unlimotion.Views.TaskRelationsControl AssignUnifiedTaskParentThroughFeedControl(string createdTaskId)
    {
        PrepareFeedTaskReferenceSurface();
        var addButton = WaitForHeadlessControl(
            () => Page.FeedTaskParentsRelationAddButton,
            "Unified Feed inline task did not expose the reusable parent relation control.");
        var nativeAddButton = GetNativeControl<Button>(addButton);
        var relationControl = HeadlessRuntime.Dispatch(() => nativeAddButton
            .GetLogicalAncestors()
            .OfType<Unlimotion.Views.TaskRelationsControl>()
            .FirstOrDefault())
            ?? throw new InvalidOperationException("Unified Feed parent add button had no reusable relation-control ancestor.");
        try
        {
            WaitUntil(
                () => HeadlessRuntime.Dispatch(() => relationControl.TargetTask?.Id),
                id => string.Equals(id, createdTaskId, StringComparison.Ordinal),
                timeout: TimeSpan.FromSeconds(10),
                timeoutMessage: "Unified Feed reusable relation control did not bind the converted task target.");
        }
        catch (TimeoutException exception)
        {
            var state = HeadlessRuntime.Dispatch(() =>
            {
                var owner = GetHeadlessMainWindowViewModel();
                var reference = owner.Feed.CreatedTaskReference;
                return $"attached={relationControl.IsAttachedToVisualTree()}; " +
                       $"visible={relationControl.IsEffectivelyVisible}; dataContextIsFeed={ReferenceEquals(relationControl.DataContext, owner.Feed)}; " +
                       $"ownerIsMain={ReferenceEquals(relationControl.Owner, owner)}; target={relationControl.TargetTask?.Id}; " +
                       $"reference={reference?.TaskId}; resolved={reference?.IsResolved}; hasCreated={owner.Feed.HasCreatedTask}";
            });
            throw new TimeoutException($"Unified Feed relation binding state: {state}.", exception);
        }
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => relationControl.IsAttachedToVisualTree()),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Unified Feed reusable relation control was not attached to the visual tree.");
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() => relationControl.IsEffectivelyVisible),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Unified Feed reusable relation control stayed visually hidden after conversion.");
        RaiseNativeClick(nativeAddButton);
        try
        {
            WaitUntil(
                () => HeadlessRuntime.Dispatch(() => relationControl.IsEditorOpen),
                timeout: TimeSpan.FromSeconds(10),
                timeoutMessage: "Unified Feed reusable relation control did not visibly open its parent editor.");
        }
        catch (TimeoutException exception)
        {
            var state = HeadlessRuntime.Dispatch(() =>
            {
                var owner = GetHeadlessMainWindowViewModel();
                var editor = owner.CurrentRelationEditor;
                var allControls = TopLevel.GetTopLevel(nativeAddButton)?
                    .GetVisualDescendants()
                    .OfType<Unlimotion.Views.TaskRelationsControl>()
                    .Select(control =>
                        $"{control.AutomationIdPrefix}:{control.TargetTask?.Id}:open={control.IsEditorOpen}:visible={control.IsEffectivelyVisible}")
                    .ToArray() ?? [];
                return $"editorOpen={editor.IsOpen}; editorTarget={editor.TargetTaskId}; editorKind={editor.Kind}; " +
                       $"ownerMatches={ReferenceEquals(relationControl?.Owner, owner)}; target={relationControl?.TargetTask?.Id}; " +
                       $"buttonVisible={nativeAddButton.IsEffectivelyVisible}; buttonEnabled={nativeAddButton.IsEnabled}; " +
                       $"controls=[{string.Join(" | ", allControls)}]";
            });
            throw new TimeoutException($"Unified Feed relation editor state: {state}.", exception);
        }

        var nativeInput = WaitUntil(
            () => HeadlessRuntime.Dispatch(() => relationControl
                .GetVisualDescendants()
                .OfType<TextBox>()
                .FirstOrDefault(input => string.Equals(
                    AutomationProperties.GetAutomationId(input),
                    $"{Unlimotion.Views.TaskRelationsControl.FeedAutomationIdPrefix}AddInput",
                    StringComparison.Ordinal)
                    && input.IsEffectivelyVisible
                    && input.IsEnabled)),
            static input => input is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Unified Feed parent relation editor did not render its input.")!;
        HeadlessRuntime.Dispatch(() =>
        {
            nativeInput.Focus();
            nativeInput.Text = UnlimotionAutomationScenarioData.FeedCurrentTaskTitle;
            Dispatcher.UIThread.RunJobs();
        });
        var nativeSuggestions = WaitUntil(
            () => HeadlessRuntime.Dispatch(() => relationControl
                .GetVisualDescendants()
                .OfType<ListBox>()
                .FirstOrDefault(list => string.Equals(
                    AutomationProperties.GetAutomationId(list),
                    $"{Unlimotion.Views.TaskRelationsControl.FeedAutomationIdPrefix}Suggestions",
                    StringComparison.Ordinal)
                    && list.IsEffectivelyVisible
                    && list.IsEnabled)),
            static list => list is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Unified Feed parent relation suggestions did not render.")!;
        Unlimotion.ViewModel.TaskRelationCandidateViewModel candidate;
        try
        {
            candidate = WaitUntil(
                () => HeadlessRuntime.Dispatch(() =>
                    GetHeadlessMainWindowViewModel().CurrentRelationEditor.Suggestions
                        .FirstOrDefault(suggestion => string.Equals(
                            suggestion.Task.Id,
                            UnlimotionAutomationScenarioData.FeedCurrentTaskId,
                            StringComparison.Ordinal))),
                static value => value is not null,
                timeout: TimeSpan.FromSeconds(10),
                timeoutMessage: "Unified Feed parent relation search did not return the seeded task.")!;
        }
        catch (TimeoutException exception)
        {
            var state = HeadlessRuntime.Dispatch(() =>
            {
                var owner = GetHeadlessMainWindowViewModel();
                var editor = owner.CurrentRelationEditor;
                var createdReference = owner.Feed.CreatedTaskReference;
                var suggestionsState = string.Join(
                    ", ",
                    editor.Suggestions.Select(static suggestion =>
                        $"{suggestion.Task.Id}:{suggestion.Title}"));
                var repositoryState = string.Join(
                    ", ",
                    owner.taskRepository?.Tasks.Items.Select(static task =>
                        $"{task.Id}:{task.Title}") ?? []);
                return $"open={editor.IsOpen}; target={editor.TargetTaskId}; kind={editor.Kind}; " +
                       $"query='{editor.Query}'; input='{nativeInput.Text}'; " +
                       $"reference={createdReference?.TaskId}; resolved={createdReference?.IsResolved}; " +
                       $"directResolver={owner.Feed.TaskResolver?.Invoke(createdReference?.TaskId ?? string.Empty)?.Id}; " +
                       $"controlTarget={relationControl?.TargetTask?.Id}; controlOpen={relationControl?.IsEditorOpen}; " +
                       $"suggestions=[{suggestionsState}]; tasks=[{repositoryState}]";
            });
            throw new TimeoutException(
                $"Unified Feed parent relation state: {state}.",
                exception);
        }

        HeadlessRuntime.Dispatch(() =>
        {
            nativeSuggestions.SelectedItem = candidate;
            Dispatcher.UIThread.RunJobs();
        });

        var confirmButton = WaitForHeadlessControl(
            () => Page.FeedTaskParentsRelationAddConfirmButton,
            "Unified Feed parent relation editor did not expose confirmation.");
        WaitUntil(
            () => confirmButton.IsEnabled,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Unified Feed parent relation confirmation stayed disabled.");
        confirmButton.Invoke();
        WaitUntil(
            () => ObserveUnifiedCreatedTask()?.Parents.Contains(
                UnlimotionAutomationScenarioData.FeedCurrentTaskId) == true,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Unified Feed parent relation was not applied to the converted task.");
        try
        {
            WaitUntil(
                () => IsUnifiedParentRendered(relationControl),
                timeout: TimeSpan.FromSeconds(10),
                timeoutMessage: "Unified Feed parent relation tree did not render the selected parent.");
        }
        catch (TimeoutException exception)
        {
            var state = HeadlessRuntime.Dispatch(() =>
            {
                var owner = GetHeadlessMainWindowViewModel();
                var repositoryTask = owner.taskRepository?.Tasks.Items.FirstOrDefault(task => string.Equals(
                    task.Id,
                    relationControl?.TargetTask?.Id,
                    StringComparison.Ordinal));
                return $"target={relationControl?.TargetTask?.Id}; " +
                       $"attached={relationControl?.IsAttachedToVisualTree()}; ownerIsMain={ReferenceEquals(relationControl?.Owner, owner)}; " +
                       $"editorOpen={relationControl?.IsEditorOpen}; " +
                       $"sameRepositoryTask={ReferenceEquals(relationControl?.TargetTask, repositoryTask)}; " +
                       $"parentIds=[{string.Join(",", relationControl?.TargetTask?.Parents ?? [])}]; " +
                       $"parentTasks=[{string.Join(",", relationControl?.TargetTask?.ParentsTasks.Select(static task => task.Id) ?? [])}]; " +
                       $"root=[{string.Join(",", relationControl?.ParentsRoot?.SubTasks.Select(static task => task.Id) ?? [])}]";
            });
            throw new TimeoutException($"Unified Feed relation projection state: {state}.", exception);
        }

        return relationControl;
    }

    private bool IsUnifiedParentRendered(Unlimotion.Views.TaskRelationsControl? relationControl)
    {
        if (relationControl is null)
        {
            return false;
        }

        return HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return relationControl.ParentsRoot?.SubTasks.Any(item => string.Equals(
                item.Id,
                UnlimotionAutomationScenarioData.FeedCurrentTaskId,
                StringComparison.Ordinal)) == true;
        });
    }

    private void ChangeUnifiedTaskStatusThroughFeedPicker(Unlimotion.Domain.TaskStatus status)
    {
        var statusPicker = GetNativeControl<TaskStatusPicker>(
            WaitForHeadlessControl(
                () => Page.FeedTaskStatusPicker,
                "Unified Feed inline task did not expose TaskStatusPicker."));
        InvokeNativeButton(statusPicker);
        HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            var flyout = statusPicker.Flyout as MenuFlyout
                ?? throw new InvalidOperationException("Unified Feed TaskStatusPicker did not create its MenuFlyout.");
            var option = flyout.Items
                .OfType<MenuItem>()
                .Single(item => string.Equals(
                    AutomationProperties.GetAutomationId(item),
                    $"TaskStatusOption{status}",
                    StringComparison.Ordinal));
            if (!option.IsEnabled)
            {
                throw new InvalidOperationException($"Unified Feed status option '{status}' was disabled.");
            }

            option.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, option));
            Dispatcher.UIThread.RunJobs();
        });
        WaitUntil(
            () => ObserveUnifiedCreatedTask()?.Status == status,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"Unified Feed TaskStatusPicker did not apply status '{status}'.");
    }

    private string? ResolveUnifiedDirtyConflict(
        string relativePath,
        string currentBlockText,
        string editorBlockText,
        string diskBlockText,
        UnifiedConflictAction action)
    {
        BeginUnifiedDirtyEdit(currentBlockText, editorBlockText);
        ReplaceExternalFeedBlock(relativePath, currentBlockText, diskBlockText);

        var conflictRoot = WaitUntil(
            () => HeadlessRuntime.Dispatch(() =>
            {
                Dispatcher.UIThread.RunJobs();
                var control = TryFindNativeControlByAutomationId<Control>("FeedDocumentConflictRoot");
                return control is { IsVisible: true } ? control : null;
            }),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"Unified Feed did not show a dirty conflict for '{action}'.")!;
        var conflictView = HeadlessRuntime.Dispatch(() => conflictRoot
            .GetLogicalAncestors()
            .OfType<Unlimotion.Views.FeedDocumentConflict>()
            .FirstOrDefault())
            ?? throw new InvalidOperationException(
                $"Unified Feed conflict '{action}' had no FeedDocumentConflict ancestor.");
        _ = WaitUntil(
            () => HeadlessRuntime.Dispatch(() =>
            {
                var textBoxes = conflictView.GetLogicalDescendants().OfType<TextBox>().ToArray();
                var editor = textBoxes.FirstOrDefault(control => string.Equals(
                    AutomationProperties.GetAutomationId(control),
                    "FeedConflictEditorText",
                    StringComparison.Ordinal));
                var disk = textBoxes.FirstOrDefault(control => string.Equals(
                    AutomationProperties.GetAutomationId(control),
                    "FeedConflictDiskText",
                    StringComparison.Ordinal));
                return editor is { IsVisible: true }
                       && disk is { IsVisible: true }
                    ? new UnifiedConflictVersions(editor.Text ?? string.Empty, disk.Text ?? string.Empty)
                    : null;
            }),
            versions => versions is not null
                        && versions.EditorText.Contains(editorBlockText, StringComparison.Ordinal)
                        && versions.DiskText.Contains(diskBlockText, StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"Unified Feed conflict '{action}' did not expose the expected editor and disk versions.");
        var actionAutomationId = action switch
        {
            UnifiedConflictAction.UseEditor => "FeedConflictUseEditorButton",
            UnifiedConflictAction.UseDisk => "FeedConflictUseDiskButton",
            UnifiedConflictAction.SaveBoth => "FeedConflictSaveBothButton",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
        var actionButton = WaitUntil(
            () => HeadlessRuntime.Dispatch(() =>
            {
                var button = conflictView
                    .GetLogicalDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(control => string.Equals(
                        AutomationProperties.GetAutomationId(control),
                        actionAutomationId,
                        StringComparison.Ordinal));
                return button is { IsVisible: true, IsEnabled: true } ? button : null;
            }),
            static button => button is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"Unified Feed conflict did not expose the enabled '{action}' action.")!;
        InvokeNativeButton(actionButton);

        WaitUntil(
            () => !IsUnifiedDocumentConflictOpen(),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"Unified Feed conflict '{action}' did not close after resolution.");
        var expectedMainText = action == UnifiedConflictAction.UseEditor
            ? editorBlockText
            : diskBlockText;
        var rejectedMainText = action == UnifiedConflictAction.UseEditor
            ? diskBlockText
            : editorBlockText;
        var resolvedMain = WaitUntil(
            () => ReadFeedVaultText(relativePath),
            text => text.Contains(expectedMainText, StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"Unified Feed conflict '{action}' did not preserve the selected main-file version.");
        if (resolvedMain.Contains(rejectedMainText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unified Feed conflict '{action}' left both alternatives in the main Markdown file.");
        }

        return action == UnifiedConflictAction.SaveBoth
            ? WaitForUnifiedConflictCopy(relativePath, editorBlockText)
            : null;
    }

    private void BeginUnifiedDirtyEdit(string currentBlockText, string editorBlockText)
    {
        var blockIds = WaitUntil(
            () => ObserveUnifiedBlockAutomationIds(currentBlockText),
            static ids => ids is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"Unified Feed could not find the Live Preview block '{currentBlockText}'.")!.Value;
        HeadlessRuntime.Dispatch(() =>
        {
            var preview = FindNativeControlByAutomationId<Unlimotion.Views.MarkdownBlockPreviewControl>(
                blockIds.PreviewAutomationId);
            preview.Focus();
            var keyDown = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.F2,
                PhysicalKey = PhysicalKey.F2,
                Source = preview
            };
            preview.RaiseEvent(keyDown);
            if (!keyDown.Handled)
            {
                throw new InvalidOperationException(
                    $"Unified Feed Live Preview block '{currentBlockText}' did not handle F2.");
            }

            Dispatcher.UIThread.RunJobs();
        });
        var editor = WaitUntil(
            () => HeadlessRuntime.Dispatch(() =>
            {
                var control = TryFindNativeControlByAutomationId<TextBox>(blockIds.EditorAutomationId);
                return control is { IsVisible: true, IsEnabled: true } ? control : null;
            }),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"Unified Feed Live Preview block '{currentBlockText}' did not enter edit mode.")!;
        HeadlessRuntime.Dispatch(() =>
        {
            editor.Focus();
            editor.Text = editorBlockText;
            Dispatcher.UIThread.RunJobs();
        });
        WaitUntil(
            () => IsUnifiedBlockDirty(editorBlockText),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"Unified Feed Live Preview did not register dirty text '{editorBlockText}'.");
    }

    private UnifiedBlockAutomationIds? ObserveUnifiedBlockAutomationIds(string blockText)
    {
        UnifiedBlockAutomationIds? result = null;
        HeadlessRuntime.Dispatch(() =>
        {
            var viewModel = GetHeadlessMainWindowViewModel();
            var today = DateOnly.FromDateTime(DateTime.Now);
            var block = viewModel.Feed.Days
                .FirstOrDefault(day => day.Date == today)?
                .MarkdownEditor.Blocks
                .FirstOrDefault(candidate => candidate.Block.Raw.Contains(blockText, StringComparison.Ordinal));
            if (block is not null)
            {
                result = new UnifiedBlockAutomationIds(block.PreviewAutomationId, block.EditorAutomationId);
            }
        });
        return result;
    }

    private bool IsUnifiedBlockDirty(string editorBlockText)
    {
        return HeadlessRuntime.Dispatch(() =>
        {
            var viewModel = GetHeadlessMainWindowViewModel();
            return viewModel.Feed.Days
                .SelectMany(static day => day.MarkdownEditor.Blocks)
                .Any(block => block.IsEditing
                    && block.IsDirty
                    && string.Equals(block.EditorText, editorBlockText, StringComparison.Ordinal));
        });
    }

    private bool IsUnifiedDocumentConflictOpen()
    {
        return HeadlessRuntime.Dispatch(() =>
            GetHeadlessMainWindowViewModel().Feed.DocumentConflict?.IsOpen == true);
    }

    private void ReplaceExternalFeedBlock(string relativePath, string currentBlockText, string diskBlockText)
    {
        var fullPath = GetFeedVaultFullPath(relativePath);
        WaitUntil(
            () =>
            {
                try
                {
                    var current = File.ReadAllText(fullPath);
                    if (!current.Contains(currentBlockText, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    File.WriteAllText(
                        fullPath,
                        current.Replace(currentBlockText, diskBlockText, StringComparison.Ordinal),
                        Utf8WithoutBom);
                    return true;
                }
                catch (IOException)
                {
                    return false;
                }
            },
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"Unified Feed external edit could not replace '{currentBlockText}'.");
    }

    private string WaitForUnifiedConflictCopy(string relativePath, string expectedEditorText)
    {
        var fullPath = GetFeedVaultFullPath(relativePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Unified Feed daily file had no parent directory.");
        var fileName = Path.GetFileNameWithoutExtension(fullPath);
        var extension = Path.GetExtension(fullPath);
        return WaitUntil(
            () =>
            {
                try
                {
                    return Directory
                        .GetFiles(directory, $"{fileName} (Unlimotion conflict *){extension}")
                        .Select(File.ReadAllText)
                        .FirstOrDefault(text => text.Contains(expectedEditorText, StringComparison.Ordinal));
                }
                catch (IOException)
                {
                    return null;
                }
            },
            static text => text is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Unified Feed SaveBoth did not create a sibling conflict copy.")!;
    }

    private string GetFeedVaultFullPath(string relativePath)
    {
        var root = feedVaultPath
            ?? throw new InvalidOperationException("Unified Feed automation vault was not captured.");
        return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void CompleteSeededPendingReview(string vaultPath)
    {
        var yesterdayPath = Path.Combine(
            vaultPath,
            UnlimotionAutomationScenarioData.GetFeedDailyRelativePath(
                    DateOnly.FromDateTime(DateTime.Now).AddDays(-1))
                .Replace('/', Path.DirectorySeparatorChar));
        var pending = $"- [ ] {UnlimotionAutomationScenarioData.FeedPendingReviewMarker}";
        var completed = $"- [x] {UnlimotionAutomationScenarioData.FeedPendingReviewMarker}";
        var original = File.ReadAllText(yesterdayPath);
        var updated = original.Replace(pending, completed, StringComparison.Ordinal);
        if (string.Equals(original, updated, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unified Feed fixture did not contain the seeded unfinished checkbox.");
        }

        File.WriteAllText(yesterdayPath, updated, Utf8WithoutBom);
    }

    private Unlimotion.ViewModel.MainWindowViewModel GetHeadlessMainWindowViewModel() =>
        Session.Inner.MainWindow.DataContext as Unlimotion.ViewModel.MainWindowViewModel
        ?? throw new InvalidOperationException("Headless Feed window did not expose MainWindowViewModel.");

    private TControl FindNativeControlByAutomationId<TControl>(string automationId)
        where TControl : Control =>
        TryFindNativeControlByAutomationId<TControl>(automationId)
        ?? throw new InvalidOperationException(
            $"Headless Feed control '{automationId}' was not found as {typeof(TControl).Name}.");

    private TControl? TryFindNativeControlByAutomationId<TControl>(string automationId)
        where TControl : Control
    {
        return Session.Inner.MainWindow
            .GetVisualDescendants()
            .OfType<TControl>()
            .FirstOrDefault(control => string.Equals(
                AutomationProperties.GetAutomationId(control),
                automationId,
                StringComparison.Ordinal));
    }

    private static TControl WaitForHeadlessControl<TControl>(
        Func<TControl> resolve,
        string timeoutMessage)
        where TControl : class =>
        WaitUntil(
            () => TryResolveHeadless(resolve),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: timeoutMessage)!;

    private static T? TryResolveHeadless<T>(Func<T> resolve)
        where T : class
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

    private readonly record struct UnifiedBlockAutomationIds(
        string PreviewAutomationId,
        string EditorAutomationId);

    private sealed record UnifiedConflictVersions(string EditorText, string DiskText);

    private sealed record UnifiedTerminalTaskState(
        bool HasCreatedTask,
        bool CanModifyReviewSource,
        bool LegacyActionsVisible,
        bool DraftGoalVisible,
        bool CreatedGoalVisible,
        bool CreatedAreasVisible,
        bool AreaMutationApplied,
        int TaskCount);

    private readonly record struct UnifiedReviewState(
        bool IsReviewActive,
        bool IsSelectionVisible,
        bool IsBusy,
        int PendingReviewBlocks,
        bool CanStartReview,
        string? SelectedMarkdown,
        string? ErrorMessage);

    private enum UnifiedConflictAction
    {
        UseEditor,
        UseDisk,
        SaveBoth
    }

    protected override void CaptureStatusContractScreenshot(string outputPath) =>
        throw new NotSupportedException(
            "The semantic Headless backend does not produce pixels; use FlaUI status-contract tests for screenshots.");

    protected override string DescribeStatusContractRuntimeState()
    {
        return HeadlessRuntime.Dispatch(() =>
        {
            var viewModel = Session.Inner.MainWindow.DataContext as Unlimotion.ViewModel.MainWindowViewModel
                ?? throw new InvalidOperationException("Headless status-contract window did not expose MainWindowViewModel.");
            var tasks = viewModel.taskRepository?.Tasks.Items
                .Select(task => $"{task.Id}:{task.Status}:{task.ArchiveDateTime:O}")
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray() ?? [];
            var archivedItems = viewModel.ArchivedItems
                .Select(item => $"{item.Id}:{item.TaskItem.Status}:{item.TaskItem.ArchiveDateTime:O}")
                .ToArray();
            return $"ArchivedMode={viewModel.ArchivedMode}; " +
                   $"Date={viewModel.ArchivedDateFilter.From:O}..{viewModel.ArchivedDateFilter.To:O}; " +
                   $"Tasks=[{string.Join(", ", tasks)}]; " +
                   $"ArchivedItems=[{string.Join(", ", archivedItems)}]";
        });
    }

    protected override bool IsArchivedContractTaskVisible()
    {
        return HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            var viewModel = Session.Inner.MainWindow.DataContext as Unlimotion.ViewModel.MainWindowViewModel
                ?? throw new InvalidOperationException("Headless status-contract window did not expose MainWindowViewModel.");
            return viewModel.ArchivedItems.Any(item => string.Equals(
                item.Id,
                UnlimotionAutomationScenarioData.StatusContractArchivedTaskId,
                StringComparison.Ordinal));
        });
    }

    protected override void OpenArchivedTab()
    {
        HeadlessRuntime.Dispatch(() =>
        {
            var viewModel = Session.Inner.MainWindow.DataContext as Unlimotion.ViewModel.MainWindowViewModel
                ?? throw new InvalidOperationException("Headless status-contract window did not expose MainWindowViewModel.");
            viewModel.ArchivedDateFilter.CurrentOption = Unlimotion.ViewModel.DateFilterDefinition.AllTime;
            viewModel.ArchivedDateFilter.SetDateTimes(Unlimotion.ViewModel.DateFilterDefinition.AllTime);
            Dispatcher.UIThread.RunJobs();
        });

        base.OpenArchivedTab();
    }

    protected override void SelectArchivedContractTask()
    {
        var tree = GetNativeControl<TreeView>(Page.ArchivedTree);
        HeadlessRuntime.Dispatch(() =>
        {
            var viewModel = Session.Inner.MainWindow.DataContext as Unlimotion.ViewModel.MainWindowViewModel
                ?? throw new InvalidOperationException("Headless status-contract window did not expose MainWindowViewModel.");
            var item = viewModel.ArchivedItems.Single(wrapper => string.Equals(
                wrapper.Id,
                UnlimotionAutomationScenarioData.StatusContractArchivedTaskId,
                StringComparison.Ordinal));
            tree.SelectedItem = item;
            Dispatcher.UIThread.RunJobs();
        });
    }

    protected override void OpenStatusPicker()
    {
        var statusPicker = GetNativeControl<TaskStatusPicker>(Page.CurrentTaskStatusButton);
        InvokeNativeButton(statusPicker);
    }

    protected override StatusContractOptionObservation ObserveOpenStatusOption(string automationId)
    {
        var statusPicker = GetNativeControl<TaskStatusPicker>(Page.CurrentTaskStatusButton);
        return HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            var flyout = statusPicker.Flyout as MenuFlyout
                ?? throw new InvalidOperationException("Current task status flyout was not created.");
            var option = flyout.Items
                .OfType<MenuItem>()
                .SingleOrDefault(item => string.Equals(
                    AutomationProperties.GetAutomationId(item),
                    automationId,
                    StringComparison.Ordinal));
            if (option is null)
            {
                return StatusContractOptionObservation.Missing(automationId);
            }

            var header = option.Header as Control;
            var displayedText = string.Join(
                "\n",
                (header is null
                    ? Enumerable.Empty<ILogical>()
                    : header.GetLogicalDescendants().Prepend(header))
                    .OfType<TextBlock>()
                    .Where(static text => text.IsVisible && !string.IsNullOrWhiteSpace(text.Text))
                    .Select(static text => text.Text!));
            return new StatusContractOptionObservation(
                Visible: option.IsVisible,
                Enabled: option.IsEnabled,
                AutomationId: AutomationProperties.GetAutomationId(option) ?? string.Empty,
                HelpText: AutomationProperties.GetHelpText(option) ?? string.Empty,
                DisplayedText: displayedText,
                ShowOnDisabled: ToolTip.GetShowOnDisabled(option));
        });
    }

    protected override void CloseStatusPicker()
    {
        var statusPicker = GetNativeControl<TaskStatusPicker>(Page.CurrentTaskStatusButton);
        HeadlessRuntime.Dispatch(() =>
        {
            statusPicker.Flyout?.Hide();
            Dispatcher.UIThread.RunJobs();
        });
    }

    protected override string GetRenderedStatusContractTheme()
    {
        var statusPicker = GetNativeControl<TaskStatusPicker>(Page.CurrentTaskStatusButton);
        return HeadlessRuntime.Dispatch(() => statusPicker.ActualThemeVariant == ThemeVariant.Dark
            ? "Dark"
            : statusPicker.ActualThemeVariant == ThemeVariant.Light
                ? "Light"
                : statusPicker.ActualThemeVariant?.ToString() ?? string.Empty);
    }

    protected override string OpenActionsAndInvokeArchiveCommand()
    {
        var actionsButton = GetNativeControl<DropDownButton>(Page.CurrentTaskActionsMenuButton);
        InvokeNativeButton(actionsButton);
        return HeadlessRuntime.Session.Dispatch(async () =>
        {
            Dispatcher.UIThread.RunJobs();
            var flyout = actionsButton.Flyout as MenuFlyout
                ?? throw new InvalidOperationException("Current task actions flyout was not created.");
            var menuItem = flyout.Items
                .OfType<MenuItem>()
                .Single(item => string.Equals(
                    AutomationProperties.GetAutomationId(item),
                    "CurrentTaskArchiveMenuItem",
                    StringComparison.Ordinal));
            var taskViewModel = actionsButton.DataContext as Unlimotion.ViewModel.TaskItemViewModel
                ?? throw new InvalidOperationException("Current task actions button did not expose TaskItemViewModel.");
            var label = menuItem.Header?.ToString();
            if (string.IsNullOrWhiteSpace(label))
            {
                // A detached headless MenuFlyout does not materialize its header binding. The
                // desktop/FlaUI path still verifies the rendered UIA name; use the same binding
                // source here while exercising the native menu item's command contract below.
                label = taskViewModel.ArchiveCommandTitle;
            }
            // Detached headless flyouts do not materialize command bindings. FlaUI verifies the
            // rendered menu-item binding; the headless path invokes the same public binding source.
            var command = menuItem.Command ?? taskViewModel.ArchiveCommand
                ?? throw new InvalidOperationException("Current task archive command was unavailable.");
            if (!command.CanExecute(menuItem.CommandParameter))
            {
                throw new InvalidOperationException("Current task archive menu command could not execute.");
            }

            var reactiveCommand = command as ReactiveCommand<Unit, Unit>
                ?? throw new InvalidOperationException("Current task archive command was not a ReactiveCommand.");
            await reactiveCommand.Execute().ToTask().WaitAsync(TimeSpan.FromSeconds(10));
            flyout.Hide();
            Dispatcher.UIThread.RunJobs();
            return label;
        }, CancellationToken.None).GetAwaiter().GetResult();
    }

    protected override void SelectStatusContractTask(string taskId, string title)
    {
        HeadlessRuntime.Dispatch(() =>
        {
            var viewModel = Session.Inner.MainWindow.DataContext as Unlimotion.ViewModel.MainWindowViewModel
                ?? throw new InvalidOperationException("Headless status-contract window did not expose MainWindowViewModel.");
            var task = viewModel.taskRepository?.Tasks.Items.Single(item => string.Equals(
                item.Id,
                taskId,
                StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Status-contract task '{taskId}' was not loaded.");
            viewModel.CurrentTaskItem = task;
            viewModel.SelectCurrentTask();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task StatusContract_RussianDarkFutureAndBlocker()
    {
        var statusPicker = GetNativeControl<TaskStatusPicker>(Page.CurrentTaskStatusButton);
        var darkThemeApplied = HeadlessRuntime.Dispatch(() =>
            statusPicker.ActualThemeVariant == ThemeVariant.Dark);

        WaitForCurrentTaskTitle(UnlimotionAutomationScenarioData.StatusContractTerminalTaskTitle);
        OpenStatusPicker();
        var terminalInProgress = ObserveOpenStatusOption("TaskStatusOptionInProgress");
        var terminalArchived = ObserveOpenStatusOption("TaskStatusOptionArchived");
        CloseStatusPicker();

        SelectStatusContractTask(
            UnlimotionAutomationScenarioData.StatusContractFutureTaskId,
            UnlimotionAutomationScenarioData.StatusContractFutureTaskTitle);
        WaitForCurrentTaskTitle(UnlimotionAutomationScenarioData.StatusContractFutureTaskTitle);
        var futureOpacity = GetCurrentStatusPickerOpacity();
        OpenStatusPicker();
        var futureInProgress = ObserveOpenStatusOption("TaskStatusOptionInProgress");
        var futureCompleted = ObserveOpenStatusOption("TaskStatusOptionCompleted");
        CloseStatusPicker();

        SelectStatusContractTask(
            UnlimotionAutomationScenarioData.StatusContractBlockedTaskId,
            UnlimotionAutomationScenarioData.StatusContractBlockedTaskTitle);
        WaitForCurrentTaskTitle(UnlimotionAutomationScenarioData.StatusContractBlockedTaskTitle);
        var blockedOpacity = GetCurrentStatusPickerOpacity();
        OpenStatusPicker();
        var blockedInProgress = ObserveOpenStatusOption("TaskStatusOptionInProgress");
        var blockedCompleted = ObserveOpenStatusOption("TaskStatusOptionCompleted");
        CloseStatusPicker();

        await Assert.That(darkThemeApplied)
            .IsTrue()
            .Because("The RU status-contract matrix must render under the configured dark theme.");
        await AssertDisabledStatusOption(
            terminalInProgress,
            "TaskStatusOptionInProgress",
            "Выполненную или архивную задачу нельзя запустить. Сначала верните задачу в активный статус.");
        await AssertDisabledStatusOption(
            terminalArchived,
            "TaskStatusOptionArchived",
            "Выполненную задачу нельзя архивировать. Сначала верните задачу в активный статус.");
        await AssertDisabledStatusOption(
            futureInProgress,
            "TaskStatusOptionInProgress",
            "Задачу нельзя начать раньше плановой даты начала.");
        await Assert.That(futureCompleted.Enabled)
            .IsTrue()
            .Because("A future planned begin blocks start only, not completion when other guards pass.");
        await Assert.That(Math.Abs(futureOpacity - 1d) < 0.001d)
            .IsTrue()
            .Because("A future planned begin must not dim a graph-available task.");
        await AssertDisabledStatusOption(
            blockedInProgress,
            "TaskStatusOptionInProgress",
            "Сначала выполните прямые блокирующие задачи.");
        await AssertDisabledStatusOption(
            blockedCompleted,
            "TaskStatusOptionCompleted",
            "Сначала выполните прямые блокирующие задачи.");
        await Assert.That(Math.Abs(blockedOpacity - 0.4d) < 0.001d)
            .IsTrue()
            .Because("An active graph blocker must dim the task to opacity 0.4.");

        var russianArchiveTitle = GetCurrentArchiveCommandTitle();
        var englishArchiveTitle = string.Empty;
        StatusContractOptionObservation? englishBlockedInProgress = null;
        OpenStatusPicker();
        try
        {
            SetStatusContractLanguage(Unlimotion.ViewModel.Localization.LocalizationService.EnglishLanguage);
            englishArchiveTitle = GetCurrentArchiveCommandTitle();
            englishBlockedInProgress = ObserveOpenStatusOption("TaskStatusOptionInProgress");
        }
        finally
        {
            CloseStatusPicker();
            SetStatusContractLanguage(Unlimotion.ViewModel.Localization.LocalizationService.RussianLanguage);
        }

        await Assert.That(russianArchiveTitle).IsEqualTo("Архивировать");
        await Assert.That(englishArchiveTitle).IsEqualTo("Archive");
        await Assert.That(englishBlockedInProgress).IsNotNull();
        await AssertDisabledStatusOption(
            englishBlockedInProgress!,
            "TaskStatusOptionInProgress",
            "Complete this task's direct blockers before starting or completing it.");
        await Assert.That(englishBlockedInProgress!.DisplayedText)
            .Contains("In progress")
            .Because("An already-created status option must refresh its visible title after a language switch.");
    }

    private double GetCurrentStatusPickerOpacity()
    {
        var statusPicker = GetNativeControl<TaskStatusPicker>(Page.CurrentTaskStatusButton);
        return HeadlessRuntime.Dispatch(() => statusPicker.Opacity);
    }

    private string GetCurrentArchiveCommandTitle()
    {
        return HeadlessRuntime.Dispatch(() =>
        {
            var viewModel = Session.Inner.MainWindow.DataContext as Unlimotion.ViewModel.MainWindowViewModel
                ?? throw new InvalidOperationException("Headless status-contract window did not expose MainWindowViewModel.");
            return viewModel.CurrentTaskItem?.ArchiveCommandTitle
                ?? throw new InvalidOperationException("Headless status-contract window did not expose a current task.");
        });
    }

    private void SetStatusContractLanguage(string language)
    {
        HeadlessRuntime.Dispatch(() =>
        {
            Unlimotion.ViewModel.Localization.LocalizationService.Current.SetLanguage(language);
            Dispatcher.UIThread.RunJobs();
        });
    }

    private static TControl GetNativeControl<TControl>(IUiControl wrappedControl)
        where TControl : Control
    {
        var innerProperty = FindProperty(
            wrappedControl.GetType(),
            "Inner",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var automationElement = innerProperty?.GetValue(wrappedControl)
            ?? throw new InvalidOperationException(
                $"Headless wrapper for '{wrappedControl.AutomationId}' did not expose its native automation element.");
        var controlProperty = FindProperty(
            automationElement.GetType(),
            "Control",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var control = controlProperty?.GetValue(automationElement) as Control;
        return control as TControl
            ?? throw new InvalidOperationException(
                $"Headless control '{wrappedControl.AutomationId}' was not a {typeof(TControl).Name}; actual type: {control?.GetType().FullName ?? "<missing>"}.");
    }

    private static void InvokeNativeButton(Button button)
    {
        HeadlessRuntime.Dispatch(() =>
        {
            var onClick = FindMethod(
                button.GetType(),
                "OnClick",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (onClick is null)
            {
                throw new InvalidOperationException(
                    $"Headless button '{AutomationProperties.GetAutomationId(button)}' did not expose OnClick.");
            }

            onClick.Invoke(button, []);
            Dispatcher.UIThread.RunJobs();
        });
    }

    private static void RaiseNativeClick(Button button)
    {
        HeadlessRuntime.Dispatch(() =>
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            Dispatcher.UIThread.RunJobs();
        });
    }

    private static PropertyInfo? FindProperty(Type type, string name, BindingFlags bindingFlags)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetProperty(name, bindingFlags);
            if (property is not null)
            {
                return property;
            }
        }

        return null;
    }

    private static MethodInfo? FindMethod(Type type, string name, BindingFlags bindingFlags)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(name, bindingFlags | BindingFlags.DeclaredOnly);
            if (method is not null)
            {
                return method;
            }
        }

        return null;
    }

    public sealed class HeadlessRuntimeSession : IUiTestSession
    {
        public HeadlessRuntimeSession(DesktopAppSession inner)
        {
            Inner = inner;
        }

        public DesktopAppSession Inner { get; }

        public void Dispose()
        {
            Inner.Dispose();
        }
    }
}
