using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData;
using Newtonsoft.Json;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Workspace;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class WorkspaceTaskSaveGuardUiTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ImmediateTaskEditorChange_IsGuardedBeforeNavigationOrScopeCommit(bool inlineScopeCommit)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            TaskItemViewModel? task = null;
            TaskItemViewModel? originalTask = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                await owner.Connect();
                var faultStorage = DispatchProxy.Create<ITaskStorage, FaultingTaskStorageProxy>();
                var fault = (FaultingTaskStorageProxy)(object)faultStorage;
                fault.Target = owner.taskRepository!;
                task = ReplaceTaskBeforeOpening(owner, MainWindowViewModelFixture.RootTask1Id, faultStorage, out originalTask);
                var filePath = Path.Combine(fixture.DefaultTasksFolderPath, task.Id);
                var originalPersistedTitle = ReadPersistedTitle(filePath);
                var shell = new MainScreen { DataContext = owner };
                window = new Window { Width = 1600, Height = 800, Content = shell };
                window.Show();
                Pump(window);
                TextBox editor;
                if (inlineScopeCommit)
                {
                    var list = shell.GetVisualDescendants().OfType<TaskListDocumentView>().Single();
                    var tree = list.TaskTree!;
                    tree.SelectedItem = tree.ItemsSource!.Cast<TaskWrapperViewModel>().Single(item => item.TaskItem.Id == task.Id);
                    tree.Focus();
                    Pump(window);
                    tree.RaiseEvent(new KeyEventArgs
                    {
                        RoutedEvent = InputElement.KeyDownEvent, Key = Key.F2, Source = tree
                    });
                    Pump(window);
                    editor = Find<TextBox>(shell, "InlineTaskTitleTextBox");
                    await Assert.That(shell.GetVisualDescendants().OfType<TaskCardView>().Any()).IsFalse();
                }
                else
                {
                    await Assert.That(await owner.OpenWorkspaceTaskAsync(task)).IsTrue();
                    Pump(window);
                    editor = Find<TextBox>(shell, "CurrentTaskTitleTextBox");
                    await Assert.That(shell.GetVisualDescendants().OfType<TaskCardView>().Single().RouteTaskItem).IsSameReferenceAs(task);
                }

                fault.FailUpdates = true;
                var pane = owner.WorkspaceNavigation.ActivePane;
                var tab = owner.WorkspaceNavigation.ActiveTab;
                var history = tab.History.Select(entry => entry.Location.HistoryKey).ToArray();
                var historyIndex = tab.CurrentIndex;
                var scope = owner.WorkspaceNavigation.ScopeRevision;
                const string draft = "Exact task title must survive rejected navigation";
                editor.Focus();
                editor.Text = draft;
                editor.CaretIndex = 12;
                // Intentionally no debounce delay: the guard must see the edit immediately.
                await Assert.That(task.HasPendingEditableChanges).IsTrue();
                if (inlineScopeCommit)
                    await Assert.That(() => owner.CommitWorkspaceEditorsAsync()).Throws<InvalidOperationException>();
                else
                    await Assert.That(await owner.OpenWorkspaceLocationAsync(
                        WorkspaceLocation.ForTaskList(TaskListKind.LastUpdated))).IsFalse();
                Pump(window);
                await Assert.That(fault.FailedUpdateCount).IsGreaterThan(0);
                await Assert.That(ReadPersistedTitle(filePath)).IsEqualTo(originalPersistedTitle);
                await Assert.That(editor.IsEffectivelyVisible).IsTrue();
                await Assert.That(editor.Text).IsEqualTo(draft);
                await Assert.That(editor.CaretIndex).IsEqualTo(12);
                await Assert.That(task.HasPendingEditableChanges).IsTrue();
                await Assert.That(owner.WorkspaceNavigation.ActivePane).IsSameReferenceAs(pane);
                await Assert.That(owner.WorkspaceNavigation.ActiveTab).IsSameReferenceAs(tab);
                await Assert.That(tab.CurrentIndex).IsEqualTo(historyIndex);
                await Assert.That(tab.History.Select(entry => entry.Location.HistoryKey).SequenceEqual(history)).IsTrue();
                await Assert.That(owner.WorkspaceNavigation.ScopeRevision).IsEqualTo(scope);
                await Assert.That(((NotificationManagerWrapperMock)owner.ManagerWrapper).LastErrorMessage!).Contains("Injected task update failure");
                if (!inlineScopeCommit)
                {
                    await Assert.That(shell.GetVisualDescendants().OfType<TaskCardView>().Single().RouteTaskItem).IsSameReferenceAs(task);
                }

                fault.FailUpdates = false;
                if (inlineScopeCommit) await owner.CommitWorkspaceEditorsAsync();
                await Assert.That(await owner.OpenWorkspaceLocationAsync(
                    WorkspaceLocation.ForTaskList(TaskListKind.LastUpdated))).IsTrue();
                Pump(window);
                await Assert.That(ReadPersistedTitle(filePath)).IsEqualTo(draft);
                await Assert.That(task.HasPendingEditableChanges).IsFalse();
                await Assert.That(owner.WorkspaceNavigation.ActiveTab).IsSameReferenceAs(tab);
                await Assert.That(tab.CurrentLocation!.TaskListKind).IsEqualTo(TaskListKind.LastUpdated);
                await Assert.That(shell.GetVisualDescendants().OfType<TaskCardView>().Any()).IsFalse();
                await Assert.That(shell.GetVisualDescendants().OfType<TaskListDocumentView>().Single().Kind)
                    .IsEqualTo(TaskListKind.LastUpdated);
            }
            finally
            {
                if (window is not null) { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
                RestoreOriginalTask(fixture, originalTask, task);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ScopeCommit_RechecksEarlierVisibleCardEditedWhileLaterCardIsPersisting()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            TaskItemViewModel? taskB = null;
            TaskItemViewModel? originalTask = null;
            FaultingTaskStorageProxy? delayed = null;
            Task? commit = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                await owner.Connect();
                // Choose the actual flush traversal order rather than assuming
                // the fixture file enumeration orders two particular IDs.
                var cached = owner.taskRepository!.Tasks.Items.ToArray();
                var taskA = cached.First();
                taskB = cached.Last();
                await Assert.That(taskA.Id).IsNotEqualTo(taskB.Id);
                var proxy = DispatchProxy.Create<ITaskStorage, FaultingTaskStorageProxy>();
                delayed = (FaultingTaskStorageProxy)(object)proxy;
                delayed.Target = owner.taskRepository!;
                taskB = ReplaceTaskBeforeOpening(owner, taskB.Id, proxy, out originalTask);
                var shell = new MainScreen { DataContext = owner };
                window = new Window { Width = 1600, Height = 800, Content = shell };
                window.Show();
                await Assert.That(await owner.OpenWorkspaceTaskAsync(taskA)).IsTrue();
                await Assert.That(await owner.OpenWorkspaceTaskAsync(taskB, WorkspaceOpenDisposition.AdjacentPane)).IsTrue();
                Pump(window);
                var primary = owner.WorkspaceNavigation.PrimaryPane;
                var secondary = owner.WorkspaceNavigation.SecondaryPane!;
                var cardA = shell.GetVisualDescendants().OfType<TaskCardView>()
                    .Single(card => ReferenceEquals(card.RouteTaskItem, taskA));
                var cardB = shell.GetVisualDescendants().OfType<TaskCardView>()
                    .Single(card => ReferenceEquals(card.RouteTaskItem, taskB));
                var editorA = Find<TextBox>(cardA, "CurrentTaskTitleTextBox");
                var editorB = Find<TextBox>(cardB, "CurrentTaskTitleTextBox");
                delayed.DelayNextUpdate = true;
                const string initialA = "A first committed title";
                const string latestA = "A edited again while B was saving";
                const string titleB = "B delayed committed title";
                editorA.Text = initialA;
                editorB.Text = titleB;
                await Assert.That(taskA.HasPendingEditableChanges).IsTrue();
                await Assert.That(taskB.HasPendingEditableChanges).IsTrue();
                var scope = owner.WorkspaceNavigation.ScopeRevision;
                var primaryHistory = (primary.ActiveTab ?? throw new InvalidOperationException("Primary tab is missing."))
                    .History.Select(entry => entry.Location.HistoryKey).ToArray();
                var secondaryHistory = (secondary.ActiveTab ?? throw new InvalidOperationException("Secondary tab is missing."))
                    .History.Select(entry => entry.Location.HistoryKey).ToArray();

                commit = owner.CommitWorkspaceEditorsAsync();
                await delayed.UpdateEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                // The earlier card has already been flushed. A second edit during
                // B's await must force another pass before a scope can be reset.
                await AssertPersistedTitleAsync(Path.Combine(fixture.DefaultTasksFolderPath, taskA.Id), initialA);
                if (commit.IsFaulted) await commit;
                await Assert.That(commit.IsCompleted).IsFalse();
                editorA.Focus();
                editorA.Text = latestA;
                await Assert.That(taskA.HasPendingEditableChanges).IsTrue();
                delayed.ReleaseUpdate.TrySetResult(true);
                await commit.WaitAsync(TimeSpan.FromSeconds(10));
                Pump(window);

                await AssertPersistedTitleAsync(Path.Combine(fixture.DefaultTasksFolderPath, taskA.Id), latestA);
                await AssertPersistedTitleAsync(Path.Combine(fixture.DefaultTasksFolderPath, taskB.Id), titleB);
                await Assert.That(taskA.HasPendingEditableChanges).IsFalse();
                await Assert.That(taskB.HasPendingEditableChanges).IsFalse();
                await Assert.That(taskA.HasPendingEditorPersistence).IsFalse();
                await Assert.That(taskB.HasPendingEditorPersistence).IsFalse();
                await Assert.That(editorA.Text).IsEqualTo(latestA);
                await Assert.That(editorB.Text).IsEqualTo(titleB);
                await Assert.That(owner.WorkspaceNavigation.ScopeRevision).IsEqualTo(scope + 1);
                await Assert.That(owner.WorkspaceNavigation.PrimaryPane).IsSameReferenceAs(primary);
                await Assert.That(owner.WorkspaceNavigation.SecondaryPane).IsSameReferenceAs(secondary);
                await Assert.That(primary.ActiveTab.History.Select(entry => entry.Location.HistoryKey).SequenceEqual(primaryHistory)).IsTrue();
                await Assert.That(secondary.ActiveTab.History.Select(entry => entry.Location.HistoryKey).SequenceEqual(secondaryHistory)).IsTrue();
            }
            finally
            {
                delayed?.ReleaseUpdate.TrySetResult(true);
                if (commit is not null && !commit.IsCompleted)
                {
                    try { await commit.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* Preserve the original assertion failure. */ }
                }
                if (window is not null) { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
                RestoreOriginalTask(fixture, originalTask, taskB);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ScopeCommit_RechecksSourceBlockEditedWhileTaskIsPersisting(bool thematicNote)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            TaskItemViewModel? task = null;
            TaskItemViewModel? originalTask = null;
            FaultingTaskStorageProxy? delayed = null;
            Task? commit = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                await owner.Connect();
                var proxy = DispatchProxy.Create<ITaskStorage, FaultingTaskStorageProxy>();
                delayed = (FaultingTaskStorageProxy)(object)proxy;
                delayed.Target = owner.taskRepository!;
                task = ReplaceTaskBeforeOpening(owner, MainWindowViewModelFixture.RootTask1Id, proxy, out originalTask);
                var vault = Path.Combine(fixture.FixtureDirectoryPath, "LateSourceDraft");
                var relativePath = thematicNote ? "Source.md" : "Ежедневные/2026-10-02.md";
                var sourcePath = Path.Combine(vault, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
                const string original = "Original source block";
                const string latest = "Late source block edited while task was saving";
                await File.WriteAllTextAsync(sourcePath, original);
                await owner.Feed.InitializeVaultAsync(vault);
                var shell = new MainScreen { DataContext = owner };
                window = new Window { Width = 1600, Height = 800, Content = shell };
                window.Show();
                await Assert.That(await owner.OpenWorkspaceLocationAsync(thematicNote
                    ? WorkspaceLocation.ForNote(relativePath, "Source") : WorkspaceLocation.FeedRoot)).IsTrue();
                await Assert.That(await owner.OpenWorkspaceTaskAsync(task, WorkspaceOpenDisposition.AdjacentPane)).IsTrue();
                Pump(window);
                var feedPane = shell.GetVisualDescendants().OfType<WorkspacePaneView>()
                    .Single(pane => AutomationProperties.GetAutomationId(pane) == "WorkspacePrimaryPane");
                var sourceEditor = thematicNote
                    ? feedPane.FeedView.DisplayedDocument!.MarkdownEditor
                    : owner.Feed.Days.Single().MarkdownEditor;
                var block = sourceEditor.Blocks.Single(candidate => candidate.PreviewText == original);
                var preview = Find<MarkdownBlockPreviewControl>(feedPane.FeedView, block.PreviewAutomationId);
                var card = shell.GetVisualDescendants().OfType<TaskCardView>().Single();
                delayed.DelayNextUpdate = true;
                const string taskTitle = "Task title with deliberately delayed storage";
                Find<TextBox>(card, "CurrentTaskTitleTextBox").Text = taskTitle;
                await Assert.That(owner.Feed.HasPendingEditorChanges).IsFalse();
                commit = owner.CommitWorkspaceEditorsAsync();
                await delayed.UpdateEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(commit.IsCompleted).IsFalse();
                await Assert.That(await File.ReadAllTextAsync(sourcePath)).IsEqualTo(original);

                // Begin editing through the real preview keyboard interaction,
                // after the source's initial guard has already completed.
                preview.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Pump(window);
                var input = Find<TextBox>(feedPane.FeedView, block.EditorAutomationId);
                input.Text = latest;
                await Assert.That(owner.Feed.HasPendingEditorChanges).IsTrue();
                delayed.ReleaseUpdate.TrySetResult(true);
                await commit.WaitAsync(TimeSpan.FromSeconds(10));
                Pump(window);

                await Assert.That(await File.ReadAllTextAsync(sourcePath)).IsEqualTo(latest);
                await Assert.That(sourceEditor.Snapshot!.Raw).IsEqualTo(latest);
                await Assert.That(owner.Feed.HasPendingEditorChanges).IsFalse();
                await Assert.That(sourceEditor.ActiveBlock?.IsDirty == true).IsFalse();
                await Assert.That(ReadPersistedTitle(Path.Combine(fixture.DefaultTasksFolderPath, task.Id))).IsEqualTo(taskTitle);
                await Assert.That(task.HasPendingEditorPersistence).IsFalse();
                await Assert.That(task.HasPendingEditableChanges).IsFalse();
            }
            finally
            {
                delayed?.ReleaseUpdate.TrySetResult(true);
                if (commit is not null && !commit.IsCompleted)
                {
                    try { await commit.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* Preserve the original assertion failure. */ }
                }
                if (window is not null) { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
                RestoreOriginalTask(fixture, originalTask, task);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OrdinaryTransition_RechecksLateEditAndRejectsItsFailedSave(bool moveTab)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            TaskItemViewModel? originalA = null, originalB = null, taskA = null, taskB = null;
            FaultingTaskStorageProxy? delayedB = null;
            Task<bool>? transition = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                await owner.Connect();
                var storageA = DispatchProxy.Create<ITaskStorage, FaultingTaskStorageProxy>();
                var faultA = (FaultingTaskStorageProxy)(object)storageA;
                faultA.Target = owner.taskRepository!;
                var storageB = DispatchProxy.Create<ITaskStorage, FaultingTaskStorageProxy>();
                delayedB = (FaultingTaskStorageProxy)(object)storageB;
                delayedB.Target = owner.taskRepository!;
                taskA = ReplaceTaskBeforeOpening(owner, MainWindowViewModelFixture.RootTask1Id, storageA, out originalA);
                taskB = ReplaceTaskBeforeOpening(owner, MainWindowViewModelFixture.RootTask4Id, storageB, out originalB);
                var shell = new MainScreen { DataContext = owner };
                window = new Window { Width = 1600, Height = 800, Content = shell };
                window.Show();
                await Assert.That(await owner.OpenWorkspaceTaskAsync(taskA)).IsTrue();
                await Assert.That(await owner.OpenWorkspaceTaskAsync(taskB, WorkspaceOpenDisposition.AdjacentPane)).IsTrue();
                var primary = owner.WorkspaceNavigation.PrimaryPane;
                var secondary = owner.WorkspaceNavigation.SecondaryPane!;
                owner.ActivateWorkspacePane(primary);
                Pump(window);
                var tabA = primary.ActiveTab ?? throw new InvalidOperationException("Primary tab is missing.");
                var tabB = secondary.ActiveTab ?? throw new InvalidOperationException("Secondary tab is missing.");
                var cardA = shell.GetVisualDescendants().OfType<TaskCardView>()
                    .Single(card => ReferenceEquals(card.RouteTaskItem, taskA));
                var cardB = shell.GetVisualDescendants().OfType<TaskCardView>()
                    .Single(card => ReferenceEquals(card.RouteTaskItem, taskB));
                var editorA = Find<TextBox>(cardA, "CurrentTaskTitleTextBox");
                var editorB = Find<TextBox>(cardB, "CurrentTaskTitleTextBox");
                const string firstA = "A saved before transition waits for B";
                const string latestA = "Late A draft must survive rejected ordinary transition";
                const string titleB = "B saved during ordinary transition";
                delayedB.DelayNextUpdate = true;
                editorA.Text = firstA;
                editorB.Text = titleB;
                var historyA = tabA.History.Select(entry => entry.Location.HistoryKey).ToArray();
                var historyB = tabB.History.Select(entry => entry.Location.HistoryKey).ToArray();
                var indexA = tabA.CurrentIndex;
                var indexB = tabB.CurrentIndex;
                var scope = owner.WorkspaceNavigation.ScopeRevision;
                Task<bool> RunTransition() => moveTab
                    ? owner.MoveWorkspaceTabAsync(primary, tabA)
                    : owner.OpenWorkspaceTaskAsync(taskB);
                transition = RunTransition();
                await delayedB.UpdateEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await AssertPersistedTitleAsync(Path.Combine(fixture.DefaultTasksFolderPath, taskA.Id), firstA);
                if (transition.IsCompleted)
                {
                    var outcome = transition.IsCompletedSuccessfully ? transition.Result.ToString() : transition.Exception?.ToString() ?? transition.Status.ToString();
                    throw new InvalidOperationException($"Early transition: move={moveTab}, outcome={outcome}, scope={scope}/{owner.WorkspaceNavigation.ScopeRevision}, A identity={ReferenceEquals(owner.ResolveTaskById(taskA.Id), taskA)}, B identity={ReferenceEquals(owner.ResolveTaskById(taskB.Id), taskB)}, pending A={taskA.HasPendingEditorPersistence}, B={taskB.HasPendingEditorPersistence}, B barrier released={delayedB.ReleaseUpdate.Task.IsCompleted}, toast={((NotificationManagerWrapperMock)owner.ManagerWrapper).LastErrorMessage}");
                }
                await Assert.That(transition.IsCompleted).IsFalse();
                faultA.FailUpdates = true;
                editorA.Focus();
                editorA.Text = latestA;
                editorA.CaretIndex = 12;
                await Assert.That(taskA.HasPendingEditableChanges).IsTrue();
                delayedB.ReleaseUpdate.TrySetResult(true);
                await Assert.That(await transition.WaitAsync(TimeSpan.FromSeconds(10))).IsFalse();
                Pump(window);

                await Assert.That(faultA.FailedUpdateCount).IsGreaterThan(0);
                await Assert.That(owner.WorkspaceNavigation.PrimaryPane).IsSameReferenceAs(primary);
                await Assert.That(owner.WorkspaceNavigation.SecondaryPane).IsSameReferenceAs(secondary);
                await Assert.That(owner.WorkspaceNavigation.ActivePane).IsSameReferenceAs(primary);
                await Assert.That(primary.ActiveTab).IsSameReferenceAs(tabA);
                await Assert.That(secondary.ActiveTab).IsSameReferenceAs(tabB);
                await Assert.That(primary.Tabs.Count).IsEqualTo(1);
                await Assert.That(secondary.Tabs.Count).IsEqualTo(1);
                await Assert.That(tabA.CurrentIndex).IsEqualTo(indexA);
                await Assert.That(tabB.CurrentIndex).IsEqualTo(indexB);
                await Assert.That(tabA.History.Select(entry => entry.Location.HistoryKey).SequenceEqual(historyA)).IsTrue();
                await Assert.That(tabB.History.Select(entry => entry.Location.HistoryKey).SequenceEqual(historyB)).IsTrue();
                await Assert.That(owner.WorkspaceNavigation.ScopeRevision).IsEqualTo(scope);
                await Assert.That(editorA.IsEffectivelyVisible).IsTrue();
                await Assert.That(editorA.Text).IsEqualTo(latestA);
                await Assert.That(editorA.CaretIndex).IsEqualTo(12);
                await Assert.That(taskA.HasPendingEditorPersistence).IsTrue();
                await AssertPersistedTitleAsync(Path.Combine(fixture.DefaultTasksFolderPath, taskA.Id), firstA);
                await Assert.That(((NotificationManagerWrapperMock)owner.ManagerWrapper).LastErrorMessage!)
                    .Contains("Injected task update failure");

                faultA.FailUpdates = false;
                await Assert.That(await RunTransition()).IsTrue();
                Pump(window);
                await AssertPersistedTitleAsync(Path.Combine(fixture.DefaultTasksFolderPath, taskA.Id), latestA);
                await AssertPersistedTitleAsync(Path.Combine(fixture.DefaultTasksFolderPath, taskB.Id), titleB);
                await Assert.That(taskA.HasPendingEditorPersistence).IsFalse();
                await Assert.That(taskB.HasPendingEditorPersistence).IsFalse();
                await Assert.That(owner.WorkspaceNavigation.ActiveTab).IsSameReferenceAs(moveTab ? tabA : tabB);
            }
            finally
            {
                delayedB?.ReleaseUpdate.TrySetResult(true);
                if (transition is not null && !transition.IsCompleted)
                {
                    try { await transition.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* Preserve original failure. */ }
                }
                if (window is not null) { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
                RestoreOriginalTask(fixture, originalA, taskA);
                RestoreOriginalTask(fixture, originalB, taskB);
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    public class FaultingTaskStorageProxy : DispatchProxy
    {
        public ITaskStorage Target { get; set; } = null!;
        public bool FailUpdates { get; set; }
        public bool FailReloads { get; set; }
        public int FailedUpdateCount { get; private set; }
        public bool DelayNextUpdate { get; set; }
        public TaskCompletionSource<bool> UpdateEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseUpdate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method is null) throw new InvalidOperationException("Missing storage method.");
            if (method.Name == nameof(ITaskStorage.ReloadTaskAsync) && FailReloads)
                return Task.FromException<TaskReloadResult>(new IOException("Injected task reload failure"));
            if (method.Name == nameof(ITaskStorage.Update) && FailUpdates)
            {
                FailedUpdateCount++;
                return Task.FromException<TaskItemViewModel>(new IOException("Injected task update failure"));
            }
            if (method.Name == nameof(ITaskStorage.Update) && DelayNextUpdate)
            {
                DelayNextUpdate = false;
                return PersistDelayedAsync(method, arguments);
            }
            try { return method.Invoke(Target, arguments); }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        private async Task<TaskItemViewModel> PersistDelayedAsync(MethodInfo method, object?[]? arguments)
        {
            UpdateEntered.TrySetResult(true);
            await ReleaseUpdate.Task;
            return await (Task<TaskItemViewModel>)method.Invoke(Target, arguments)!;
        }
    }

    internal static TaskItemViewModel ReplaceTaskBeforeOpening(
        MainWindowViewModel owner, string id, ITaskStorage proxy, out TaskItemViewModel original)
    {
        original = owner.ResolveTaskById(id)!;
        var clone = JsonConvert.DeserializeObject<TaskItem>(JsonConvert.SerializeObject(original.Model))!;
        var replacement = new TaskItemViewModel(clone, proxy, original.IsInitializedProvider,
            new TaskItemViewModelContext
            {
                SourceId = original.SourceId,
                NotificationManager = original.NotificationManager,
                MainWindow = original.MainWindow
            });
        owner.taskRepository!.Tasks.AddOrUpdate(replacement);
        Dispatcher.UIThread.RunJobs();
        return replacement;
    }

    internal static void RestoreOriginalTask(
        MainWindowViewModelFixture fixture, TaskItemViewModel? original, TaskItemViewModel? replacement)
    {
        if (original is not null) fixture.MainWindowViewModelTest.taskRepository!.Tasks.AddOrUpdate(original);
        replacement?.Dispose();
        Dispatcher.UIThread.RunJobs();
    }

    private static string ReadPersistedTitle(string path) => JsonConvert.DeserializeObject<TaskItem>(File.ReadAllText(path))!.Title;
    private static async Task AssertPersistedTitleAsync(string path, string expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        string? actual = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                actual = JsonConvert.DeserializeObject<TaskItem>(await File.ReadAllTextAsync(path))!.Title;
                if (actual == expected) break;
            }
            catch (IOException) { /* An in-flight autosave can temporarily hold the file. */ }
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }
        await Assert.That(actual).IsEqualTo(expected);
    }
    private static T Find<T>(Control root, string id) where T : Control => root.GetVisualDescendants().OfType<T>()
        .Single(control => AutomationProperties.GetAutomationId(control) == id && control.IsEffectivelyVisible);
    private static void Pump(Window window)
    {
        for (var index = 0; index < 8; index++) Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
