using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AppAutomation.FlaUI.Session;
using AppAutomation.Session.Contracts;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Exceptions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;

namespace Unlimotion.UiTests.FlaUI.Tests;

public sealed class TaskLoadingPerformanceFlaUiTests
{
    // Optional local performance inputs. Every run launches with disposable copies;
    // the input directory is never passed to the application or its migrations.
    [Test]
    [NotInParallel("DesktopUi")]
    public void Startup_and_space_switches_allow_task_actions_after_loading()
    {
        var options = UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(
            UnlimotionAutomationScenario.TaskSpaces, buildBeforeLaunch: false,
            buildConfiguration: "Release", mainWindowTimeout: TimeSpan.FromMinutes(10));
        var configPath = options.Arguments.Single(a => a.StartsWith("--config=", StringComparison.Ordinal))[9..];
        var root = Path.GetDirectoryName(configPath)!;
        DesktopAppSession? session = null;
        try
        {
            var dataset = Environment.GetEnvironmentVariable("UNLIMOTION_LOADING_DATASET");
            var copied = 0;
            var fingerprints = new List<string>();
            if (!string.IsNullOrWhiteSpace(dataset))
            {
                foreach (var file in Directory.EnumerateFiles(dataset).Order(StringComparer.Ordinal))
                {
                    var name = Path.GetFileName(file);
                    var extension = Path.GetExtension(name);
                    if (name.StartsWith('.') || (extension.Length != 0 &&
                        !extension.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(".report", StringComparison.OrdinalIgnoreCase))) continue;
                    File.Copy(file, Path.Combine(root, "Tasks", name), overwrite: false);
                    File.Copy(file, Path.Combine(root, "Tasks-B", name), overwrite: false);
                    var hash = Hash(file);
                    if (Hash(Path.Combine(root, "Tasks", name)) != hash || Hash(Path.Combine(root, "Tasks-B", name)) != hash)
                        throw new InvalidOperationException("The performance snapshot changed while copying.");
                    fingerprints.Add(name + ":" + hash);
                    copied++;
                }
                if (copied == 0) throw new InvalidOperationException("The performance dataset is empty.");
            }

            var executable = Environment.GetEnvironmentVariable("UNLIMOTION_LOADING_EXE");
            var tieredCompilation = Environment.GetEnvironmentVariable(
                "UNLIMOTION_LOADING_DOTNET_TIERED_COMPILATION");
            if (!string.IsNullOrWhiteSpace(executable) || !string.IsNullOrWhiteSpace(tieredCompilation))
            {
                var environmentVariables = new Dictionary<string, string?>(
                    options.EnvironmentVariables, StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(tieredCompilation))
                    environmentVariables["DOTNET_TieredCompilation"] = tieredCompilation;

                options = new DesktopAppLaunchOptions
                {
                    ExecutablePath = string.IsNullOrWhiteSpace(executable) ? options.ExecutablePath : executable,
                    WorkingDirectory = string.IsNullOrWhiteSpace(executable)
                        ? options.WorkingDirectory
                        : Path.GetDirectoryName(executable)!,
                    Arguments = options.Arguments, EnvironmentVariables = environmentVariables,
                    MainWindowTimeout = options.MainWindowTimeout, PollInterval = options.PollInterval,
                    WindowPlacement = options.WindowPlacement, DisposeCallback = options.DisposeCallback
                };
            }

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                inputFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', fingerprints)))),
                assemblyFingerprints = new[] { "Unlimotion.FileStorage.dll", "Unlimotion.ViewModel.dll", "Unlimotion.dll", "Unlimotion.Desktop.dll" }
                    .ToDictionary(name => name, name => Hash(Path.Combine(Path.GetDirectoryName(options.ExecutablePath)!, name))),
                copiedFiles = copied
            }));

            var watch = Stopwatch.StartNew();
            session = DesktopAppSession.Launch(options);
            var windowAvailableMs = watch.Elapsed.TotalMilliseconds;
            session.MainWindow.Focus();
            var readyMs = ReadyAndAct(session, UnlimotionAutomationScenarioData.TaskSpacesSpaceATitle, watch);
            WriteMeasurement(session, "startup", readyMs, watch.Elapsed.TotalMilliseconds, windowAvailableMs, copied);
            MeasureSwitch(session, "Space B", UnlimotionAutomationScenarioData.TaskSpacesSpaceBTitle, copied);
            MeasureSwitch(session, "Space A", UnlimotionAutomationScenarioData.TaskSpacesSpaceATitle, copied);
        }
        finally
        {
            if (session is not null) session.Dispose();
            else options.DisposeCallback?.Invoke();
        }
    }

    private static void MeasureSwitch(DesktopAppSession session, string space, string title, int copied)
    {
        var selector = Find(session, "TaskSpaceSelector")!.AsComboBox();
        selector.Focus();
        selector.Expand();
        Wait(() => selector.Items.Any(item => item.Name == space), "Task-space choices did not open.");
        var target = selector.Items.Single(item => item.Name == space);
        var watch = Stopwatch.StartNew();
        var selection = target.Patterns.SelectionItem.PatternOrDefault;
        if (selection is not null)
        {
            selection.Select();
        }
        else
        {
            target.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
            target.Click();
        }
        var readyMs = ReadyAndAct(session, title, watch);
        WriteMeasurement(session, space, readyMs, watch.Elapsed.TotalMilliseconds, null, copied);
    }

    private static double ReadyAndAct(DesktopAppSession session, string title, Stopwatch watch)
    {
        Wait(() =>
        {
            CheckErrors(session);
            if (Visible(Find(session, "TasksLoadingOverlay")) || Visible(Find(session, "TaskSpaceSwitchProgress")) ||
                Find(session, "TaskSpaceSelector")?.IsEnabled != true) return false;
            return session.MainWindow.FindAllDescendants().Any(e => e.Name == title && Visible(e));
        }, "The expected task did not become available after loading.");
        var readyMs = watch.Elapsed.TotalMilliseconds;

        // A successful task-card action distinguishes readiness from a hidden overlay on failure.
        var details = Find(session, "DetailsPaneToggleButton")!.AsToggleButton();
        if (Find(session, "CurrentTaskTitleTextBox")?.AsTextBox().Text != title)
        {
            // Close a previously selected card before clicking the new row. Closing after the click
            // races with the same toggle that the row click opens and made the benchmark flaky.
            // The button binds to !DetailsAreOpen: checked means the pane is closed.
            if (details.IsToggled != true)
            {
                details.Toggle();
                Wait(() => details.IsToggled == true, "The previous task card did not close.", TimeSpan.FromSeconds(15));
            }
            var lastClick = Stopwatch.StartNew();
            var clicked = false;
            var toggledAfterClick = false;
            Wait(() =>
            {
                if (Find(session, "CurrentTaskTitleTextBox")?.AsTextBox().Text == title) return true;
                if (clicked && lastClick.Elapsed >= TimeSpan.FromSeconds(1) &&
                    details.IsToggled == true && !toggledAfterClick)
                {
                    // Some UIA providers select the row without opening the pane. Toggle once,
                    // but never close a pane that the row click has already opened.
                    details.Toggle();
                    toggledAfterClick = true;
                    return false;
                }

                if (clicked && lastClick.Elapsed < TimeSpan.FromSeconds(3)) return false;

                var task = session.MainWindow
                    .FindAllDescendants(session.ConditionFactory.ByAutomationId("InlineTaskTitleTextBlock"))
                    .FirstOrDefault(e => e.Name == title && Visible(e));
                if (task is null) return false;
                task.Focus();
                task.Click();
                clicked = true;
                toggledAfterClick = false;
                lastClick.Restart();
                return false;
            }, "The selected task card did not open.", TimeSpan.FromSeconds(15));
        }
        Find(session, "CurrentTaskParentsRelationAddButton")!.AsButton().Invoke();
        Wait(() => Visible(Find(session, "CurrentTaskParentsRelationAddInput")),
            "The task relation editor did not respond.", TimeSpan.FromSeconds(15));
        Find(session, "CurrentTaskParentsRelationAddCancelButton")!.AsButton().Invoke();
        Wait(() => !Visible(Find(session, "CurrentTaskParentsRelationAddInput")),
            "The task relation editor did not close.", TimeSpan.FromSeconds(15));
        CheckErrors(session);
        return readyMs;
    }

    private static AutomationElement? Find(DesktopAppSession session, string id) =>
        session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId(id));

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool Visible(AutomationElement? element) =>
        element is not null && !element.Properties.IsOffscreen.ValueOrDefault;

    private static void CheckErrors(DesktopAppSession session)
    {
        if (Visible(Find(session, "ToastNotificationError")) || Visible(Find(session, "TaskSpaceRecoveryOverlay")))
            throw new InvalidOperationException("The app reported an error instead of loading tasks.");
    }

    private static void Wait(Func<bool> predicate, string failure, TimeSpan? timeout = null)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            try { if (predicate()) return; }
            catch (COMException) { /* Avalonia may replace its UIA tree while switching. */ }
            catch (NoClickablePointException) { /* Retry after layout gives the element a clickable point. */ }
            Thread.Sleep(100);
        } while (watch.Elapsed < (timeout ?? TimeSpan.FromMinutes(10)));
        throw new TimeoutException(failure);
    }

    private static void WriteMeasurement(DesktopAppSession session, string scenario, double readyMs, double readyAndActionMs, double? windowAvailableMs, int copiedFiles)
    {
        using var process = Process.GetProcessById(session.MainWindow.Properties.ProcessId.ValueOrDefault);
        var peakPrivateMemoryBytes = GetPeakPrivateMemoryBytes(process);
        var json = JsonSerializer.Serialize(new
        {
            label = Environment.GetEnvironmentVariable("UNLIMOTION_LOADING_LABEL") ?? "smoke", scenario,
            readyMs, readyAndActionMs, windowAvailableMs, copiedFiles,
            privateMemoryBytes = process.PrivateMemorySize64,
            peakPrivateMemoryBytes,
            peakWorkingSetBytes = process.PeakWorkingSet64,
            utc = DateTime.UtcNow
        });
        Console.WriteLine(json);
        var report = Environment.GetEnvironmentVariable("UNLIMOTION_LOADING_REPORT");
        if (!string.IsNullOrWhiteSpace(report)) File.AppendAllText(report, json + Environment.NewLine);
    }

    private static long GetPeakPrivateMemoryBytes(Process process)
    {
        var counters = new ProcessMemoryCounters
        {
            Size = (uint)Marshal.SizeOf<ProcessMemoryCounters>()
        };
        if (!GetProcessMemoryInfo(process.Handle, ref counters, counters.Size))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        // Win32 PeakPagefileUsage is the process's peak committed private memory.
        return checked((long)counters.PeakPagefileUsage.ToUInt64());
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(
        IntPtr process,
        ref ProcessMemoryCounters counters,
        uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint Size;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
    }
}
