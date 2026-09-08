using AppAutomation.FlaUI.Session;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;

namespace Unlimotion.UiTests.FlaUI.Tests;

public sealed class FeedReadingPolishFlaUiTests
{
    private const string EvidenceDirectoryVariable = "UNLIMOTION_FEED_POLISH_EVIDENCE_DIR";

    [Test]
    [Arguments(520)]
    [Arguments(1000)]
    [Arguments(1400)]
    [NotInParallel("DesktopUi")]
    public void Long_day_returns_to_heading_and_search_hides_chronology_filter(int width)
    {
        EnsurePhysicalPixelDpiAwareness();
        var today = DateOnly.FromDateTime(DateTime.Now);
        string? dailyPath = null;
        string? original = null;
        using var session = DesktopAppSession.Launch(
            UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(
                UnlimotionAutomationScenario.Feed,
                language: "ru",
                buildBeforeLaunch: false,
                mainWindowTimeout: TimeSpan.FromSeconds(90),
                theme: "Light",
                feedVaultPrepared: root =>
                {
                    dailyPath = Path.Combine(root,
                        UnlimotionAutomationScenarioData.GetFeedDailyRelativePath(today));
                    original = $"---\nunlimotion-id: reading-polish-{today:yyyyMMdd}\nareas: [area-unlimotion]\n---\n" +
                               $"# {today:yyyy-MM-dd}\n\n" +
                               string.Concat(Enumerable.Range(1, 100).Select(index =>
                                   $"Строка {index} — синтетическая запись для проверки чтения длинного дня.\n\n"));
                    File.WriteAllText(dailyPath, original);
                }));
        try
        {
            session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
            Resize(session.MainWindow, width, 850);
            session.MainWindow.Focus();
            WaitUntil(() => Visible(session, "FeedModeButton") || Visible(session, "GlobalOverflowMenuButton"),
                "Shell mode navigation did not become ready.");
            if (Visible(session, "FeedModeButton"))
            {
                Find(session, "FeedModeButton")!.AsRadioButton().IsChecked = true;
            }
            else
            {
                Activate(Find(session, "GlobalOverflowMenuButton")!);
                WaitUntil(() => Visible(session, "GlobalFeedModeMenuItem"), "Feed mode is absent from shell overflow.");
                Find(session, "GlobalFeedModeMenuItem")!.AsMenuItem().Click();
            }

            var metadataId = $"FeedDay-{today:yyyyMMdd}-ServiceDataToggle";
            var headingId = $"FeedDay-{today:yyyyMMdd}-DateText";
            var firstBlockId = $"FeedDay-{today:yyyyMMdd}-Markdown-RawFallback-0";
            WaitUntil(() => Visible(session, metadataId), "Today's service-data disclosure is absent.");
            WaitUntil(() => Visible(session, "FeedAreaFilterButton"), "Chronology area filter is absent.");
            Require(!Visible(session, "FeedReturnToCurrentDayButton"), "Return action should be hidden at the heading.");
            Capture(session, $"native-{width}-top.png");

            var toggle = Find(session, metadataId)!;
            Activate(toggle);
            WaitUntil(() => Visible(session, firstBlockId), "Expanding metadata did not reveal the raw first block.");
            Capture(session, $"native-{width}-metadata.png");
            Activate(Find(session, metadataId)!);
            WaitUntil(() => !Visible(session, firstBlockId), "Collapsing metadata left the raw first block visible.");

            ScrollInsideLongDay(session, headingId);
            Capture(session, $"native-{width}-scrolled.png");
            Activate(Find(session, "FeedReturnToCurrentDayButton")!);
            WaitUntil(() => HeadingVisible(session, headingId) && !Visible(session, "FeedReturnToCurrentDayButton"),
                "Return button did not bring today's heading back into the viewport.");

            ScrollInsideLongDay(session, headingId);
            Keyboard.TypeSimultaneously(VirtualKeyShort.ALT, VirtualKeyShort.HOME);
            WaitUntil(() => HeadingVisible(session, headingId) && !Visible(session, "FeedReturnToCurrentDayButton"),
                "Alt+Home did not return to today's heading.");

            WaitUntil(() => Visible(session, "GlobalSearchBox"), "Search box is inaccessible at this window width.");
            Find(session, "GlobalSearchBox")!.AsTextBox().Text = "синтетическая";
            WaitUntil(() => Visible(session, "GlobalSearchAreaPicker") && !Visible(session, "FeedAreaFilterButton"),
                "Search should show its own area filter without the chronology area filter.");
            Require(!Visible(session, "FeedReturnToCurrentDayButton"), "Return action leaked into search.");
            Capture(session, $"native-{width}-search.png");
            Find(session, "GlobalSearchBox")!.AsTextBox().Text = string.Empty;
            Keyboard.Press(VirtualKeyShort.ESCAPE);
            Keyboard.Release(VirtualKeyShort.ESCAPE);
            WaitUntil(() => Visible(session, "FeedAreaFilterButton"), "Chronology filter did not return after search.");
            Require(File.ReadAllText(dailyPath!) == original, "Read-only navigation or metadata disclosure mutated Markdown.");
            Capture(session, $"native-{width}-returned.png");
            Console.WriteLine($"Feed reading polish: width={width}; long-day pointer scroll, button, Alt+Home, metadata and search PASS.");
        }
        catch
        {
            try { Capture(session, $"native-{width}-failure.png"); }
            catch (Exception ex) { Console.Error.WriteLine($"Screenshot unavailable: {ex.Message}"); }
            throw;
        }
    }

