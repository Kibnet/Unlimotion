using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.FlaUI.Automation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;
using AvaloniaApp = Avalonia.Application;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using FlaUiWindow = FlaUI.Core.AutomationElements.Window;
using FlaUiDesktopAppSession = AppAutomation.FlaUI.Session.DesktopAppSession;
using HeadlessDesktopAppSession = AppAutomation.Avalonia.Headless.Session.DesktopAppSession;
using HeadlessRuntime = AppAutomation.Avalonia.Headless.Session.HeadlessRuntime;

namespace Unlimotion.ReadmeMedia;

internal static class Program
{
    private const int DesktopCaptureWindowWidth = 1760;
    private const int DesktopCaptureWindowHeight = 1060;

    private static readonly IReadOnlyList<ReadmeCaptureStep> CaptureSteps =
    [
        new("all-tasks", "all-tasks.png", static page => page.AllTasksTabItem, "All Tasks", 1000),
        new("last-created", "last-created.png", static page => page.LastCreatedTabItem, "Last Created", 900),
        new("last-updated", "last-updated.png", static page => page.LastUpdatedTabItem, "Last Updated", 900),
        new("unlocked", "unlocked.png", static page => page.UnlockedTabItem, "Unlocked", 900),
        new("completed", "completed.png", static page => page.CompletedTabItem, "Completed", 900),
        new("archived", "archived.png", static page => page.ArchivedTabItem, "Archived", 900),
        new("last-opened", "last-opened.png", static page => page.LastOpenedTabItem, "Last Opened", 900),
        new("roadmap", "roadmap.png", static page => page.RoadmapTabItem, "Roadmap", 5000),
        new("settings", "settings.png", static page => page.SettingsTabItem, "Settings", 1100)
    ];

    private static readonly IReadOnlyList<string> GeneratedFileNames =
    [
        .. CaptureSteps.Select(static step => step.FileName),
        "tab-tour.gif",
        "report.json"
    ];

    private static readonly IReadOnlyList<ReadmeCaptureLanguage> CaptureLanguages =
    [
        new("en", "English", "en"),
        new("ru", "Russian", "ru")
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        TrySetDpiAwareness();

        var options = CaptureOptions.Parse(args);
        var repoRoot = FindRepositoryRoot();
        var outputRoot = options.ResolveOutputRoot(repoRoot);
        DeleteStaleGeneratedFiles(outputRoot);
        Directory.CreateDirectory(outputRoot);

        var mediaBaseRoot = Path.Combine(repoRoot, "media", "readme");
        var reports = new List<CaptureReport>();

        foreach (var language in options.ResolveLanguages())
        {
            var languageOutputRoot = Path.Combine(outputRoot, language.DirectoryName);
            var languageMediaRoot = Path.Combine(mediaBaseRoot, language.DirectoryName);
            var currentTaskTitle = UnlimotionAppLaunchHost.GetCurrentTaskTitle(
                UnlimotionAutomationScenario.ReadmeDemo,
                language.LanguageMode);
            var report = new CaptureReport
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                Scenario = UnlimotionAutomationScenario.ReadmeDemo.ToString(),
                Language = language.LanguageMode,
                OutputRoot = languageOutputRoot,
                MediaRoot = languageMediaRoot,
                CurrentTaskTitle = currentTaskTitle
            };

            DeleteStaleGeneratedFiles(languageOutputRoot);
            Directory.CreateDirectory(languageOutputRoot);

            var gifFrames = CaptureDesktopMedia(
                languageOutputRoot,
                report,
                languageMediaRoot,
                currentTaskTitle,
                language.LanguageMode,
                options.NoBuildBeforeLaunch);

            var gifPath = Path.Combine(languageOutputRoot, "tab-tour.gif");
            WriteAnimatedGif(gifPath, gifFrames);
            report.Assets.Add(new CapturedAsset
            {
                Key = "tab-tour",
                DisplayName = "Tab tour GIF",
                OutputPath = gifPath,
                MediaTargetPath = Path.Combine(languageMediaRoot, "tab-tour.gif")
            });

            WriteReport(Path.Combine(languageOutputRoot, "report.json"), report);
            reports.Add(report);

            if (options.CopyToMedia)
            {
                Directory.CreateDirectory(languageMediaRoot);
                DeleteStaleGeneratedFiles(languageMediaRoot);
                foreach (var asset in report.Assets)
                {
                    File.Copy(asset.OutputPath, asset.MediaTargetPath, overwrite: true);
                }
            }
        }

