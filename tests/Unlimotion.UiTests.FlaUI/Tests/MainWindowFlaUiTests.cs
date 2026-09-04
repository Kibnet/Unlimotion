using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Automation;
using AppAutomation.FlaUI.Session;
using AppAutomation.TUnit;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;
using Unlimotion.UiTests.Authoring.Tests;

namespace Unlimotion.UiTests.FlaUI.Tests;

[InheritsTests]
public sealed class MainWindowFlaUiTests
    : FeedScenariosBase<MainWindowFlaUiTests.FlaUiRuntimeSession>
{
    private const string FeedScreenshotThemeEnvironmentVariable = "UNLIMOTION_FEED_SCREENSHOT_THEME";
    private const string FeedScreenshotWidthEnvironmentVariable = "UNLIMOTION_FEED_SCREENSHOT_WIDTH";
    private const string UiRecordingPauseEnvironmentVariable = "UNLIMOTION_UI_RECORDING_PAUSE_MS";
    private static int _physicalPixelDpiAwarenessConfigured;
    private string? feedVaultPath;

    protected override FlaUiRuntimeSession LaunchSession()
    {
        var isStatusContract = IsStatusContractScenarioTest;
        var isFeed = IsFeedScenarioTest;
        var isDailyNoteFilenameFormatScenario = IsDailyNoteFilenameFormatScenarioTest;
        var isFeedScreenshotCapture = isFeed &&
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ScreenshotPathEnvironmentVariable));
        var isDailyNoteFilenameFormatScreenshotCapture = isDailyNoteFilenameFormatScenario &&
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                DailyNoteFilenameFormatScreenshotPathEnvironmentVariable));
        if (isStatusContract
            || isFeedScreenshotCapture
            || isDailyNoteFilenameFormatScreenshotCapture
            || IsEditorDragScenarioTest)
        {
            EnsurePhysicalPixelDpiAwareness();
        }

        var session = DesktopAppSession.Launch(
            UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(
                isStatusContract
                    ? UnlimotionAutomationScenario.StatusContract
                    : isFeed
                        ? UnlimotionAutomationScenario.Feed
                    : UnlimotionAutomationScenario.Smoke,
                language: isStatusContract ? StatusContractLanguage : null,
                currentTaskId: isStatusContract ? StatusContractCurrentTaskId : null,
                buildBeforeLaunch: true,
                mainWindowTimeout: TimeSpan.FromSeconds(90),
                theme: isStatusContract
                    ? StatusContractTheme
                    : Environment.GetEnvironmentVariable(FeedScreenshotThemeEnvironmentVariable),
                feedVaultPrepared: path =>
                {
                    feedVaultPath = path;
                    if (isDailyNoteFilenameFormatScenario)
                    {
                        SeedDottedDailyNoteForFilenameFormatScenario(path);
                    }

                    if (IsEditorDragScenarioTest)
                    {
                        SeedEditorDragSection(path);
                    }
                }));

        session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(
            isStatusContract || isFeed ? WindowVisualState.Normal : WindowVisualState.Maximized);
        session.MainWindow.Focus();
        if (isStatusContract)
        {
            Mouse.MoveTo(0, 0);
        }
        else
        {
            var readiness = Retry.WhileNull(
                () => session.MainWindow.FindFirstDescendant(
                    session.ConditionFactory.ByAutomationId("CurrentTaskTitleTextBox")),
                timeout: TimeSpan.FromSeconds(30),
                interval: TimeSpan.FromMilliseconds(200),
                throwOnTimeout: false);
            if (!readiness.Success)
            {
                session.Dispose();
                throw new TimeoutException(
                    "The main task card did not become ready within 30 seconds.");
            }
        }

        return new FlaUiRuntimeSession(session);
    }

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
            return string.Empty;
        }
    }

    protected override void WriteExternalDailyNoteFilenameFormat(string format)
    {
        var root = feedVaultPath
            ?? throw new InvalidOperationException("Feed automation vault was not captured.");
        var sidecarPath = Path.Combine(root, ".unlimotion", "daily-note-settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sidecarPath)!);
        File.WriteAllText(
            sidecarPath,
            $"{{\"schemaVersion\":1,\"dailyFileNameFormat\":\"{format}\"}}\n");
    }

    protected override FeedTaskGeometrySnapshot GetFeedTaskGeometrySnapshot()
    {
        var status = FindProcessElement("FeedTask-feed-live-task-StatusPicker")
            ?? throw new InvalidOperationException("Feed task status picker was absent from UI Automation.");
        var title = FindProcessElement("FeedTask-feed-live-task-TitleButton")
            ?? throw new InvalidOperationException("Feed task title button was absent from UI Automation.");
        return new FeedTaskGeometrySnapshot(ToFeedBounds(status), ToFeedBounds(title));
    }

    protected override void CaptureFeedScreenshotIfRequested()
    {
        var configuredPath = Environment.GetEnvironmentVariable(ScreenshotPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return;
        }

        var mainWindow = Session.Inner.MainWindow;
        var handle = mainWindow.Properties.NativeWindowHandle.ValueOrDefault;
        const int defaultCaptureWidth = 1280;
        var captureWidth = int.TryParse(
            Environment.GetEnvironmentVariable(FeedScreenshotWidthEnvironmentVariable),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var configuredWidth)
                ? Math.Clamp(configuredWidth, 480, 2560)
                : defaultCaptureWidth;
        if (handle == IntPtr.Zero || !MoveWindow(handle, 0, 0, captureWidth, 2000, true))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not place the Feed window for screenshot capture.");
        }

        Thread.Sleep(150);
        var taskTitle = FindProcessElement("FeedTask-feed-live-task-TitleButton")
            ?? throw new InvalidOperationException("Feed task title was absent before screenshot capture.");
        taskTitle.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        var chronology = RequireProcessElement("FeedChronologyList");
        var scrollPattern = chronology.Patterns.Scroll.PatternOrDefault;
        for (var attempt = 0; attempt < 16 && !IsInsideViewport(taskTitle, chronology); attempt++)
        {
            scrollPattern?.Scroll(ScrollAmount.NoAmount, ScrollAmount.SmallIncrement);
            Thread.Sleep(75);
            taskTitle = FindProcessElement("FeedTask-feed-live-task-TitleButton") ?? taskTitle;
        }

        taskTitle = WaitUntil(
            () => FindProcessElement("FeedTask-feed-live-task-TitleButton"),
            element => element is not null && IsInsideViewport(element, chronology),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Feed task title did not become visible before screenshot capture.")!;

        var outputPath = Path.GetFullPath(configuredPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var screenshot = global::FlaUI.Core.Capturing.Capture.Element(
            mainWindow,
            new global::FlaUI.Core.Capturing.CaptureSettings());
        screenshot.ToFile(outputPath);
        if (new FileInfo(outputPath).Length == 0)
        {
            throw new InvalidOperationException($"Feed screenshot '{outputPath}' is empty.");
        }
    }

    protected override void CaptureDailyNoteFilenameFormatScreenshotIfRequested()
    {
        var configuredPath = Environment.GetEnvironmentVariable(
            DailyNoteFilenameFormatScreenshotPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return;
        }

        var mainWindow = Session.Inner.MainWindow;
        var handle = mainWindow.Properties.NativeWindowHandle.ValueOrDefault;
        const int captureMargin = 20;
        var captureWidth = Math.Min(1600, Math.Max(1, GetSystemMetrics(0) - captureMargin));
        var captureHeight = Math.Min(940, Math.Max(1, GetSystemMetrics(1) - captureMargin));
        if (handle == IntPtr.Zero || !MoveWindow(handle, 0, 0, captureWidth, captureHeight, true))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not place the Settings window for daily note format screenshot capture.");
        }

        OpenSettings();
        CloseTaskDetailsPaneForDailyNoteFilenameFormatScreenshot();
        var settingsSection = RequireProcessElement("NoteDailyFileNameFormatSection");
        settingsSection.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        var formatInput = RequireProcessElement("NoteDailyFileNameFormatTextBox");
        formatInput.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        var preview = RequireProcessElement("NoteDailyFileNameFormatPreviewText");
        var status = RequireProcessElement("NoteDailyFileNameFormatStatusText");
        Thread.Sleep(250);
        if (!IsInsideViewport(formatInput, mainWindow) ||
            !IsInsideViewport(preview, mainWindow) ||
            !IsInsideViewport(status, mainWindow))
        {
            throw new InvalidOperationException(
                "Daily note filename format settings were not visible in the Settings viewport before screenshot capture. " +
                $"window={DescribeViewportElement(mainWindow)}; " +
                $"input={DescribeViewportElement(formatInput)}; " +
                $"preview={DescribeViewportElement(preview)}; " +
                $"status={DescribeViewportElement(status)}.");
        }

        var outputPath = Path.GetFullPath(configuredPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var screenshot = global::FlaUI.Core.Capturing.Capture.Element(
            mainWindow,
            new global::FlaUI.Core.Capturing.CaptureSettings());
        screenshot.ToFile(outputPath);
        if (new FileInfo(outputPath).Length == 0)
        {
            throw new InvalidOperationException(
                $"Daily note filename format screenshot '{outputPath}' is empty.");
        }
    }

    protected override bool? IsDailyNoteFilenameFormatAppliedInUi(string expectedFormat)
    {
        var formatInput = FindProcessElement("NoteDailyFileNameFormatTextBox");
        var preview = FindProcessElement("NoteDailyFileNameFormatPreviewText");
        var apply = FindProcessElement("ApplyNoteDailyFileNameFormatButton");
        var status = FindProcessElement("NoteDailyFileNameFormatStatusText");
        if (formatInput is null || preview is null || apply is null || status is null)
        {
            return false;
        }

        var expectedStem = DateOnly.FromDateTime(DateTime.Now)
            .ToString(expectedFormat, CultureInfo.InvariantCulture);
        var statusText = status.Properties.Name.ValueOrDefault;
        var previewText = preview.Properties.Name.ValueOrDefault;
        return string.Equals(formatInput.AsTextBox().Text, expectedFormat, StringComparison.Ordinal) &&
               !apply.Properties.IsEnabled.ValueOrDefault &&
               statusText?.Contains(expectedFormat, StringComparison.Ordinal) == true &&
               previewText?.Contains($"Ежедневные/{expectedStem}.md", StringComparison.Ordinal) == true;
    }

    protected override void OpenSettings()
    {
        InvokeMainWindowButton("GlobalSettingsButton");
    }

    protected override void EnterDailyNoteFilenameFormat(ITextBoxControl input, string format)
    {
        var textBox = RequireProcessElement("NoteDailyFileNameFormatTextBox");
        textBox.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        textBox.Click();
        Keyboard.TypeSimultaneously([VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A]);
        Keyboard.Type(format);
    }

    private void CloseTaskDetailsPaneForDailyNoteFilenameFormatScreenshot()
    {
        var toggle = RequireProcessElement("DetailsPaneToggleButton");
        var togglePattern = toggle.Patterns.Toggle.PatternOrDefault
            ?? throw new InvalidOperationException(
                "Details pane toggle did not expose the Toggle UI Automation pattern.");
        if (togglePattern.ToggleState != ToggleState.On)
        {
            togglePattern.Toggle();
        }

        _ = WaitUntil(
            () => RequireProcessElement("DetailsPaneToggleButton")
                .Patterns.Toggle.PatternOrDefault?.ToggleState,
            static state => state == ToggleState.On,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Task details pane did not close before the Settings screenshot.");
    }

    protected override FeedNarrowLayoutSnapshot GetFeedNarrowLayoutSnapshot()
    {
        var window = Session.Inner.MainWindow;
        var handle = window.Properties.NativeWindowHandle.ValueOrDefault;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var currentBounds))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read Feed window bounds before narrow resize.");
        }

        if (!MoveWindow(handle, currentBounds.Left, currentBounds.Top, 720, 800, true))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not resize Feed window to its narrow contract width.");
        }

        var resized = WaitUntil(
            () => TryReadWindowBounds(handle),
            bounds => bounds is { Width: <= 760, Height: > 0 },
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Feed window did not reach its narrow contract width.")
            ?? throw new InvalidOperationException("Feed window bounds were unavailable after narrow resize.");
        var viewport = new FeedElementBounds(resized.Left, resized.Top, resized.Width, resized.Height);
        var feedRoot = FindProcessElement("FeedRoot")
            ?? throw new InvalidOperationException("Feed root was absent after narrow resize.");
        var hasHorizontalOverflow = feedRoot.FindAllDescendants()
            .Where(element => !element.Properties.IsOffscreen.ValueOrDefault)
            .Select(ToFeedBounds)
            .Any(bounds => bounds.Width > 0 &&
                           (bounds.Left < viewport.Left - 1 || bounds.Right > viewport.Right + 1));

        return new FeedNarrowLayoutSnapshot(
            viewport,
            ToFeedBounds(RequireProcessElement("FeedModeButton")),
            ToFeedBounds(RequireProcessElement("TasksModeButton")),
            ToFeedBounds(RequireProcessElement("GlobalCreateMenuButton")),
            ToFeedBounds(RequireProcessElement("FeedStartReviewButton")),
            ToFeedBounds(RequireProcessElement("FeedAreaFilterButton")),
            new[]
            {
                ToFeedBounds(RequireProcessElement("FeedAreasButton")),
                ToFeedBounds(RequireProcessElement("FeedFilesButton")),
                ToFeedBounds(RequireProcessElement("FeedRefreshButton"))
            },
            hasHorizontalOverflow);
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Feed_editor_pointer_drag_reorders_blocks()
    {
        const string dragSectionMarker = "Pointer drag section";
        Page.FeedModeButton.IsChecked = true;
        _ = WaitUntil(
            () => FindProcessElement("FeedRoot"),
            static element => element is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Feed root did not become available for the pointer-drag scenario.");

        var today = DateOnly.FromDateTime(DateTime.Now);
        var prefix = $"FeedDay-{today:yyyyMMdd}-Markdown";
        var handlePrefix = prefix + "-BlockMoveHandle-";
        var movableHandles = WaitUntil(
            () => Session.Inner.MainWindow.FindAllDescendants()
                .Where(element => element.IsEnabled
                    && element.Properties.AutomationId.ValueOrDefault?.StartsWith(
                        handlePrefix,
                        StringComparison.Ordinal) == true)
                .OrderBy(element => int.Parse(
                    element.Properties.AutomationId.ValueOrDefault![handlePrefix.Length..],
                    CultureInfo.InvariantCulture))
                .ToArray(),
            handles => handles.Length >= 4,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Feed move handles did not become available for pointer drag.");
        var sourceHandle = movableHandles[^2];
        var targetBlock = movableHandles[1];
        targetBlock.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        sourceHandle.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        _ = WaitUntil(
            () => (!sourceHandle.Properties.IsOffscreen.ValueOrDefault,
                !targetBlock.Properties.IsOffscreen.ValueOrDefault),
            static visibility => visibility.Item1 && visibility.Item2,
            timeout: TimeSpan.FromSeconds(5),
            timeoutMessage: "Feed drag controls did not become pointer-visible after scrolling.");
        var sourceBounds = sourceHandle.Properties.BoundingRectangle.ValueOrDefault;
        var targetBounds = targetBlock.Properties.BoundingRectangle.ValueOrDefault;
        if (sourceBounds.Width <= 0 || sourceBounds.Height <= 0 || targetBounds.Width <= 0 || targetBounds.Height <= 0)
        {
            throw new InvalidOperationException("Feed drag controls did not expose usable screen bounds.");
        }

        var start = new System.Drawing.Point(
            (int)Math.Round(sourceBounds.Left + sourceBounds.Width / 2d),
            (int)Math.Round(sourceBounds.Top + sourceBounds.Height / 2d));
        var finish = new System.Drawing.Point(
            (int)Math.Round(targetBounds.Left + targetBounds.Width / 2d),
            (int)Math.Round(targetBounds.Top + targetBounds.Height * 0.05d));
        if (int.TryParse(
                Environment.GetEnvironmentVariable(UiRecordingPauseEnvironmentVariable),
                CultureInfo.InvariantCulture,
                out var recordingPauseMilliseconds))
        {
            var remainingPause = Math.Clamp(recordingPauseMilliseconds, 0, 15000);
            while (remainingPause > 0)
            {
                Session.Inner.MainWindow.Focus();
                var interval = Math.Min(remainingPause, 250);
                Thread.Sleep(interval);
                remainingPause -= interval;
            }
        }

        Mouse.MoveTo(start);
        Mouse.Down(MouseButton.Left);
        try
        {
            Thread.Sleep(TimeSpan.FromMilliseconds(120));
            for (var step = 1; step <= 12; step++)
            {
                Mouse.MoveTo(new System.Drawing.Point(
                    start.X + (finish.X - start.X) * step / 12,
                    start.Y + (finish.Y - start.Y) * step / 12));
                Thread.Sleep(TimeSpan.FromMilliseconds(35));
            }
        }
        finally
        {
            Mouse.Up(MouseButton.Left);
        }

        var relativePath = UnlimotionAutomationScenarioData.GetFeedDailyRelativePath(today);
        var reordered = WaitUntil(
            () => ReadFeedVaultText(relativePath),
            text => text.IndexOf(dragSectionMarker, StringComparison.Ordinal)
                    < text.IndexOf(UnlimotionAutomationScenarioData.FeedNewestMarker, StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(15),
            timeoutMessage: "Pointer drag did not persist the new Markdown block order. "
                + $"Source={sourceHandle.Properties.AutomationId.ValueOrDefault}; "
                + $"target={targetBlock.Properties.AutomationId.ValueOrDefault}.");

        await Assert.That(reordered.IndexOf(
                dragSectionMarker,
                StringComparison.Ordinal))
            .IsLessThan(reordered.IndexOf(
                UnlimotionAutomationScenarioData.FeedNewestMarker,
                StringComparison.Ordinal));
    }

    private static void SeedEditorDragSection(string vaultPath)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var relativePath = UnlimotionAutomationScenarioData.GetFeedDailyRelativePath(today)
            .Replace('/', Path.DirectorySeparatorChar);
        File.AppendAllText(
            Path.Combine(vaultPath, relativePath),
            "\n## Drag section <!-- unlimotion-area:area-drag -->\nPointer drag section\n",
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private AutomationElement RequireProcessElement(string automationId) =>
        FindProcessElement(automationId)
        ?? throw new InvalidOperationException($"Feed UI Automation element '{automationId}' was absent.");

    private static FeedElementBounds ToFeedBounds(AutomationElement element)
    {
        var bounds = element.Properties.BoundingRectangle.ValueOrDefault;
        return new FeedElementBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
    }

    private static bool IsInsideViewport(AutomationElement element, AutomationElement viewportElement)
    {
        var bounds = ToFeedBounds(element);
        var viewport = ToFeedBounds(viewportElement);
        return bounds.Width > 0 && bounds.Height > 0 && bounds.IsInside(viewport);
    }

    private static string DescribeViewportElement(AutomationElement element)
    {
        var bounds = element.Properties.BoundingRectangle.ValueOrDefault;
        return $"bounds=({bounds.Left},{bounds.Top},{bounds.Width},{bounds.Height}),offscreen={element.Properties.IsOffscreen.ValueOrDefault}";
    }

    private static FeedElementBounds? TryReadWindowBounds(IntPtr handle)
    {
        return GetWindowRect(handle, out var bounds)
            ? new FeedElementBounds(
                bounds.Left,
                bounds.Top,
                bounds.Right - bounds.Left,
                bounds.Bottom - bounds.Top)
            : null;
    }

    protected override StatusContractWindowSnapshot GetStatusContractWindowSnapshot()
    {
        var window = Session.Inner.MainWindow;
        var nativeHandle = window.Properties.NativeWindowHandle.ValueOrDefault;
        if (nativeHandle == IntPtr.Zero || !GetWindowRect(nativeHandle, out var bounds))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read native outer window bounds.");
        }

        return new StatusContractWindowSnapshot(
            window.Properties.ProcessId.ValueOrDefault,
            window.Title,
            bounds.Left,
            bounds.Top,
            bounds.Right - bounds.Left,
            bounds.Bottom - bounds.Top);
    }

    protected override void CaptureStatusContractScreenshot(string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var bitmap = Session.Inner.MainWindow.Capture();
        bitmap.Save(outputPath);
        var screenshot = new FileInfo(outputPath);
        if (!screenshot.Exists || screenshot.Length == 0)
        {
            throw new InvalidOperationException(
                $"FlaUI status-contract screenshot '{outputPath}' was not created.");
        }
    }

    protected override string GetRenderedStatusContractTheme()
    {
        using var bitmap = Session.Inner.MainWindow.Capture();
        const int sampleColumns = 19;
        const int sampleRows = 13;
        var luminances = new int[sampleColumns * sampleRows];
        var sampleIndex = 0;

        for (var row = 0; row < sampleRows; row++)
        {
            var y = Math.Min(bitmap.Height - 1, (2 * row + 1) * bitmap.Height / (2 * sampleRows));
            for (var column = 0; column < sampleColumns; column++)
            {
                var x = Math.Min(bitmap.Width - 1, (2 * column + 1) * bitmap.Width / (2 * sampleColumns));
                var color = bitmap.GetPixel(x, y);
                luminances[sampleIndex++] =
                    (color.R * 2_126 + color.G * 7_152 + color.B * 722) / 10_000;
            }
        }

        Array.Sort(luminances);
        var medianLuminance = luminances[luminances.Length / 2];
        return medianLuminance switch
        {
            >= 160 => "Light",
            <= 96 => "Dark",
            _ => throw new InvalidOperationException(
                $"Rendered window theme was ambiguous (median luminance {medianLuminance}).")
        };
    }

    protected override void OpenStatusPicker()
    {
        ClickMainWindowElement("CurrentTaskStatusButton");
        _ = WaitUntil(
            IsAnyStatusOptionVisible,
            static visible => visible,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Status picker did not expose its first non-current option.");
    }

    protected override StatusContractOptionObservation ObserveOpenStatusOption(string automationId)
    {
        var option = FindProcessElement(automationId);
        if (option is null)
        {
            return StatusContractOptionObservation.Missing(automationId);
        }

        var displayedText = string.Join(
            "\n",
            option.FindAllDescendants()
                .Prepend(option)
                .Select(static element => element.Properties.Name.ValueOrDefault)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal));
        return new StatusContractOptionObservation(
            Visible: !option.Properties.IsOffscreen.ValueOrDefault,
            Enabled: option.IsEnabled,
            AutomationId: option.Properties.AutomationId.ValueOrDefault ?? string.Empty,
            HelpText: option.Properties.HelpText.ValueOrDefault ?? string.Empty,
            DisplayedText: displayedText,
            ShowOnDisabled: null);
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task StatusContract_RussianDarkFuture()
    {
        WaitForCurrentTaskTitle(UnlimotionAutomationScenarioData.StatusContractFutureTaskTitle);
        var renderedTheme = GetRenderedStatusContractTheme();
        OpenStatusPicker();
        var futureInProgress = ObserveOpenStatusOption("TaskStatusOptionInProgress");
        var futureCompleted = ObserveOpenStatusOption("TaskStatusOptionCompleted");
        CaptureStatusContractScreenshot(Path.Combine(
            ResolveArtifactDirectory(),
            "after-future-vs-blocked.png"));
        CloseStatusPicker();

        await Assert.That(renderedTheme)
            .IsEqualTo("Dark")
            .Because("The RU status-contract evidence must use the configured dark theme.");
        await AssertDisabledStatusOption(
            futureInProgress,
            "TaskStatusOptionInProgress",
            "Задачу нельзя начать раньше плановой даты начала.");
        await Assert.That(futureCompleted.Enabled)
            .IsTrue()
            .Because("A future planned begin blocks start only, not completion when other guards pass.");
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task StatusContract_RussianDarkBlocked()
    {
        WaitForCurrentTaskTitle(UnlimotionAutomationScenarioData.StatusContractBlockedTaskTitle);
        var renderedTheme = GetRenderedStatusContractTheme();
        OpenStatusPicker();
        var blockedInProgress = ObserveOpenStatusOption("TaskStatusOptionInProgress");
        var blockedCompleted = ObserveOpenStatusOption("TaskStatusOptionCompleted");
        const string blockerReason = "Сначала выполните прямые блокирующие задачи.";
        var blockerReasonElementCount = CountProcessElementsNamed(blockerReason);
        using var beforeTooltipHover = Session.Inner.MainWindow.Capture();
        var hoveredOptionBounds = HoverStatusOption("TaskStatusOptionInProgress");
        var blockedTooltipOpened = WaitForAdditionalNamedElement(
            blockerReason,
            blockerReasonElementCount,
            beforeTooltipHover,
            hoveredOptionBounds);
        CaptureStatusContractScreenshot(Path.Combine(
            ResolveArtifactDirectory(),
            "after-blocked.png"));
        CloseStatusPicker();

        await Assert.That(renderedTheme)
            .IsEqualTo("Dark")
            .Because("The RU status-contract evidence must use the configured dark theme.");
        await AssertDisabledStatusOption(
            blockedInProgress,
            "TaskStatusOptionInProgress",
            "Сначала выполните прямые блокирующие задачи.");
        await AssertDisabledStatusOption(
            blockedCompleted,
            "TaskStatusOptionCompleted",
            "Сначала выполните прямые блокирующие задачи.");
        await Assert.That(blockedTooltipOpened)
            .IsTrue()
            .Because("Pointer hover must open the tooltip for a disabled status row.");
    }

    private (double Left, double Top, double Right, double Bottom) HoverStatusOption(string automationId)
    {
        var option = FindProcessElement(automationId)
            ?? throw new InvalidOperationException(
                $"Status option '{automationId}' was not exposed by UIA for pointer hover.");
        var bounds = option.Properties.BoundingRectangle.ValueOrDefault;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException(
                $"Status option '{automationId}' did not expose usable screen bounds.");
        }

        Mouse.MoveTo(
            (int)Math.Round(bounds.Left + bounds.Width / 2d),
            (int)Math.Round(bounds.Top + bounds.Height / 2d));
        return (bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }

    private bool WaitForAdditionalNamedElement(
        string expectedText,
        int baselineCount,
        System.Drawing.Bitmap beforeHover,
        (double Left, double Top, double Right, double Bottom) optionBounds)
    {
        try
        {
            _ = WaitUntil(
                () => (NamedElementCount: CountProcessElementsNamed(expectedText),
                    HasVisibleTooltip: IsNamedTooltipVisible(expectedText),
                    HasTooltipVisualChange: HasTooltipVisualChange(beforeHover, optionBounds)),
                state => state.NamedElementCount > baselineCount
                    || state.HasVisibleTooltip
                    || state.HasTooltipVisualChange,
                timeout: TimeSpan.FromSeconds(5),
                timeoutMessage: "Tooltip was neither exposed through UIA nor rendered beside the hovered status row.");
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private bool HasTooltipVisualChange(
        System.Drawing.Bitmap beforeHover,
        (double Left, double Top, double Right, double Bottom) optionBounds)
    {
        using var afterHover = Session.Inner.MainWindow.Capture();
        if (beforeHover.Width != afterHover.Width || beforeHover.Height != afterHover.Height)
        {
            return false;
        }

        var windowBounds = Session.Inner.MainWindow.Properties.BoundingRectangle.ValueOrDefault;
        var optionHeight = optionBounds.Bottom - optionBounds.Top;
        var left = Math.Clamp(
            (int)Math.Floor(optionBounds.Right - windowBounds.Left + 4),
            0,
            afterHover.Width);
        var top = Math.Clamp(
            (int)Math.Floor(optionBounds.Top - windowBounds.Top - optionHeight / 2d),
            0,
            afterHover.Height);
        var bottom = Math.Clamp(
            (int)Math.Ceiling(optionBounds.Bottom - windowBounds.Top + optionHeight * 1.5d),
            0,
            afterHover.Height);
        if (left >= afterHover.Width || top >= bottom)
        {
            return false;
        }

        const int requiredChangedSamples = 150;
        var changedSamples = 0;
        for (var y = top; y < bottom; y += 2)
        {
            for (var x = left; x < afterHover.Width; x += 2)
            {
                var before = beforeHover.GetPixel(x, y);
                var after = afterHover.GetPixel(x, y);
                var colorDistance = Math.Abs(before.R - after.R)
                    + Math.Abs(before.G - after.G)
                    + Math.Abs(before.B - after.B);
                if (colorDistance < 48)
                {
                    continue;
                }

                changedSamples++;
                if (changedSamples >= requiredChangedSamples)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsNamedTooltipVisible(string expectedText)
    {
        try
        {
            var processId = Session.Inner.MainWindow.Properties.ProcessId.ValueOrDefault;
            var processCondition = Session.Inner.ConditionFactory.ByProcessId(processId);
            return Session.Inner.MainWindow.Automation
                .GetDesktop()
                .FindAllDescendants(processCondition)
                .Where(element =>
                    element.Properties.ControlType.ValueOrDefault == ControlType.ToolTip
                    && !element.Properties.IsOffscreen.ValueOrDefault)
                .Any(element =>
                    string.Equals(element.Properties.Name.ValueOrDefault, expectedText, StringComparison.Ordinal)
                    || element.FindAllDescendants().Any(descendant => string.Equals(
                        descendant.Properties.Name.ValueOrDefault,
                        expectedText,
                        StringComparison.Ordinal)));
        }
        catch
        {
            return false;
        }
    }

    private int CountProcessElementsNamed(string expectedText)
    {
        try
        {
            var processId = Session.Inner.MainWindow.Properties.ProcessId.ValueOrDefault;
            var processCondition = Session.Inner.ConditionFactory.ByProcessId(processId);
            return Session.Inner.MainWindow.Automation
                .GetDesktop()
                .FindAllDescendants(processCondition)
                .Count(element => string.Equals(
                    element.Properties.Name.ValueOrDefault,
                    expectedText,
                    StringComparison.Ordinal));
        }
        catch
        {
            return 0;
        }
    }

    protected override void CloseStatusPicker()
    {
        Keyboard.Press(VirtualKeyShort.ESCAPE);
        _ = WaitUntil(
            IsAnyStatusOptionVisible,
            static visible => !visible,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Status picker remained open after Escape.");
    }

    private bool IsAnyStatusOptionVisible()
    {
        return FindProcessElement("TaskStatusOptionNotReady") is not null ||
               FindProcessElement("TaskStatusOptionPrepared") is not null ||
               FindProcessElement("TaskStatusOptionInProgress") is not null ||
               FindProcessElement("TaskStatusOptionCompleted") is not null ||
               FindProcessElement("TaskStatusOptionArchived") is not null;
    }

    protected override string OpenActionsAndInvokeArchiveCommand()
    {
        InvokeMainWindowButton("CurrentTaskActionsMenuButton");
        var menuItem = WaitUntil(
            () => FindProcessElement("CurrentTaskArchiveMenuItem"),
            static element => element is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Archived task actions menu did not expose the archive command.")!;
        var label = menuItem.Name;
        menuItem.Click();
        return label;
    }

    protected override bool IsArchivedContractTaskVisible() => FindArchivedTaskElement() is not null;

    protected override void OpenArchivedTab()
    {
        var directTab = Session.Inner.MainWindow.FindFirstDescendant(
            Session.Inner.ConditionFactory.ByAutomationId("ArchivedTabItem"));
        if (directTab is not null)
        {
            directTab.AsTabItem().Select();
        }
        else
        {
            InvokeMainWindowButton("MainTabsOverflowButton");
            var overflowItem = WaitUntil(
                () => FindProcessElement("MainTabsOverflowArchivedTabItem"),
                static element => element is not null,
                timeout: TimeSpan.FromSeconds(10),
                timeoutMessage: "Main-tabs overflow did not expose Archived.")!;
            var invoke = overflowItem.Patterns.Invoke.PatternOrDefault;
            if (invoke is not null)
            {
                invoke.Invoke();
            }
            else
            {
                overflowItem.Click();
            }
        }

        SelectArchivedAllTimeDateFilter();
    }

    private void SelectArchivedAllTimeDateFilter()
    {
        InvokeMainWindowButton("ArchivedFiltersButton");
        var statusFilter = WaitUntil(
            () => FindProcessElement("ArchivedStatusFilterComboBox"),
            static element => element is not null && !element.Properties.IsOffscreen.ValueOrDefault,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Archived filter panel did not expose its status filter.")!;
        var dateFilterElement = WaitUntil(
            () => FindProcessElement("ArchivedDateFilterComboBox"),
            static element => element is not null && !element.Properties.IsOffscreen.ValueOrDefault,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Archived filter panel did not expose its date filter.")!;
        var selectedItem = dateFilterElement.AsComboBox().Select("All Time")
            ?? throw new InvalidOperationException("Archived date filter did not expose the All Time option.");
        if (!string.Equals(selectedItem.Text, "All Time", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Archived date filter selected '{selectedItem.Text}' instead of 'All Time'.");
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var visibleStatusFilter = FindProcessElement("ArchivedStatusFilterComboBox");
            if (visibleStatusFilter is null || visibleStatusFilter.Properties.IsOffscreen.ValueOrDefault)
            {
                return;
            }

            Keyboard.Press(VirtualKeyShort.ESCAPE);
            Thread.Sleep(TimeSpan.FromMilliseconds(200));
        }

        InvokeMainWindowButton("ArchivedFiltersButton");
        _ = WaitUntil(
            () => FindProcessElement("ArchivedStatusFilterComboBox"),
            static filter => filter is null || filter.Properties.IsOffscreen.ValueOrDefault,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Archived filter panel remained open after selecting All Time.");
    }

    protected override string DescribeStatusContractRuntimeState()
    {
        var tree = Session.Inner.MainWindow.FindFirstDescendant(
            Session.Inner.ConditionFactory.ByAutomationId("ArchivedTree"));
        if (tree is null)
        {
            return "ArchivedTree missing";
        }

        var descendants = tree.FindAllDescendants()
            .Select(element =>
                $"{element.Properties.ControlType.ValueOrDefault}:" +
                $"{element.Properties.AutomationId.ValueOrDefault}:" +
                $"{element.Properties.Name.ValueOrDefault}")
            .ToArray();
        return $"ArchivedTree descendants=[{string.Join(" | ", descendants)}]";
    }

    protected override void SelectArchivedContractTask()
    {
        SelectTaskTreeItem(
            "ArchivedTree",
            UnlimotionAutomationScenarioData.StatusContractArchivedTaskTitle);
    }

    private void SelectTaskTreeItem(string treeAutomationId, string title)
    {
        var titleElement = FindTaskTitleElement(treeAutomationId, title)
            ?? throw new InvalidOperationException(
                $"Tree '{treeAutomationId}' did not expose title '{title}'.");
        var task = FindTreeItemAncestor(titleElement, treeAutomationId)
            ?? throw new InvalidOperationException(
                $"Title '{title}' was not contained by a TreeItem.");
        var selectionPattern = task.Patterns.SelectionItem.PatternOrDefault;
        if (selectionPattern is not null)
        {
            selectionPattern.Select();
            return;
        }

        task.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        var visibleTitle = WaitUntil(
            () => FindTaskTitleElement(treeAutomationId, title),
            static element => element is not null &&
                              !element.Properties.IsOffscreen.ValueOrDefault &&
                              element.Properties.BoundingRectangle.ValueOrDefault.Width > 0 &&
                              element.Properties.BoundingRectangle.ValueOrDefault.Height > 0,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"Tree title '{title}' did not become pointer-visible.")!;
        var bounds = visibleTitle.Properties.BoundingRectangle.ValueOrDefault;
        task.Focus();
        Keyboard.Press(VirtualKeyShort.SPACE);
        Thread.Sleep(TimeSpan.FromMilliseconds(200));
        Mouse.LeftClick(new System.Drawing.Point(
            (int)Math.Round(bounds.Left + bounds.Width / 2d),
            (int)Math.Round(bounds.Top + bounds.Height / 2d)));
    }

    private AutomationElement? FindProcessElement(string automationId)
    {
        try
        {
            var processId = Session.Inner.MainWindow.Properties.ProcessId.ValueOrDefault;
            var processOption = Session.Inner.ConditionFactory
                .ByAutomationId(automationId)
                .And(Session.Inner.ConditionFactory.ByProcessId(processId));
            return Session.Inner.MainWindow.Automation
                .GetDesktop()
                .FindFirstDescendant(processOption);
        }
        catch
        {
            return null;
        }
    }

    private void ClickMainWindowElement(string automationId)
    {
        var element = Session.Inner.MainWindow.FindFirstDescendant(
            Session.Inner.ConditionFactory.ByAutomationId(automationId))
            ?? throw new InvalidOperationException(
                $"Main window did not expose automation element '{automationId}'.");
        element.Click();
    }

    private void InvokeMainWindowButton(string automationId)
    {
        var element = Session.Inner.MainWindow.FindFirstDescendant(
            Session.Inner.ConditionFactory.ByAutomationId(automationId))
            ?? throw new InvalidOperationException(
                $"Main window did not expose automation element '{automationId}'.");
        element.AsButton().Invoke();
    }

    private AutomationElement? FindArchivedTaskElement() => FindTaskTreeItem(
        "ArchivedTree",
        UnlimotionAutomationScenarioData.StatusContractArchivedTaskTitle);

    private AutomationElement? FindTaskTreeItem(string treeAutomationId, string title)
    {
        try
        {
            var titleElement = FindTaskTitleElement(treeAutomationId, title);
            return titleElement is null
                ? null
                : FindTreeItemAncestor(titleElement, treeAutomationId);
        }
        catch
        {
            return null;
        }
    }

    private AutomationElement? FindTaskTitleElement(string treeAutomationId, string title)
    {
        var tree = Session.Inner.MainWindow.FindFirstDescendant(
            Session.Inner.ConditionFactory.ByAutomationId(treeAutomationId));
        var titleCondition = Session.Inner.ConditionFactory
            .ByAutomationId("InlineTaskTitleTextBlock")
            .And(Session.Inner.ConditionFactory.ByName(title));
        return tree?.FindFirstDescendant(titleCondition);
    }

    private static AutomationElement? FindTreeItemAncestor(
        AutomationElement titleElement,
        string treeAutomationId)
    {
        for (var current = titleElement.Parent; current is not null; current = current.Parent)
        {
            if (current.Properties.ControlType.ValueOrDefault == ControlType.TreeItem)
            {
                return current;
            }

            if (string.Equals(
                current.Properties.AutomationId.ValueOrDefault,
                treeAutomationId,
                StringComparison.Ordinal))
            {
                break;
            }
        }

        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(
        IntPtr windowHandle,
        int x,
        int y,
        int width,
        int height,
        [MarshalAs(UnmanagedType.Bool)] bool repaint);

    private static void EnsurePhysicalPixelDpiAwareness()
    {
        if (Volatile.Read(ref _physicalPixelDpiAwarenessConfigured) != 0)
        {
            return;
        }

        var perMonitorAwareV2 = new IntPtr(-4);
        if (!SetProcessDpiAwarenessContext(perMonitorAwareV2))
        {
            var error = Marshal.GetLastWin32Error();
            var currentAwareness = GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext());
            if (currentAwareness != DpiAwareness.PerMonitorAware)
            {
                throw new Win32Exception(
                    error,
                    "Could not enable per-monitor DPI awareness for physical-pixel UI capture.");
            }
        }

        Volatile.Write(ref _physicalPixelDpiAwarenessConfigured, 1);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    private static extern DpiAwareness GetAwarenessFromDpiAwarenessContext(IntPtr dpiContext);

    private enum DpiAwareness
    {
        Invalid = -1,
        Unaware = 0,
        SystemAware = 1,
        PerMonitorAware = 2
    }

    protected override MainWindowPage CreatePage(FlaUiRuntimeSession session)
    {
        return new MainWindowPage(
            new FlaUiControlResolver(session.Inner.MainWindow, session.Inner.ConditionFactory));
    }

    public sealed class FlaUiRuntimeSession : IUiTestSession
    {
        public FlaUiRuntimeSession(DesktopAppSession inner)
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
