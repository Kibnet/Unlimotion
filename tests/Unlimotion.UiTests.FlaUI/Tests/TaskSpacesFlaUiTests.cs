using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Automation;
using AppAutomation.FlaUI.Session;
using AppAutomation.FlaUI.Input;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;

namespace Unlimotion.UiTests.FlaUI.Tests;

public sealed class TaskSpacesFlaUiTests
{
    private DesktopAppSession? _session;
    private string? _configPath;

    [Before(Test)]
    public void Launch()
    {
        var launchOptions = UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(
            UnlimotionAutomationScenario.TaskSpaces,
            buildBeforeLaunch: true,
            mainWindowTimeout: TimeSpan.FromSeconds(90));
        _configPath = launchOptions.Arguments
            .Single(argument => argument.StartsWith("--config=", StringComparison.Ordinal))
            ["--config=".Length..];
        _session = DesktopAppSession.Launch(launchOptions);
        _session.MainWindow.Focus();
    }

    [After(Test)]
    public void Cleanup()
    {
        _session?.Dispose();
        _session = null;
        _configPath = null;
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public void Task_spaces_switch_A_B_A_and_emit_visual_evidence()
    {
        var session = _session ?? throw new InvalidOperationException("Desktop session was not initialized.");
        var evidenceHandshake = TaskSpaceEvidenceHandshake.TryCreate();
        Exception? scenarioFailure = null;

        try
        {
            evidenceHandshake?.SignalReadyAndWaitForScenario();
            var selector = session.MainWindow.FindFirstDescendant(
                    session.ConditionFactory.ByAutomationId("TaskSpaceSelector"))
                ?.AsComboBox()
                ?? throw new InvalidOperationException("Task-space header selector was not found.");
            Capture(session, "space-a.png");
            WaitUntilTaskVisible(session, UnlimotionAutomationScenarioData.TaskSpacesSpaceATitle);

            SelectTaskSpace(selector, "Space B");
            WaitUntilTaskVisible(session, UnlimotionAutomationScenarioData.TaskSpacesSpaceBTitle);
            WaitUntil(
                () => IsTaskSpaceOperationIdle(session),
                value => value,
                "Space B remained in the switching state.",
                TimeSpan.FromSeconds(45));
            Capture(session, "space-b.png");

            selector = FindTaskSpaceCombo(session, "TaskSpaceSelector");
            SelectTaskSpace(selector, "Space A");
            WaitUntilTaskVisible(session, UnlimotionAutomationScenarioData.TaskSpacesSpaceATitle);
            WaitUntil(
                () => IsTaskSpaceOperationIdle(session),
                value => value,
                "Space A remained in the switching state after returning.",
                TimeSpan.FromSeconds(45));
            Capture(session, "space-a-return.png");

            OpenTaskSpaceSettings(session);
            AddAndRenameTaskSpace(session, "Space C", "Space C renamed", _configPath);
            Capture(session, "settings-spaces.png");

            RemoveTaskSpaceFromSettings(session, "Space C renamed");
            try
            {
                WaitUntil(
                    () =>
                    {
                        ThrowIfToastError(session);
                        return ReadPersistedTaskSpaceNames(_configPath);
                    },
                    names => !names.Contains("Space C renamed", StringComparer.Ordinal),
                    "Removed active task space remained in the persisted catalog.",
                    TimeSpan.FromSeconds(45));
            }
            catch
            {
                Capture(session, "settings-remove-failure.png");
                DumpTaskSpaceFiles(_configPath);
                throw;
            }

            WaitUntil(
                () => IsVisibleText(session, "Space A") &&
                      IsTaskSpaceOperationIdle(session),
                value => value,
                "The fallback task space did not become active after removing the active space.",
                TimeSpan.FromSeconds(45));
        }
        catch (Exception ex)
        {
            scenarioFailure = ex;
            throw;
        }
        finally
        {
            evidenceHandshake?.SignalCompleteAndWaitForRecorder(scenarioFailure);
        }
    }

    private static void OpenTaskSpaceSettings(DesktopAppSession session)
    {
        var page = new MainWindowPage(
            new FlaUiControlResolver(session.MainWindow, session.ConditionFactory));
        page.ClickButton(static currentPage => currentPage.GlobalSettingsButton);
        WaitUntil(
            () => session.MainWindow.FindFirstDescendant(
                    session.ConditionFactory.ByAutomationId("TaskSpacesList"))
                is { Properties.IsOffscreen.ValueOrDefault: false },
            value => value,
            "Task-space Settings list did not become visible.");
    }

    private static void AddAndRenameTaskSpace(
        DesktopAppSession session,
        string initialName,
        string renamedName,
        string? configPath)
    {
        var page = new MainWindowPage(
            new FlaUiControlResolver(session.MainWindow, session.ConditionFactory));
        var settingsList = FindTaskSpaceCombo(session, "TaskSpacesList");
        var initialCount = ReadPersistedTaskSpaceNames(configPath).Count;
        page.NewTaskSpaceNameTextBox.Enter(initialName);
        page.ClickButton(static currentPage => currentPage.AddTaskSpaceButton);
        IReadOnlyList<string> namesAfterAdd;
        try
        {
            namesAfterAdd = WaitUntil(
                () =>
                {
                    ThrowIfToastError(session);
                    return ReadPersistedTaskSpaceNames(configPath);
                },
                names => names.Count > initialCount &&
                         names.Contains(initialName, StringComparer.Ordinal) &&
                         IsVisibleText(session, initialName) &&
                         IsTaskSpaceOperationIdle(session),
                "Added task space did not become active in the UI and persisted catalog.",
                TimeSpan.FromSeconds(45));
        }
        catch
        {
            Capture(session, "settings-add-failure.png");
            var diagnosticHeaderSelector = FindTaskSpaceCombo(session, "TaskSpaceSelector");
            settingsList = FindTaskSpaceCombo(session, "TaskSpacesList");
            Console.WriteLine(
                $"HeaderEnabled={diagnosticHeaderSelector.IsEnabled}; SettingsListEnabled={settingsList.IsEnabled}; " +
                $"AddEnabled={page.AddTaskSpaceButton.IsEnabled}; EnteredName='{page.NewTaskSpaceNameTextBox.Text}'");
            DumpTaskSpaceFiles(configPath);
            Console.WriteLine(
                string.Join(
                    Environment.NewLine,
                    session.MainWindow.FindAllDescendants()
                        .Where(element =>
                            !element.Properties.IsOffscreen.ValueOrDefault &&
                            !string.IsNullOrWhiteSpace(element.Name))
                        .Select(element => $"{element.ControlType}: {element.Name}")
                        .Distinct(StringComparer.Ordinal)));
            throw;
        }

        if (!namesAfterAdd.Contains(initialName, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"The add action created [{string.Join(", ", namesAfterAdd)}] instead of '{initialName}'.");
        }

        page.TaskSpaceNameTextBox.Enter(renamedName);
        page.ClickButton(static currentPage => currentPage.RenameTaskSpaceButton);
        WaitUntil(
            () => ReadPersistedTaskSpaceNames(configPath),
            names => names.Contains(renamedName, StringComparer.Ordinal) &&
                     !names.Contains(initialName, StringComparer.Ordinal) &&
                     IsVisibleText(session, renamedName) &&
                     IsTaskSpaceOperationIdle(session),
            $"Renamed task space '{renamedName}' did not appear in the UI and persisted catalog.",
            TimeSpan.FromSeconds(45));
    }

    private static void RemoveTaskSpaceFromSettings(DesktopAppSession session, string displayName)
    {
        var page = new MainWindowPage(
            new FlaUiControlResolver(session.MainWindow, session.ConditionFactory));
        // The name editor is bound to SelectedTaskSpace.DisplayName. Rename leaves that space selected.
        WaitUntil(() => page.TaskSpaceNameTextBox.Text,
            value => string.Equals(value, displayName, StringComparison.Ordinal),
            $"Task space '{displayName}' was not the selected Settings space before removal.");
        WaitUntil(
            () => page.RemoveTaskSpaceButton.IsEnabled,
            value => value,
            $"Task space '{displayName}' did not become the removable Settings selection.");
        page.ClickButton(static currentPage => currentPage.RemoveTaskSpaceButton);

        var confirmation = WaitUntil(
            () => session.MainWindow.FindFirstDescendant(
                session.ConditionFactory.ByAutomationId("AskYesButton")),
            value => value != null,
            "Task-space removal confirmation did not appear.")
            ?? throw new InvalidOperationException("Task-space removal confirmation was not found.");
        confirmation.AsButton().Invoke();
    }

    private static bool IsVisibleText(DesktopAppSession session, string text)
        => session.MainWindow.FindAllDescendants()
            .Any(element =>
                element.ControlType == ControlType.Text &&
                string.Equals(element.Name, text, StringComparison.Ordinal) &&
                !element.Properties.IsOffscreen.ValueOrDefault);

    private static IReadOnlyList<string> ReadPersistedTaskSpaceNames(string? configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
        {
            return [];
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(configPath));
            if (!document.RootElement.TryGetProperty("TaskSources", out var taskSources))
            {
                return [];
            }

            return taskSources.EnumerateObject()
                .Where(property =>
                    property.Name.StartsWith("SourceEntry", StringComparison.Ordinal) &&
                    property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                .Select(property =>
                {
                    using var source = System.Text.Json.JsonDocument.Parse(property.Value.GetString()!);
                    return source.RootElement.TryGetProperty("DisplayName", out var displayName)
                        ? displayName.GetString()
                        : null;
                })
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private static ComboBox FindTaskSpaceCombo(DesktopAppSession session, string automationId)
        => session.MainWindow.FindFirstDescendant(
                session.ConditionFactory.ByAutomationId(automationId))
            ?.AsComboBox()
           ?? throw new InvalidOperationException(
               $"Task-space combo box '{automationId}' was not found.");

    private static void SelectTaskSpace(ComboBox selector, string displayName)
    {
        WaitUntil(() => selector.IsEnabled, enabled => enabled,
            "Task-space selector did not become enabled before opening its popup.");
        selector.Focus();
        selector.Expand();
        WaitUntil(() => selector.ExpandCollapseState, state => state == ExpandCollapseState.Expanded,
            "Task-space selector did not open its popup.");
        var item = WaitUntil(
            () => selector.Items.SingleOrDefault(candidate =>
                string.Equals(candidate.Name, displayName, StringComparison.Ordinal)),
            value => value != null,
            $"Task-space selector did not expose '{displayName}'.")
            ?? throw new InvalidOperationException($"Task space '{displayName}' was not found.");
        var selection = item.Patterns.SelectionItem.PatternOrDefault;
        if (selection is not null)
        {
            selection.Select();
            // FlaUI Collapse is a no-op while the app temporarily disables the selector.
            WaitUntil(() => selector.IsEnabled, enabled => enabled,
                "Task-space selector did not become enabled after selection.");
            selector.Collapse();
            WaitUntil(() => selector.ExpandCollapseState,
                state => state == ExpandCollapseState.Collapsed,
                "Task-space selector did not close its popup.");
        }
        else
            DesktopPointer.ClickAsync(() => selector.Items.Single(candidate =>
                string.Equals(candidate.Name, displayName, StringComparison.Ordinal)))
                .GetAwaiter().GetResult();
    }

    private static void WaitUntilTaskVisible(DesktopAppSession session, string expectedTitle)
    {
        WaitUntil(
            () => session.MainWindow.FindAllDescendants()
                .Any(element =>
                    string.Equals(element.Name, expectedTitle, StringComparison.Ordinal) &&
                    !element.Properties.IsOffscreen.ValueOrDefault) &&
                  IsTaskSpaceOperationIdle(session),
            value => value,
            $"Task '{expectedTitle}' did not become visible.",
            TimeSpan.FromSeconds(45));
    }

    private static bool IsTaskSpaceOperationIdle(DesktopAppSession session)
    {
        ThrowIfToastError(session);
        var overlay = session.MainWindow.FindFirstDescendant(
            session.ConditionFactory.ByAutomationId("TaskSpaceSwitchOverlay"));
        return overlay == null || overlay.Properties.IsOffscreen.ValueOrDefault;
    }

    private static T WaitUntil<T>(
        Func<T> read,
        Predicate<T> condition,
        string failureMessage,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                var value = read();
                if (condition(value))
                {
                    return value;
                }
            }
            catch (COMException) when (DateTime.UtcNow < deadline)
            {
                // UI Automation can briefly invalidate the tree while Avalonia updates it.
            }

            Thread.Sleep(100);
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException(failureMessage);
    }

    private static void Capture(DesktopAppSession session, string fileName)
    {
        var root = FindRepositoryRoot();
        var configuredOutputDirectory = Environment.GetEnvironmentVariable(
            "UNLIMOTION_TASK_SPACES_EVIDENCE_ARTIFACT_DIR");
        var outputDirectory = string.IsNullOrWhiteSpace(configuredOutputDirectory)
            ? Path.Combine(root, "artifacts", "ui-evidence", "task-spaces")
            : Path.GetFullPath(configuredOutputDirectory);
        var outputPath = Path.Combine(outputDirectory, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var bitmap = CaptureWindow(session.MainWindow);
        bitmap.Save(outputPath);
        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
        {
            throw new InvalidOperationException($"Screenshot '{outputPath}' was not created.");
        }
    }

    private static Bitmap CaptureWindow(AutomationElement window)
    {
        var handle = new IntPtr(window.Properties.NativeWindowHandle.ValueOrDefault);
        if (handle == IntPtr.Zero || !NativeMethods.GetWindowRect(handle, out var bounds))
        {
            throw new InvalidOperationException("Task-space window bounds are unavailable.");
        }

        var width = Math.Max(1, bounds.Right - bounds.Left);
        var height = Math.Max(1, bounds.Bottom - bounds.Top);
        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        var deviceContext = graphics.GetHdc();
        var captured = false;

        try
        {
            captured = NativeMethods.PrintWindow(
                    handle,
                    deviceContext,
                    NativeMethods.PrintWindowRenderFullContent) ||
                NativeMethods.PrintWindow(handle, deviceContext, 0);
        }
        finally
        {
            graphics.ReleaseHdc(deviceContext);
        }

        if (!captured)
        {
            bitmap.Dispose();
            throw new InvalidOperationException("PrintWindow failed for the task-space window.");
        }

        return bitmap;
    }

    private sealed class TaskSpaceEvidenceHandshake(string directory)
    {
        private const string HandshakeEnvironmentVariable =
            "UNLIMOTION_TASK_SPACES_EVIDENCE_HANDSHAKE_DIR";
        private static readonly TimeSpan SignalTimeout = TimeSpan.FromMinutes(2);

        public static TaskSpaceEvidenceHandshake? TryCreate()
        {
            var directory = Environment.GetEnvironmentVariable(HandshakeEnvironmentVariable);
            return string.IsNullOrWhiteSpace(directory)
                ? null
                : new TaskSpaceEvidenceHandshake(Path.GetFullPath(directory));
        }

        public void SignalReadyAndWaitForScenario()
        {
            Directory.CreateDirectory(directory);
            WriteJson(
                "window-ready.json",
                new
                {
                    WindowTitle = Environment.GetEnvironmentVariable(
                        UnlimotionAppLaunchHost.AutomationWindowTitleEnvironmentVariable),
                    ReadyAtUtc = DateTime.UtcNow
                });
            WaitForSignal("scenario-go.signal");
        }

        public void SignalCompleteAndWaitForRecorder(Exception? failure)
        {
            WriteJson(
                "scenario-complete.json",
                new
                {
                    Success = failure is null,
                    Error = failure?.ToString(),
                    CompletedAtUtc = DateTime.UtcNow
                });
            WaitForSignal("recording-finished.signal");
        }

        private void WriteJson(string fileName, object document)
        {
            var path = Path.Combine(directory, fileName);
            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, path, overwrite: true);
        }

        private void WaitForSignal(string fileName)
        {
            var path = Path.Combine(directory, fileName);
            var deadline = DateTime.UtcNow + SignalTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(path))
                {
                    return;
                }

                Thread.Sleep(100);
            }

            throw new TimeoutException($"Evidence handshake signal '{path}' was not received.");
        }
    }