    private static void ScrollInsideLongDay(DesktopAppSession session, string headingId)
    {
        var chronology = Find(session, "FeedChronologyList")
            ?? throw new InvalidOperationException("Chronology viewport is absent.");
        var bounds = chronology.BoundingRectangle;
        Mouse.MoveTo(new Point((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2)));
        Mouse.Scroll(-8);
        WaitUntil(() => Visible(session, "FeedReturnToCurrentDayButton") && !HeadingVisible(session, headingId),
            "Scrolling within today's long note did not expose the return action after its heading left the viewport.");
    }

    private static AutomationElement? Find(DesktopAppSession session, string id)
    {
        var condition = session.ConditionFactory.ByAutomationId(id);
        var element = session.MainWindow.FindFirstDescendant(condition);
        if (element is not null) return element;
        var process = session.ConditionFactory.ByProcessId(session.MainWindow.Properties.ProcessId.ValueOrDefault);
        foreach (var ownedWindow in session.MainWindow.Automation.GetDesktop().FindAllChildren(process))
        {
            if (ownedWindow.Equals(session.MainWindow)) continue;
            element = ownedWindow.FindFirstDescendant(condition);
            if (element is not null) return element;
        }
        return null;
    }

    private static bool HeadingVisible(DesktopAppSession session, string id)
    {
        var heading = Find(session, id);
        var chronology = Find(session, "FeedChronologyList");
        if (heading is null || chronology is null || heading.Properties.IsOffscreen.ValueOrDefault) return false;
        var bounds = heading.BoundingRectangle;
        var viewport = chronology.BoundingRectangle;
        return bounds.Height > 0 && bounds.Top >= viewport.Top - 1 && bounds.Bottom <= viewport.Bottom + 1;
    }

    private static bool Visible(DesktopAppSession session, string id)
    {
        var element = Find(session, id);
        if (element is null || element.Properties.IsOffscreen.ValueOrDefault) return false;
        var bounds = element.BoundingRectangle;
        var window = session.MainWindow.BoundingRectangle;
        return bounds.Width > 0 && bounds.Height > 0 && bounds.IntersectsWith(window);
    }

    private static void Activate(AutomationElement element)
    {
        if (element.Patterns.Invoke.PatternOrDefault is { } invoke) invoke.Invoke();
        else if (element.Patterns.Toggle.PatternOrDefault is { } toggle) toggle.Toggle();
        else throw new InvalidOperationException($"'{element.AutomationId}' does not expose an action pattern.");
    }

    private static void Resize(AutomationElement window, int width, int height)
    {
        var handle = new IntPtr(window.Properties.NativeWindowHandle.ValueOrDefault);
        if (!MoveWindow(handle, 0, 0, width, height, true))
            throw new InvalidOperationException($"Could not resize the native window: {Marshal.GetLastWin32Error()}.");
        try
        {
            // MoveWindow sizes the decorated HWND; Avalonia's UIA root exposes its client area.
            WaitUntil(() => GetWindowRect(handle, out var bounds)
                && Math.Abs(bounds.Right - bounds.Left - width) <= 2
                && Math.Abs(bounds.Bottom - bounds.Top - height) <= 2
                && window.BoundingRectangle.Width > 0,
                $"Window did not reach the requested outer size {width}x{height}.");
            Console.WriteLine($"Reading polish window: outer={width}x{height}; client UIA={window.BoundingRectangle}.");
        }
        catch (TimeoutException ex)
        {
            var actual = window.BoundingRectangle;
            var hasNativeBounds = GetWindowRect(handle, out var nativeBounds);
            throw new TimeoutException(
                $"Window did not reach the requested size {width}x{height}; " +
                $"UIA bounds={actual}; native bounds=" +
                (hasNativeBounds
                    ? $"{nativeBounds.Left},{nativeBounds.Top} {nativeBounds.Right - nativeBounds.Left}x{nativeBounds.Bottom - nativeBounds.Top}"
                    : $"unavailable (Win32={Marshal.GetLastWin32Error()})") +
                $"; thread DPI awareness={GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext())}.",
                ex);
        }
    }

    private static void Capture(DesktopAppSession session, string name)
    {
        var directory = Environment.GetEnvironmentVariable(EvidenceDirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var capture = global::FlaUI.Core.Capturing.Capture.Element(session.MainWindow,
            new global::FlaUI.Core.Capturing.CaptureSettings());
        capture.ToFile(Path.Combine(directory, name));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void WaitUntil(Func<bool> predicate, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        do
        {
            try { if (predicate()) return; }
            catch (COMException) when (DateTime.UtcNow < deadline) { }
            Thread.Sleep(100);
        } while (DateTime.UtcNow < deadline);
        throw new TimeoutException(message);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height,
        [MarshalAs(UnmanagedType.Bool)] bool repaint);

    private static void EnsurePhysicalPixelDpiAwareness()
    {
        if (!SetProcessDpiAwarenessContext(new IntPtr(-4)) &&
            GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()) != 2)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Per-monitor DPI awareness is required for physical-pixel UI evidence.");
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
    private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);
}
