using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Automation;
using AppAutomation.FlaUI.Input;
using AppAutomation.FlaUI.Session;
using AppAutomation.TUnit;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using System.ComponentModel;
using System.Runtime.InteropServices;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;
using Unlimotion.UiTests.Authoring.Tests;

namespace Unlimotion.UiTests.FlaUI.Tests;

[InheritsTests]
public sealed class MainWindowFlaUiTests
    : StatusContractScenariosBase<MainWindowFlaUiTests.FlaUiRuntimeSession>
{
    private static int _physicalPixelDpiAwarenessConfigured;
    private int _statusViewportPreparationCount;

    protected override FlaUiRuntimeSession LaunchSession()
    {
        var isStatusContract = IsStatusContractScenarioTest ||
            TestContext.Current?.Metadata.TestName == nameof(StatusContract_PickerRestoresClippedHeader);
        if (isStatusContract)
        {
            EnsurePhysicalPixelDpiAwareness();
        }

        var session = DesktopAppSession.Launch(
            UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(
                isStatusContract
                    ? UnlimotionAutomationScenario.StatusContract
                    : UnlimotionAutomationScenario.Smoke,
                language: isStatusContract ? StatusContractLanguage : null,
                currentTaskId: isStatusContract ? StatusContractCurrentTaskId : null,
                mainWindowTimeout: TimeSpan.FromSeconds(90),
                theme: isStatusContract ? StatusContractTheme : null));

        session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(
            isStatusContract ? WindowVisualState.Normal : WindowVisualState.Maximized);
        session.MainWindow.Focus();
        if (!isStatusContract)
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
        // Capture is read-only: HoverAsync callbacks must not change foreground or
        // focus, and an open recovery menu must stay open while taking its frame.
        Thread.Sleep(TimeSpan.FromMilliseconds(200));
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
        PrepareStatusButtonInDetailsViewport();
        ClickMainWindowElement("CurrentTaskStatusButton");
        _ = WaitUntil(
            IsAnyStatusOptionVisible,
            static visible => visible,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Status picker did not expose its first non-current option.");
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task StatusContract_PickerRestoresClippedHeader()
    {
        WaitForCurrentTaskTitle(UnlimotionAutomationScenarioData.StatusContractTerminalTaskTitle);
        var viewport = Session.Inner.MainWindow.FindFirstDescendant(
            Session.Inner.ConditionFactory.ByAutomationId("CurrentTaskDetailsScrollViewer"))
            ?? throw new InvalidOperationException("Task details viewport was unavailable.");
        viewport.Patterns.Scroll.Pattern.SetScrollPercent(-1, 100);
        _ = WaitUntil(
            () => Session.Inner.MainWindow.FindFirstDescendant(
                Session.Inner.ConditionFactory.ByAutomationId("CurrentTaskStatusButton")),
            button => button is not null && button.BoundingRectangle.Width > 0 &&
                button.BoundingRectangle.Height > 0 && viewport.BoundingRectangle.Width > 0 &&
                viewport.BoundingRectangle.Height > 0 &&
                button.BoundingRectangle.Top < viewport.BoundingRectangle.Top,
            timeout: TimeSpan.FromSeconds(5),
            timeoutMessage: "The regression fixture did not clip the task status header.");

        var preparationsBefore = _statusViewportPreparationCount;
        OpenStatusPicker();
        await Assert.That(_statusViewportPreparationCount).IsEqualTo(preparationsBefore + 1);
        await Assert.That(ObserveOpenStatusOption("TaskStatusOptionArchived").Visible).IsTrue();
        CloseStatusPicker();
        var visibleHeader = Session.Inner.MainWindow.FindFirstDescendant(
            Session.Inner.ConditionFactory.ByAutomationId("CurrentTaskStatusButton"))!;
        var headerBounds = visibleHeader.BoundingRectangle;
        var viewportBounds = viewport.BoundingRectangle;
        await Assert.That(headerBounds.Width > 0 && headerBounds.Height > 0 &&
            headerBounds.Left >= viewportBounds.Left && headerBounds.Top >= viewportBounds.Top &&
            headerBounds.Right <= viewportBounds.Right && headerBounds.Bottom <= viewportBounds.Bottom).IsTrue();
    }

    private void PrepareStatusButtonInDetailsViewport()
    {
        var conditions = Session.Inner.ConditionFactory;
        var viewport = WaitUntil(
            () => Session.Inner.MainWindow.FindFirstDescendant(
                conditions.ByAutomationId("CurrentTaskDetailsScrollViewer")),
            control => control is not null && control.BoundingRectangle.Width > 0,
            timeout: TimeSpan.FromSeconds(30),
            timeoutMessage: "Task details viewport was unavailable.")!;
        var button = WaitUntil(
            () => Session.Inner.MainWindow.FindFirstDescendant(
                conditions.ByAutomationId("CurrentTaskStatusButton")),
            control => control is not null && control.IsEnabled && control.BoundingRectangle.Width > 0,
            timeout: TimeSpan.FromSeconds(30),
            timeoutMessage: "Task status button was unavailable.")!;
        var viewportBounds = viewport.BoundingRectangle;
        var beforeBounds = button.BoundingRectangle;
        if (IsInsideViewport(button)) return;
        var beforeHit = ObserveCenterHit(button);

        // UIA can report IsOffscreen=false for a header clipped by this ancestor.
        // Prepare the owned viewport; status activation still uses a physical hit test.
        var artifacts = Environment.GetEnvironmentVariable(ArtifactDirectoryEnvironmentVariable);
        var testName = TestContext.Current?.Metadata.TestName ?? "unknown";
        var evidencePrefix = "status-contract-viewport-" + testName;
        if (!string.IsNullOrWhiteSpace(artifacts))
            CaptureStatusContractScreenshot(Path.Combine(artifacts, evidencePrefix + "-before-scroll.png"));
        var scroll = viewport.Patterns.Scroll.PatternOrDefault
            ?? throw new InvalidOperationException("Clipped task status has no scrollable details viewport.");
        scroll.SetScrollPercent(-1, 0);
        button = WaitUntil(
            () => Session.Inner.MainWindow.FindFirstDescendant(
                conditions.ByAutomationId("CurrentTaskStatusButton")),
            control => control is not null && IsInsideViewport(control),
            timeout: TimeSpan.FromSeconds(5),
            timeoutMessage: "Task status button stayed clipped after preparing its details viewport.")!;
        _statusViewportPreparationCount++;
        if (!string.IsNullOrWhiteSpace(artifacts))
        {
            CaptureStatusContractScreenshot(Path.Combine(artifacts, evidencePrefix + "-after-scroll.png"));
            File.WriteAllText(Path.Combine(artifacts, evidencePrefix + ".json"),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    TestName = testName,
                    PreparationCount = _statusViewportPreparationCount,
                    BeforeButton = beforeBounds,
                    BeforeCenterHit = beforeHit,
                    Viewport = viewportBounds,
                    AfterButton = button.BoundingRectangle,
                    AfterCenterHit = ObserveCenterHit(button),
                    Prepared = true
                }));
        }

        bool IsInsideViewport(AutomationElement control)
        {
            var bounds = control.BoundingRectangle;
            var visible = viewport.BoundingRectangle;
            return bounds.Width > 0 && bounds.Height > 0 &&
                   bounds.Left >= visible.Left && bounds.Top >= visible.Top &&
                   bounds.Right <= visible.Right && bounds.Bottom <= visible.Bottom;
        }

        object ObserveCenterHit(AutomationElement control)
        {
            var bounds = control.BoundingRectangle;
            var point = new System.Drawing.Point(
                (int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2));
            var hit = control.Automation.FromPoint(point);
            var targetOrDescendant = false;
            for (var current = hit; current is not null; current = current.Parent)
            {
                if (!current.Equals(control)) continue;
                targetOrDescendant = true;
                break;
            }
            return new
            {
                point.X, point.Y,
                HitAutomationId = hit?.Properties.AutomationId.ValueOrDefault,
                HitProcessId = hit?.Properties.ProcessId.ValueOrDefault,
                TargetOrDescendant = targetOrDescendant
            };
        }
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
        await DesktopPointer.HoverAsync(
            () => FindProcessElement("TaskStatusOptionInProgress")
                ?? throw new InvalidOperationException("The disabled status option was not exposed for hover."),
            async cancellationToken =>
            {
                using var tooltipDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                tooltipDeadline.CancelAfter(TimeSpan.FromSeconds(15));
                await WaitForVisibleTooltipAsync(blockerReason, tooltipDeadline.Token);
                await Assert.That(HasVisibleProcessTooltip(blockerReason))
                    .IsTrue()
                    .Because("Pointer hover must expose the disabled status row's tooltip.");
                CaptureStatusContractScreenshot(Path.Combine(
                    ResolveArtifactDirectory(),
                    "after-blocked.png"));
            },
            // The action includes owner validation and read-only UIA assertions/capture.
            // Pointer restoration has a separate framework cleanup budget.
            // The action deadline must outlive the tooltip wait.
            new PointerHoverOptions { Timeout = TimeSpan.FromSeconds(30) });
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
    }

    private async Task WaitForVisibleTooltipAsync(string expectedText, CancellationToken cancellationToken)
    {
        // The pointer may already be on this row when the picker opens. A visible
        // tooltip is valid in that case; forcing a leave/re-enter or counting new
        // copies of the inline reason would add unnecessary pointer movement.
        while (!HasVisibleProcessTooltip(expectedText))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    private bool HasVisibleProcessTooltip(string expectedText)
    {
        var previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            var processId = Session.Inner.MainWindow.Properties.ProcessId.ValueOrDefault;
            var tooltipCondition = Session.Inner.ConditionFactory.ByName(expectedText);
            var statusOption = FindProcessElement("TaskStatusOptionInProgress");
            if (statusOption is null) return false;
            var rowBounds = statusOption.BoundingRectangle;
            var rowWindow = WindowFromPoint(new System.Drawing.Point(
                rowBounds.Left + rowBounds.Width / 2, rowBounds.Top + rowBounds.Height / 2));
            var mainWindow = Session.Inner.MainWindow.Properties.NativeWindowHandle.Value;
            return Session.Inner.MainWindow.Automation
                .GetDesktop()
                .FindAllChildren(Session.Inner.ConditionFactory.ByProcessId(processId))
                .SelectMany(root => root.FindAllDescendants(tooltipCondition))
                .Any(tooltip =>
                    !tooltip.Properties.IsOffscreen.ValueOrDefault &&
                    tooltip.Properties.BoundingRectangle.ValueOrDefault.Width > 0 &&
                    tooltip.Properties.BoundingRectangle.ValueOrDefault.Height > 0 &&
                    IsOwnedTooltipPopup(tooltip, processId, mainWindow, rowWindow));
        }
        catch
        {
            return false;
        }
        finally
        {
            if (previousDpi != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpi);
        }
    }

    protected override void InvokeOpenStatusOption(string automationId)
    {
        var option = WaitUntil(() => FindProcessElement(automationId),
            static element => element is not null, timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"Status option '{automationId}' was unavailable.")!;
        DesktopPointer.Click(option);
    }

    private static bool IsOwnedTooltipPopup(
        AutomationElement text, int processId, IntPtr mainWindow, IntPtr rowWindow)
    {
        // Avalonia exposes a string tooltip as Text directly under the main window
        // in the UIA control view. Its native popup, unlike either inline reason,
        // has a separate HWND owned by the same application.
        for (var ancestor = text.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.Properties.AutomationId.ValueOrDefault?.StartsWith(
                    "TaskStatusOption", StringComparison.Ordinal) == true)
                return false;
            if (ancestor.Properties.ControlType.ValueOrDefault == ControlType.Window)
                break;
        }
        var bounds = text.BoundingRectangle;
        var popup = WindowFromPoint(new System.Drawing.Point(
            bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
        _ = GetWindowThreadProcessId(popup, out var popupProcessId);
        return popup != IntPtr.Zero && popup != mainWindow && popup != rowWindow &&
               popupProcessId == processId;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(System.Drawing.Point point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    protected override void CloseStatusPicker()
    {
        Keyboard.Type(VirtualKeyShort.ESCAPE);
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

    protected override bool OpenActionsAndFindReloadCommand()
    {
        InvokeMainWindowButton("CurrentTaskActionsMenuButton");
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var item = FindProcessElement("CurrentTaskReloadButton");
            if (item is not null && !item.Properties.IsOffscreen.ValueOrDefault) return true;
            Thread.Sleep(100);
        }
        return false;
    }

    protected override bool IsRecoveryErrorCleared()
    {
        var error = FindProcessElement("CurrentTaskOperationErrorText");
        return error is null || error.Properties.IsOffscreen.ValueOrDefault || string.IsNullOrWhiteSpace(error.Name);
    }

    protected override void InvokeReloadCommand()
    {
        var item = FindProcessElement("CurrentTaskReloadButton")
            ?? throw new InvalidOperationException("Task actions menu did not expose reload.");
        if (!item.IsEnabled) throw new InvalidOperationException("Task reload menu item was disabled.");
        DesktopPointer.Click(item);
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
        DesktopPointer.Click(menuItem);
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
                DesktopPointer.Click(overflowItem);
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
        var dateFilterElement = FindArchivedDateFilterElement(statusFilter);
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

            Keyboard.Type(VirtualKeyShort.ESCAPE);
            Thread.Sleep(TimeSpan.FromMilliseconds(200));
        }

        InvokeMainWindowButton("ArchivedFiltersButton");
        _ = WaitUntil(
            () => FindProcessElement("ArchivedStatusFilterComboBox"),
            static filter => filter is null || filter.Properties.IsOffscreen.ValueOrDefault,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Archived filter panel remained open after selecting All Time.");
    }

    private AutomationElement FindArchivedDateFilterElement(AutomationElement statusFilter)
    {
        var processId = Session.Inner.MainWindow.Properties.ProcessId.ValueOrDefault;
        var comboBoxCondition = Session.Inner.ConditionFactory.ByControlType(ControlType.ComboBox);
        for (var ancestor = statusFilter.Parent;
             ancestor is not null && ancestor.Properties.ProcessId.ValueOrDefault == processId;
             ancestor = ancestor.Parent)
        {
            var dateFilter = ancestor
                .FindAllDescendants(comboBoxCondition)
                .FirstOrDefault(element =>
                    !element.Properties.IsOffscreen.ValueOrDefault &&
                    !string.Equals(
                        element.Properties.AutomationId.ValueOrDefault,
                        "ArchivedStatusFilterComboBox",
                        StringComparison.Ordinal));
            if (dateFilter is not null)
            {
                return dateFilter;
            }
        }

        throw new InvalidOperationException("Archived date filter combo box was not exposed by UIA.");
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
        _ = WaitUntil(
            () => FindTaskTitleElement(treeAutomationId, title),
            static element => element is not null &&
                              !element.Properties.IsOffscreen.ValueOrDefault &&
                              element.Properties.BoundingRectangle.ValueOrDefault.Width > 0 &&
                              element.Properties.BoundingRectangle.ValueOrDefault.Height > 0,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"Tree title '{title}' did not become pointer-visible.")!;
        task.Focus();
        Keyboard.Type(VirtualKeyShort.SPACE);
        Thread.Sleep(TimeSpan.FromMilliseconds(200));
        if (string.Equals(Page.CurrentTaskTitleTextBox.Text, title, StringComparison.Ordinal))
            return;

        DesktopPointer.ClickAsync(() => FindTaskTitleElement(treeAutomationId, title)
            ?? throw new InvalidOperationException($"Tree title '{title}' disappeared before selection."))
            .GetAwaiter().GetResult();
    }

    private AutomationElement? FindProcessElement(string automationId)
    {
        try
        {
            var processId = Session.Inner.MainWindow.Properties.ProcessId.ValueOrDefault;
            var processOption = Session.Inner.ConditionFactory.ByAutomationId(automationId);
            return Session.Inner.MainWindow.Automation
                .GetDesktop()
                .FindAllChildren(Session.Inner.ConditionFactory.ByProcessId(processId))
                .Select(root => root.FindFirstDescendant(processOption))
                .FirstOrDefault(element => element is not null);
        }
        catch
        {
            return null;
        }
    }

    private void ClickMainWindowElement(string automationId)
    {
        var element = WaitUntil(
            () => Session.Inner.MainWindow.FindFirstDescendant(
                Session.Inner.ConditionFactory.ByAutomationId(automationId)),
            static control => control is not null && control.IsEnabled &&
                !control.Properties.IsOffscreen.ValueOrDefault,
            timeout: TimeSpan.FromSeconds(30),
            timeoutMessage: $"Main window button '{automationId}' did not become enabled and visible.")!;
        DesktopPointer.Click(element);
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
                    "Could not enable per-monitor DPI awareness for status-contract capture.");
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
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

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