    private static class NativeMethods
    {
        internal const int PrintWindowRenderFullContent = 2;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr window, out Rect bounds);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PrintWindow(
            IntPtr window,
            IntPtr destinationDeviceContext,
            int flags);
    }

    private static void DumpTaskSpaceFiles(string? configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            Console.WriteLine("Task-space config path was not captured.");
            return;
        }

        Console.WriteLine($"ConfigPath={configPath}");
        if (File.Exists(configPath))
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath));
                if (document.RootElement.TryGetProperty("TaskSources", out var taskSources))
                {
                    var activeSourceId = taskSources.TryGetProperty("ActiveSourceId", out var activeSource)
                        ? activeSource.GetString()
                        : null;
                    var sourceCount = taskSources.TryGetProperty("SourcesCount", out var count)
                        ? count.GetString()
                        : null;
                    Console.WriteLine($"ActiveSourceId={activeSourceId}; SourcesCount={sourceCount}");
                    foreach (var property in taskSources.EnumerateObject()
                                 .Where(property => property.Name.StartsWith("SourceEntry", StringComparison.Ordinal) &&
                                                    property.Value.ValueKind == System.Text.Json.JsonValueKind.String))
                    {
                        using var source = System.Text.Json.JsonDocument.Parse(property.Value.GetString()!);
                        var sourceRoot = source.RootElement;
                        Console.WriteLine(
                            $"{property.Name}: Id={sourceRoot.GetProperty("Id").GetString()}; " +
                            $"DisplayName={sourceRoot.GetProperty("DisplayName").GetString()}; " +
                            $"Path={sourceRoot.GetProperty("Path").GetString()}");
                    }
                }
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Task-space config could not be read: {ex.Message}");
            }
            catch (System.Text.Json.JsonException ex)
            {
                Console.WriteLine($"Task-space config was being rewritten: {ex.Message}");
            }
        }

        var rootPath = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(rootPath) && Directory.Exists(rootPath))
        {
            Console.WriteLine(
                "Task-space directories:" + Environment.NewLine +
                string.Join(Environment.NewLine, Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)));
        }
    }

    private static void ThrowIfToastError(DesktopAppSession session)
    {
        var toast = session.MainWindow.FindFirstDescendant(
            session.ConditionFactory.ByAutomationId("ToastNotificationError"));
        if (toast == null || toast.Properties.IsOffscreen.ValueOrDefault)
        {
            return;
        }

        var message = string.Join(
            " ",
            toast.FindAllDescendants()
                .Where(element =>
                    element.ControlType == ControlType.Text &&
                    !string.IsNullOrWhiteSpace(element.Name))
                .Select(element => element.Name)
                .Distinct(StringComparer.Ordinal));
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(message)
                ? "Task-space operation displayed an error toast."
                : $"Task-space operation failed: {message}");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "Unlimotion.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the repository root.");
    }
}
