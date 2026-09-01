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
    public const string ReviewScenarioTestName = nameof(Feed_review_uses_global_dialog);
    public const string TaskReferenceScenarioTestName = nameof(Feed_task_status_precedes_title_and_title_navigates);
    public const string NarrowScenarioTestName = nameof(Feed_narrow_layout_keeps_primary_actions_available);
    public const string EditorDragScenarioTestName = "Feed_editor_pointer_drag_reorders_blocks";
    public const string DailyNoteFilenameFormatScenarioTestName = nameof(Daily_note_filename_format_settings);
    public const string UnifiedScenarioTestName = "Feed_unified_capture_review_task_parent_status_navigation_search_and_conflicts";
    public const string ScreenshotPathEnvironmentVariable = "UNLIMOTION_FEED_SCREENSHOT_PATH";
    public const string DailyNoteFilenameFormatScreenshotPathEnvironmentVariable =
        "UNLIMOTION_DAILY_NOTE_FORMAT_SCREENSHOT_PATH";
    private const string DailyNoteSettingsRelativePath = ".unlimotion/daily-note-settings.json";

    protected static bool IsFeedScenarioTest => TestContext.Current?.Metadata.TestName is
        ShellScenarioTestName or
        CaptureScenarioTestName or
        SearchScenarioTestName or
        ReviewScenarioTestName or
        TaskReferenceScenarioTestName or
        NarrowScenarioTestName or
        EditorDragScenarioTestName or
        DailyNoteFilenameFormatScenarioTestName or
        UnifiedScenarioTestName;

    protected static bool IsDailyNoteFilenameFormatScenarioTest => string.Equals(
        TestContext.Current?.Metadata.TestName,
        DailyNoteFilenameFormatScenarioTestName,
        StringComparison.Ordinal);

    protected static bool IsEditorDragScenarioTest => string.Equals(
        TestContext.Current?.Metadata.TestName,
        EditorDragScenarioTestName,
        StringComparison.Ordinal);

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

    protected virtual void ConfigureDailyNoteFilenameFormatSettings()
    {
    }

    protected virtual void CaptureDailyNoteFilenameFormatScreenshotIfRequested()
    {
    }

    protected virtual void OpenSettings()
    {
        Page.ClickButton(static page => page.GlobalSettingsButton);
    }

    protected virtual void OpenQuickCapture()
    {
        Page.ClickButton(static page => page.GlobalCreateMenuButton);
        var noteItem = WaitForControl(
            () => Page.GlobalCreateNoteMenuItem,
            "Global create menu did not expose the quick-note action.");
        noteItem.Invoke();
    }

    protected virtual bool IsQuickCaptureClosedAfterSave() =>
        TryResolve(() => Page.FeedQuickCaptureTextBox) is null;

    protected virtual void WriteExternalDailyNoteFilenameFormat(string format)
    {
        throw new NotSupportedException(
            "The active AppAutomation runtime does not expose a writable Feed vault.");
    }

    protected virtual void FlushDailyNoteFilenameFormatUi()
    {
    }

    /// <summary>
    /// Avalonia Headless exposes a native control tree but its Button adapter
    /// can retain an earlier IsEnabled snapshot while a binding is recomputed.
    /// The dedicated responsive test observes the native button; this hook lets
    /// the shared scenario additionally assert the ViewModel predicate in that
    /// adapter-only case.
    /// </summary>
    protected virtual bool? GetDailyNoteFilenameFormatCanApply() => null;

    protected virtual string? DescribeDailyNoteFilenameFormatApplyAvailability() => null;

    /// <summary>
    /// AppAutomation's Avalonia Headless TextBox model updates its visible text
    /// but does not raise the binding's source-update notification. The
    /// Headless override mirrors that input into the Settings draft after the
    /// stable-ID field is exercised; FlaUI uses the actual text input only.
    /// </summary>
    protected virtual void EnsureDailyNoteFilenameFormatDraft(string format)
    {
    }

    /// <summary>
    /// Enters a daily filename format through the active automation adapter.
    /// Desktop adapters may use keyboard input when a UIA value-pattern write
    /// would not exercise Avalonia's two-way text binding.
    /// </summary>
    protected virtual void EnterDailyNoteFilenameFormat(ITextBoxControl input, string format)
    {
        input.Enter(format);
    }

    protected virtual bool? IsDailyNoteFilenameFormatOperationIdle() => null;

    /// <summary>
    /// Headless dispatches the Feed's applied-state event separately from the
    /// asynchronous command completion. This hook waits for that event before
    /// deriving the next draft from the previous applied value. Desktop
    /// automation leaves it unset and observes the bound controls directly.
    /// </summary>
    protected virtual string? GetAppliedDailyNoteFilenameFormat() => null;

    /// <summary>
    /// Lets desktop automation wait for the visible terminal result of an
    /// Apply or Reload. Headless observes the Feed state directly instead.
    /// </summary>
    protected virtual bool? IsDailyNoteFilenameFormatAppliedInUi(string expectedFormat) => null;

    private void WaitForAppliedDailyNoteFilenameFormat(string expectedFormat)
    {
        if (GetAppliedDailyNoteFilenameFormat() is null)
        {
            return;
        }

        WaitUntil(
            () => GetAppliedDailyNoteFilenameFormat(),
            actual => string.Equals(actual, expectedFormat, StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(20),
            timeoutMessage: $"The daily note filename applied state did not become '{expectedFormat}'.");
    }

    private void WaitForDailyNoteFilenameFormatOperationIdle()
    {
        if (IsDailyNoteFilenameFormatOperationIdle() is not { } isIdle)
        {
            return;
        }

        if (!isIdle)
        {
            WaitUntil(
                () => IsDailyNoteFilenameFormatOperationIdle(),
                isIdle => isIdle == true,
                timeout: TimeSpan.FromSeconds(20),
                timeoutMessage: "The daily note filename format reconfiguration did not become idle.");
        }
    }

    private void WaitForDailyNoteFilenameFormatOperationCompletion(string expectedFormat)
    {
        WaitForDailyNoteFilenameFormatOperationIdle();
        WaitForAppliedDailyNoteFilenameFormat(expectedFormat);

        if (IsDailyNoteFilenameFormatAppliedInUi(expectedFormat) is not { } isAppliedInUi)
        {
            return;
        }

        if (!isAppliedInUi)
        {
            WaitUntil(
                () => IsDailyNoteFilenameFormatAppliedInUi(expectedFormat),
                isApplied => isApplied == true,
                timeout: TimeSpan.FromSeconds(20),
                timeoutMessage: $"The daily note filename format UI did not settle on '{expectedFormat}'.");
        }
    }

    protected static string GetDottedDailyRelativePath(DateOnly date) =>
        $"Ежедневные/{date:yyyy.MM.dd}.md";

    protected static void SeedDottedDailyNoteForFilenameFormatScenario(string vaultPath)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var relativePath = GetDottedDailyRelativePath(today);
        var absolutePath = Path.Combine(
            vaultPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        File.WriteAllText(
            absolutePath,
            $"# {today:yyyy.MM.dd}\n\nExisting dotted daily note for the filename format scenario.\n");
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
        OpenQuickCapture();
        WaitForChronologyDayCount(
            minimumCount: 1,
            "Feed chronology did not expose its newest daily note.");
        var todayPath = UnlimotionAutomationScenarioData.GetFeedDailyRelativePath(
            DateOnly.FromDateTime(DateTime.Now));
        using (Assert.Multiple())
        {
            await Assert.That(Page.FeedChronologyList.Name).IsNotEmpty();
            await Assert.That(Page.FeedChronologyList.Name)
                .DoesNotContain(UnlimotionAutomationScenarioData.FeedNewestMarker);
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
            IsQuickCaptureClosedAfterSave,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Quick capture overlay did not close after the Markdown write completed.");

        using (Assert.Multiple())
        {
            await Assert.That(ReadFeedVaultText(todayPath))
                .Contains(UnlimotionAutomationScenarioData.FeedQuickCaptureMarker);
            await Assert.That(IsQuickCaptureClosedAfterSave()).IsTrue();
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
        WaitForChronologyDayCount(
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
    public async Task Feed_review_uses_global_dialog()
    {
        OpenFeed();
        var startReview = WaitForControl(
            () => Page.FeedStartReviewButton,
            "Feed review banner did not expose its start action.");

        await Assert.That(Page.FeedReviewBanner.AutomationId).IsEqualTo("FeedReviewBanner");

        startReview.Invoke();
        var selection = WaitForControl(
            () => Page.FeedReviewSelectionText,
            "Starting Feed review did not expose the selected source block.");
        WaitUntil(
            () => selection.Text,
            text => text.Contains(UnlimotionAutomationScenarioData.FeedPendingReviewMarker, StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Global review did not select the seeded unfinished checkbox.");

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
    public async Task Daily_note_filename_format_settings()
    {
        const string captureMarker = "Daily format capture from AppAutomation";
        ConfigureDailyNoteFilenameFormatSettings();

        // Wait for the configured vault to finish its real startup binding before
        // visiting Settings. The quick-capture input exists only for an initialized
        // Feed session, so this avoids asserting an intentionally disabled Apply
        // action while startup is still in progress.
        OpenFeed();
        OpenQuickCapture();
        _ = WaitForControl(
            () => Page.FeedQuickCaptureTextBox,
            "Feed vault did not finish initialization before opening daily format settings.");
        const string readinessProbe = "Daily format startup readiness probe";
        Page.FeedQuickCaptureTextBox.Enter(readinessProbe);
        WaitUntil(
            () => Page.FeedCaptureButton.IsEnabled,
            timeout: TimeSpan.FromSeconds(30),
            timeoutMessage: "Feed vault did not become ready before opening daily format settings.");
        Page.FeedQuickCaptureTextBox.Enter(string.Empty);

        // Settings is a global overlay and must remain available from the Tasks mode too.
        OpenTasks();
        OpenSettings();
        _ = WaitForControl(
            () => Page.SettingsRoot,
            "Global Settings overlay did not become available after navigation.");

        var formatInput = WaitForControl(
            () => Page.NoteDailyFileNameFormatTextBox,
            "Daily note filename format input was not exposed in Settings.");
        var preview = WaitForControl(
            () => Page.NoteDailyFileNameFormatPreviewText,
            "Daily note filename format preview was not exposed in Settings.");
        var apply = WaitForControl(
            () => Page.ApplyNoteDailyFileNameFormatButton,
            "Daily note filename format Apply action was not exposed in Settings.");

        await Assert.That(preview.AutomationId)
            .IsEqualTo("NoteDailyFileNameFormatPreviewText");

        EnterDailyNoteFilenameFormat(formatInput, "yyyy.MM.dd");
        EnsureDailyNoteFilenameFormatDraft("yyyy.MM.dd");
        FlushDailyNoteFilenameFormatUi();
        WaitUntil(
            () => formatInput.Text,
            text => string.Equals(text, "yyyy.MM.dd", StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Daily note filename format input did not accept the dotted draft.");
        WaitUntil(
            () => apply.IsEnabled,
            timeout: TimeSpan.FromSeconds(20),
            timeoutMessage: "A valid dotted daily note filename format did not enable Apply. " +
                DescribeDailyNoteFilenameFormatApplyAvailability());
        if (GetDailyNoteFilenameFormatCanApply() is { } headlessCanApplyForDottedDraft)
        {
            if (!headlessCanApplyForDottedDraft)
            {
                throw new InvalidOperationException(
                    "The Headless daily note filename format bridge did not enable Apply: " +
                    DescribeDailyNoteFilenameFormatApplyAvailability());
            }

            await Assert.That(headlessCanApplyForDottedDraft).IsTrue();
        }

        EnterDailyNoteFilenameFormat(formatInput, "yyyy/MM/dd");
        EnsureDailyNoteFilenameFormatDraft("yyyy/MM/dd");
        FlushDailyNoteFilenameFormatUi();
        WaitUntil(
            () => formatInput.Text,
            text => string.Equals(text, "yyyy/MM/dd", StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Daily note filename format input did not accept the invalid draft.");
        var validation = WaitForControl(
            () => Page.NoteDailyFileNameFormatValidationText,
            "An invalid daily note filename format did not expose validation.");
        WaitUntil(
            () => validation.Text,
            text => !string.IsNullOrWhiteSpace(text),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "An invalid daily note filename format did not render validation text.");
        await Assert.That(validation.AutomationId)
            .IsEqualTo("NoteDailyFileNameFormatValidationText");
        if (GetDailyNoteFilenameFormatCanApply() is { } headlessCanApply)
        {
            await Assert.That(headlessCanApply).IsFalse();
        }
        else
        {
            WaitUntil(
                () => !apply.IsEnabled,
                timeout: TimeSpan.FromSeconds(10),
                timeoutMessage: "An invalid daily note filename format did not disable Apply.");
        }

        EnterDailyNoteFilenameFormat(formatInput, "yyyy.MM.dd");
        EnsureDailyNoteFilenameFormatDraft("yyyy.MM.dd");
        FlushDailyNoteFilenameFormatUi();
        WaitUntil(
            () => apply.IsEnabled,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Applying after an invalid draft did not restore the valid dotted format.");

        apply.Invoke();
        FlushDailyNoteFilenameFormatUi();
        WaitUntil(
            () => ReadFeedVaultText(DailyNoteSettingsRelativePath),
            text => text.Contains("\"dailyFileNameFormat\": \"yyyy.MM.dd\"", StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(20),
            timeoutMessage: "Applying the dotted daily note filename format did not persist the sidecar. " +
                DescribeDailyNoteFilenameFormatApplyAvailability());
        WaitForDailyNoteFilenameFormatOperationCompletion("yyyy.MM.dd");

        // A second pair of changes proves that a completed Apply does not leave
        // the surface stuck or collapse subsequent reconfiguration requests.
        EnterDailyNoteFilenameFormat(formatInput, "yyyy-MM-dd");
        EnsureDailyNoteFilenameFormatDraft("yyyy-MM-dd");
        FlushDailyNoteFilenameFormatUi();
        WaitUntil(
            () => apply.IsEnabled,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "A second daily note filename format draft did not enable Apply.");
        apply.Invoke();
        FlushDailyNoteFilenameFormatUi();
        WaitUntil(
            () => ReadFeedVaultText(DailyNoteSettingsRelativePath),
            text => text.Contains("\"dailyFileNameFormat\": \"yyyy-MM-dd\"", StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(20),
            timeoutMessage: "The repeated Apply did not persist the hyphenated format.");
        WaitForDailyNoteFilenameFormatOperationCompletion("yyyy-MM-dd");

        EnterDailyNoteFilenameFormat(formatInput, "yyyy.MM.dd");
        EnsureDailyNoteFilenameFormatDraft("yyyy.MM.dd");
        FlushDailyNoteFilenameFormatUi();
        WaitUntil(
            () => apply.IsEnabled,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Returning to the dotted daily note filename format did not enable Apply.");
        apply.Invoke();
        FlushDailyNoteFilenameFormatUi();
        WaitUntil(
            () => ReadFeedVaultText(DailyNoteSettingsRelativePath),
            text => text.Contains("\"dailyFileNameFormat\": \"yyyy.MM.dd\"", StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(20),
            timeoutMessage: "Returning to the dotted daily note filename format did not complete.");
        WaitForDailyNoteFilenameFormatOperationCompletion("yyyy.MM.dd");

        // Keep a local draft while an external device first writes an invalid
        // vault setting. The watcher must keep the draft, publish a diagnostic,
        // and expose Reload by its stable ID instead of losing the last valid
        // runtime configuration.
        EnterDailyNoteFilenameFormat(formatInput, "yyyy-MM-dd");
        EnsureDailyNoteFilenameFormatDraft("yyyy-MM-dd");
        FlushDailyNoteFilenameFormatUi();
        WaitUntil(
            () => apply.IsEnabled,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The local draft was not available before the external change.");
        WriteExternalDailyNoteFilenameFormat("yyyy/MM/dd");
        FlushDailyNoteFilenameFormatUi();
        var reload = WaitForControl(
            () => Page.ReloadExternalNoteDailyFileNameFormatButton,
            "An invalid watched daily note filename format did not expose Reload.");
        WaitUntil(
            () => reload.IsEnabled,
            timeout: TimeSpan.FromSeconds(20),
            timeoutMessage: "An invalid watched daily note filename format did not enable Reload.");
        var status = WaitForControl(
            () => Page.NoteDailyFileNameFormatStatusText,
            "An invalid watched daily note filename format did not expose a diagnostic.");
        using (Assert.Multiple())
        {
            await Assert.That(reload.AutomationId)
                .IsEqualTo("ReloadExternalNoteDailyFileNameFormatButton");
            await Assert.That(status.AutomationId)
                .IsEqualTo("NoteDailyFileNameFormatStatusText");
        }
        WaitUntil(
            () => status.Text,
            text => !string.IsNullOrWhiteSpace(text),
            timeout: TimeSpan.FromSeconds(20),
            timeoutMessage: "An invalid watched daily note filename format did not expose a diagnostic.");
        using (Assert.Multiple())
        {
            await Assert.That(formatInput.Text).IsEqualTo("yyyy-MM-dd");
            await Assert.That(string.IsNullOrWhiteSpace(status.Text)).IsFalse();
        }

        // Once the external file has been corrected, Reload remains an
        // explicit decision: it replaces the preserved local draft only after
        // the user invokes the accessible action.
        WriteExternalDailyNoteFilenameFormat("dd.MM.yyyy");
        FlushDailyNoteFilenameFormatUi();
        WaitUntil(
            () => ReadFeedVaultText(DailyNoteSettingsRelativePath),
            text => text.Contains("\"dailyFileNameFormat\":\"dd.MM.yyyy\"", StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(20),
            timeoutMessage: "The corrected external daily note filename format was not written to the vault.");
        WaitForAppliedDailyNoteFilenameFormat("dd.MM.yyyy");
        WaitUntil(
            () => reload.IsEnabled,
            timeout: TimeSpan.FromSeconds(20),
            timeoutMessage: "Reload was cleared before the corrected external value was explicitly accepted.");
        await Assert.That(formatInput.Text).IsEqualTo("yyyy-MM-dd");

        reload.Invoke();
        FlushDailyNoteFilenameFormatUi();
        WaitUntil(
            () => formatInput.Text,
            text => string.Equals(text, "dd.MM.yyyy", StringComparison.Ordinal) && !reload.IsEnabled,
            timeout: TimeSpan.FromSeconds(20),
            timeoutMessage: "Reload did not accept the watched external daily note filename format.");
        WaitForDailyNoteFilenameFormatOperationCompletion("dd.MM.yyyy");

        EnterDailyNoteFilenameFormat(formatInput, "yyyy.MM.dd");
        EnsureDailyNoteFilenameFormatDraft("yyyy.MM.dd");
        FlushDailyNoteFilenameFormatUi();
        WaitUntil(
            () => apply.IsEnabled,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "The dotted format could not be reapplied after Reload.");
        apply.Invoke();
        FlushDailyNoteFilenameFormatUi();
        WaitUntil(
            () => ReadFeedVaultText(DailyNoteSettingsRelativePath),
            text => text.Contains("\"dailyFileNameFormat\": \"yyyy.MM.dd\"", StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(20),
            timeoutMessage: "The dotted daily note filename format was not restored after Reload.");
        WaitForDailyNoteFilenameFormatOperationCompletion("yyyy.MM.dd");

        CaptureDailyNoteFilenameFormatScreenshotIfRequested();

        OpenFeed();
        OpenQuickCapture();
        Page.FeedQuickCaptureTextBox.Enter(captureMarker);
        WaitUntil(
            () => Page.FeedCaptureButton.IsEnabled,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Feed capture was not available after applying the dotted filename format.");
        Page.FeedCaptureButton.Invoke();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var dottedPath = GetDottedDailyRelativePath(today);
        var hyphenPath = UnlimotionAutomationScenarioData.GetFeedDailyRelativePath(today);
        WaitUntil(
            () => ReadFeedVaultText(dottedPath),
            text => text.Contains(captureMarker, StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Quick capture was not written to the dotted daily Markdown file.");

        using (Assert.Multiple())
        {
            await Assert.That(ReadFeedVaultText(dottedPath)).Contains(captureMarker);
            await Assert.That(ReadFeedVaultText(hyphenPath)).DoesNotContain(captureMarker);
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
            await Assert.That(snapshot.AreaFilter.IsInside(snapshot.Viewport)).IsTrue();
            await Assert.That(snapshot.AreaActions.All(action => action.IsInside(snapshot.Viewport))).IsTrue();
            await Assert.That(snapshot.AreaActions.All(action => !action.Overlaps(snapshot.AreaFilter))).IsTrue();
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

    private void WaitForChronologyDayCount(int minimumCount, string timeoutMessage)
    {
        WaitUntil(
            () => ParseChronologyDayCount(TryResolve(() => Page.FeedChronologyList)?.Name),
            count => count >= minimumCount,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: timeoutMessage);
    }

    private static int ParseChronologyDayCount(string? automationName)
    {
        if (string.IsNullOrWhiteSpace(automationName))
        {
            return 0;
        }

        return automationName
            .Split([' ', ':'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => int.TryParse(token, out var count) ? count : 0)
            .FirstOrDefault(count => count > 0);
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

    public bool Overlaps(FeedElementBounds other) =>
        Math.Min(Right, other.Right) > Math.Max(Left, other.Left) &&
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
    FeedElementBounds AreaFilter,
    IReadOnlyList<FeedElementBounds> AreaActions,
    bool HasHorizontalOverflow);
