using System.Collections;
using System.Diagnostics;
using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TUnit;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Automation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;
using Unlimotion.UiTests.Authoring.Tests;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion.UiTests.Headless.Tests;

public sealed class TaskTabEmojiFilterHeadlessTests
{
    private const string ChildFlag = "UNLIMOTION_EMOJI_HEADLESS_CHILD";

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task EmojiFilters_RenderedIndependentTabsAndNarrowLastOpened()
    {
        if (await RunRenderedChildIfNeeded()) return;
        using var session = DesktopAppSession.Launch(UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(language: "ru"));
        var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow));
        var vm = HeadlessRuntime.Dispatch(() => (MainWindowViewModel)session.MainWindow.DataContext!);
        await Task.WhenAll(HeadlessRuntime.Dispatch(() => vm.taskRepository!.Tasks.Items.Select(task => task.SealPendingSaves()).ToArray()));
        var first = HeadlessRuntime.Dispatch(() => vm.taskRepository!.Tasks.Items.Single(task => task.Id == "10c107c1-a6f0-41fe-9b44-1f2fc5ff0fcf"));
        var second = HeadlessRuntime.Dispatch(() => vm.taskRepository!.Tasks.Items.Single(task => task.Id == "d63fbe66-4a91-44e4-b704-d85091831c56"));
        var directory = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("UNLIMOTION_EMOJI_SCREENSHOTS")
            ?? Path.Combine("artifacts", "emoji-tabs", "appautomation"), Guid.NewGuid().ToString("N")));
        try
        {
            HeadlessRuntime.Dispatch(() =>
            {
                Unlimotion.ViewModel.Localization.LocalizationService.Current.SetLanguage("ru");
                first.Title = "🚀 Запуск проекта";
                second.Title = "🧰 Работа с инструментами";
                first.CreatedDateTime = second.CreatedDateTime = DateTimeOffset.UtcNow;
                vm.DetailsAreOpen = false;
                session.MainWindow.Width = 900;
                session.MainWindow.Height = 760;
                session.MainWindow.Show();
                Dispatcher.UIThread.RunJobs();
            });
            await TaskTabEmojiFilterScenario.RunAsync(new Driver(session.MainWindow, page, vm, first, second, Capture), first.Id, second.Id);
            Console.WriteLine("Rendered runtime capabilities: " + string.Join(", ", session.GetType().GetMethods()
                .Where(method => method.Name.Contains("Capture", StringComparison.Ordinal)).Select(method => method.Name).Distinct()));
        }
        catch
        {
            try { Capture("failure"); }
            catch (Exception captureError) { Console.WriteLine("Failure screenshot unavailable: " + captureError.Message); }
            throw;
        }
        finally { HeadlessRuntime.Dispatch(session.MainWindow.Close); }

        void Capture(string name) => Console.WriteLine("AppAutomation screenshot: " + session.CaptureScreenshot(Path.Combine(directory, name + ".png")));
    }

    private sealed class Driver(Window window, MainWindowPage page, MainWindowViewModel vm,
        TaskItemViewModel first, TaskItemViewModel second, Action<string> capture) : ITaskTabEmojiFilterScenarioDriver
    {
        public void SelectTab(EmojiFilterScenarioTab tab)
        {
            switch (tab)
            {
                case EmojiFilterScenarioTab.AllTasks: page.SelectTabItem(static page => page.AllTasksTabItem, 10_000); break;
                case EmojiFilterScenarioTab.LastCreated: page.SelectTabItem(static page => page.LastCreatedTabItem, 10_000); break;
                case EmojiFilterScenarioTab.LastOpened: page.SelectTabItem(static page => page.LastOpenedTabItem, 10_000); break;
                default: throw new ArgumentOutOfRangeException(nameof(tab));
            }
        }

        public void Toggle(bool excluding, string emoji) => TaskTabEmojiFilterHeadlessTests.Toggle(window, excluding, emoji);
        public string[] SelectedEmoji(bool excluding) => HeadlessRuntime.Dispatch(() =>
            ((IEnumerable)Control(window, excluding).Filters!).OfType<EmojiFilter>()
                .Where(filter => filter.ShowTasks).Select(filter => filter.Emoji).ToArray());
        public string[] VisibleTaskIds() => HeadlessRuntime.Dispatch(() =>
            window.GetVisualDescendants().OfType<TreeView>().Single(tree => tree.IsEffectivelyVisible)
                .Items.OfType<TaskWrapperViewModel>().Select(task => task.Id).ToArray());
        public void CapturePopup(bool excluding, string name)
        {
            Open(window, excluding);
            capture(name);
            Close(window);
        }
        public void PrepareLastOpened() => HeadlessRuntime.Dispatch(() =>
        {
            vm.DetailsAreOpen = true;
            vm.CurrentTaskItem = first;
            vm.CurrentTaskItem = second;
            vm.DetailsAreOpen = false;
        });
        public void SetWidth(double width) => HeadlessRuntime.Dispatch(() => { window.Width = width; window.UpdateLayout(); });

        public void CopyFrom(EmojiFilterScenarioTab source) => HeadlessRuntime.Dispatch(() =>
        {
            var panel = OpenCopyPanel(window);
            var picker = panel.FindControl<DropDownButton>("CopySourceButton")!;
            Press(window, picker);
            var key = source switch
            {
                EmojiFilterScenarioTab.AllTasks => "AllTasks",
                EmojiFilterScenarioTab.LastCreated => "LastCreated",
                _ => "LastOpened"
            };
            var sourceButton = ((Control)((Flyout)picker.Flyout!).Content!).GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetAutomationId(button) == "EmojiFilterCopySource" + key);
            Press(window, sourceButton);
            CloseCopyPanel(window);
        });

        public void UndoCopy() => HeadlessRuntime.Dispatch(() =>
        {
            var panel = OpenCopyPanel(window);
            Press(window, panel.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetAutomationId(button) == "EmojiFilterCopyUndoButton"));
            CloseCopyPanel(window);
        });

        public void CaptureCopyPanel(string name, bool sources)
        {
            HeadlessRuntime.Dispatch(() =>
            {
                var panel = OpenCopyPanel(window);
                if (sources) Press(window, panel.FindControl<DropDownButton>("CopySourceButton")!);
            });
            capture(name);
            HeadlessRuntime.Dispatch(() =>
            {
                if (sources)
                {
                    var button = window.GetVisualDescendants().OfType<DropDownButton>()
                        .Single(button => button.IsEffectivelyVisible && AutomationProperties.GetAutomationId(button) == "AllTasksFiltersButton");
                    var panel = ((Control)((Flyout)button.Flyout!).Content!).GetLogicalDescendants().OfType<EmojiFilterCopyControl>().Single();
                    panel.FindControl<DropDownButton>("CopySourceButton")!.Flyout!.Hide();
                }
                CloseCopyPanel(window);
            });
        }
    }

    private static EmojiFilterCopyControl OpenCopyPanel(Window window)
    {
        var button = window.GetVisualDescendants().OfType<DropDownButton>()
            .Single(button => button.IsEffectivelyVisible && AutomationProperties.GetAutomationId(button) == "AllTasksFiltersButton");
        Press(window, button);
        return ((Control)((Flyout)button.Flyout!).Content!).GetLogicalDescendants().OfType<EmojiFilterCopyControl>().Single();
    }

    private static void CloseCopyPanel(Window window)
    {
        var button = window.GetVisualDescendants().OfType<DropDownButton>()
            .Single(button => button.IsEffectivelyVisible && AutomationProperties.GetAutomationId(button) == "AllTasksFiltersButton");
        button.Flyout!.Hide();
    }

    private static void Press(Window window, Control control)
    {
        control.Focus();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static EmojiFilterMultiSelectSearchBox Control(Window window, bool exclude) =>
        window.GetVisualDescendants().OfType<EmojiFilterMultiSelectSearchBox>()
            .Single(control => control.IsEffectivelyVisible && control.IsExclude == exclude);

    private static void Open(Window window, bool exclude) => HeadlessRuntime.Dispatch(() =>
    {
        window.UpdateLayout();
        Control(window, exclude).FindControl<TextBox>("PART_Input")!.Focus();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        if (!Control(window, exclude).FindControl<Popup>("PART_DropDownPopup")!.IsOpen)
            throw new InvalidOperationException("Emoji popup did not open using Enter.");
    });

    private static void Toggle(Window window, bool exclude, string emoji)
    {
        Open(window, exclude);
        HeadlessRuntime.Dispatch(() =>
        {
            var control = Control(window, exclude);
            var list = control.FindControl<ListBox>("PART_List")!;
            list.SelectedItem = ((IEnumerable)control.Filters!).OfType<EmojiFilter>().Single(filter => filter.Emoji == emoji);
            list.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Dispatcher.UIThread.RunJobs();
        });
        Close(window);
    }

    private static void Close(Window window) => HeadlessRuntime.Dispatch(() =>
    {
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
    });

    private static async Task<bool> RunRenderedChildIfNeeded()
    {
        if (Environment.GetEnvironmentVariable(ChildFlag) == "1") return false;
        // Keep actual Skia pixels isolated from the full suite's cached semantic backend.
        var directory = Path.Combine(AppContext.BaseDirectory, "artifacts", "emoji-tabs", "rendered-child", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { typeof(TaskTabEmojiFilterHeadlessTests).Assembly.Location,
            "--treenode-filter", "/*/*/TaskTabEmojiFilterHeadlessTests/*", "--maximum-parallel-tests", "1",
            "--minimum-expected-tests", "1", "--report-trx", "--output", "Detailed",
            "--results-directory", Path.Combine(directory, "results") }) start.ArgumentList.Add(argument);
        start.Environment[ChildFlag] = "1";
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Cannot start rendered emoji Headless test.");
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
        if (child.ExitCode != 0) throw new InvalidOperationException($"Rendered emoji Headless failed ({child.ExitCode}); logs: {directory}");
        return true;
    }
}
