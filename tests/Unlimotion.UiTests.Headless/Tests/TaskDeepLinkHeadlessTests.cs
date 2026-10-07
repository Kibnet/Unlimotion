using System.Diagnostics;
using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TUnit;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Localization;

namespace Unlimotion.UiTests.Headless.Tests;

public sealed class TaskDeepLinkHeadlessTests
{
    private const string ChildFlag = "UNLIMOTION_DEEP_LINK_HEADLESS_CHILD";

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task TaskDeepLink_RenderedCardFiltersAndLocalizedMissingTask()
    {
        if (await RunRenderedChildIfNeeded()) return;
        var notifications = new AppToastNotificationManager();
        using var session = DesktopAppSession.Launch(UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
            afterViewModelPrepared: vm => vm.ToastNotificationManager = notifications,
            notificationManagerOverride: new NotificationManagerWrapper(notifications)));
        var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow));
        var directory = Path.GetFullPath(Path.Combine("artifacts", "deep-links", "appautomation", Guid.NewGuid().ToString("N")));
        var vm = HeadlessRuntime.Dispatch(() => (MainWindowViewModel)session.MainWindow.DataContext!);
        var previousLanguage = LocalizationService.Current.LanguageMode;
        try
        {
            var initial = HeadlessRuntime.Dispatch(() => vm.CurrentTaskItem!);
            var target = HeadlessRuntime.Dispatch(() => vm.taskRepository!.Tasks.Items.First(task => task.Id != vm.CurrentTaskItem!.Id));
            foreach (var filtered in new[] { false, true })
            {
                HeadlessRuntime.Dispatch(() =>
                {
                    Localization.SetLanguage("ru");
                    vm.AllTasksMode = true;
                    vm.CurrentTaskItem = initial;
                    vm.DetailsAreOpen = false;
                    vm.Search.SearchText = filtered ? "no-task-matches-deep-link-fixture" : string.Empty;
                    Dispatcher.UIThread.RunJobs();
                });
                if (filtered) await UiAssert.TextEqualsAsync(() => HeadlessRuntime.Dispatch(
                    () => vm.CurrentAllTasksItems.Count.ToString()), "0");
                HeadlessRuntime.Dispatch(() =>
                {
                    App.ActivateTaskDeepLink(vm, new TaskDeepLink(target.Id), session.MainWindow);
                    Dispatcher.UIThread.RunJobs();
                });
                await UiAssert.TextEqualsAsync(() => page.CurrentTaskTitleTextBox.Text, target.Title);
                await Assert.That(HeadlessRuntime.Dispatch(() => vm.CurrentTaskItem!.Id)).IsEqualTo(target.Id);
                await Assert.That(HeadlessRuntime.Dispatch(() => vm.DetailsAreOpen)).IsTrue();
                if (filtered) await Assert.That(HeadlessRuntime.Dispatch(() => vm.Search.SearchText))
                    .IsEqualTo("no-task-matches-deep-link-fixture");
                Console.WriteLine("AppAutomation screenshot: " + session.CaptureScreenshot(
                    Path.Combine(directory, $"opened-filtered-{filtered}.png")));
            }
            var before = HeadlessRuntime.Dispatch(() => (vm.CurrentTaskItem!.Id, vm.taskRepository!.Tasks.Count));
            foreach (var language in new[] { "ru", "en" })
            {
                var missingId = "missing-deep-link-" + language;
                HeadlessRuntime.Dispatch(() =>
                {
                    Localization.SetLanguage(language);
                    App.ActivateTaskDeepLink(vm, new TaskDeepLink(missingId), session.MainWindow);
                    Dispatcher.UIThread.RunJobs();
                });
                await Assert.That(HeadlessRuntime.Dispatch(() => (vm.CurrentTaskItem!.Id, vm.taskRepository!.Tasks.Count)))
                    .IsEqualTo(before);
                await UiAssert.TextContainsAsync(() => HeadlessRuntime.Dispatch(() =>
                    session.MainWindow.GetVisualDescendants().OfType<TextBlock>()
                        .FirstOrDefault(text => text.IsEffectivelyVisible && text.Text?.Contains(missingId, StringComparison.Ordinal) == true)
                        ?.Text ?? string.Empty), language == "ru" ? "не найдена" : "was not found");
                Console.WriteLine("AppAutomation screenshot: " + session.CaptureScreenshot(
                    Path.Combine(directory, $"missing-{language}.png")));
            }
        }
        finally
        {
            HeadlessRuntime.Dispatch(() =>
            {
                Localization.SetLanguage(previousLanguage);
                session.MainWindow.Close();
            });
        }
    }

    private static async Task<bool> RunRenderedChildIfNeeded()
    {
        if (Environment.GetEnvironmentVariable(ChildFlag) == "1") return false;
        // Avalonia caches its drawing backend. A fresh process keeps actual Skia
        // pixels independent of the full suite's existing semantic Headless tests.
        var directory = Path.Combine(AppContext.BaseDirectory, "artifacts", "deep-links", "rendered-child", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { typeof(TaskDeepLinkHeadlessTests).Assembly.Location,
                     "--treenode-filter", "/*/*/TaskDeepLinkHeadlessTests/*", "--maximum-parallel-tests", "1",
                     "--minimum-expected-tests", "1", "--report-trx", "--output", "Detailed",
                     "--results-directory", Path.Combine(directory, "results") }) start.ArgumentList.Add(argument);
        start.Environment[ChildFlag] = "1";
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Cannot start rendered Headless test.");
        var output = child.StandardOutput.ReadToEndAsync();
        var errors = child.StandardError.ReadToEndAsync();
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3)); }
        catch (TimeoutException)
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            throw;
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "stdout.log"), await output);
            await File.WriteAllTextAsync(Path.Combine(directory, "stderr.log"), await errors);
        }
        Console.WriteLine(await output);
        if (child.ExitCode != 0) throw new InvalidOperationException($"Rendered Headless failed ({child.ExitCode}); logs: {directory}");
        return true;
    }
}
