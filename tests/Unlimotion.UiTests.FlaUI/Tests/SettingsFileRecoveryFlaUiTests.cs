using AppAutomation.FlaUI.Session;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;

namespace Unlimotion.UiTests.FlaUI.Tests;

public sealed class SettingsFileRecoveryFlaUiTests
{
    [Test]
    [Arguments(SettingsFileDamage.EmptyWithBackup)]
    [Arguments(SettingsFileDamage.TruncatedWithBackup)]
    [Arguments(SettingsFileDamage.MissingWithBackup)]
    [NotInParallel("DesktopUi")]
    public void Empty_settings_restores_backup_and_opens_the_original_task(SettingsFileDamage damage)
    {
        using var fixture = new SettingsFileRecoveryTestFixture(damage);
        using (var session = DesktopAppSession.Launch(fixture.LaunchOptions))
        {
            AssertOriginalTask(session);
            AssertRecoveryWarning(session, "warning-" + damage);

            AssertTaskPath(fixture.ConfigPath, fixture.TasksPath);
            AssertTaskPath(fixture.BackupPath, fixture.TasksPath);
            var copies = fixture.CorruptCopies();
            if (fixture.InitialMain == null ? copies.Length != 0 :
                copies.Length != 1 || !File.ReadAllBytes(copies[0]).SequenceEqual(fixture.InitialMain))
            {
                throw new InvalidOperationException("The original damaged bytes were not preserved exactly once.");
            }

            CaptureEvidence(session, "restored-" + damage);
            WaitFor(session, "SettingsRecoveredWarningClose").AsButton().Invoke();
            var dismissed = Retry.WhileTrue(
                () => FindRecoveryWarningClose(session) != null,
                timeout: TimeSpan.FromSeconds(5), interval: TimeSpan.FromMilliseconds(100),
                throwOnTimeout: false);
            if (!dismissed.Success)
            {
                throw new InvalidOperationException("The recovery warning could not be dismissed.");
            }
        }

        var originalCopyCount = fixture.CorruptCopies().Length;
        using (var restarted = DesktopAppSession.Launch(fixture.LaunchOptions))
        {
            AssertOriginalTask(restarted);
            if (FindRecoveryWarningClose(restarted) != null ||
                fixture.CorruptCopies().Length != originalCopyCount)
            {
                throw new InvalidOperationException("A normal restart repeated recovery or its warning.");
            }
            AssertTaskPath(fixture.ConfigPath, fixture.TasksPath);
        }
    }

