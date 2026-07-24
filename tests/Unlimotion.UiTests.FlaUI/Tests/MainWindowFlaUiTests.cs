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

    protected override FlaUiRuntimeSession LaunchSession()
    {
        var isStatusContract = IsStatusContractScenarioTest;
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
        HoverStatusOption("TaskStatusOptionInProgress");
        var blockedTooltipOpened = WaitForAdditionalNamedElement(
            blockerReason,
            blockerReasonElementCount);
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

    private void HoverStatusOption(string automationId)
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
    }

    private bool WaitForAdditionalNamedElement(string expectedText, int baselineCount)
    {
        try
        {
            _ = WaitUntil(
                () => CountProcessElementsNamed(expectedText),
                count => count > baselineCount,
                timeout: TimeSpan.FromSeconds(5),
                timeoutMessage: "Tooltip did not add its text to the process UIA tree.");
            return true;
        }
        catch (TimeoutException)
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
