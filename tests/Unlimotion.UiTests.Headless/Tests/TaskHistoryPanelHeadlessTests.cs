using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using AppAutomation.Avalonia.Headless.Session;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Input.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LibGit2Sharp;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Headless.Infrastructure;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Localization;
using Unlimotion.Views;

namespace Unlimotion.UiTests.Headless.Tests;

public sealed class TaskHistoryPanelHeadlessTests
{
    private const string ChildFlag = "UNLIMOTION_HISTORY_HEADLESS_CHILD";

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task History_is_compact_unselected_and_keeps_nested_actions_and_scrolling()
    {
        var output = Path.GetFullPath(Environment.GetEnvironmentVariable("UNLIMOTION_HISTORY_EVIDENCE")
            ?? Path.Combine(AppContext.BaseDirectory, "artifacts", "history-panel", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(output);
        if (await RunRenderedChildIfNeeded(output)) return;
        var historyPath = string.Empty;
        using var session = DesktopAppSession.Launch(UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
            language: "ru", prepareConfiguration: config => historyPath = CreateHistory(config)));
        using var presentation = new HeadlessWindowPresentation(session.MainWindow);
        var window = session.MainWindow;
        var failures = new List<string>();
        var observations = new List<object>();
        var main = Ui(() => window.GetVisualDescendants().OfType<MainControl>().Single());
        var vm = Ui(() => (MainWindowViewModel)window.DataContext!);
        Ui(() => { window.Width = 1200; window.Height = 850; vm.DetailsAreOpen = true; Layout(); });
        var expander = Find<Expander>("StatusHistoryExpander");
        var neighbor = Ui(() => main.GetVisualDescendants().OfType<Expander>()
            .Single(control => control.Classes.Contains("TaskOperationDetailsExpander")));
        var neighborMinimum = Ui(() => neighbor.MinHeight);
        var tree = Ui(() => window.GetVisualDescendants().OfType<TreeView>()
            .First(control => control.IsEffectivelyVisible && control.SelectedItem is not null));
        var selectedTask = Ui(() => tree.SelectedItem);
        try
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var width in new[] { 1200d, 650d })
            {
                var name = $"{theme}-{width:0}";
                Ui(() => { Application.Current!.RequestedThemeVariant = theme; window.Width = width;
                    expander.IsExpanded = true; Layout(); });
                await WaitUntil(() => !main.TaskHistory.IsLoading && main.TaskHistory.Entries.Count > 0, "Git history load");
                Ui(Layout);
                Capture(name + "-expanded");
                var list = Find<ListBox>("TaskHistoryItems");
                var row = Ui(() => list.GetRealizedContainers().OfType<ListBoxItem>().First());
                Check(Ui(() => AutomationProperties.GetName(row) == ((TaskHistoryEntry)row.DataContext!).Heading),
                    name + " commit accessible name");
                var scroll = Ui(() => list.GetVisualDescendants().OfType<ScrollViewer>().First());
                var before = Ui(() => scroll.Offset);
                for (var click = 0; click < 3; click++)
                {
                    Ui(() => Click(row, new Point(12, 12)));
                    CheckUnselected(list, name + " pointer");
                }
                Capture(name + "-after-pointer");
                Ui(() => row.Focus());
                foreach (var key in new[] { Key.Down, Key.Up, Key.Home, Key.End, Key.Space })
                {
                    Ui(() => Press(key));
                    CheckUnselected(list, name + " keyboard " + key);
                }
                // A nested action must survive both pointer selection and keyboard navigation.
                Ui(() => { scroll.Offset = default; Layout(); });
                var copy = Find<Button>("TaskHistoryCopyCommitButton");
                var sha = Ui(() => ((TaskHistoryEntry)copy.DataContext!).CommitSha);
                Ui(() => Click(copy));
                await WaitUntil(() => copy.IsFocused, "Pointer focus on SHA button");
                Check(await UiAsync(() => window.Clipboard!.TryGetTextAsync()) == sha, name + " pointer SHA copy");
                Ui(() => Press(Key.Enter));
                Check(await UiAsync(() => window.Clipboard!.TryGetTextAsync()) == sha, name + " Enter SHA copy");
                CheckUnselected(list, name + " nested SHA");
                Check(Ui(() => AutomationProperties.GetName(copy))!.Length > 0, "SHA accessible name");
                Ui(() => Press(Key.Tab));
                Check(Ui(() => window.FocusManager!.GetFocusedElement() is Control { IsEffectivelyVisible: true }),
                    name + " Tab keeps a visible focus target");
                // ListBox's existing TabNavigation=Once leaves the list after its active
                // descendant. Verify the other action through its own accessible focus.
                var show = Find<Button>("TaskHistoryShowDetailsButton");
                Ui(() => show.Focus());
                Check(Ui(() => show.IsFocused), name + " diff action accepts keyboard focus");
                Ui(() => Press(Key.Enter));
                await WaitUntil(() => main.TaskHistory.HasDetails, "Long diff opens using Enter");
                Check(Ui(() => main.TaskHistory.DetailNewValue.Length >= 460), "Full Git value loaded");
                Capture(name + "-diff");
                var close = Find<Button>("TaskHistoryCloseDetailsButton");
                await WaitUntil(() => close.IsFocused, "Diff close button receives focus");
                Ui(() => Press(Key.Enter));
                await WaitUntil(() => !main.TaskHistory.HasDetails && show.IsFocused, "Diff close restores focus");
                CheckUnselected(list, name + " diff close");
                Ui(() => { scroll.Offset = default; Layout(); Click(row, new Point(12, 12)); });
                Check(Ui(() => scroll.Offset) == before, name + " selection does not force scroll");
                Check(Ui(() => list.ItemsPanelRoot is VirtualizingStackPanel), "Virtualizing panel retained");
                Check(Ui(() => list.GetRealizedContainers().Count() < list.ItemCount), "Only viewport rows realized");
                Ui(() => { scroll.ScrollToEnd(); Layout(); });
                Check(Ui(() => scroll.Offset.Y > 0), "History scroll reaches later commits");
                Check(Ui(() => list.GetRealizedContainers().OfType<ListBoxItem>()
                    .Any(item => list.IndexFromContainer(item) >= list.ItemCount - 2)), "Last viewport rows realized");
                var historyPosition = Ui(() => expander.TranslatePoint(default, window));
                var body = Find<ScrollViewer>("CurrentTaskDetailsScrollViewer");
                Ui(() => { body.ScrollToEnd(); Layout(); });
                Check(Ui(() => expander.TranslatePoint(default, window)) == historyPosition, "History stays docked while body scrolls");
                Ui(() => Click(Find<ToggleButton>("TaskHistoryStatusModeButton")));
                Check(Ui(() => Find<ScrollViewer>("TaskHistoryStatusScrollViewer").IsEffectivelyVisible), "Statuses visible");
                Check(!Ui(() => list.IsEffectivelyVisible), "Git rows hidden in Status mode");
                Ui(() => Click(Find<ToggleButton>("TaskHistoryGitModeButton")));
                Ui(() => { expander.IsExpanded = false; Layout(); });
                var section = Find<Border>("CurrentTaskStatusHistorySection");
                var header = Ui(() => expander.GetVisualDescendants().OfType<ToggleButton>().Single(x => x.Name == "PART_HeaderSite"));
                var geometry = Ui(() => new
                {
                    width, theme = theme.ToString(), sectionHeight = section.Bounds.Height,
                    expanderHeight = expander.Bounds.Height, headerHeight = header.Bounds.Height,
                    bottomGap = section.Bounds.Height - header.TranslatePoint(new Point(0, header.Bounds.Height), section)!.Value.Y,
                    hiddenContent = !expander.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "TaskHistoryContent").IsVisible
                });
                observations.Add(geometry);
                Check(geometry.expanderHeight == 28 && geometry.sectionHeight == 34 && geometry.bottomGap <= 1 && geometry.hiddenContent,
                    name + " collapsed geometry " + JsonSerializer.Serialize(geometry));
                Capture(name + "-collapsed");
                Ui(() => { header.FontSize = 30; Layout(); });
                Check(Ui(() => header.Bounds.Height >= header.DesiredSize.Height), "Large header text is not clipped");
                Check(Ui(() => expander.Bounds.Height - header.Bounds.Height <= 1), "Large header has no bottom gap");
                Ui(() => { header.ClearValue(TemplatedControl.FontSizeProperty); Layout(); });
                Check(Ui(() => neighbor.MinHeight) == neighborMinimum, "Operation details minimum unchanged");
                Check(ReferenceEquals(Ui(() => tree.SelectedItem), selectedTask), "Task tree selection preserved");
            }
            Ui(() => { expander.IsExpanded = true; Layout(); });
            await WaitUntil(() => !main.TaskHistory.IsLoading && main.TaskHistory.CanLoadMore, "First page ready");
            var count = Ui(() => main.TaskHistory.Entries.Count);
            Ui(() => Click(Find<Button>("TaskHistoryLoadMoreButton")));
            await WaitUntil(() => !main.TaskHistory.IsLoading && main.TaskHistory.Entries.Count > count, "Load more through real button");
            Check(Ui(() => main.TaskHistory.Entries.Count == 61), "All Git pages loaded");
            var refresh = Ui(() =>
            {
                var pending = main.TaskHistory.RefreshAsync();
                Check(main.TaskHistory.IsLoading, "Refresh exposes loading state");
                Check(Find<ProgressBar>("TaskHistoryProgressBar").IsVisible, "Loading indicator visible");
                return pending;
            });
            await CompleteUiTask(refresh, "Refresh completes");
            Ui(() => { main.TaskHistory.SelectTask(historyPath, "local", Guid.NewGuid().ToString()); });
            await CompleteUiTask(Ui(() => main.TaskHistory.RefreshAsync()), "Empty history completes");
            Ui(Layout);
            Check(Ui(() => main.TaskHistory.Entries.Count == 0 && Find<TextBlock>("TaskHistoryStatusMessage").Text == Localization.Get("TaskHistoryEmpty")),
                "Empty Git history has a visible message");
            Ui(() => main.TaskHistory.SelectTask("\0", "local", "missing"));
            await CompleteUiTask(Ui(() => main.TaskHistory.RefreshAsync()), "Load error completes");
            Ui(Layout);
            Check(Ui(() => !main.TaskHistory.IsLoading && main.TaskHistory.HasStatusMessage &&
                main.TaskHistory.StatusMessage.StartsWith(Localization.Format("TaskHistoryLoadFailed", string.Empty), StringComparison.Ordinal) &&
                Find<TextBlock>("TaskHistoryStatusMessage").Text == main.TaskHistory.StatusMessage), "Load error has a visible message");
            Ui(() =>
            {
                // Expose the sibling's existing error state without a failing save or disk mutation.
                typeof(TaskItemViewModel).GetMethod("SetTaskOperationError", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(vm.CurrentTaskItem, ["Fixture operation error", "Fixture operation details"]);
                Find<ScrollViewer>("CurrentTaskDetailsScrollViewer").ScrollToHome();
                Layout();
            });
            Check(Ui(() => neighbor.IsEffectivelyVisible && neighbor.Bounds.Height == 48), "Sibling retains its collapsed height");
            Ui(() => { neighbor.IsExpanded = true; Layout(); });
            Check(Ui(() => neighbor.GetVisualDescendants().OfType<TextBlock>().Any(text =>
                text.IsEffectivelyVisible && text.Text == "Fixture operation details")), "Sibling still expands details");
            Capture("error-and-operation-details");
        }
        catch
        {
            Capture("failure");
            throw;
        }
        finally
        {
            File.WriteAllText(Path.Combine(output, "observations.json"), JsonSerializer.Serialize(new { observations, failures }, new JsonSerializerOptions { WriteIndented = true }));
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));

        void Check(bool condition, string message) { if (!condition) failures.Add(message); }
        void CheckUnselected(ListBox list, string action) => Check(Ui(() => list.SelectedItem is null && list.SelectedIndex == -1 &&
            list.GetRealizedContainers().OfType<ListBoxItem>().All(item => !item.IsSelected)), action + " leaves no selection");
        void Layout() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        void Press(Key key)
        {
            Enum.TryParse<PhysicalKey>(key.ToString(), out var physical);
            window.KeyPress(key, RawInputModifiers.None, physical, null);
            window.KeyRelease(key, RawInputModifiers.None, physical, null);
            Layout();
        }
        void Click(Control control, Point? local = null)
        {
            var point = control.TranslatePoint(local ?? new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            window.MouseMove(new Point(5, 5)); Layout();
        }
        T Find<T>(string id) where T : Control
        {
            T Resolve() => window.GetVisualDescendants().OfType<T>()
                .First(control => AutomationProperties.GetAutomationId(control) == id && control.IsEffectivelyVisible &&
                    (control is not Button || Hittable(control)));
            return Dispatcher.UIThread.CheckAccess() ? Resolve() : Ui(Resolve);
        }
        bool Hittable(Control control)
        {
            return control.Bounds.Width > 0 && control.GetVisualAncestors().OfType<ScrollViewer>().All(viewer =>
            {
                var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), viewer);
                return point is { } p && p.Y >= 0 && p.Y < viewer.Bounds.Height && p.X >= 0 && p.X < viewer.Bounds.Width;
            });
        }
        void Capture(string name) => Console.WriteLine("History screenshot: " + session.CaptureScreenshot(Path.Combine(output, name + ".png")));
    }

    private static string CreateHistory(string configPath)
    {
        var path = JsonNode.Parse(File.ReadAllText(configPath))!["TaskStorage"]!["Path"]!.GetValue<string>();
        var taskPath = Path.Combine(path, UnlimotionAppLaunchHost.CurrentTaskId);
        var task = JsonNode.Parse(File.ReadAllText(taskPath))!;
        task["PlannedBeginDateTime"] = new DateTimeOffset(DateTime.Today).ToString("yyyy-MM-ddTHH:mm:sszzz");
        task["Description"] = new string('a', 460);
        Repository.Init(path);
        using var repository = new Repository(path);
        for (var index = 0; index <= 60; index++)
        {
            task["Description"] = new string(index % 2 == 0 ? 'a' : 'b', 460) + index;
            File.WriteAllText(taskPath, task.ToJsonString());
            Commands.Stage(repository, "*");
            var author = new Signature("History UI fixture", "history@unlimotion.local", DateTimeOffset.Parse("2026-11-01T00:00:00Z").AddMinutes(index));
            repository.Commit($"History change {index}", author, author);
        }
        return path;
    }

    private static T Ui<T>(Func<T> action) => HeadlessRuntime.Dispatch(action);
    private static void Ui(Action action) => HeadlessRuntime.Dispatch(action);
    private static async Task<T> UiAsync<T>(Func<Task<T>> action)
    {
        var task = Ui(action);
        await CompleteUiTask(task, "Clipboard operation completes");
        return await task;
    }
    private static async Task CompleteUiTask(Task task, string reason)
    {
        // A Headless session pumps its dispatcher during Dispatch, not while this
        // test awaits externally. Poll through the existing session until ready.
        await WaitUntil(() => task.IsCompleted, reason);
        await task;
    }
    private static async Task WaitUntil(Func<bool> predicate, string reason)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!Ui(predicate))
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException(reason);
            await Task.Delay(30);
        }
        Ui(() => { Dispatcher.UIThread.RunJobs(); });
    }

    private static async Task<bool> RunRenderedChildIfNeeded(string directory)
    {
        if (Environment.GetEnvironmentVariable(ChildFlag) == "1") return false;
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { typeof(TaskHistoryPanelHeadlessTests).Assembly.Location, "--treenode-filter",
            "/*/*/TaskHistoryPanelHeadlessTests/*", "--maximum-parallel-tests", "1", "--minimum-expected-tests", "1",
            "--report-trx", "--results-directory", Path.Combine(directory, "child-results") }) start.ArgumentList.Add(argument);
        start.Environment[ChildFlag] = "1";
        start.Environment["UNLIMOTION_HISTORY_EVIDENCE"] = directory;
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Cannot start rendered history test.");
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(4)); }
        catch (TimeoutException) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); throw; }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "stdout.log"), await stdout);
            await File.WriteAllTextAsync(Path.Combine(directory, "stderr.log"), await stderr);
        }
        Console.WriteLine(await stdout);
        if (child.ExitCode != 0) throw new InvalidOperationException($"Rendered history failed ({child.ExitCode}); logs: {directory}");
        return true;
    }
}