    [Test]
    [Arguments(SettingsFileDamage.EmptyWithoutBackup)]
    [Arguments(SettingsFileDamage.TruncatedWithoutBackup)]
    [Arguments(SettingsFileDamage.InvalidRootWithoutBackup)]
    [Arguments(SettingsFileDamage.MissingWithInvalidBackup)]
    [Arguments(SettingsFileDamage.ValidWithRestrictedBackup)]
    [NotInParallel("DesktopUi")]
    public void Unrecoverable_settings_opens_early_shell_without_touching_profile(SettingsFileDamage damage)
    {
        using var fixture = new SettingsFileRecoveryTestFixture(damage);
        using (var session = DesktopAppSession.Launch(fixture.LaunchOptions))
        {
            var view = WaitFor(session, "SettingsRecoveryView");
            var message = WaitFor(session, "SettingsRecoveryMessage");
            var path = WaitFor(session, "SettingsRecoveryPath");
            var pathText = path.Patterns.Value.IsSupported ? path.Patterns.Value.Pattern.Value : path.Name;
            if (string.IsNullOrWhiteSpace(message.Name) || pathText != fixture.ConfigPath)
            {
                throw new InvalidOperationException("The recovery shell must explain the failure and show the exact profile path.");
            }
            if (damage == SettingsFileDamage.ValidWithRestrictedBackup &&
                message.Name != "The settings or backup cannot be accessed with the required permissions. Check file access and restart the application. Your settings have not been reset." &&
                message.Name != "Не удалось получить доступ к настройкам или резервной копии с необходимыми правами. Проверьте права доступа к файлам и запустите приложение снова. Настройки не сброшены.")
            {
                throw new InvalidOperationException("Different backup rights must show the permissions error, not a corrupt-JSON error.");
            }

            var buttons = view.FindAllDescendants(session.ConditionFactory.ByControlType(ControlType.Button));
            if (buttons.Length != 2 || FindVisible(session, "SettingsRecoveryOpenFolder") == null ||
                FindVisible(session, "SettingsRecoveryClose") == null)
            {
                throw new InvalidOperationException("The early shell must offer exactly Open folder and Close.");
            }
            if (FindVisible(session, "CurrentTaskTitleTextBox") != null ||
                FindVisible(session, "AllTasksTabItem") != null || FindVisible(session, "TaskSpaceRecoveryOverlay") != null)
            {
                throw new InvalidOperationException("Task controls must not be created for unreadable settings.");
            }
            CaptureEvidence(session, "blocked-" + damage);
        }

        AssertSameBytes(fixture.InitialMain, SettingsFileRecoveryTestFixture.ReadOptional(fixture.ConfigPath));
        AssertSameBytes(fixture.InitialBackup, SettingsFileRecoveryTestFixture.ReadOptional(fixture.BackupPath));
        if (fixture.InitialTaskFiles != fixture.TaskFilesFingerprint() || fixture.CorruptCopies().Length != 0)
        {
            throw new InvalidOperationException("Blocked startup modified the synthetic tasks or created a recovery copy.");
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public void Valid_settings_bootstraps_backup_and_restarts_without_warning()
    {
        using var fixture = new SettingsFileRecoveryTestFixture(SettingsFileDamage.None);
        for (var start = 0; start < 2; start++)
        {
            using var session = DesktopAppSession.Launch(fixture.LaunchOptions);
            AssertOriginalTask(session);
            AssertTaskPath(fixture.ConfigPath, fixture.TasksPath);
            AssertTaskPath(fixture.BackupPath, fixture.TasksPath);
            if (FindRecoveryWarningClose(session) != null || fixture.CorruptCopies().Length != 0)
            {
                throw new InvalidOperationException("Valid settings should not require recovery.");
            }
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public void Characterize_normal_startup_before_and_after_when_baseline_is_supplied()
    {
        var baseline = Environment.GetEnvironmentVariable("UNLIMOTION_SETTINGS_BASELINE_EXECUTABLE");
        if (string.IsNullOrWhiteSpace(baseline))
        {
            Skip.Test("Set UNLIMOTION_SETTINGS_BASELINE_EXECUTABLE for the opt-in before/after startup comparison.");
        }
        var candidate = Environment.GetEnvironmentVariable("UNLIMOTION_SETTINGS_RECOVERY_EXECUTABLE");
        var directory = Environment.GetEnvironmentVariable("UNLIMOTION_SETTINGS_RECOVERY_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The startup comparison requires the final published executable and an artifact directory.");
        }

        var samples = new List<StartupTiming>();
        samples.Add(MeasureStartup(baseline!, "before", warmup: true, pair: 0));
        samples.Add(MeasureStartup(candidate, "after", warmup: true, pair: 0));
        for (var pair = 1; pair <= 2; pair++)
        {
            if (pair == 1)
            {
                samples.Add(MeasureStartup(baseline!, "before", warmup: false, pair));
                samples.Add(MeasureStartup(candidate, "after", warmup: false, pair));
            }
            else
            {
                samples.Add(MeasureStartup(candidate, "after", warmup: false, pair));
                samples.Add(MeasureStartup(baseline!, "before", warmup: false, pair));
            }
        }

        Directory.CreateDirectory(directory);
        var outputPath = Path.Combine(directory, "normal-startup-before-after.json");
        File.WriteAllText(outputPath, JsonSerializer.Serialize(new
        {
            RecordedAtUtc = DateTimeOffset.UtcNow,
            Metric = "DesktopAppSession.Launch to visible original-task title; milliseconds",
            BaselineExecutable = Path.GetFullPath(baseline!),
            CandidateExecutable = Path.GetFullPath(candidate),
            IndependentSyntheticProfilePerLaunch = true,
            Samples = samples
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Normal startup comparison: " + outputPath);
    }

    private static StartupTiming MeasureStartup(string executable, string version, bool warmup, int pair)
    {
        using var fixture = new SettingsFileRecoveryTestFixture(SettingsFileDamage.None, executable);
        var watch = Stopwatch.StartNew();
        using var session = DesktopAppSession.Launch(fixture.LaunchOptions);
        AssertOriginalTask(session);
        watch.Stop();
        // Characterization only: timing thresholds would be unreliable on a shared desktop.
        return new StartupTiming(version, warmup, pair, watch.Elapsed.TotalMilliseconds);
    }

    private sealed record StartupTiming(string Version, bool Warmup, int Pair, double Milliseconds);

    private static void AssertOriginalTask(DesktopAppSession session)
    {
        var title = Retry.WhileNull(
            () => FindVisible(session, "CurrentTaskTitleTextBox") is { } element &&
                  element.AsTextBox().Text == UnlimotionAppLaunchHost.CurrentTaskTitle ? element : null,
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromMilliseconds(100),
            throwOnTimeout: false).Result;
        if (title == null || title.AsTextBox().Text != UnlimotionAppLaunchHost.CurrentTaskTitle)
        {
            throw new InvalidOperationException("Recovery did not reopen the original task.");
        }
    }

    private static AutomationElement WaitFor(DesktopAppSession session, string automationId) =>
        Retry.WhileNull(() => FindVisible(session, automationId),
            timeout: TimeSpan.FromSeconds(15), interval: TimeSpan.FromMilliseconds(100),
            throwOnTimeout: false).Result ??
        throw new InvalidOperationException($"Visible control '{automationId}' was not found.");

    private static AutomationElement? FindVisible(DesktopAppSession session, string automationId)
    {
        var control = session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId(automationId));
        return control is { IsAvailable: true } && !control.Properties.IsOffscreen.ValueOrDefault ? control : null;
    }

    private static AutomationElement? FindRecoveryWarningClose(DesktopAppSession session) =>
        FindVisible(session, "SettingsRecoveredWarningClose");

    private static void AssertRecoveryWarning(DesktopAppSession session, string scenario)
    {
        // Avalonia's decorative Border uses NoneAutomationPeer and is absent from UIA's control view.
        // Its close button is the stable native anchor. The native tree can flatten the message and
        // button into different parents, so use exact content plus visible, overlapping row geometry.
        var close = WaitFor(session, "SettingsRecoveredWarningClose");
        var expectedMessages = new[]
        {
            "Settings were restored from a backup. Your latest settings changes may not have been saved.",
            "Настройки восстановлены из резервной копии. Последние изменения настроек могли не сохраниться."
        };
        var messages = session.MainWindow.FindAllDescendants(
            session.ConditionFactory.ByName(expectedMessages[0]).Or(
                session.ConditionFactory.ByName(expectedMessages[1])));
        var closeBounds = close.BoundingRectangle;
        if (!messages.Any(message => !message.Properties.IsOffscreen.ValueOrDefault &&
                                     message.BoundingRectangle.Width > 0 && message.BoundingRectangle.Height > 0 &&
                                     message.BoundingRectangle.Top < closeBounds.Bottom &&
                                     message.BoundingRectangle.Bottom > closeBounds.Top))
        {
            var diagnostics = DescribeWarningAutomation(session, close);
            var directory = Environment.GetEnvironmentVariable("UNLIMOTION_SETTINGS_RECOVERY_ARTIFACTS");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, scenario + ".uia.log"), diagnostics);
            }
            throw new InvalidOperationException(
                "The visible recovery warning did not explain restoration and possible lost changes. " + diagnostics);
        }
    }

    private static string DescribeWarningAutomation(DesktopAppSession session, AutomationElement close)
    {
        static string Describe(AutomationElement element)
        {
            var name = element.Name ?? string.Empty;
            if (name.Length > 180) name = name[..180];
            var id = element.AutomationId ?? string.Empty;
            if (id.Length > 80) id = id[..80];
            return $"{element.ControlType}: id={id}; name={name}; offscreen={element.Properties.IsOffscreen.ValueOrDefault}; bounds={element.BoundingRectangle}";
        }

        var output = new StringBuilder("Warning ancestry:\n");
        AutomationElement? ancestor = close;
        for (var level = 0; ancestor != null && level < 5; ancestor = ancestor.Parent, level++)
        {
            output.AppendLine(Describe(ancestor));
        }
        output.AppendLine("Synthetic window controls around the warning row:");
        var bounds = close.BoundingRectangle;
        foreach (var element in session.MainWindow.FindAllDescendants()
                     .Where(element => element.BoundingRectangle.Top < bounds.Bottom + 50 &&
                                       element.BoundingRectangle.Bottom > bounds.Top - 50)
                     .Take(40))
        {
            output.AppendLine(Describe(element));
        }
        return output.ToString(0, Math.Min(output.Length, 12000));
    }

    private static void AssertTaskPath(string configPath, string expectedPath)
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(configPath));
        if (json.RootElement.GetProperty("TaskStorage").GetProperty("Path").GetString() != expectedPath)
        {
            throw new InvalidOperationException("Settings changed the original task-storage path.");
        }
    }

