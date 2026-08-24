using AppAutomation.Abstractions;
using AppAutomation.TUnit;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;

namespace Unlimotion.UiTests.Authoring.Tests;

[InheritsTests]
public abstract class FeedScenariosBase<TSession> : StatusContractScenariosBase<TSession>
    where TSession : class, IUiTestSession
{
    public const string ShellScenarioTestName = nameof(Feed_shell_switch_preserves_task_context);
    public const string CaptureScenarioTestName = nameof(Feed_chronology_and_quick_capture_are_persisted);
    public const string SearchScenarioTestName = nameof(Feed_search_clear_restores_chronology);
    public const string ReviewScenarioTestName = nameof(Feed_review_stays_inline);
    public const string TaskReferenceScenarioTestName = nameof(Feed_task_status_precedes_title_and_title_navigates);
    public const string NarrowScenarioTestName = nameof(Feed_narrow_layout_keeps_primary_actions_available);
    public const string UnifiedScenarioTestName = "Feed_unified_capture_review_task_parent_status_navigation_search_and_conflicts";
    public const string ScreenshotPathEnvironmentVariable = "UNLIMOTION_FEED_SCREENSHOT_PATH";

    protected static bool IsFeedScenarioTest => TestContext.Current?.Metadata.TestName is
        ShellScenarioTestName or
        CaptureScenarioTestName or
        SearchScenarioTestName or
        ReviewScenarioTestName or
        TaskReferenceScenarioTestName or
        NarrowScenarioTestName or
        UnifiedScenarioTestName;

    protected static bool IsUnifiedFeedScenarioTest =>
        string.Equals(
            TestContext.Current?.Metadata.TestName,
            UnifiedScenarioTestName,
            StringComparison.Ordinal);

    protected abstract string ReadFeedVaultText(string relativePath);

    protected abstract FeedTaskGeometrySnapshot GetFeedTaskGeometrySnapshot();

    protected abstract FeedNarrowLayoutSnapshot GetFeedNarrowLayoutSnapshot();

    protected virtual void PrepareFeedTaskReferenceSurface()
    {
    }

    protected virtual void CaptureFeedScreenshotIfRequested()
    {
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Feed_shell_switch_preserves_task_context()
    {
        Page.SelectTabItem(static page => page.LastCreatedTabItem, timeoutMs: 10_000);
        await Assert.That(Page.LastCreatedTabItem.IsSelected).IsTrue();

        OpenFeed();
        using (Assert.Multiple())
        {
            await Assert.That(Page.FeedModeButton.IsChecked).IsTrue();
            await Assert.That(Page.FeedRoot.AutomationId).IsEqualTo("FeedRoot");
        }

        OpenTasks();
        using (Assert.Multiple())
        {
            await Assert.That(Page.TasksModeButton.IsChecked).IsTrue();
            await Assert.That(Page.LastCreatedTabItem.IsSelected).IsTrue();
            await UiAssert.TextEqualsAsync(
                () => Page.CurrentTaskTitleTextBox.Text,
                UnlimotionAutomationScenarioData.FeedCurrentTaskTitle,
                TimeSpan.FromSeconds(10));
        }

        OpenFeed();
        await Assert.That(Page.FeedChronologyList.AutomationId).IsEqualTo("FeedChronologyList");
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Feed_chronology_and_quick_capture_are_persisted()
    {
        OpenFeed();
        var items = WaitForListItems(
            () => Page.FeedChronologyList.Items,
            minimumCount: 1,
            "Feed chronology did not expose its newest daily note.");
        var todayPath = UnlimotionAutomationScenarioData.GetFeedDailyRelativePath(
            DateOnly.FromDateTime(DateTime.Now));
        using (Assert.Multiple())
        {
            await Assert.That(items[0].Name).IsNotEmpty();
            await Assert.That(items[0].Name).DoesNotContain(UnlimotionAutomationScenarioData.FeedNewestMarker);
            await Assert.That(ReadFeedVaultText(todayPath))
                .Contains(UnlimotionAutomationScenarioData.FeedNewestMarker);
        }

        Page.FeedQuickCaptureTextBox.Enter(UnlimotionAutomationScenarioData.FeedQuickCaptureMarker);
        WaitUntil(
            () => Page.FeedCaptureButton.IsEnabled,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Feed capture button did not become enabled for entered text.");
        Page.FeedCaptureButton.Invoke();

        WaitUntil(
            () => ReadFeedVaultText(todayPath),
            text => text.Contains(UnlimotionAutomationScenarioData.FeedQuickCaptureMarker, StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Quick capture was not persisted in today's Markdown file.");
        WaitUntil(
            () => Page.FeedQuickCaptureTextBox.Text,
            string.IsNullOrEmpty,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Quick capture input was not cleared after the Markdown write completed.");

        using (Assert.Multiple())
        {
            await Assert.That(ReadFeedVaultText(todayPath))
                .Contains(UnlimotionAutomationScenarioData.FeedQuickCaptureMarker);
            await Assert.That(Page.FeedQuickCaptureTextBox.Text).IsEmpty();
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Feed_search_clear_restores_chronology()
    {
        OpenFeed();
        Page.FeedSearchBox.Enter(UnlimotionAutomationScenarioData.FeedOlderMarker);

        var results = WaitForListItems(
            () => Page.FeedSearchResultsList.Items,
            minimumCount: 1,
            "Feed search did not return the seeded older daily fragment.");
        await Assert.That(results.Any(item => item.Text?.Contains(
                UnlimotionAutomationScenarioData.FeedOlderMarker,
                StringComparison.Ordinal) == true))
            .IsTrue();

        Page.FeedSearchBox.Enter(string.Empty);
        _ = WaitForListItems(
            () => Page.FeedChronologyList.Items,
            minimumCount: 2,
            "Clearing Feed search did not restore chronology.");

        using (Assert.Multiple())
        {
            await Assert.That(Page.FeedSearchBox.Text).IsEmpty();
            await Assert.That(ReadFeedVaultText(UnlimotionAutomationScenarioData.GetFeedDailyRelativePath(
                    DateOnly.FromDateTime(DateTime.Now))))
                .Contains(UnlimotionAutomationScenarioData.FeedNewestMarker);
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Feed_review_stays_inline()
    {
        OpenFeed();
        var startReview = WaitForControl(
            () => Page.FeedStartReviewButton,
            "Feed review banner did not expose its start action.");

        using (Assert.Multiple())
        {
            await Assert.That(Page.FeedReviewBanner.AutomationId).IsEqualTo("FeedReviewBanner");
            await Assert.That(Page.FeedBootstrapSummary.AutomationId).IsEqualTo("FeedBootstrapSummary");
            await Assert.That(Page.FeedBootstrapIndexedFilesText.Text).IsNotEmpty();
            await Assert.That(Page.FeedBootstrapPendingCheckboxesText.Text).IsNotEmpty();
        }

        startReview.Invoke();
        var selection = WaitForControl(
            () => Page.FeedReviewSelectionText,
            "Starting Feed review did not expose an inline selection.");
        WaitUntil(
            () => selection.Text,
            text => text.Contains(UnlimotionAutomationScenarioData.FeedPendingReviewMarker, StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Inline review did not select the seeded unfinished checkbox.");

        using (Assert.Multiple())
        {
            await Assert.That(Page.FeedRoot.AutomationId).IsEqualTo("FeedRoot");
            await Assert.That(Page.FeedReviewPanel.AutomationId).IsEqualTo("FeedReviewPanel");
            await Assert.That(Page.FeedReviewLeaveButton.AutomationId).IsEqualTo("FeedReviewLeaveButton");
            await Assert.That(Page.FeedReviewSkipButton.AutomationId).IsEqualTo("FeedReviewSkipButton");
            await Assert.That(selection.Text).Contains(UnlimotionAutomationScenarioData.FeedPendingReviewMarker);
        }

        var finishReview = TryResolve(() => Page.FeedFinishReviewButton);
        if (finishReview?.IsEnabled == true)
        {
            finishReview.Invoke();
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Feed_task_status_precedes_title_and_title_navigates()
    {
        OpenFeed();
        PrepareFeedTaskReferenceSurface();
        var status = WaitForControl(
            () => Page.FeedSeededTaskStatusPicker,
            "Seeded task live link did not expose the existing status picker.");
        var title = WaitForControl(
            () => Page.FeedSeededTaskTitleButton,
            "Seeded task live link did not expose its title navigation button.");
        var geometry = GetFeedTaskGeometrySnapshot();

        using (Assert.Multiple())
        {
            await Assert.That(status.IsEnabled).IsTrue();
            await Assert.That(title.Name).Contains(UnlimotionAutomationScenarioData.FeedCurrentTaskTitle);
            await Assert.That(geometry.Status.Right).IsLessThanOrEqualTo(geometry.Title.Left);
            await Assert.That(geometry.Status.VerticallyOverlaps(geometry.Title)).IsTrue();
        }

        CaptureFeedScreenshotIfRequested();

        title.Invoke();
        WaitUntil(
            () => Page.TasksModeButton.IsChecked == true,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Task title navigation did not switch from Feed to Tasks.");
        await UiAssert.TextEqualsAsync(
            () => Page.CurrentTaskTitleTextBox.Text,
            UnlimotionAutomationScenarioData.FeedCurrentTaskTitle,
            TimeSpan.FromSeconds(10));
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Feed_narrow_layout_keeps_primary_actions_available()
    {
        OpenFeed();
        var snapshot = GetFeedNarrowLayoutSnapshot();

        using (Assert.Multiple())
        {
            await Assert.That(snapshot.Viewport.Width).IsLessThanOrEqualTo(760);
            await Assert.That(snapshot.FeedMode.IsInside(snapshot.Viewport)).IsTrue();
            await Assert.That(snapshot.TasksMode.IsInside(snapshot.Viewport)).IsTrue();
            await Assert.That(snapshot.QuickCapture.IsInside(snapshot.Viewport)).IsTrue();
            await Assert.That(snapshot.ReviewAction.IsInside(snapshot.Viewport)).IsTrue();
            await Assert.That(snapshot.HasHorizontalOverflow).IsFalse();
        }
    }

    private void OpenFeed()
    {
        Page.FeedModeButton.IsChecked = true;
        WaitUntil(
            () => Page.FeedModeButton.IsChecked == true,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Feed workspace mode did not become selected.");
        _ = WaitForControl(() => Page.FeedRoot, "Feed root did not become available.");
    }

    private void OpenTasks()
    {
        Page.TasksModeButton.IsChecked = true;
        WaitUntil(
            () => Page.TasksModeButton.IsChecked == true,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Tasks workspace mode did not become selected.");
        _ = WaitForControl(() => Page.TasksModeRoot, "Tasks root did not become available.");
    }

    private static IReadOnlyList<IListBoxItem> WaitForListItems(
        Func<IReadOnlyList<IListBoxItem>> resolve,
        int minimumCount,
        string timeoutMessage)
    {
        return WaitUntil(
            () => TryResolve(resolve) ?? Array.Empty<IListBoxItem>(),
            items => items.Count >= minimumCount,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: timeoutMessage);
    }

    private static TControl WaitForControl<TControl>(Func<TControl> resolve, string timeoutMessage)
        where TControl : class
    {
        return WaitUntil(
            () => TryResolve(resolve),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: timeoutMessage)!;
    }

    private static T? TryResolve<T>(Func<T> resolve)
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
}

public readonly record struct FeedElementBounds(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public bool IsInside(FeedElementBounds viewport) =>
        Width > 0 &&
        Height > 0 &&
        Left >= viewport.Left - 1 &&
        Top >= viewport.Top - 1 &&
        Right <= viewport.Right + 1 &&
        Bottom <= viewport.Bottom + 1;

    public bool VerticallyOverlaps(FeedElementBounds other) =>
        Math.Min(Bottom, other.Bottom) > Math.Max(Top, other.Top);
}

public readonly record struct FeedTaskGeometrySnapshot(
    FeedElementBounds Status,
    FeedElementBounds Title);

public readonly record struct FeedNarrowLayoutSnapshot(
    FeedElementBounds Viewport,
    FeedElementBounds FeedMode,
    FeedElementBounds TasksMode,
    FeedElementBounds QuickCapture,
    FeedElementBounds ReviewAction,
    bool HasHorizontalOverflow);
