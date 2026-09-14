using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AppAutomation.FlaUI.Session;
using AppAutomation.Session.Contracts;
using FlaUI.Core.AutomationElements;
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
            if (!string.IsNullOrWhiteSpace(executable))
                options = new DesktopAppLaunchOptions
                {
                    ExecutablePath = executable, WorkingDirectory = Path.GetDirectoryName(executable)!,
                    Arguments = options.Arguments, EnvironmentVariables = options.EnvironmentVariables,
                    MainWindowTimeout = options.MainWindowTimeout, PollInterval = options.PollInterval,
                    WindowPlacement = options.WindowPlacement, DisposeCallback = options.DisposeCallback
                };

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
            WriteMeasurement("startup", readyMs, watch.Elapsed.TotalMilliseconds, windowAvailableMs, copied);
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
        target.Click();
        var readyMs = ReadyAndAct(session, title, watch);
        WriteMeasurement(space, readyMs, watch.Elapsed.TotalMilliseconds, null, copied);
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
        if (Find(session, "CurrentTaskTitleTextBox")?.AsTextBox().Text != title)
        {
            var task = session.MainWindow.FindAllDescendants(session.ConditionFactory.ByAutomationId("InlineTaskTitleTextBlock"))
                .First(e => e.Name == title && Visible(e));
            task.Click();
        }
        var details = Find(session, "DetailsPaneToggleButton")!.AsToggleButton();
        if (details.IsToggled == true) details.Toggle();
        Wait(() => Find(session, "CurrentTaskTitleTextBox")?.AsTextBox().Text == title,
            "The selected task card did not open.", TimeSpan.FromSeconds(15));
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
            Thread.Sleep(100);
        } while (watch.Elapsed < (timeout ?? TimeSpan.FromMinutes(10)));
        throw new TimeoutException(failure);
    }

    private static void WriteMeasurement(string scenario, double readyMs, double readyAndActionMs, double? windowAvailableMs, int copiedFiles)
    {
        var json = JsonSerializer.Serialize(new
        {
            label = Environment.GetEnvironmentVariable("UNLIMOTION_LOADING_LABEL") ?? "smoke",
            scenario, readyMs, readyAndActionMs, windowAvailableMs, copiedFiles, utc = DateTime.UtcNow
        });
        Console.WriteLine(json);
        var report = Environment.GetEnvironmentVariable("UNLIMOTION_LOADING_REPORT");
        if (!string.IsNullOrWhiteSpace(report)) File.AppendAllText(report, json + Environment.NewLine);
    }
}