    private static void AssertSameBytes(byte[]? expected, byte[]? actual)
    {
        if (expected == null ? actual != null : actual == null || !expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException("Blocked startup modified the settings file.");
        }
    }

    private static void CaptureEvidence(DesktopAppSession session, string scenario)
    {
        var directory = Environment.GetEnvironmentVariable("UNLIMOTION_SETTINGS_RECOVERY_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        session.MainWindow.Focus();

        var recorder = Environment.GetEnvironmentVariable("UNLIMOTION_WINDOW_RECORDER_SCRIPT");
        if (string.IsNullOrWhiteSpace(recorder))
        {
            return;
        }

        var startInfo = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-File", recorder,
                     // The recorder strips one extension; keep .Desktop as part of the process name.
                     "-ProcessName", "Unlimotion.Desktop.exe", "-WindowTitle", session.MainWindow.Title,
                     "-Output", Path.Combine(directory, scenario + ".mp4"),
                     "-DurationSeconds", "4", "-Fps", "15", "-NoCursor"
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Recorder did not start.");
        var standardOutput = DrainBoundedOutputAsync(process.StandardOutput);
        var standardError = DrainBoundedOutputAsync(process.StandardError);
        var completed = process.WaitForExit(30000);
        if (!completed)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        var streamsCompleted = Task.WhenAll(standardOutput, standardError).Wait(TimeSpan.FromSeconds(5));
        var output = streamsCompleted ? standardOutput.Result : "Recorder stdout did not close.";
        var error = streamsCompleted ? standardError.Result : "Recorder stderr did not close.";
        var diagnostics = SanitizeRecorderOutput(output + Environment.NewLine + error);
        File.WriteAllText(Path.Combine(directory, scenario + ".recorder.log"), diagnostics);
        if (!completed)
        {
            throw new TimeoutException("Window recorder did not finish within 30 seconds. " + diagnostics);
        }
        if (!streamsCompleted || process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Window recorder exited with code {process.ExitCode}. {diagnostics}");
        }
    }

    private static async Task<string> DrainBoundedOutputAsync(StreamReader reader)
    {
        const int limit = 12000;
        var output = new StringBuilder();
        var buffer = new char[1024];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) != 0)
        {
            output.Append(buffer, 0, count);
            if (output.Length > limit)
            {
                output.Remove(0, output.Length - limit);
            }
        }
        return output.ToString();
    }

    private static string SanitizeRecorderOutput(string output)
    {
        // The optional recorder lists unrelated window titles if no match exists. Keep the cause,
        // but do not copy that desktop inventory into synthetic test artifacts or the test report.
        var inventory = output.IndexOf("Visible windows:", StringComparison.OrdinalIgnoreCase);
        if (inventory >= 0)
        {
            return output[..inventory] + "[unrelated window inventory omitted]";
        }
        return output;
    }
}
