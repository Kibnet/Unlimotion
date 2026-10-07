using System.Reflection;
using System.Runtime.ExceptionServices;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TUnit;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData;
using Newtonsoft.Json;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.Domain;
using Unlimotion.UiTests.Authoring.Pages;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Workspace;

namespace Unlimotion.UiTests.Headless.Tests;

public sealed class WorkspaceTaskScopeSaveFailureUiTests
    : UiTestBase<MainWindowHeadlessTests.HeadlessRuntimeSession, MainWindowPage>
{
    protected override MainWindowHeadlessTests.HeadlessRuntimeSession LaunchSession() => new(
        DesktopAppSession.Launch(UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(UnlimotionAutomationScenario.TaskSpaces)));

    protected override MainWindowPage CreatePage(MainWindowHeadlessTests.HeadlessRuntimeSession session)
    {
        HeadlessRuntime.Dispatch(() =>
        {
            session.Inner.MainWindow.Show();
            Dispatcher.UIThread.RunJobs();
        });
        return new(new HeadlessControlResolver(session.Inner.MainWindow));
    }

    [Test, NotInParallel(DesktopUiConstraint)]
    public async Task ActualSpaceSelector_TaskSaveFailureKeepsStorageDocumentsAndDraft_ThenRetrySucceeds()
    {
        var owner = HeadlessRuntime.Dispatch(() => (MainWindowViewModel)Session.Inner.MainWindow.DataContext!);
        TaskItemViewModel? replacement = null;
        TaskItemViewModel? originalTask = null;
        ITaskStorage? originalStorage = null;
        try
        {
            await RunUiAsync(async () =>
            {
                owner.Feed.IsExternalVaultSupported = true;
                owner.Feed.TaskOwner = owner;
                owner.Feed.TaskResolver = owner.ResolveTaskById;
                await owner.Feed.InitializeVaultAsync(owner.Settings.NoteVaultRootPath);
            });
            originalStorage = HeadlessRuntime.Dispatch(() => owner.taskRepository!);
            var taskFile = HeadlessRuntime.Dispatch(() => Path.Combine(owner.Settings.TaskStoragePath!,
                UnlimotionAutomationScenarioData.TaskSpacesTaskId));
            var persistedBefore = JsonConvert.DeserializeObject<TaskItem>(await File.ReadAllTextAsync(taskFile))!.Title;
            var root = HeadlessRuntime.Dispatch(() => owner.Feed.VaultRootPath)!;
            const string notePath = "scope-preserved-inactive.md";
            await File.WriteAllTextAsync(Path.Combine(root, notePath), "# Scope guard\n\nInactive note content\n");
            var proxyStorage = DispatchProxy.Create<ITaskStorage, FailingTaskStorageProxy>();
            var fault = (FailingTaskStorageProxy)(object)proxyStorage;
            fault.Target = originalStorage;
            replacement = HeadlessRuntime.Dispatch(() =>
            {
                var original = owner.ResolveTaskById(UnlimotionAutomationScenarioData.TaskSpacesTaskId)!;
                originalTask = original;
                var model = JsonConvert.DeserializeObject<TaskItem>(JsonConvert.SerializeObject(original.Model))!;
                var task = new TaskItemViewModel(model, proxyStorage, original.IsInitializedProvider,
                    new TaskItemViewModelContext
                    {
                        SourceId = original.SourceId, MainWindow = owner,
                        NotificationManager = original.NotificationManager
                    });
                originalStorage.Tasks.AddOrUpdate(task);
                Dispatcher.UIThread.RunJobs();
                return task;
            });
            await RunUiAsync(() => owner.OpenWorkspaceLocationAsync(WorkspaceLocation.FeedRoot));
            await RunUiAsync(() => owner.OpenWorkspaceTaskAsync(replacement, WorkspaceOpenDisposition.AdjacentPane));
            var cardTab = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ActiveTab);
            await RunUiAsync(() => owner.OpenWorkspaceLocationAsync(WorkspaceLocation.ForNote(notePath, "Inactive guard note"), WorkspaceOpenDisposition.NewTab));
            await RunUiAsync(() => owner.SelectWorkspaceTabAsync(owner.WorkspaceNavigation.SecondaryPane!, cardTab));
            WaitUntil(() => HeadlessRuntime.Dispatch(() =>
            {
                Dispatcher.UIThread.RunJobs();
                Session.Inner.MainWindow.UpdateLayout();
                return Session.Inner.MainWindow.GetVisualDescendants().OfType<TextBox>().Any(box =>
                    box.IsEffectivelyVisible && AutomationProperties.GetAutomationId(box) == "CurrentTaskTitleTextBox");
            }), visible => visible, timeout: TimeSpan.FromSeconds(15),
                timeoutMessage: "Task card title did not render after returning to its tab.");
            var title = HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants().OfType<TextBox>()
                .Single(box => box.IsEffectivelyVisible && AutomationProperties.GetAutomationId(box) == "CurrentTaskTitleTextBox"));
            var inactiveEditor = HeadlessRuntime.Dispatch(() => owner.Feed.DocumentWorkspace.Find(notePath)!.MarkdownEditor);
            const string draft = "Task draft survives failed actual space selection";
            HeadlessRuntime.Dispatch(() =>
            {
                fault.FailUpdates = true;
                title.Focus(); title.Text = draft; title.CaretIndex = 12;
                var block = inactiveEditor.Blocks.First(block => block.PreviewText.Contains("Inactive note content", StringComparison.Ordinal));
                inactiveEditor.BeginEdit(block); block.EditorText += " retained note draft";
            });
            var before = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.Panes.SelectMany(pane => pane.Tabs)
                .Select(tab => (tab.Id, tab.CurrentIndex, Keys: string.Join("|", tab.History.Select(entry => entry.Location.HistoryKey)))).ToArray());
            var revision = HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ScopeRevision);
            var selector = HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants().OfType<ComboBox>()
                .Single(control => AutomationProperties.GetAutomationId(control) == "TaskSpaceSelector"));
            var destinationId = HeadlessRuntime.Dispatch(() => owner.Settings.TaskSpaces.Single(space => space.DisplayName == "Space B").SourceId);
            HeadlessRuntime.Dispatch(() => selector.SelectedItem = owner.Settings.TaskSpaces.Single(space => space.SourceId == destinationId));
            WaitUntil(() => Volatile.Read(ref fault.FailedUpdateCount), count => count > 0,
                timeout: TimeSpan.FromSeconds(15), timeoutMessage: "Actual space selection never attempted the task editor save.");
            WaitUntil(() => HeadlessRuntime.Dispatch(() => !owner.Settings.IsTaskSpaceSwitching), ready => ready,
                timeout: TimeSpan.FromSeconds(30), timeoutMessage: "Rejected space selection did not settle.");
            await Assert.That(HeadlessRuntime.Dispatch(() => owner.taskRepository)).IsSameReferenceAs(originalStorage);
            await Assert.That(HeadlessRuntime.Dispatch(() => owner.Settings.TaskSpaces.Single(space => space.DisplayName == "Space A").IsActive)).IsTrue();
            await Assert.That(HeadlessRuntime.Dispatch(() => owner.Settings.IsTaskSpaceRecoveryRequired)).IsFalse();
            await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.ScopeRevision)).IsEqualTo(revision);
            await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.Panes.SelectMany(pane => pane.Tabs)
                .Select(tab => (tab.Id, tab.CurrentIndex, Keys: string.Join("|", tab.History.Select(entry => entry.Location.HistoryKey))))
                .SequenceEqual(before))).IsTrue();
            await Assert.That(HeadlessRuntime.Dispatch(() => title.IsEffectivelyVisible && title.Text == draft && title.CaretIndex == 12)).IsTrue();
            await Assert.That(HeadlessRuntime.Dispatch(() => replacement.HasPendingEditorPersistence)).IsTrue();
            await Assert.That(JsonConvert.DeserializeObject<TaskItem>(await File.ReadAllTextAsync(taskFile))!.Title)
                .IsEqualTo(persistedBefore);
            await Assert.That(HeadlessRuntime.Dispatch(() => owner.Feed.VaultRootPath)).IsEqualTo(root);
            await Assert.That(HeadlessRuntime.Dispatch(() => owner.Feed.DocumentWorkspace.Find(notePath))).IsNotNull();
            await Assert.That(HeadlessRuntime.Dispatch(() => inactiveEditor.Blocks.Any(block =>
                block.PreviewText.Contains("retained note draft", StringComparison.Ordinal) || block.EditorText.Contains("retained note draft", StringComparison.Ordinal)))).IsTrue();
            if (Environment.GetEnvironmentVariable("UNLIMOTION_RENDERED_HEADLESS_SCREENSHOTS") == "1")
            {
                var directory = Environment.GetEnvironmentVariable("UNLIMOTION_WORKSPACE_SCREENSHOT_DIR") ?? AppContext.BaseDirectory;
                Console.WriteLine("Headless screenshot: " + Session.Inner.CaptureScreenshot(Path.Combine(directory,
                    "scope-task-save", Guid.NewGuid().ToString("N"), "task-save-failure-keeps-space.png")));
            }
            HeadlessRuntime.Dispatch(() =>
            {
                fault.FailUpdates = false;
                selector.SelectedItem = owner.Settings.TaskSpaces.Single(space => space.SourceId == destinationId);
            });
            WaitUntil(() => HeadlessRuntime.Dispatch(() => !owner.Settings.IsTaskSpaceSwitching &&
                    owner.Settings.TaskSpaces.Single(space => space.SourceId == destinationId).IsActive && owner.Feed.IsVaultInitialized),
                ready => ready, timeout: TimeSpan.FromSeconds(40), timeoutMessage: "Retry did not switch after task persistence recovered.");
            await Assert.That(HeadlessRuntime.Dispatch(() => owner.WorkspaceNavigation.Panes.Sum(pane => pane.Tabs.Count))).IsEqualTo(1);
            await Assert.That(HeadlessRuntime.Dispatch(() => owner.ResolveTaskById(UnlimotionAutomationScenarioData.TaskSpacesTaskId)!.Title))
                .IsEqualTo(UnlimotionAutomationScenarioData.TaskSpacesSpaceBTitle);
            await Assert.That(await File.ReadAllTextAsync(Path.Combine(root, notePath))).Contains("retained note draft");
            await Assert.That(JsonConvert.DeserializeObject<TaskItem>(await File.ReadAllTextAsync(taskFile))!.Title)
                .IsEqualTo(draft);
        }
        finally
        {
            var restored = HeadlessRuntime.Dispatch(() =>
            {
                if (originalTask is null || originalStorage is null || !owner.IsInitialized ||
                    !ReferenceEquals(owner.taskRepository, originalStorage)) return false;
                originalStorage.Tasks.AddOrUpdate(originalTask);
                return true;
            });
            if (!restored) originalTask?.Dispose();
            replacement?.Dispose();
        }
    }

    private static Task RunUiAsync(Func<Task> action) => HeadlessRuntime.Session.Dispatch(async () =>
        { await action(); return true; }, CancellationToken.None).ContinueWith(task => task.GetAwaiter().GetResult(),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

    public class FailingTaskStorageProxy : DispatchProxy
    {
        public ITaskStorage Target { get; set; } = null!;
        public bool FailUpdates;
        public int FailedUpdateCount;
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method is null) throw new InvalidOperationException("Missing storage method.");
            if (method.Name == nameof(ITaskStorage.Update) && FailUpdates)
            {
                Interlocked.Increment(ref FailedUpdateCount);
                return Task.FromException<TaskItemViewModel>(new IOException("Injected actual scope task-save failure"));
            }
            try { return method.Invoke(Target, arguments); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }
    }
}
