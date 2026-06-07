using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
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

    private static readonly IReadOnlyList<TaskCardUxReviewCase> TaskCardUxReviewCases =
    [
        new(
            "root-description",
            "launch-pilot",
            "Root task with description",
            "CurrentTaskDescriptionTextBox"),
        new(
            "repeater-planning",
            "capture-readme-tour",
            "Planning and repeater task",
            "CurrentTaskRepeaterSelector"),
        new(
            "blocked-relation",
            "publish-landing",
            "Blocked relation task",
            "CurrentTaskBlockedRelationAddButton")
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        TrySetDpiAwareness();

        var options = CaptureOptions.Parse(args);
        var repoRoot = FindRepositoryRoot();
        var outputRoot = options.ResolveOutputRoot(repoRoot);

        if (!string.IsNullOrWhiteSpace(options.UxReview))
        {
            if (string.Equals(options.UxReview, "filter-toolbar", StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(outputRoot);
                CaptureFilterToolbarUxReview(outputRoot, options);
                Console.WriteLine(outputRoot);
                return 0;
            }

            if (!string.Equals(options.UxReview, "task-card", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Unsupported UX review target '{options.UxReview}'. Supported values: task-card, filter-toolbar.");
            }

            Directory.CreateDirectory(outputRoot);
            CaptureTaskCardUxReview(repoRoot, outputRoot, options);
            Console.WriteLine(outputRoot);
            return 0;
        }

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

    private static void CaptureFilterToolbarUxReview(string outputRoot, CaptureOptions options)
    {
        var languages = options.ResolveLanguages();
        if (languages.Count != 1)
        {
            throw new ArgumentException("--ux-review filter-toolbar expects exactly one --language value.");
        }

        var language = languages[0];
        var launchOptions = UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(
            scenario: UnlimotionAutomationScenario.ReadmeDemo,
            language: language.LanguageMode,
            buildBeforeLaunch: !options.NoBuildBeforeLaunch,
            buildOncePerProcess: true);

        using var session = FlaUiDesktopAppSession.Launch(launchOptions);
        var page = new MainWindowPage(new FlaUiControlResolver(session.MainWindow, session.ConditionFactory));
        var currentTaskTitle = UnlimotionAppLaunchHost.GetCurrentTaskTitle(
            UnlimotionAutomationScenario.ReadmeDemo,
            language.LanguageMode);

        WaitFor(
            () => string.Equals(page.CurrentTaskTitleTextBox.Text, currentTaskTitle, StringComparison.Ordinal),
            TimeSpan.FromSeconds(20),
            $"current task '{currentTaskTitle}'");

        if (!page.DetailsPaneToggleButton.IsToggled)
        {
            page.DetailsPaneToggleButton.Toggle();
            WaitFor(
                () => page.DetailsPaneToggleButton.IsToggled,
                TimeSpan.FromSeconds(10),
                "details pane closed for filter toolbar capture");
        }

        ResizeWindowExact(session.MainWindow, 390, 760);
        session.MainWindow.Focus();
        Pause(800);

        page.SelectTabItem(static ui => ui.AllTasksTabItem, timeoutMs: 10_000);
        Pause(800);
        SaveFilterToolbarCapture(
            session.MainWindow,
            Path.Combine(outputRoot, "task-narrow-alltasks.png"),
            "task-narrow-alltasks");
        OpenFilterPanelAndCapture(
            session.MainWindow,
            session.ConditionFactory,
            "AllTasksFiltersButton",
            Path.Combine(outputRoot, "task-narrow-alltasks-open.png"),
            "task-narrow-alltasks-open");

        page.SelectTabItem(static ui => ui.LastCreatedTabItem, timeoutMs: 10_000);
        Pause(800);
        SaveFilterToolbarCapture(
            session.MainWindow,
            Path.Combine(outputRoot, "task-narrow-lastcreated.png"),
            "task-narrow-lastcreated");
        OpenFilterPanelAndCapture(
            session.MainWindow,
            session.ConditionFactory,
            "LastCreatedFiltersButton",
            Path.Combine(outputRoot, "task-narrow-lastcreated-open.png"),
            "task-narrow-lastcreated-open");

        page.SelectTabItem(static ui => ui.RoadmapTabItem, timeoutMs: 10_000);
        Pause(1200);
        SaveFilterToolbarCapture(
            session.MainWindow,
            Path.Combine(outputRoot, "roadmap-narrow.png"),
            "roadmap-narrow");
        OpenFilterPanelAndCapture(
            session.MainWindow,
            session.ConditionFactory,
            "RoadmapFiltersButton",
            Path.Combine(outputRoot, "roadmap-narrow-open.png"),
            "roadmap-narrow-open");

        ResizeWindowExact(session.MainWindow, 1200, 760);
        session.MainWindow.Focus();
        Pause(800);

        page.SelectTabItem(static ui => ui.AllTasksTabItem, timeoutMs: 10_000);
        Pause(800);
        SaveFilterToolbarCapture(
            session.MainWindow,
            Path.Combine(outputRoot, "task-wide-alltasks.png"),
            "task-wide-alltasks");
    }

    private static void SaveFilterToolbarCapture(
        FlaUiWindow window,
        string outputPath,
        string assetKey,
        bool includeDesktopOverlays = false)
    {
        using var bitmap = CaptureDesktopWindowBitmap(window, includeDesktopOverlays);
        VerifyNonBlankBitmap(bitmap, assetKey);
        bitmap.Save(outputPath, ImageFormat.Png);
    }

    private static void OpenFilterPanelAndCapture(
        FlaUiWindow window,
        FlaUI.Core.Conditions.ConditionFactory conditionFactory,
        string filtersButtonAutomationId,
        string outputPath,
        string assetKey)
    {
        var filtersButton = window.FindFirstDescendant(conditionFactory.ByAutomationId(filtersButtonAutomationId))
                            ?? throw new InvalidOperationException(
                                $"Filter button '{filtersButtonAutomationId}' was not found for UX review capture.");

        filtersButton.Click();
        Pause(500);
        SaveFilterToolbarCapture(window, outputPath, assetKey, includeDesktopOverlays: true);
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

    private static void CaptureTaskCardUxReview(
        string repositoryRoot,
        string outputRoot,
        CaptureOptions options)
    {
        var languages = options.ResolveLanguages();
        if (languages.Count != 1)
        {
            throw new ArgumentException("--ux-review task-card expects exactly one --language value.");
        }

        var language = languages[0];
        var report = new TaskCardUxReviewReport
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Scenario = UnlimotionAutomationScenario.ReadmeDemo.ToString(),
            Language = language.LanguageMode,
            Branch = TryRunGit(repositoryRoot, "rev-parse", "--abbrev-ref", "HEAD"),
            CommitSha = TryRunGit(repositoryRoot, "rev-parse", "HEAD"),
            OutputRoot = outputRoot,
            CaptureCommand = Environment.CommandLine,
            DesktopViewport = $"{DesktopCaptureWindowWidth}x{DesktopCaptureWindowHeight}",
            PhoneViewport = "390x844"
        };

        Directory.CreateDirectory(Path.Combine(outputRoot, "desktop"));
        Directory.CreateDirectory(Path.Combine(outputRoot, "phone"));

        foreach (var reviewCase in TaskCardUxReviewCases)
        {
            CaptureTaskCardUxDesktopCase(outputRoot, report, language.LanguageMode, reviewCase, options.NoBuildBeforeLaunch);
            CaptureTaskCardUxPhoneCase(outputRoot, report, language.LanguageMode, reviewCase, options.NoBuildBeforeLaunch);
        }

        CaptureTaskCardUxRelationEditorCase(outputRoot, report, language.LanguageMode, isPhone: false, options.NoBuildBeforeLaunch);
        CaptureTaskCardUxRelationEditorCase(outputRoot, report, language.LanguageMode, isPhone: true, options.NoBuildBeforeLaunch);

        WriteReport(Path.Combine(outputRoot, "report.json"), report);
    }

    private static void CaptureTaskCardUxDesktopCase(
        string outputRoot,
        TaskCardUxReviewReport report,
        string languageMode,
        TaskCardUxReviewCase reviewCase,
        bool noBuildBeforeLaunch)
    {
        using var session = LaunchTaskCardUxReviewSession(languageMode, reviewCase.TaskId, noBuildBeforeLaunch);
        var page = new MainWindowPage(new FlaUiControlResolver(session.MainWindow, session.ConditionFactory));
        WaitForTaskCardReviewTask(page, reviewCase, languageMode);

        ResizeDesktopWindow(session.MainWindow, DesktopCaptureWindowWidth, DesktopCaptureWindowHeight);
        session.MainWindow.Focus();
        Pause(800);

        SaveTaskCardUxReviewCapture(
            session.MainWindow,
            Path.Combine(outputRoot, "desktop", $"{reviewCase.Key}.png"),
            report,
            reviewCase,
            viewport: "desktop",
            assetKey: $"desktop/{reviewCase.Key}");
    }

    private static void CaptureTaskCardUxPhoneCase(
        string outputRoot,
        TaskCardUxReviewReport report,
        string languageMode,
        TaskCardUxReviewCase reviewCase,
        bool noBuildBeforeLaunch)
    {
        using var session = LaunchTaskCardUxReviewSession(languageMode, reviewCase.TaskId, noBuildBeforeLaunch);
        var page = new MainWindowPage(new FlaUiControlResolver(session.MainWindow, session.ConditionFactory));
        WaitForTaskCardReviewTask(page, reviewCase, languageMode);

        ResizeWindowExact(session.MainWindow, 390, 844);
        session.MainWindow.Focus();
        Pause(800);

        SaveTaskCardUxReviewCapture(
            session.MainWindow,
            Path.Combine(outputRoot, "phone", $"{reviewCase.Key}-top.png"),
            report,
            reviewCase,
            viewport: "phone-top",
            assetKey: $"phone/{reviewCase.Key}-top");

        if (!TryFocusAutomationElement(
                session.MainWindow,
                session.ConditionFactory,
                reviewCase.CardFocusAutomationId,
                report.Warnings))
        {
            report.Warnings.Add(
                $"Phone card capture for '{reviewCase.Key}' reused current viewport because " +
                $"'{reviewCase.CardFocusAutomationId}' could not be focused.");
        }

        Pause(700);
        SaveTaskCardUxReviewCapture(
            session.MainWindow,
            Path.Combine(outputRoot, "phone", $"{reviewCase.Key}-card.png"),
            report,
            reviewCase,
            viewport: "phone-card",
            assetKey: $"phone/{reviewCase.Key}-card");
    }

    private static void CaptureTaskCardUxRelationEditorCase(
        string outputRoot,
        TaskCardUxReviewReport report,
        string languageMode,
        bool isPhone,
        bool noBuildBeforeLaunch)
    {
        var reviewCase = new TaskCardUxReviewCase(
            "blocked-relation-editor-open",
            "publish-landing",
            "Blocked relation editor open",
            "CurrentTaskBlockedRelationAddInput");

        using var session = LaunchTaskCardUxReviewSession(languageMode, reviewCase.TaskId, noBuildBeforeLaunch);
        var page = new MainWindowPage(new FlaUiControlResolver(session.MainWindow, session.ConditionFactory));
        WaitForTaskCardReviewTask(page, reviewCase, languageMode);

        if (isPhone)
        {
            ResizeWindowExact(session.MainWindow, 390, 844);
        }
        else
        {
            ResizeDesktopWindow(session.MainWindow, DesktopCaptureWindowWidth, DesktopCaptureWindowHeight);
        }

        session.MainWindow.Focus();
        Pause(800);
        page.ClickButton(static window => window.CurrentTaskBlockedRelationAddButton);
        WaitFor(
            () =>
            {
                try
                {
                    return string.Equals(
                        page.CurrentTaskBlockedRelationAddInput.AutomationId,
                        "CurrentTaskBlockedRelationAddInput",
                        StringComparison.Ordinal);
                }
                catch
                {
                    return false;
                }
            },
            TimeSpan.FromSeconds(10),
            "blocked relation editor input");

        TryFocusAutomationElement(
            session.MainWindow,
            session.ConditionFactory,
            "CurrentTaskBlockedRelationAddConfirmButton",
            report.Warnings);

        Pause(700);
        var folder = isPhone ? "phone" : "desktop";
        SaveTaskCardUxReviewCapture(
            session.MainWindow,
            Path.Combine(outputRoot, folder, "blocked-relation-editor-open.png"),
            report,
            reviewCase,
            viewport: isPhone ? "phone-editor" : "desktop-editor",
            assetKey: $"{folder}/blocked-relation-editor-open");
    }

    private static FlaUiDesktopAppSession LaunchTaskCardUxReviewSession(
        string languageMode,
        string currentTaskId,
        bool noBuildBeforeLaunch)
    {
        var launchOptions = UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(
            scenario: UnlimotionAutomationScenario.ReadmeDemo,
            language: languageMode,
            currentTaskId: currentTaskId,
            buildBeforeLaunch: !noBuildBeforeLaunch,
            buildOncePerProcess: true);

        return FlaUiDesktopAppSession.Launch(launchOptions);
    }

    private static void WaitForTaskCardReviewTask(
        MainWindowPage page,
        TaskCardUxReviewCase reviewCase,
        string languageMode)
    {
        var expectedTitle = UnlimotionAutomationScenarioData.GetTaskTitle(
            UnlimotionAutomationScenario.ReadmeDemo,
            reviewCase.TaskId,
            languageMode);

        WaitFor(
            () => string.Equals(page.CurrentTaskTitleTextBox.Text, expectedTitle, StringComparison.Ordinal),
            TimeSpan.FromSeconds(20),
            $"task-card UX review task '{expectedTitle}'");
    }

    private static bool TryFocusAutomationElement(
        FlaUiWindow window,
        FlaUI.Core.Conditions.ConditionFactory conditionFactory,
        string automationId,
        List<string> warnings)
    {
        var element = window.FindFirstDescendant(conditionFactory.ByAutomationId(automationId));
        if (element is null)
        {
            warnings.Add($"Automation element '{automationId}' was not found for UX review focus.");
            return false;
        }

        try
        {
            if (element.Patterns.ScrollItem.IsSupported)
            {
                element.Patterns.ScrollItem.Pattern.ScrollIntoView();
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Automation element '{automationId}' ScrollIntoView failed: {ex.Message}");
        }

        try
        {
            element.Focus();
            return true;
        }
        catch (Exception ex)
        {
            warnings.Add($"Automation element '{automationId}' focus failed: {ex.Message}");
            return false;
        }
    }

    private static void SaveTaskCardUxReviewCapture(
        FlaUiWindow window,
        string outputPath,
        TaskCardUxReviewReport report,
        TaskCardUxReviewCase reviewCase,
        string viewport,
        string assetKey)
    {
        using var bitmap = CaptureDesktopWindowBitmap(window);
        VerifyNonBlankBitmap(bitmap, assetKey);
        bitmap.Save(outputPath, ImageFormat.Png);

        var exactWindowTitle = window.Title;
        if (string.IsNullOrWhiteSpace(report.ExactWindowTitle))
        {
            report.ExactWindowTitle = exactWindowTitle;
        }

        report.Assets.Add(new TaskCardUxReviewAsset
        {
            Key = assetKey,
            DisplayName = reviewCase.DisplayName,
            TaskId = reviewCase.TaskId,
            Viewport = viewport,
            OutputPath = outputPath,
            WindowTitle = exactWindowTitle,
            Width = bitmap.Width,
            Height = bitmap.Height
        });
    }

    private static void VerifyNonBlankBitmap(Bitmap bitmap, string assetKey)
    {
        var minBrightness = 255;
        var maxBrightness = 0;
        var stepX = Math.Max(1, bitmap.Width / 24);
        var stepY = Math.Max(1, bitmap.Height / 24);

        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var color = bitmap.GetPixel(x, y);
                var brightness = (color.R + color.G + color.B) / 3;
                minBrightness = Math.Min(minBrightness, brightness);
                maxBrightness = Math.Max(maxBrightness, brightness);
            }
        }

        if (maxBrightness - minBrightness < 4)
        {
            throw new InvalidOperationException($"Captured UX review image '{assetKey}' appears blank.");
        }
    }

    private static string TryRunGit(string repositoryRoot, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);

            if (process is null)
            {
                return string.Empty;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? output : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
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

    private static Bitmap CaptureDesktopWindowBitmap(FlaUiWindow window, bool includeDesktopOverlays = false)
    {
        var handle = new IntPtr(window.Properties.NativeWindowHandle.ValueOrDefault);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Main window handle is not available for capture.");
        }

        var windowBounds = GetWindowBounds(handle);
        var captureBounds = GetCaptureBounds(handle);

        if (includeDesktopOverlays)
        {
            using var overlayBitmap = new Bitmap(captureBounds.Width, captureBounds.Height);
            if (TryBitBltDesktop(
                    captureBounds.Left,
                    captureBounds.Top,
                    overlayBitmap,
                    captureBounds.Width,
                    captureBounds.Height))
            {
                return new Bitmap(overlayBitmap);
            }
        }

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

    private static void ResizeWindowExact(FlaUiWindow window, int width, int height)
    {
        var handle = new IntPtr(window.Properties.NativeWindowHandle.ValueOrDefault);
        TryResize(window, width, height);
        Pause(250);

        if (handle == IntPtr.Zero)
        {
            return;
        }

        var placement = GetPreferredMonitorPlacement(handle);
        var scale = placement.Scale;
        var physicalWidth = Math.Max(1, (int)Math.Round(width * scale));
        var physicalHeight = Math.Max(1, (int)Math.Round(height * scale));
        var workArea = placement.WorkArea;
        var appliedLeft = workArea.Left + Math.Max(0, (workArea.Width - physicalWidth) / 2);
        var appliedTop = workArea.Top + Math.Max(0, (workArea.Height - physicalHeight) / 2);

        try
        {
            if (NativeMethods.SetWindowPos(
                    handle,
                    IntPtr.Zero,
                    appliedLeft,
                    appliedTop,
                    physicalWidth,
                    physicalHeight,
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

    private sealed record TaskCardUxReviewCase(
        string Key,
        string TaskId,
        string DisplayName,
        string CardFocusAutomationId);

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
        string? UxReview = null,
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
                    case "--ux-review":
                        if (index + 1 >= args.Length)
                        {
                            throw new ArgumentException("--ux-review requires a value.");
                        }

                        options = options with { UxReview = args[++index] };
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
            if (!string.IsNullOrWhiteSpace(OutputRoot))
            {
                return Path.GetFullPath(OutputRoot);
            }

            if (string.Equals(UxReview, "task-card", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(
                    repositoryRoot,
                    "artifacts",
                    "ux-review",
                    $"{DateTime.Now:yyyyMMdd-HHmm}-task-card");
            }

            return Path.Combine(repositoryRoot, "artifacts", "readme-media", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
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

    private sealed class TaskCardUxReviewReport
    {
        public DateTimeOffset GeneratedAt { get; init; }

        public string Scenario { get; init; } = string.Empty;

        public string Language { get; init; } = string.Empty;

        public string Branch { get; init; } = string.Empty;

        public string CommitSha { get; init; } = string.Empty;

        public string OutputRoot { get; init; } = string.Empty;

        public string CaptureCommand { get; init; } = string.Empty;

        public string ExactWindowTitle { get; set; } = string.Empty;

        public string DesktopViewport { get; init; } = string.Empty;

        public string PhoneViewport { get; init; } = string.Empty;

        public List<TaskCardUxReviewAsset> Assets { get; } = [];

        public List<string> Warnings { get; } = [];
    }

    private sealed class TaskCardUxReviewAsset
    {
        public string Key { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string TaskId { get; init; } = string.Empty;

        public string Viewport { get; init; } = string.Empty;

        public string OutputPath { get; init; } = string.Empty;

        public string WindowTitle { get; init; } = string.Empty;

        public int Width { get; init; }

        public int Height { get; init; }
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
