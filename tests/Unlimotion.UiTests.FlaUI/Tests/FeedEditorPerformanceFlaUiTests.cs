using AppAutomation.FlaUI.Session;
using AppAutomation.Session.Contracts;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;

namespace Unlimotion.UiTests.FlaUI.Tests;

public sealed class FeedEditorPerformanceFlaUiTests
{
    [Test]
    [NotInParallel("DesktopUi")]
    public void EightHundredNotes_RecordNativeFirstAndThirtyWarmClickToFocusSamples()
    {
        EnsurePhysicalPixelDpiAwareness();
        var today = DateOnly.FromDateTime(DateTime.Now);
        string? dailyPath = null;
        string? original = null;
        using var session = DesktopAppSession.Launch(
            UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(
                UnlimotionAutomationScenario.Feed,
                language: "ru", buildBeforeLaunch: false,
                mainWindowTimeout: TimeSpan.FromSeconds(120), theme: "Light",
                windowPlacement: DesktopWindowPlacement.Centered(1200, 800),
                feedVaultPrepared: root =>
                {
                    var date = today;
                    for (var note = 0; note < 800; note++)
                    {
                        // Include today on weekends as well, so the native first viewport is deterministic.
                        if (note > 0)
                            while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) date = date.AddDays(-1);
                        var lines = note == 0 ? 200 : 1 + note % 200;
                        var text = new StringBuilder();
                        for (var line = 0; line < lines; line++)
                            text.AppendLine($"День {note}, строка {line}: синтетическая мысль для проверки редактора.");
                        var path = Path.Combine(root, UnlimotionAutomationScenarioData.GetFeedDailyRelativePath(date));
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.WriteAllText(path, text.ToString());
                        if (note == 0) { dailyPath = path; original = text.ToString(); }
                        date = date.AddDays(-1);
                    }
                }));
        session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
        FeedReadingPolishFlaUiTests.Resize(session.MainWindow, 1200, 800);
        session.MainWindow.Focus();
        var feedMode = WaitForElement(session, "FeedModeButton");
        feedMode.AsRadioButton().IsChecked = true;
        var prefix = $"FeedDay-{today:yyyyMMdd}-Markdown";
        // Native UIA exposes the rendered TextBlock, not the surrounding ContentControl preview.
        // The fixture is one 200-line paragraph, so its editor has the stable block index zero.
        var previewId = prefix + "-BlockPreview-0";
        var editorId = prefix + "-BlockEditor-0";
        var first = ClickToFocus();
        Console.WriteLine($"NATIVE_EDITOR_FIRST observedFocusMs={first:F2}");
        var samples = new List<double>();
        for (var sample = 0; sample < 30; sample++)
        {
            Keyboard.Press(VirtualKeyShort.ESCAPE);
            Keyboard.Release(VirtualKeyShort.ESCAPE);
            samples.Add(ClickToFocus());
            Console.WriteLine($"NATIVE_EDITOR_WARM sample={sample + 1}; observedFocusMs={samples[^1]:F2}");
        }
        var ordered = samples.Order().ToArray();
        var timing = $"NATIVE_EDITOR_TIMING runtime={RuntimeInformation.FrameworkDescription}; os={RuntimeInformation.OSDescription}; cpuCount={Environment.ProcessorCount}; clientBounds={session.MainWindow.BoundingRectangle}; generatedDailyNotes=800; lines=1..200; preview={previewId}; firstInteractionMs={first:F2}; warmSamples=30; p50Ms={ordered[14]:F2}; p95Ms={ordered[28]:F2}; maxMs={ordered[^1]:F2}; allMs=[{string.Join(",", samples.Select(value => value.ToString("F2", CultureInfo.InvariantCulture)))}]";
        var budget = $"NATIVE_EDITOR_BUDGET firstUnder250ms={first <= 250}; warmP95Under100ms={ordered[28] <= 100}";
        var boundary = "TIMING_BOUNDARY: fresh app process; first editor gesture after vault indexing and preview readiness. Measured input injection to observed native UIA keyboard focus, including injection/UIA polling overhead; not a rendered caret/frame timestamp. Test success alone does not establish the printed latency budgets.";
        Console.WriteLine(timing);
        Console.WriteLine(budget);
        Console.WriteLine(boundary);
        var timingEvidence = Environment.GetEnvironmentVariable("UNLIMOTION_FEED_TIMING_EVIDENCE_PATH");
        if (!string.IsNullOrWhiteSpace(timingEvidence))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(timingEvidence)!);
            File.WriteAllLines(timingEvidence, [timing, budget, boundary]);
        }
        if (File.ReadAllText(dailyPath!) != original)
            throw new InvalidOperationException("Click/focus/cancel performance checks must not mutate Markdown.");

        double ClickToFocus()
        {
            var preview = FindVisiblePreview(session, previewId, editorId);
            var viewport = session.MainWindow.FindFirstDescendant(
                session.ConditionFactory.ByAutomationId("FeedChronologyList"))?.BoundingRectangle
                ?? session.MainWindow.BoundingRectangle;
            var bounds = Rectangle.Intersect(preview.BoundingRectangle, viewport);
            if (bounds.Width <= 30 || bounds.Height <= 0)
                throw new InvalidOperationException("The timed preview has no clickable visible intersection.");
            Mouse.MoveTo(new Point((int)bounds.Left + 30, (int)bounds.Top + Math.Min(10, (int)bounds.Height / 2)));
            var timer = Stopwatch.StartNew();
            Mouse.Down(MouseButton.Left);
            Mouse.Up(MouseButton.Left);
            var injectionMs = timer.Elapsed.TotalMilliseconds;
            string? lastFocus = null;
            while (timer.Elapsed < TimeSpan.FromSeconds(15))
            {
                try
                {
                    var editor = session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId(editorId));
                    lastFocus = editor is null ? "editor=null" : $"hasKeyboardFocus={editor.Properties.HasKeyboardFocus.ValueOrDefault}; process={editor.Properties.ProcessId.ValueOrDefault}";
                    if (editor is not null && editor.Properties.HasKeyboardFocus.ValueOrDefault
                        && editor.Properties.ProcessId.ValueOrDefault == session.MainWindow.Properties.ProcessId.ValueOrDefault)
                        return timer.Elapsed.TotalMilliseconds;
                }
                catch (COMException error) when (timer.Elapsed < TimeSpan.FromSeconds(15)) { lastFocus = error.GetType().Name + ": " + error.Message; }
                catch (Win32Exception error) when (timer.Elapsed < TimeSpan.FromSeconds(15)) { lastFocus = error.GetType().Name + ": " + error.Message; }
                Thread.Sleep(2);
            }
            CaptureFailure(session, "native-editor-focus-failure.png");
            Console.WriteLine($"NATIVE_FOCUS_FAILURE previewBounds={preview.BoundingRectangle}; visibleBounds={bounds}; viewport={viewport}; injectionMs={injectionMs:F2}; lastFocus={lastFocus}");
            throw new TimeoutException("A native pointer click did not focus the expected block editor.");
        }
    }

    private static AutomationElement FindVisiblePreview(DesktopAppSession session, string id, string editorId)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(30))
        {
            var viewport = session.MainWindow.FindFirstDescendant(
                session.ConditionFactory.ByAutomationId("FeedChronologyList"))?.BoundingRectangle
                ?? session.MainWindow.BoundingRectangle;
            var preview = session.MainWindow.FindAllDescendants()
                .Where(element => (element.Properties.AutomationId.ValueOrDefault == id
                    || element.Properties.ControlType.ValueOrDefault == ControlType.Text
                    && element.Properties.Name.ValueOrDefault?.StartsWith("День 0, строка 0:", StringComparison.Ordinal) == true)
                    && !element.Properties.IsOffscreen.ValueOrDefault)
                .Where(element => Rectangle.Intersect(element.BoundingRectangle, viewport) is var visible
                    && visible.Width > 30 && visible.Height > 0)
                .OrderBy(element => element.BoundingRectangle.Top).FirstOrDefault();
            var editor = session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId(editorId));
            if (preview is not null && editor?.Properties.HasKeyboardFocus.ValueOrDefault != true)
                return preview;
            Thread.Sleep(100);
        }
        CaptureFailure(session, "native-editor-preview-failure.png");
        foreach (var element in session.MainWindow.FindAllDescendants().Take(300))
            Console.WriteLine($"NATIVE_PREVIEW_DIAGNOSTIC id={element.Properties.AutomationId.ValueOrDefault}; type={element.Properties.ControlType.ValueOrDefault}; bounds={element.BoundingRectangle}");
        throw new TimeoutException($"No rendered native preview for '{id}' intersects the viewport outside editing.");
    }

    private static void CaptureFailure(DesktopAppSession session, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("UNLIMOTION_FEED_POLISH_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            using var capture = global::FlaUI.Core.Capturing.Capture.Element(session.MainWindow,
                new global::FlaUI.Core.Capturing.CaptureSettings());
            capture.ToFile(Path.Combine(directory, fileName));
        }
    }

    private static AutomationElement WaitForElement(DesktopAppSession session, string id, string? notFocusedId = null)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(90))
        {
            var element = session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId(id));
            if (element is not null && !element.Properties.IsOffscreen.ValueOrDefault
                && element.BoundingRectangle.Width > 0 && element.BoundingRectangle.Height > 0
                && (notFocusedId is null || session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId(notFocusedId))?.Properties.HasKeyboardFocus.ValueOrDefault != true))
                return element;
            Thread.Sleep(50);
        }
        throw new TimeoutException($"Native element '{id}' did not become visible.");
    }

    private static void EnsurePhysicalPixelDpiAwareness()
    {
        if (!SetProcessDpiAwarenessContext(new IntPtr(-4))
            && GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()) != 2)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Per-monitor DPI awareness is required for native pointer timing.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
}