        if (options.CopyToMedia)
        {
            DeleteStaleGeneratedFiles(mediaBaseRoot);
        }

        WriteReport(
            Path.Combine(outputRoot, "report.json"),
            new CaptureRunReport
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                Scenario = UnlimotionAutomationScenario.ReadmeDemo.ToString(),
                OutputRoot = outputRoot,
                MediaRoot = mediaBaseRoot,
                Reports = reports
            });

        Console.WriteLine(outputRoot);
        return 0;
    }

    private static void WriteReport<T>(string path, T report)
    {
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
    }

    private static List<GifFrame> CaptureDesktopMedia(
        string outputRoot,
        CaptureReport report,
        string mediaRoot,
        string currentTaskTitle,
        string languageMode,
        bool noBuildBeforeLaunch)
    {
        var launchOptions = UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(
            scenario: UnlimotionAutomationScenario.ReadmeDemo,
            language: languageMode,
            buildBeforeLaunch: !noBuildBeforeLaunch,
            buildOncePerProcess: true);

        using var session = FlaUiDesktopAppSession.Launch(launchOptions);
        var page = new MainWindowPage(new FlaUiControlResolver(session.MainWindow, session.ConditionFactory));

        WaitFor(
            () => string.Equals(page.CurrentTaskTitleTextBox.Text, currentTaskTitle, StringComparison.Ordinal),
            TimeSpan.FromSeconds(20),
            $"current task '{currentTaskTitle}'");

        ResizeDesktopWindow(session.MainWindow, DesktopCaptureWindowWidth, DesktopCaptureWindowHeight);
        session.MainWindow.Focus();
        Pause(1000);

        var gifFrames = new List<GifFrame>();

        CaptureCurrentDesktopWindow(
            session.MainWindow,
            Path.Combine(outputRoot, "all-tasks.png"),
            report,
            mediaRoot,
            "all-tasks.png",
            "All Tasks overview",
            gifFrames,
            delayMs: 1000);

        foreach (var step in CaptureSteps.Skip(1))
        {
            page.SelectTabItem(step.Selector, timeoutMs: 10_000);
            Pause(step.DelayAfterSelectMs);

            if (string.Equals(step.Key, "roadmap", StringComparison.Ordinal))
            {
                WaitForRoadmapRoot(page);
                TryCaptureRoadmap(
                    Path.Combine(outputRoot, step.FileName),
                    report,
                    mediaRoot,
                    step.FileName,
                    step.DisplayName,
                    gifFrames,
                    step.DelayAfterSelectMs,
                    () => CaptureDesktopWindowBitmap(session.MainWindow));
                continue;
            }

            if (string.Equals(step.Key, "settings", StringComparison.Ordinal))
            {
                WaitForSettingsRoot(page);
            }

            CaptureCurrentDesktopWindow(
                session.MainWindow,
                Path.Combine(outputRoot, step.FileName),
                report,
                mediaRoot,
                step.FileName,
                step.DisplayName,
                gifFrames,
                step.DelayAfterSelectMs);
        }

        return gifFrames;
    }

    private static List<GifFrame> CaptureHeadlessMedia(
        string outputRoot,
        CaptureReport report,
        string mediaRoot,
        string currentTaskTitle,
        string languageMode)
    {
        using var headlessRuntime = HeadlessUnitTestSession.StartNew(GetHeadlessEntryPointType());
        HeadlessRuntime.SetSession(headlessRuntime);

        try
        {
            using var session = HeadlessDesktopAppSession.Launch(
                UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
                    UnlimotionAutomationScenario.ReadmeDemo,
                    languageMode));
            var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow));
            var mainWindow = GetHeadlessMainWindow(session.MainWindow);

            WaitFor(
                () => string.Equals(page.CurrentTaskTitleTextBox.Text, currentTaskTitle, StringComparison.Ordinal),
                TimeSpan.FromSeconds(20),
                $"headless current task '{currentTaskTitle}'");

            mainWindow.Width = DesktopCaptureWindowWidth;
            mainWindow.Height = DesktopCaptureWindowHeight;

            var gifFrames = new List<GifFrame>();

            foreach (var step in CaptureSteps)
            {
                if (!string.Equals(step.Key, "all-tasks", StringComparison.Ordinal))
                {
                    page.SelectTabItem(step.Selector, timeoutMs: 10_000);
                }

                if (string.Equals(step.Key, "roadmap", StringComparison.Ordinal))
                {
                    WaitForRoadmapRoot(page);
                    TryCaptureRoadmap(
                        Path.Combine(outputRoot, step.FileName),
                        report,
                        mediaRoot,
                        step.FileName,
                        step.DisplayName,
                        gifFrames,
                        step.DelayAfterSelectMs,
                        () => CaptureRoadmapBitmapHeadless(languageMode));
                    continue;
                }

                if (string.Equals(step.Key, "settings", StringComparison.Ordinal))
                {
                    WaitForSettingsRoot(page);
                }

                Pause(step.DelayAfterSelectMs);
                CaptureCurrentHeadlessWindow(
                    mainWindow,
                    Path.Combine(outputRoot, step.FileName),
                    report,
                    mediaRoot,
                    step.FileName,
                    step.DisplayName,
                    gifFrames,
                    step.DelayAfterSelectMs);
            }

            return gifFrames;
        }
        finally
        {
            HeadlessRuntime.SetSession(null);
        }
    }

    private static void WaitForRoadmapRoot(MainWindowPage page)
    {
        WaitFor(
            () =>
            {
                try
                {
                    return string.Equals(page.RoadmapRoot.AutomationId, "RoadmapRoot", StringComparison.Ordinal);
                }
                catch
                {
                    return false;
                }
            },
            TimeSpan.FromSeconds(10),
            "Roadmap root");
    }

    private static void TryCaptureRoadmap(
        string outputPath,
        CaptureReport report,
        string mediaRoot,
        string mediaFileName,
        string displayName,
        List<GifFrame> gifFrames,
        int delayMs,
        Func<Bitmap> captureBitmap)
    {
        try
        {
            using var bitmap = captureBitmap();
            bitmap.Save(outputPath, ImageFormat.Png);
            gifFrames.Add(new GifFrame(new Bitmap(bitmap), delayMs));
            report.Assets.Add(new CapturedAsset
            {
                Key = Path.GetFileNameWithoutExtension(mediaFileName),
                DisplayName = displayName,
                OutputPath = outputPath,
                MediaTargetPath = Path.Combine(mediaRoot, mediaFileName)
            });
        }
        catch (Exception ex)
        {
            var warning = $"Roadmap capture skipped: {ex.Message}";
            report.Warnings.Add(warning);
            Console.Error.WriteLine(warning);
        }
    }

    private static Bitmap CaptureRoadmapBitmapHeadless(string languageMode)
    {
        using var headlessRuntime = HeadlessUnitTestSession.StartNew(GetHeadlessEntryPointType());
        HeadlessRuntime.SetSession(headlessRuntime);

        try
        {
            using var session = HeadlessDesktopAppSession.Launch(
                UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
                    UnlimotionAutomationScenario.ReadmeDemo,
                    languageMode));
            var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow));
            var currentTaskTitle =
                UnlimotionAppLaunchHost.GetCurrentTaskTitle(
                    UnlimotionAutomationScenario.ReadmeDemo,
                    languageMode);

            WaitFor(
                () => string.Equals(page.CurrentTaskTitleTextBox.Text, currentTaskTitle, StringComparison.Ordinal),
                TimeSpan.FromSeconds(20),
                $"headless current task '{currentTaskTitle}'");

            page.SelectTabItem(static ui => ui.RoadmapTabItem, timeoutMs: 10_000);
            WaitForRoadmapRoot(page);

            var mainWindow = GetHeadlessMainWindow(session.MainWindow);
            mainWindow.Width = DesktopCaptureWindowWidth;
            mainWindow.Height = DesktopCaptureWindowHeight;
            return CaptureHeadlessWindowBitmap(mainWindow);
        }
        finally
        {
            HeadlessRuntime.SetSession(null);
        }
    }

    private static Window GetHeadlessMainWindow(object sessionMainWindow)
    {
        var lifetime = AvaloniaApp.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (lifetime?.MainWindow is not null)
        {
            return lifetime.MainWindow;
        }

        var topLevel = TryResolveTopLevel(sessionMainWindow);
        return topLevel as Window
            ?? throw new InvalidOperationException(
                $"Headless main window is not available. Session root type: {sessionMainWindow.GetType().FullName}");
    }

    private static TopLevel? TryResolveTopLevel(object? source, int depth = 0)
    {
        if (source is null || depth > 4)
        {
            return null;
        }

        if (source is TopLevel topLevel)
        {
            return topLevel;
        }

        var type = source.GetType();
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? value;
            try
            {
                value = property.GetValue(source);
            }
            catch
            {
                continue;
            }

            if (value is TopLevel resolved)
            {
                return resolved;
            }

            var namespaceName = value?.GetType().Namespace;
            if (namespaceName is null)
            {
                continue;
            }

            if (!namespaceName.StartsWith("Avalonia", StringComparison.Ordinal)
                && !namespaceName.StartsWith("AppAutomation", StringComparison.Ordinal))
            {
                continue;
            }

            var nested = TryResolveTopLevel(value, depth + 1);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static Type GetHeadlessEntryPointType()
    {
        var repositoryRoot = FindRepositoryRoot();
        var assemblyPath = Path.Combine(
            repositoryRoot,
            "src",
            "Unlimotion.Desktop",
            "bin",
            "Debug",
            "net10.0",
            "Unlimotion.Desktop.dll");

        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException("Desktop entry point assembly was not found for headless capture.", assemblyPath);
        }

        var assembly = System.Reflection.Assembly.LoadFrom(assemblyPath);
        return assembly.GetType("Unlimotion.Desktop.Program", throwOnError: true)!
            ?? throw new InvalidOperationException("Unable to resolve Unlimotion.Desktop.Program type.");
    }

    private static Bitmap ConvertAvaloniaBitmap(AvaloniaBitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        using var loaded = new Bitmap(stream);
        return new Bitmap(loaded);
    }

    private static void WaitForSettingsRoot(MainWindowPage page)
    {
        WaitFor(
            () =>
            {
                try
                {
                    return string.Equals(page.SettingsRoot.AutomationId, "SettingsRoot", StringComparison.Ordinal);
                }
                catch
                {
                    return false;
                }
            },
            TimeSpan.FromSeconds(10),
            "Settings root");
    }

    private static void CaptureCurrentHeadlessWindow(
        Window mainWindow,
        string outputPath,
        CaptureReport report,
        string mediaRoot,
        string mediaFileName,
        string displayName,
        List<GifFrame> gifFrames,
        int delayMs)
    {
        using var bitmap = CaptureHeadlessWindowBitmap(mainWindow);
        bitmap.Save(outputPath, ImageFormat.Png);
        gifFrames.Add(new GifFrame(new Bitmap(bitmap), delayMs));
        report.Assets.Add(new CapturedAsset
        {
            Key = Path.GetFileNameWithoutExtension(mediaFileName),
            DisplayName = displayName,
            OutputPath = outputPath,
            MediaTargetPath = Path.Combine(mediaRoot, mediaFileName)
        });
    }

    private static Bitmap CaptureHeadlessWindowBitmap(Window mainWindow)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(200);
            Pause(150);
        }

        using var frame = mainWindow.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("Headless capture did not produce a rendered frame.");
        return ConvertAvaloniaBitmap(frame);
    }

    private static void CaptureCurrentDesktopWindow(
        FlaUiWindow window,
        string outputPath,
        CaptureReport report,
        string mediaRoot,
        string mediaFileName,
        string displayName,
        List<GifFrame> gifFrames,
        int delayMs)
    {
        using var bitmap = CaptureDesktopWindowBitmap(window);
        bitmap.Save(outputPath, ImageFormat.Png);
        gifFrames.Add(new GifFrame(new Bitmap(bitmap), delayMs));
        report.Assets.Add(new CapturedAsset
        {
            Key = Path.GetFileNameWithoutExtension(mediaFileName),
            DisplayName = displayName,
            OutputPath = outputPath,
            MediaTargetPath = Path.Combine(mediaRoot, mediaFileName)
        });
    }

    private static Bitmap CaptureDesktopWindowBitmap(FlaUiWindow window)
    {
        var handle = new IntPtr(window.Properties.NativeWindowHandle.ValueOrDefault);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Main window handle is not available for capture.");
        }

        var windowBounds = GetWindowBounds(handle);
        var captureBounds = GetCaptureBounds(handle);

        using var windowBitmap = new Bitmap(windowBounds.Width, windowBounds.Height);
        if (TryPrintWindow(handle, windowBitmap)
            || TryBitBltWindow(handle, windowBitmap, windowBounds.Width, windowBounds.Height))
        {
            return CropBitmap(windowBitmap, windowBounds, captureBounds);
        }

        using var desktopBitmap = new Bitmap(captureBounds.Width, captureBounds.Height);
        if (TryBitBltDesktop(
                captureBounds.Left,
                captureBounds.Top,
                desktopBitmap,
                captureBounds.Width,
                captureBounds.Height))
        {
            return new Bitmap(desktopBitmap);
        }

        throw new InvalidOperationException("Failed to capture the main window using Win32 capture methods.");
    }

    private static Bitmap CropBitmap(Bitmap source, WindowBounds sourceBounds, WindowBounds targetBounds)
    {
        var left = Math.Max(0, targetBounds.Left - sourceBounds.Left);
        var top = Math.Max(0, targetBounds.Top - sourceBounds.Top);
        var right = Math.Min(source.Width, targetBounds.Left + targetBounds.Width - sourceBounds.Left);
        var bottom = Math.Min(source.Height, targetBounds.Top + targetBounds.Height - sourceBounds.Top);
        var width = Math.Max(1, right - left);
        var height = Math.Max(1, bottom - top);

        return source.Clone(new Rectangle(left, top, width, height), source.PixelFormat);
    }

    private static WindowBounds GetWindowBounds(IntPtr handle)
    {
        if (!NativeMethods.GetWindowRect(handle, out var rect))
        {
            throw new InvalidOperationException("GetWindowRect failed for the main window.");
        }

        return new WindowBounds(
            rect.Left,
            rect.Top,
            Math.Max(1, rect.Right - rect.Left),
            Math.Max(1, rect.Bottom - rect.Top));
    }

    private static WindowBounds GetCaptureBounds(IntPtr handle)
    {
        var windowBounds = GetWindowBounds(handle);
        var hr = NativeMethods.DwmGetWindowAttribute(
            handle,
            NativeMethods.DwmaExtendedFrameBounds,
            out var rect,
            Marshal.SizeOf<NativeMethods.RECT>());

        if (hr != 0 || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
        {
            return windowBounds;
        }

        var frameBounds = new WindowBounds(
            rect.Left,
            rect.Top,
            Math.Max(1, rect.Right - rect.Left),
            Math.Max(1, rect.Bottom - rect.Top));

        return IsContainedWithin(frameBounds, windowBounds, tolerance: 64)
            ? frameBounds
            : windowBounds;
    }

    private static bool IsContainedWithin(WindowBounds inner, WindowBounds outer, int tolerance)
    {
        return inner.Left >= outer.Left - tolerance
            && inner.Top >= outer.Top - tolerance
            && inner.Left + inner.Width <= outer.Left + outer.Width + tolerance
            && inner.Top + inner.Height <= outer.Top + outer.Height + tolerance;
    }

    private static bool TryPrintWindow(IntPtr handle, Bitmap bitmap)
    {
        using var graphics = Graphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();

        try
        {
            return NativeMethods.PrintWindow(handle, hdc, NativeMethods.PrintWindowRenderFullContent)
                || NativeMethods.PrintWindow(handle, hdc, 0);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }
    }

    private static bool TryBitBltWindow(IntPtr handle, Bitmap bitmap, int width, int height)
    {
        var sourceDc = NativeMethods.GetWindowDC(handle);
        if (sourceDc == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            var targetDc = graphics.GetHdc();

            try
            {
                return NativeMethods.BitBlt(
                    targetDc,
                    0,
                    0,
                    width,
                    height,
                    sourceDc,
                    0,
                    0,
                    NativeMethods.Srccopy | NativeMethods.CaptureBlt);
            }
            finally
            {
                graphics.ReleaseHdc(targetDc);
            }
        }
        finally
        {
            NativeMethods.ReleaseDC(handle, sourceDc);
        }
    }

    private static bool TryBitBltDesktop(int sourceLeft, int sourceTop, Bitmap bitmap, int width, int height)
    {
        var sourceDc = NativeMethods.GetDC(IntPtr.Zero);
        if (sourceDc == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            var targetDc = graphics.GetHdc();

            try
            {
                return NativeMethods.BitBlt(
                    targetDc,
                    0,
                    0,
                    width,
                    height,
                    sourceDc,
                    sourceLeft,
                    sourceTop,
                    NativeMethods.Srccopy | NativeMethods.CaptureBlt);
            }
            finally
            {
                graphics.ReleaseHdc(targetDc);
            }
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, sourceDc);
        }
    }

    private static void WriteAnimatedGif(string path, IReadOnlyList<GifFrame> frames)
    {
        using var stream = File.Create(path);
        var encoder = new GifBitmapEncoder();

        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            using var bitmap = frame.Bitmap;
            var source = CreateBitmapSource(bitmap);
            source.Freeze();

            var metadata = new BitmapMetadata("gif");
            metadata.SetQuery("/grctlext/Delay", (ushort)Math.Max(1, frame.DelayMs / 10));
            metadata.SetQuery("/grctlext/Disposal", (byte)2);

            if (index == 0)
            {
                TrySetLoopMetadata(metadata);
            }

            encoder.Frames.Add(BitmapFrame.Create(source, null, metadata, null));
        }

        encoder.Save(stream);
    }

    private static BitmapSource CreateBitmapSource(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            return Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            NativeMethods.DeleteObject(hBitmap);
        }
    }

    private static void TrySetLoopMetadata(BitmapMetadata metadata)
    {
        try
        {
            metadata.SetQuery("/appext/Application", "NETSCAPE2.0");
            metadata.SetQuery("/appext/Data", new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
        }
        catch
        {
            // Loop metadata is best-effort; browsers will still render a valid GIF without it.
        }
    }

    private static void WaitFor(Func<bool> condition, TimeSpan timeout, string description)
    {
        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            if (condition())
            {
                return;
            }

            Pause(150);
        }

        throw new TimeoutException($"Timed out waiting for {description}.");
    }

    private static void Pause(int milliseconds)
    {
        Thread.Sleep(milliseconds);
    }

    private static void TrySetDpiAwareness()
    {
        try
        {
            if (NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DpiAwarenessContextPerMonitorAwareV2))
            {
                return;
            }
        }
        catch
        {
        }

        try
        {
            NativeMethods.SetProcessDPIAware();
        }
        catch
        {
        }
    }

    private static void ResizeDesktopWindow(FlaUiWindow window, int width, int height)
    {
        var handle = new IntPtr(window.Properties.NativeWindowHandle.ValueOrDefault);

        if (handle == IntPtr.Zero)
        {
            TryResize(window, width, height);
            Pause(300);
            return;
        }

        var placement = GetPreferredMonitorPlacement(handle);
        const int margin = 16;
        var workArea = placement.WorkArea;
        var scale = placement.Scale;
        var appliedWidth = Math.Max(1, workArea.Width - (margin * 2));
        var appliedHeight = Math.Max(1, workArea.Height - (margin * 2));
        var appliedLeft = workArea.Left + margin;
        var appliedTop = workArea.Top + margin;
        var logicalWidth = Math.Max(1, appliedWidth / scale);
        var logicalHeight = Math.Max(1, appliedHeight / scale);

        TryResize(window, logicalWidth, logicalHeight);
        Pause(250);

        try
        {
            if (NativeMethods.SetWindowPos(
                    handle,
                    IntPtr.Zero,
                    appliedLeft,
                    appliedTop,
                    appliedWidth,
                    appliedHeight,
                    NativeMethods.SwpNoOwnerZOrder
                    | NativeMethods.SwpShowWindow))
            {
                NativeMethods.SetForegroundWindow(handle);
                Pause(300);
            }
        }
        catch
        {
        }
    }

    private static MonitorPlacement GetPreferredMonitorPlacement(IntPtr handle)
    {
        var placements = EnumerateMonitorPlacements();
        if (placements.Count > 0)
        {
            return placements
                .OrderByDescending(static placement => placement.LogicalArea)
                .ThenByDescending(static placement => placement.PhysicalArea)
                .First();
        }

        var monitor = NativeMethods.MonitorFromWindow(handle, NativeMethods.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return new MonitorPlacement(GetWindowBounds(handle), 1d);
        }

        return GetMonitorPlacement(monitor) ?? new MonitorPlacement(GetWindowBounds(handle), 1d);
    }

    private static List<MonitorPlacement> EnumerateMonitorPlacements()
    {
        var placements = new List<MonitorPlacement>();
        NativeMethods.EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (monitor, _, _, _) =>
            {
                var placement = GetMonitorPlacement(monitor);
                if (placement is not null)
                {
                    placements.Add(placement);
                }

                return true;
            },
            IntPtr.Zero);

        return placements;
    }

    private static MonitorPlacement? GetMonitorPlacement(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero)
        {
            return null;
        }

        var monitorInfo = new NativeMethods.MONITORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return null;
        }

        var workArea = new WindowBounds(
            monitorInfo.rcWork.Left,
            monitorInfo.rcWork.Top,
            Math.Max(1, monitorInfo.rcWork.Right - monitorInfo.rcWork.Left),
            Math.Max(1, monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top));

        return new MonitorPlacement(workArea, GetMonitorScaleFactor(monitor));
    }

    private static double GetMonitorScaleFactor(IntPtr monitor)
    {
        try
        {
            var result = NativeMethods.GetDpiForMonitor(
                monitor,
                NativeMethods.MdtEffectiveDpi,
                out var dpiX,
                out _);

            if (result == 0 && dpiX > 0)
            {
                return dpiX / 96d;
            }
        }
        catch
        {
        }

        return 1d;
    }

    private static void TryResize(FlaUiWindow window, double width, double height)
    {
        try
        {
            if (window.Patterns.Transform.IsSupported)
            {
                window.Patterns.Transform.Pattern.Resize(width, height);
            }
        }
        catch
        {
        }
    }

    private static void DeleteStaleGeneratedFiles(string outputRoot)
    {
        Directory.CreateDirectory(outputRoot);

        foreach (var fileName in GeneratedFileNames)
        {
            var path = Path.Combine(outputRoot, fileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
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

        throw new DirectoryNotFoundException("Unable to locate Unlimotion repository root.");
    }

    private sealed record ReadmeCaptureStep(
        string Key,
        string FileName,
        Expression<Func<MainWindowPage, ITabItemControl>> Selector,
        string DisplayName,
        int DelayAfterSelectMs);

    private sealed record ReadmeCaptureLanguage(
        string LanguageMode,
        string DisplayName,
        string DirectoryName);

    private sealed record WindowBounds(int Left, int Top, int Width, int Height);

    private sealed record MonitorPlacement(WindowBounds WorkArea, double Scale)
    {
        public double LogicalArea => (WorkArea.Width / Scale) * (WorkArea.Height / Scale);

        public long PhysicalArea => (long)WorkArea.Width * WorkArea.Height;
    }

    private sealed record GifFrame(Bitmap Bitmap, int DelayMs);

    private sealed record CaptureOptions(
        bool CopyToMedia = false,
        bool NoBuildBeforeLaunch = false,
        string? OutputRoot = null,
        IReadOnlyList<string>? Languages = null)
    {
        public static CaptureOptions Parse(string[] args)
        {
            var options = new CaptureOptions();

            for (var index = 0; index < args.Length; index++)
            {
                var argument = args[index];
                switch (argument)
                {
                    case "--copy-to-media":
                        options = options with { CopyToMedia = true };
                        break;
                    case "--no-build-before-launch":
                        options = options with { NoBuildBeforeLaunch = true };
                        break;
                    case "--language":
                    case "--languages":
                        if (index + 1 >= args.Length)
                        {
                            throw new ArgumentException($"{argument} requires a value.");
                        }

                        options = options with { Languages = ParseLanguageList(args[++index]) };
                        break;
                    case "--output-root":
                        if (index + 1 >= args.Length)
                        {
                            throw new ArgumentException("--output-root requires a value.");
                        }

                        options = options with { OutputRoot = args[++index] };
                        break;
                }
            }

            return options;
        }

        public IReadOnlyList<ReadmeCaptureLanguage> ResolveLanguages()
        {
            var requestedLanguages = Languages is { Count: > 0 }
                ? Languages
                : CaptureLanguages.Select(static language => language.LanguageMode).ToArray();
            var resolvedLanguages = new List<ReadmeCaptureLanguage>();

            foreach (var requestedLanguage in requestedLanguages)
            {
                foreach (var language in ResolveLanguage(requestedLanguage))
                {
                    if (resolvedLanguages.Any(existing =>
                            string.Equals(existing.LanguageMode, language.LanguageMode, StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    resolvedLanguages.Add(language);
                }
            }

            return resolvedLanguages;
        }

        public string ResolveOutputRoot(string repositoryRoot)
        {
            return string.IsNullOrWhiteSpace(OutputRoot)
                ? Path.Combine(repositoryRoot, "artifacts", "readme-media", DateTime.Now.ToString("yyyyMMdd-HHmmss"))
                : Path.GetFullPath(OutputRoot);
        }

        private static IReadOnlyList<string> ParseLanguageList(string value)
        {
            return value
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .DefaultIfEmpty(value)
                .ToArray();
        }

        private static IEnumerable<ReadmeCaptureLanguage> ResolveLanguage(string language)
        {
            var normalized = language.Trim().ToLowerInvariant();
            if (normalized is "all" or "*")
            {
                return CaptureLanguages;
            }

            var captureLanguage = CaptureLanguages.FirstOrDefault(candidate =>
                string.Equals(candidate.LanguageMode, normalized, StringComparison.Ordinal)
                || string.Equals(candidate.DirectoryName, normalized, StringComparison.Ordinal));

            if (captureLanguage is null)
            {
                throw new ArgumentException(
                    $"Unsupported README media language '{language}'. Supported values: en, ru, all.");
            }

            return [captureLanguage];
        }
    }

    private sealed class CaptureRunReport
    {
        public DateTimeOffset GeneratedAt { get; init; }

        public string Scenario { get; init; } = string.Empty;

        public string OutputRoot { get; init; } = string.Empty;

        public string MediaRoot { get; init; } = string.Empty;

        public List<CaptureReport> Reports { get; init; } = [];
    }

    private sealed class CaptureReport
    {
        public DateTimeOffset GeneratedAt { get; init; }

        public string Scenario { get; init; } = string.Empty;

        public string Language { get; init; } = string.Empty;

        public string OutputRoot { get; init; } = string.Empty;

        public string MediaRoot { get; init; } = string.Empty;

        public string CurrentTaskTitle { get; init; } = string.Empty;

        public List<CapturedAsset> Assets { get; } = [];

        public List<string> Warnings { get; } = [];
    }

    private sealed class CapturedAsset
    {
        public string Key { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string OutputPath { get; init; } = string.Empty;

        public string MediaTargetPath { get; init; } = string.Empty;
    }

    private static class NativeMethods
    {
        public static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new(-4);

        public const int PrintWindowRenderFullContent = 2;
        public const int DwmaExtendedFrameBounds = 9;
        public const uint MonitorDefaultToNearest = 2;
        public const int MdtEffectiveDpi = 0;
        public const int Srccopy = 0x00CC0020;
        public const int CaptureBlt = 0x40000000;
        public const uint SwpNoOwnerZOrder = 0x0200;
        public const uint SwpShowWindow = 0x0040;

        public delegate bool MonitorEnumProc(
            IntPtr hMonitor,
            IntPtr hdcMonitor,
            IntPtr lprcMonitor,
            IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, int nFlags);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(
            IntPtr hdc,
            IntPtr lprcClip,
            MonitorEnumProc lpfnEnum,
            IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            out RECT pvAttribute,
            int cbAttribute);

        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(
            IntPtr hmonitor,
            int dpiType,
            out uint dpiX,
            out uint dpiY);

        [DllImport("gdi32.dll")]
        public static extern bool BitBlt(
            IntPtr hdcDest,
            int nXDest,
            int nYDest,
            int nWidth,
            int nHeight,
            IntPtr hdcSrc,
            int nXSrc,
            int nYSrc,
            int dwRop);
    }
}
