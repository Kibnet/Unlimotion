using AppAutomation.FlaUI.Session;
using AppAutomation.Session.Contracts;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;

namespace Unlimotion.UiTests.FlaUI.Tests;

public sealed class RepeaterStartDateFlaUiTests
{
    [Test]
    [NotInParallel("DesktopUi")]
    public async Task ClearingStart_HidesRepeater_AndRestoringDateDoesNotRestorePattern()
    {
        EnsurePhysicalPixelDpiAwareness();
        const string taskId = "3445eef8-4382-4607-b2fb-37a820467f1c";
        var evidence = Environment.GetEnvironmentVariable("UNLIMOTION_REPEATER_EVIDENCE_DIR");
        if (evidence is not null) Directory.CreateDirectory(evidence);
        var options = UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(
            currentTaskId: taskId, language: "ru", buildBeforeLaunch: false,
            windowPlacement: DesktopWindowPlacement.Centered(1280, 800));
        var configPath = options.Arguments.Single(arg => arg.StartsWith("--config="))[9..];
        var config = JsonNode.Parse(File.ReadAllText(configPath))!;
        config["Appearance"]!["Language"] = "ru";
        File.WriteAllText(configPath, config.ToJsonString());
        var tasksPath = config["TaskStorage"]!["Path"]!.GetValue<string>();
        var taskPath = Path.Combine(tasksPath, taskId);
        var seed = JsonNode.Parse(File.ReadAllText(taskPath))!;
        seed["Title"] = "Проверка повторения";
        File.WriteAllText(taskPath, seed.ToJsonString());

        using var session = DesktopAppSession.Launch(options);
        session.MainWindow.Focus();
        var failures = new List<string>();
        Exception? scenarioFailure = null;
        try
        {
            await Until(() => Find(session, "CurrentTaskSetBeginButton") is not null, 30);
            Capture(session, evidence, "dated.png");
            Check(IsVisible(session, "CurrentTaskRepeaterSection"), "Initial repeater section is missing.");

            // The date picker itself has no UIA peer; use its stable quick-action button.
            await SelectBeginMenuItem("Нет");
            await Until(() => ReadTask(taskPath) is { } cleared && cleared["PlannedBeginDateTime"] is null, 20);
            Capture(session, evidence, "cleared.png");
            Check(!IsVisible(session, "CurrentTaskRepeaterSection"), "Repeater section remains visible after clearing start.");
            Check(ReadTask(taskPath) is { } clearedTask && clearedTask["Repeater"] is null,
                "Repeater was not reset in the saved task.");

            await SelectBeginMenuItem("Сегодня");
            await Until(() => ReadTask(taskPath)?["PlannedBeginDateTime"] is not null, 20);
            Capture(session, evidence, "restored-date.png");
            Check(IsVisible(session, "CurrentTaskRepeaterSection"), "Repeater section did not return with the date.");
            Check(ReadTask(taskPath) is { } restoredTask && restoredTask["Repeater"] is null,
                "Restoring date revived the old repeater.");
            await Assert.That(failures).IsEmpty();
        }
        catch (Exception exception)
        {
            scenarioFailure = exception;
            try { Capture(session, evidence, "failure.png"); }
            catch (Exception captureFailure) { Console.Error.WriteLine(captureFailure); }
            throw;
        }
        finally
        {
            if (evidence is not null)
            {
                File.WriteAllText(Path.Combine(evidence, "complete.json"), JsonSerializer.Serialize(new
                {
                    Success = scenarioFailure is null, Error = scenarioFailure?.ToString(), Failures = failures
                }));
            }
        }

        void Check(bool value, string message)
        {
            if (!value) failures.Add(message);
        }

        async Task SelectBeginMenuItem(string name)
        {
            session.MainWindow.Focus();
            Find(session, "CurrentTaskSetBeginButton")!.AsButton().Invoke();
            AutomationElement? item = null;
            await Until(() => (item = session.MainWindow.FindFirstDescendant(
                session.ConditionFactory.ByControlType(ControlType.MenuItem).And(
                session.ConditionFactory.ByName(name)))) is { IsOffscreen: false });
            item!.Focus();
            await Until(() => item.Properties.HasKeyboardFocus.ValueOrDefault);
            Keyboard.Press(VirtualKeyShort.RETURN);
        }
    }

    private static AutomationElement? Find(DesktopAppSession session, string id) =>
        session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId(id));

    private static bool IsVisible(DesktopAppSession session, string id) =>
        Find(session, id) is { IsOffscreen: false };

    private static JsonNode? ReadTask(string path)
    {
        try { return JsonNode.Parse(File.ReadAllText(path)); }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    private static void Capture(DesktopAppSession session, string? directory, string name)
    {
        if (directory is null) return;
        Find(session, "CurrentTaskDetailsScrollViewer")?.Patterns.Scroll.PatternOrDefault?.SetScrollPercent(-1, 50);
        Thread.Sleep(150); // Let the scrolled planning section render before PrintWindow captures it.
        var handle = session.MainWindow.Properties.NativeWindowHandle.Value;
        if (!GetWindowRect(handle, out var bounds)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var bitmap = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        using var graphics = Graphics.FromImage(bitmap);
        var dc = graphics.GetHdc();
        try
        {
            // Never fall back to desktop pixels: another application may obscure this window.
            if (!PrintWindow(handle, dc, 2)) throw new InvalidOperationException("Safe window capture failed.");
        }
        finally { graphics.ReleaseHdc(dc); }
        bitmap.Save(Path.Combine(directory, name));
    }

    private static async Task Until(Func<bool> condition, int seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Repeater UI condition did not become true.");
            await Task.Delay(100);
        }
    }

    private static void EnsurePhysicalPixelDpiAwareness()
    {
        if (!SetProcessDpiAwarenessContext(new IntPtr(-4)) &&
            GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()) != 2)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Per-monitor DPI awareness is required for UI evidence.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect bounds);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr handle, IntPtr dc, uint flags);
}
