using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Automation;
using AppAutomation.FlaUI.Session;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using FlaUI.UIA3;
using System.Drawing;
using System.Runtime.InteropServices;
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
            buildBeforeLaunch: false,
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
        var selector = session.MainWindow.FindFirstDescendant(
                session.ConditionFactory.ByAutomationId("TaskSpaceSelector"))
            ?.AsComboBox()
            ?? throw new InvalidOperationException("Task-space header selector was not found.");
        Capture(session, "space-a.png");
        WaitUntilTaskVisible(session, UnlimotionAutomationScenarioData.TaskSpacesSpaceATitle);

        SelectTaskSpace(selector, "Space B");
        WaitUntilTaskVisible(session, UnlimotionAutomationScenarioData.TaskSpacesSpaceBTitle);
        Capture(session, "space-b.png");

        selector = FindTaskSpaceCombo(session, "TaskSpaceSelector");
        SelectTaskSpace(selector, "Space A");
        WaitUntilTaskVisible(session, UnlimotionAutomationScenarioData.TaskSpacesSpaceATitle);
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

    private static void OpenTaskSpaceSettings(DesktopAppSession session)
    {
        var page = new MainWindowPage(
            new FlaUiControlResolver(session.MainWindow, session.ConditionFactory));
        page.SelectTabItem(static currentPage => currentPage.SettingsTabItem, timeoutMs: 10_000);
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
        var settingsList = FindTaskSpaceCombo(session, "TaskSpacesList");
        SelectTaskSpaceFromDesktopPopup(session, settingsList, displayName);
        var page = new MainWindowPage(
            new FlaUiControlResolver(session.MainWindow, session.ConditionFactory));
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

    private static void SelectTaskSpaceFromDesktopPopup(
        DesktopAppSession session,
        ComboBox selector,
        string displayName)
    {
        selector.Focus();
        selector.Expand();
        Thread.Sleep(250);

        using var automation = new UIA3Automation();
        var desktop = automation.GetDesktop();
        var processId = session.MainWindow.Properties.ProcessId.ValueOrDefault;
        var item = WaitUntil(
            () => FindDesktopPopupItem(desktop, processId, displayName),
            value => value != null,
            $"Task-space Settings popup did not expose '{displayName}'.")
            ?? throw new InvalidOperationException($"Task space '{displayName}' was not found.");
        item.Click();
    }

    private static AutomationElement? FindDesktopPopupItem(
        AutomationElement desktop,
        int processId,
        string displayName)
    {
        AutomationElement? bestMatch = null;
        var bestProcessPriority = int.MaxValue;
        var bestControlPriority = int.MaxValue;

        foreach (var (controlType, controlPriority) in new[]
                 {
                     (ControlType.ListItem, 0),
                     (ControlType.Text, 1),
                 })
        {
            AutomationElement[] elements;
            try
            {
                elements = desktop.FindAllDescendants(
                    factory => factory.ByControlType(controlType));
            }
            catch (COMException)
            {
                continue;
            }

            foreach (var element in elements)
            {
                try
                {
                    if (element.Properties.IsOffscreen.ValueOrDefault)
                    {
                        continue;
                    }

                    var name = element.Properties.Name.ValueOrDefault;
                    if (string.IsNullOrEmpty(name) ||
                        !name.Contains(displayName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var processPriority =
                        element.Properties.ProcessId.ValueOrDefault == processId ? 0 : 1;
                    if (processPriority < bestProcessPriority ||
                        processPriority == bestProcessPriority &&
                        controlPriority < bestControlPriority)
                    {
                        bestMatch = element;
                        bestProcessPriority = processPriority;
                        bestControlPriority = controlPriority;
                    }
                }
                catch (COMException)
                {
                    // Popup elements can disappear while Avalonia rebuilds the tree.
                }
                catch (PropertyNotSupportedException)
                {
                    // Some UIA providers expose matching controls without all properties.
                }
                catch (NullReferenceException)
                {
                    // FlaUI can observe a released native element between property reads.
                }
            }
        }

        return bestMatch;
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
        selector.Focus();
        selector.Expand();
        Thread.Sleep(250);
        var item = WaitUntil(
            () => selector.Items.SingleOrDefault(candidate =>
                string.Equals(candidate.Name, displayName, StringComparison.Ordinal)),
            value => value != null,
            $"Task-space selector did not expose '{displayName}'.")
            ?? throw new InvalidOperationException($"Task space '{displayName}' was not found.");
        item.Click();
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
        var outputPath = Path.Combine(root, "artifacts", "ui-evidence", "task-spaces", fileName);
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
