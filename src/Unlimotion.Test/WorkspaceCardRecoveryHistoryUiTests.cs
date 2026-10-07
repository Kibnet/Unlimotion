using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Newtonsoft.Json;
using Unlimotion.Domain;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Workspace;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class WorkspaceCardRecoveryHistoryUiTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReloadA_FromItsMenu_LeavesActiveCardBAndHistoryUnchanged(bool failReload)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(EmojiTitleTestAppBuilder));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = MainControlFilterToolbarResponsiveUiTests.CreateControlledEmojiFixture();
            var owner = fixture.MainWindowViewModelTest;
            await owner.Connect();
            var proxy = DispatchProxy.Create<ITaskStorage, WorkspaceTaskSaveGuardUiTests.FaultingTaskStorageProxy>();
            var fault = (WorkspaceTaskSaveGuardUiTests.FaultingTaskStorageProxy)(object)proxy;
            fault.Target = owner.taskRepository!;
            var a = WorkspaceTaskSaveGuardUiTests.ReplaceTaskBeforeOpening(owner, MainWindowViewModelFixture.RootTask1Id,
                proxy, out var originalA);
            var b = TestHelpers.GetTask(owner, MainWindowViewModelFixture.RootTask3Id);
            b.Description = string.Join('\n', Enumerable.Range(0, 80).Select(i => $"B context line {i}"));
            await owner.CommitWorkspaceEditorsAsync();
            var shell = new MainScreen { DataContext = owner };
            var window = new Window { Content = shell, Width = 1600, Height = 800 };
            try
            {
                window.Show(); Pump(window);
                await Assert.That(await owner.OpenWorkspaceTaskAsync(a)).IsTrue(); Pump(window);
                await Assert.That(await owner.OpenWorkspaceTaskAsync(b, WorkspaceOpenDisposition.AdjacentPane)).IsTrue(); Pump(window);
                var cards = shell.GetVisualDescendants().OfType<TaskCardView>().ToArray();
                var cardA = cards.Single(card => card.RouteTaskItem == a);
                var cardB = cards.Single(card => card.RouteTaskItem == b);
                var scrollB = Find<ScrollViewer>(cardB, "CurrentTaskDetailsScrollViewer");
                scrollB.Offset = new Vector(0, 100); Pump(window);
                await Assert.That(scrollB.Offset.Y).IsGreaterThan(90);
                var offset = scrollB.Offset;
                var tab = owner.WorkspaceNavigation.ActiveTab;
                var history = tab.History.Select(entry => entry.Location.HistoryKey).ToArray();
                var index = tab.CurrentIndex;
                var selection = owner.CurrentTaskItem;
                var titleB = Find<TextBox>(cardB, "CurrentTaskTitleTextBox");
                titleB.Text = "B unsaved text remains";
                var revision = EditableRevision(b);
                var path = Path.Combine(fixture.DefaultTasksFolderPath, a.Id);
                var snapshot = JsonConvert.DeserializeObject<TaskItem>(File.ReadAllText(path))!;
                File.WriteAllText(path, JsonConvert.SerializeObject(snapshot with { Title = "A reloaded from disk" }));
                var originalTitle = a.Title;
                fault.FailReloads = failReload;
                var actions = Find<DropDownButton>(cardA, "CurrentTaskActionsMenuButton");
                var menu = (MenuFlyout)actions.Flyout!;
                menu.ShowAt(actions); Pump(window);
                var reload = menu.Items.OfType<MenuItem>().First();
                await Assert.That(AutomationProperties.GetAutomationId(reload)).IsEqualTo("CurrentTaskReloadButton");
                await Assert.That(reload.Command).IsSameReferenceAs(a.ReloadTaskCommand);
                var input = TopLevel.GetTopLevel(reload)!;
                input.UpdateLayout();
                var point = reload.TranslatePoint(new Point(reload.Bounds.Width / 2, reload.Bounds.Height / 2), input)!.Value;
                input.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
                input.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                await Wait(() => (failReload ? a.HasTaskOperationError : a.Title == "A reloaded from disk") && a.CanReloadTask, window);
                if (failReload)
                {
                    await Assert.That(a.Title).IsEqualTo(originalTitle);
                    await Assert.That(Find<TextBlock>(cardA, "CurrentTaskOperationErrorText").Text).IsEqualTo(a.TaskOperationError);
                    await Assert.That(Find<Border>(cardB, "CurrentTaskOperationError").IsVisible).IsFalse();
                }
                await Assert.That(Find<TextBox>(cardA, "CurrentTaskTitleTextBox").Text).IsEqualTo(a.Title);
                await Assert.That(titleB.Text).IsEqualTo("B unsaved text remains");
                await Assert.That(EditableRevision(b)).IsEqualTo(revision);
                await Assert.That(scrollB.Offset).IsEqualTo(offset);
                await Assert.That(owner.CurrentTaskItem).IsSameReferenceAs(selection);
                await Assert.That(owner.WorkspaceNavigation.ActiveTab).IsSameReferenceAs(tab);
                await Assert.That(tab.CurrentIndex).IsEqualTo(index);
                await Assert.That(tab.History.Select(entry => entry.Location.HistoryKey).SequenceEqual(history)).IsTrue();
                Capture(window, failReload ? "recovery-error-a-card-b" : "recovery-a-card-b");
            }
            finally { window.Close(); WorkspaceTaskSaveGuardUiTests.RestoreOriginalTask(fixture, originalA, a); }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments("neighbor")]
    [Arguments("owner")]
    [Arguments("route")]
    [Arguments("scope")]
    [Arguments("detach")]
    public async Task OwnHistoryPaging_DisposingOtherCard_DoesNotResetCursorOrAcceptLateReply(string action)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(EmojiTitleTestAppBuilder));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = MainControlFilterToolbarResponsiveUiTests.CreateControlledEmojiFixture();
            var owner = fixture.MainWindowViewModelTest;
            await owner.Connect();
            var taskA = TestHelpers.GetTask(owner, MainWindowViewModelFixture.RootTask1Id);
            var taskB = TestHelpers.GetTask(owner, MainWindowViewModelFixture.RootTask3Id);
            var providerA = new ControlledHistory();
            var providerB = new ControlledHistory();
            using var a = new TaskCardView(owner, taskA, providerA);
            using var b = new TaskCardView(owner, taskB, providerB);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
            grid.Children.Add(a); Grid.SetColumn(b, 1); grid.Children.Add(b);
            var window = new Window { Content = grid, Width = 1600, Height = 800 };
            try
            {
                window.Show(); Pump(window);
                Find<Expander>(a, "StatusHistoryExpander").IsExpanded = true;
                Find<Expander>(b, "StatusHistoryExpander").IsExpanded = true;
                Pump(window);
                await Wait(() => a.TaskHistory.Entries.Count == 1 && b.TaskHistory.Entries.Count == 1, window);
                await Assert.That(providerA.Requests.Single().TaskId).IsEqualTo(taskA.Id);
                await Assert.That(providerB.Requests.Single().TaskId).IsEqualTo(taskB.Id);
                if (action == "neighbor")
                {
                    var field = a.GetVisualDescendants().OfType<TaskHistoryFieldChangeView>().Single();
                    Click(Find<Button>(field, "TaskHistoryShowDetailsButton"), window);
                    await Wait(() => field.IsDetailsExpanded, window);
                    await Assert.That(b.TaskHistory.HasDetails).IsFalse();
                    await Assert.That(a.TaskHistory.HasDetails).IsTrue();
                    Capture(window, "history-wide-details");
                    Click(Find<Button>(field, "TaskHistoryCloseDetailsButton"), window);
                    await Assert.That(field.IsDetailsExpanded).IsFalse();
                }
                var resets = providerA.Resets;
                var latePage = new TaskCompletionSource<TaskHistoryPage>();
                providerA.Delay = latePage;
                Click(Find<Button>(a, "TaskHistoryLoadMoreButton"), window);
                await Wait(() => providerA.Requests.Count == 2, window);
                await Assert.That(providerA.Requests.Last().Cursor).IsEqualTo("page2");
                var closeOwner = action != "neighbor";
                switch (action)
                {
                    case "owner": a.Dispose(); grid.Children.Remove(a); break;
                    case "neighbor": b.Dispose(); grid.Children.Remove(b); break;
                    case "route": a.RouteTaskItem = taskB; break;
                    case "scope": owner.WorkspaceNavigation.InvalidatePendingNavigation(); break;
                    case "detach": grid.Children.Remove(a); break;
                }
                latePage.SetResult(Page("late A page", null));
                await Task.Delay(50); Pump(window);
                await Assert.That(a.TaskHistory.Entries.Count).IsEqualTo(action == "scope" ? 1 : closeOwner ? 0 : 2);
                await Assert.That(providerA.Resets).IsEqualTo(action == "scope" ? resets + 2 : closeOwner ? resets + 1 : resets);
                if (action == "scope")
                {
                    await Assert.That(a.TaskHistory.Entries.Single().Message).IsEqualTo(taskA.Id);
                    await Assert.That(providerA.Requests.Count).IsEqualTo(3);
                }
                if (!closeOwner)
                {
                    await Assert.That(a.TaskHistory.Entries.Last().Message).IsEqualTo("late A page");
                    window.Width = 700; Pump(window);
                    Capture(window, "history-narrow");
                }
                if (action == "detach")
                {
                    providerA.Delay = null;
                    grid.Children.Add(a); Pump(window);
                    await Wait(() => a.TaskHistory.Entries.Count == 1, window);
                    await Assert.That(a.TaskHistory.Entries.Single().Message).IsEqualTo(taskA.Id);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments("dispose")]
    [Arguments("route")]
    [Arguments("scope")]
    [Arguments("detach")]
    public async Task LateHistoryDetails_DoNotExpandAfterOwnerInvalidation(string action)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(EmojiTitleTestAppBuilder));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = MainControlFilterToolbarResponsiveUiTests.CreateControlledEmojiFixture();
            var owner = fixture.MainWindowViewModelTest;
            await owner.Connect();
            var task = TestHelpers.GetTask(owner, MainWindowViewModelFixture.RootTask1Id);
            var provider = new ControlledHistory { DetailDelay = new TaskCompletionSource<string>() };
            using var card = new TaskCardView(owner, task, provider);
            var window = new Window { Content = card, Width = 800, Height = 800 };
            try
            {
                window.Show(); Pump(window);
                Find<Expander>(card, "StatusHistoryExpander").IsExpanded = true;
                await Wait(() => card.TaskHistory.Entries.Count == 1, window); Pump(window);
                var field = card.GetVisualDescendants().OfType<TaskHistoryFieldChangeView>().Single();
                Click(Find<Button>(field, "TaskHistoryShowDetailsButton"), window);
                await Wait(() => provider.DetailReads == 1, window);
                switch (action)
                {
                    case "dispose": card.Dispose(); break;
                    case "route": card.RouteTaskItem = TestHelpers.GetTask(owner, MainWindowViewModelFixture.RootTask3Id); break;
                    case "scope": owner.WorkspaceNavigation.InvalidatePendingNavigation(); break;
                    case "detach": window.Content = null; break;
                }
                provider.DetailDelay.SetResult("late private details");
                await Task.Delay(50); Pump(window);
                await Assert.That(card.TaskHistory.HasDetails).IsFalse();
                await Assert.That(field.IsDetailsExpanded).IsFalse();
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static TaskHistoryPage Page(string message, string? next) => new(
        new[] { new TaskHistoryEntry("1234567890", "Fixture author", DateTimeOffset.UtcNow, "Git", message,
            new[] { new TaskHistoryFieldChange("Title", "Title", "before…", "after…", TaskHistoryChangeType.Modified, false,
                new TaskHistoryValueReference("fixture", "123", "task", "Title", false),
                new TaskHistoryValueReference("fixture", "124", "task", "Title", false)) }) }, next, "");

    private sealed class ControlledHistory : ITaskHistoryProvider
    {
        public List<TaskHistoryRequest> Requests { get; } = new();
        public int Resets { get; private set; }
        public TaskCompletionSource<TaskHistoryPage>? Delay { get; set; }
        public TaskCompletionSource<string>? DetailDelay { get; set; }
        public int DetailReads { get; private set; }
        public Task<TaskHistoryPage> GetPageAsync(TaskHistoryRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            // Deliberately ignore cancellation: owner must reject a provider's late reply.
            var delayed = Delay;
            Delay = null;
            return delayed?.Task ?? Task.FromResult(Page(request.TaskId, "page2"));
        }
        public Task<string> ReadValueAsync(TaskHistoryValueReference reference, CancellationToken cancellationToken)
        { DetailReads++; return DetailDelay?.Task ?? Task.FromResult("full text"); }
        public void ResetSession() => Resets++;
    }
    private static T Find<T>(Control root, string id) where T : Control => root.GetVisualDescendants().OfType<T>()
        .Single(control => AutomationProperties.GetAutomationId(control) == id);
    private static long EditableRevision(Unlimotion.ViewModel.TaskItemViewModel task) =>
        (long)typeof(Unlimotion.ViewModel.TaskItemViewModel).GetField("_editableRevision", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(task)!;
    private static void Click(Control control, Window window)
    {
        control.BringIntoView(); Pump(window);
        var input = TopLevel.GetTopLevel(control)!;
        input.UpdateLayout();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), input)!.Value;
        input.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        input.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Pump(window);
    }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static async Task Wait(Func<bool> ready, Window window)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!ready()) { if (DateTime.UtcNow > deadline) throw new TimeoutException(); await Task.Delay(20); Pump(window); }
    }
    private static void Capture(Window window, string name)
    {
        Pump(window);
        Directory.CreateDirectory("chat-artifacts/card-recovery");
        using var frame = window.CaptureRenderedFrame();
        if (frame is null) throw new InvalidOperationException("No rendered frame");
        using var file = File.Create($"chat-artifacts/card-recovery/{name}.png");
        frame.Save(file);
    }
}
