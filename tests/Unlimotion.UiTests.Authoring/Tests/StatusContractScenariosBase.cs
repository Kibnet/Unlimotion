using System.Text.Json;
using AppAutomation.Abstractions;
using AppAutomation.TUnit;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;

namespace Unlimotion.UiTests.Authoring.Tests;

[InheritsTests]
public abstract class StatusContractScenariosBase<TSession> : MainWindowScenariosBase<TSession>
    where TSession : class, IUiTestSession
{
    public const string StatusContractTestName = nameof(StatusContract_TerminalPickerAndUnarchive);
    public const string StatusContractRussianDarkTestName = "StatusContract_RussianDarkFutureAndBlocker";
    public const string StatusContractRussianDarkFutureTestName = "StatusContract_RussianDarkFuture";
    public const string StatusContractRussianDarkBlockedTestName = "StatusContract_RussianDarkBlocked";
    public const string HandshakeDirectoryEnvironmentVariable = "UNLIMOTION_STATUS_CONTRACT_HANDSHAKE_DIR";
    public const string ArtifactDirectoryEnvironmentVariable = "UNLIMOTION_STATUS_CONTRACT_ARTIFACT_DIR";
    public const string PhaseEnvironmentVariable = "UNLIMOTION_STATUS_CONTRACT_PHASE";

    protected static bool IsStatusContractScenarioTest =>
        TestContext.Current?.Metadata.TestName is
            StatusContractTestName or
            StatusContractRussianDarkTestName or
            StatusContractRussianDarkFutureTestName or
            StatusContractRussianDarkBlockedTestName;

    protected static bool IsStatusContractRussianDarkTest =>
        TestContext.Current?.Metadata.TestName is
            StatusContractRussianDarkTestName or
            StatusContractRussianDarkFutureTestName or
            StatusContractRussianDarkBlockedTestName;

    protected static string StatusContractLanguage => IsStatusContractRussianDarkTest ? "ru" : "en";

    protected static string StatusContractTheme => IsStatusContractRussianDarkTest ? "Dark" : "Light";

    protected static string StatusContractCurrentTaskId => TestContext.Current?.Metadata.TestName switch
    {
        StatusContractRussianDarkFutureTestName =>
            UnlimotionAutomationScenarioData.StatusContractFutureTaskId,
        StatusContractRussianDarkBlockedTestName =>
            UnlimotionAutomationScenarioData.StatusContractBlockedTaskId,
        _ => UnlimotionAutomationScenarioData.StatusContractTerminalTaskId
    };

    protected abstract StatusContractWindowSnapshot GetStatusContractWindowSnapshot();

    protected virtual bool SupportsStatusContractScreenshotCapture => true;

    protected abstract void CaptureStatusContractScreenshot(string outputPath);

    protected abstract void OpenStatusPicker();

    protected abstract StatusContractOptionObservation ObserveOpenStatusOption(string automationId);

    protected abstract void CloseStatusPicker();

    protected virtual string? GetRenderedStatusContractTheme() => null;

    protected virtual string DescribeStatusContractRuntimeState() => string.Empty;

    protected virtual void SelectArchivedContractTask()
    {
        Page.SelectTreeItem(
            static page => page.ArchivedTree,
            UnlimotionAutomationScenarioData.StatusContractArchivedTaskTitle,
            timeoutMs: 10_000);
    }

    protected virtual void OpenArchivedTab()
    {
        Page.SelectTabItem(static page => page.ArchivedTabItem, timeoutMs: 10_000);
    }

    protected virtual string OpenActionsAndInvokeArchiveCommand()
    {
        Page.ClickButton(static page => page.CurrentTaskActionsMenuButton, timeoutMs: 10_000);
        var archiveMenuItem = WaitUntil(
            () => TryResolveDuringWait(() => Page.CurrentTaskArchiveMenuItem),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Archived task actions menu did not expose the archive command.")!;
        var label = archiveMenuItem.Name;
        archiveMenuItem.Invoke();
        return label;
    }

    protected virtual StatusContractPickerObservation OpenPickerAndObserveAfterUnarchive()
    {
        OpenStatusPicker();
        return new StatusContractPickerObservation(
            NotReadyVisible: ObserveOpenStatusOption("TaskStatusOptionNotReady").Visible,
            PreparedVisible: ObserveOpenStatusOption("TaskStatusOptionPrepared").Visible,
            InProgressVisible: ObserveOpenStatusOption("TaskStatusOptionInProgress").Visible,
            CompletedVisible: ObserveOpenStatusOption("TaskStatusOptionCompleted").Visible,
            ArchivedVisible: ObserveOpenStatusOption("TaskStatusOptionArchived").Visible);
    }

    protected virtual void SelectStatusContractTask(string taskId, string title)
    {
        Page.SelectTabItem(static page => page.AllTasksTabItem, timeoutMs: 10_000);
        Page.SelectTreeItem(static page => page.AllTasksTree, title, timeoutMs: 10_000);
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task StatusContract_TerminalPickerAndUnarchive()
    {
        var handshake = StatusContractHandshake.TryCreate();
        var observations = new StatusContractObservations();
        var artifactDirectory = ResolveArtifactDirectory();
        Directory.CreateDirectory(artifactDirectory);

        await handshake.WriteReadyAndWaitForGoAsync(GetStatusContractWindowSnapshot());

        var renderedTheme = GetRenderedStatusContractTheme();
        if (renderedTheme is not null)
        {
            await Assert.That(renderedTheme)
                .IsEqualTo(StatusContractTheme)
                .Because("The status-contract scenario must render under its configured theme.");
        }

        WaitForCurrentTaskTitle(UnlimotionAutomationScenarioData.StatusContractTerminalTaskTitle);
        OpenStatusPicker();
        var terminalInProgress = ObserveOpenStatusOption("TaskStatusOptionInProgress");
        var terminalArchived = ObserveOpenStatusOption("TaskStatusOptionArchived");
        observations.TerminalInProgressVisible = terminalInProgress.Visible;
        observations.TerminalInProgressEnabled = terminalInProgress.Enabled;
        if (SupportsStatusContractScreenshotCapture)
        {
            CaptureStatusContractScreenshot(
                Path.Combine(artifactDirectory, GetPhaseSpecificScreenshotName("terminal-picker")));
        }
        await Task.Delay(TimeSpan.FromMilliseconds(1_500));
        CloseStatusPicker();

        OpenArchivedTab();
        WaitForArchivedTaskToAppear();
        SelectArchivedContractTask();
        WaitForCurrentTaskTitle(UnlimotionAutomationScenarioData.StatusContractArchivedTaskTitle);

        observations.ArchiveMenuLabel = OpenActionsAndInvokeArchiveCommand();
        WaitForArchivedTaskToLeaveTree();

        var afterUnarchive = OpenPickerAndObserveAfterUnarchive();
        observations.PreparedOptionVisibleAfterUnarchive = afterUnarchive.PreparedVisible;
        observations.InProgressOptionVisibleAfterUnarchive = afterUnarchive.InProgressVisible;
        observations.UnarchiveRestoredPrepared =
            string.Equals(observations.ArchiveMenuLabel, "Unarchive", StringComparison.Ordinal) &&
            afterUnarchive.PreparedIsCurrent;
        observations.FlowCompleted = true;
        if (SupportsStatusContractScreenshotCapture)
        {
            CaptureStatusContractScreenshot(
                Path.Combine(artifactDirectory, GetPhaseSpecificScreenshotName("after-unarchive")));
        }
        await Task.Delay(TimeSpan.FromMilliseconds(1_500));

        var failureIds = observations.GetFailureIds();
        await handshake.WriteCompleteAndWaitForRecordingFinishedAsync(observations, failureIds);

        using (Assert.Multiple())
        {
            await Assert.That(
                    observations.TerminalInProgressVisible &&
                    !observations.TerminalInProgressEnabled)
                .IsTrue()
                .Because("TerminalInProgressWasEnabled");
            await Assert.That(observations.UnarchiveRestoredPrepared)
                .IsTrue()
                .Because("UnarchiveDidNotRestorePrepared");
        }

        await AssertDisabledStatusOption(
            terminalInProgress,
            "TaskStatusOptionInProgress",
            "Completed or archived tasks cannot be started. Return the task to an active status first.");
        await AssertDisabledStatusOption(
            terminalArchived,
            "TaskStatusOptionArchived",
            "Completed tasks cannot be archived. Return the task to an active status first.");
    }

    protected static async Task AssertDisabledStatusOption(
        StatusContractOptionObservation observation,
        string expectedAutomationId,
        string expectedReason)
    {
        using (Assert.Multiple())
        {
            await Assert.That(observation.Visible)
                .IsTrue()
                .Because($"{expectedAutomationId} was absent from the status picker.");
            await Assert.That(observation.Enabled)
                .IsFalse()
                .Because($"{expectedAutomationId} was expected to be disabled.");
            await Assert.That(observation.AutomationId)
                .IsEqualTo(expectedAutomationId)
                .Because("Status rows must keep stable automation identities.");
            await Assert.That(observation.HelpText)
                .IsEqualTo(expectedReason)
                .Because("The disabled status row must expose the localized reason as HelpText.");
            await Assert.That(observation.DisplayedText)
                .Contains(expectedReason)
                .Because("The disabled status row must render its reason inline.");
            if (observation.ShowOnDisabled is { } showOnDisabled)
            {
                await Assert.That(showOnDisabled)
                    .IsTrue()
                    .Because("The disabled status row tooltip must remain available.");
            }
        }
    }

    protected void WaitForCurrentTaskTitle(string expectedTitle)
    {
        _ = WaitUntil(
            () => TryResolveDuringWait(() => Page.CurrentTaskTitleTextBox.Text),
            title => string.Equals(title, expectedTitle, StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: $"Current task title did not become '{expectedTitle}'.");
    }

    private static TControl? TryResolveDuringWait<TControl>(Func<TControl> resolve)
        where TControl : class
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

    protected static string ResolveArtifactDirectory()
    {
        var configured = Environment.GetEnvironmentVariable(ArtifactDirectoryEnvironmentVariable);
        return string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(Path.Combine("artifacts", "ui-tests", "status-contract"))
            : Path.GetFullPath(configured);
    }

    private void WaitForArchivedTaskToLeaveTree()
    {
        try
        {
            WaitUntil(
                IsArchivedContractTaskVisible,
                static visible => !visible,
                timeout: TimeSpan.FromSeconds(10),
                timeoutMessage: "Archived status-contract task remained visible after the unarchive command.");
        }
        catch (TimeoutException exception)
        {
            var diagnostics = DescribeStatusContractRuntimeState();
            throw new TimeoutException(
                $"{exception.Message} Runtime state: {diagnostics}",
                exception);
        }
    }

    private void WaitForArchivedTaskToAppear()
    {
        try
        {
            WaitUntil(
                IsArchivedContractTaskVisible,
                static visible => visible,
                timeout: TimeSpan.FromSeconds(10),
                timeoutMessage: "Archived status-contract task did not appear after opening the Archived tab.");
        }
        catch (TimeoutException exception)
        {
            var diagnostics = DescribeStatusContractRuntimeState();
            throw new TimeoutException(
                $"{exception.Message} Runtime state: {diagnostics}",
                exception);
        }
    }

    protected virtual bool IsArchivedContractTaskVisible()
    {
        try
        {
            return ContainsTreeItem(
                Page.ArchivedTree.Items,
                UnlimotionAutomationScenarioData.StatusContractArchivedTaskTitle);
        }
        catch
        {
            return true;
        }
    }

    private static bool ContainsTreeItem(IEnumerable<ITreeItemControl> items, string expectedText)
    {
        return items.Any(item =>
            string.Equals(item.Text, expectedText, StringComparison.Ordinal) ||
            ContainsTreeItem(item.Items, expectedText));
    }

    private static string GetPhaseSpecificScreenshotName(string state)
    {
        var configuredPhase = Environment.GetEnvironmentVariable(PhaseEnvironmentVariable);
        var phase = string.Equals(configuredPhase, "Before", StringComparison.Ordinal)
            ? "before"
            : string.Equals(configuredPhase, "After", StringComparison.Ordinal)
                ? "after"
                : "status-contract";
        return $"{phase}-{state}.png";
    }
}

public sealed record StatusContractWindowSnapshot(
    int ProcessId,
    string Title,
    double Left,
    double Top,
    double Width,
    double Height);

public sealed record StatusContractOptionObservation(
    bool Visible,
    bool Enabled,
    string AutomationId,
    string HelpText,
    string DisplayedText,
    bool? ShowOnDisabled)
{
    public static StatusContractOptionObservation Missing(string automationId) =>
        new(false, false, automationId, string.Empty, string.Empty, null);
}

public sealed record StatusContractPickerObservation(
    bool NotReadyVisible,
    bool PreparedVisible,
    bool InProgressVisible,
    bool CompletedVisible,
    bool ArchivedVisible)
{
    public bool PreparedIsCurrent =>
        NotReadyVisible &&
        !PreparedVisible &&
        InProgressVisible &&
        CompletedVisible &&
        ArchivedVisible;
}

public sealed class StatusContractObservations
{
    public bool FlowCompleted { get; set; }

    public bool TerminalInProgressVisible { get; set; }

    public bool TerminalInProgressEnabled { get; set; }

    public string ArchiveMenuLabel { get; set; } = string.Empty;

    public bool PreparedOptionVisibleAfterUnarchive { get; set; }

    public bool InProgressOptionVisibleAfterUnarchive { get; set; }

    public bool UnarchiveRestoredPrepared { get; set; }

    public IReadOnlyList<string> GetFailureIds()
    {
        var failures = new List<string>();
        if (!TerminalInProgressVisible || TerminalInProgressEnabled)
        {
            failures.Add("TerminalInProgressWasEnabled");
        }

        if (!UnarchiveRestoredPrepared)
        {
            failures.Add("UnarchiveDidNotRestorePrepared");
        }

        return failures;
    }
}

internal sealed class StatusContractHandshake
{
    private const string HandshakeDirectoryEnvironmentVariable =
        "UNLIMOTION_STATUS_CONTRACT_HANDSHAKE_DIR";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string? _directory;

    private StatusContractHandshake(string? directory)
    {
        _directory = directory;
    }

    public static StatusContractHandshake TryCreate()
    {
        var directory = Environment.GetEnvironmentVariable(HandshakeDirectoryEnvironmentVariable);
        return new StatusContractHandshake(string.IsNullOrWhiteSpace(directory) ? null : Path.GetFullPath(directory));
    }

    public async Task WriteReadyAndWaitForGoAsync(StatusContractWindowSnapshot snapshot)
    {
        if (_directory is null)
        {
            return;
        }

        Directory.CreateDirectory(_directory);
        await WriteJsonAsync("window-ready.json", new
        {
            snapshot.ProcessId,
            WindowTitle = snapshot.Title,
            OuterRect = new
            {
                snapshot.Left,
                snapshot.Top,
                Right = snapshot.Left + snapshot.Width,
                Bottom = snapshot.Top + snapshot.Height
            }
        });
        await WaitForSignalAsync("scenario-go.signal", TimeSpan.FromSeconds(90));
    }

    public async Task WriteCompleteAndWaitForRecordingFinishedAsync(
        StatusContractObservations observations,
        IReadOnlyList<string> failureIds)
    {
        if (_directory is null)
        {
            return;
        }

        await WriteJsonAsync("scenario-complete.json", new
        {
            observations.FlowCompleted,
            FailureIds = failureIds
        });
        await WaitForSignalAsync("recording-finished.signal", TimeSpan.FromSeconds(180));
    }

    private async Task WriteJsonAsync(string fileName, object payload)
    {
        var path = Path.Combine(_directory!, fileName);
        var temporaryPath = path + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(payload, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private async Task WaitForSignalAsync(string fileName, TimeSpan timeout)
    {
        var path = Path.Combine(_directory!, fileName);
        var startedAt = DateTimeOffset.UtcNow;
        while (!File.Exists(path))
        {
            if (DateTimeOffset.UtcNow - startedAt >= timeout)
            {
                throw new TimeoutException($"Status-contract handshake signal '{fileName}' was not received within {timeout}.");
            }

            await Task.Delay(100);
        }
    }
}
