using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TUnit;
using AppAutomation.Abstractions;
using Avalonia.Threading;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;
using Unlimotion.ViewModel;
using Unlimotion.UiTests.Headless.Infrastructure;
using Unlimotion.ViewModel.Feed;
using Unlimotion.ViewModel.Workspace;

namespace Unlimotion.UiTests.Headless.Tests;

public sealed class TaskSpacesHeadlessTests
    : UiTestBase<MainWindowHeadlessTests.HeadlessRuntimeSession, MainWindowPage>
{
    protected override MainWindowHeadlessTests.HeadlessRuntimeSession LaunchSession() =>
        new(
            DesktopAppSession.Launch(
                UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
                    UnlimotionAutomationScenario.TaskSpaces)));

    protected override MainWindowPage CreatePage(MainWindowHeadlessTests.HeadlessRuntimeSession session)
    {
        HeadlessRuntime.Dispatch(() =>
        {
            session.Inner.MainWindow.Show();
            Dispatcher.UIThread.RunJobs();
        });
        return new MainWindowPage(new HeadlessControlResolver(session.Inner.MainWindow));
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Spaces_render_selector_and_settings_management_controls()
    {
        using var presentation = new HeadlessWindowPresentation(Session.Inner.MainWindow);
        await Assert.That(Page.TaskSpaceSelector.AutomationId).IsEqualTo("TaskSpaceSelector");
        var vm = GetViewModel();
        await Assert.That(vm.Settings.TaskSpaces.Select(space => space.DisplayName))
            .IsEquivalentTo(["Space A", "Space B"]);
        await Assert.That(GetOnlyTaskTitle(vm)).IsEqualTo(
            UnlimotionAutomationScenarioData.TaskSpacesSpaceATitle);

        Page.ClickButton(static page => page.GlobalSettingsButton);
        await Assert.That(vm.IsSettingsOpen).IsTrue();
        HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            Session.Inner.MainWindow.UpdateLayout();
        });
        await Assert.That(Page.TaskSpacesSection.AutomationId).IsEqualTo("TaskSpacesSection");
        await Assert.That(Page.TaskSpacesList.AutomationId).IsEqualTo("TaskSpacesList");
        await Assert.That(Page.AddTaskSpaceButton.AutomationId).IsEqualTo("AddTaskSpaceButton");
        await Assert.That(Page.RenameTaskSpaceButton.AutomationId).IsEqualTo("RenameTaskSpaceButton");
        await Assert.That(Page.RemoveTaskSpaceButton.AutomationId).IsEqualTo("RemoveTaskSpaceButton");
        Page.WaitUntilIsEnabled(static page => page.RemoveTaskSpaceButton, true, 10_000);
        await Assert.That(Page.RemoveTaskSpaceButton.IsEnabled).IsTrue();
        await Assert.That(vm.Settings.SwitchTaskSpaceCommand).IsNotNull();
        await Assert.That(vm.Settings.AddTaskSpaceCommand).IsNotNull();
        await Assert.That(vm.Settings.RenameTaskSpaceCommand).IsNotNull();
        await Assert.That(vm.Settings.RemoveTaskSpaceCommand).IsNotNull();
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Space_switch_rebinds_tasks_and_note_vault_as_one_context()
    {
        var vm = GetViewModel();
        InitializeFeed(vm);
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return vm.Feed.IsVaultInitialized
                       && vm.Feed.Days.SelectMany(static day => day.MarkdownEditor.Blocks)
                           .Any(static block => block.PreviewText.Contains("Space A note", StringComparison.Ordinal));
            }),
            static ready => ready,
            timeout: TimeSpan.FromSeconds(40),
            timeoutMessage: "Space A note vault did not initialize.");
        var spaceARoot = HeadlessRuntime.Dispatch(() => vm.Feed.VaultRootPath);
        var spaceB = vm.Settings.TaskSpaces.Single(space => space.DisplayName == "Space B");

        HeadlessRuntime.Dispatch(() => vm.Settings.HeaderTaskSpace = spaceB);

        WaitUntil(
            () => HeadlessRuntime.Dispatch(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return !vm.Settings.IsTaskSpaceSwitching
                       && vm.Settings.TaskSpaces.Single(space => space.DisplayName == "Space B").IsActive
                       && vm.Feed.IsVaultInitialized
                       && vm.Feed.Days.SelectMany(static day => day.MarkdownEditor.Blocks)
                           .Any(static block => block.PreviewText.Contains("Space B note", StringComparison.Ordinal));
            }),
            static ready => ready,
            timeout: TimeSpan.FromSeconds(40),
            timeoutMessage: "Space B tasks and notes did not become active together.");

        using (Assert.Multiple())
        {
            await Assert.That(GetOnlyTaskTitle(vm)).IsEqualTo(
                UnlimotionAutomationScenarioData.TaskSpacesSpaceBTitle);
            await Assert.That(vm.Feed.VaultRootPath).IsNotEqualTo(spaceARoot);
            await Assert.That(vm.Feed.IsBoundToVaultRoot(vm.Settings.NoteVaultRootPath)).IsTrue();
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Space_switch_stays_on_current_context_when_dirty_feed_editor_cannot_commit()
    {
        var vm = GetViewModel();
        InitializeFeed(vm);
        WaitUntil(
            () => HeadlessRuntime.Dispatch(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return vm.Feed.IsVaultInitialized && vm.Feed.Days.Count > 0;
            }),
            static ready => ready,
            timeout: TimeSpan.FromSeconds(40),
            timeoutMessage: "Space A note vault did not initialize.");
        var originalRoot = HeadlessRuntime.Dispatch(() => vm.Feed.VaultRootPath);
        var editor = HeadlessRuntime.Dispatch(() => vm.Feed.Days[0].MarkdownEditor);
        HeadlessRuntime.Dispatch(() =>
        {
            var block = editor.Blocks.First(candidate => candidate.PreviewText.Contains("Space A note", StringComparison.Ordinal));
            editor.BeginEdit(block);
            block.EditorText += " unsaved";
            editor.CommitBlockAsync = (_, _) => Task.FromResult(
                MarkdownBlockCommitResult.Rejected("Synthetic commit failure"));
            vm.Settings.HeaderTaskSpace = vm.Settings.TaskSpaces.Single(space => space.DisplayName == "Space B");
        });

        WaitUntil(
            () => HeadlessRuntime.Dispatch(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return !vm.Settings.IsTaskSpaceSwitching;
            }),
            static ready => ready,
            timeout: TimeSpan.FromSeconds(20),
            timeoutMessage: "Rejected task-space switch did not settle.");

        using (Assert.Multiple())
        {
            await Assert.That(vm.Settings.TaskSpaces.Single(space => space.DisplayName == "Space A").IsActive).IsTrue();
            await Assert.That(GetOnlyTaskTitle(vm)).IsEqualTo(
                UnlimotionAutomationScenarioData.TaskSpacesSpaceATitle);
            await Assert.That(vm.Feed.VaultRootPath).IsEqualTo(originalRoot);
            await Assert.That(editor.ActiveBlock).IsNotNull();
            await Assert.That(editor.ActiveBlock!.EditorText).Contains("unsaved");
            await Assert.That(editor.ActiveBlock.ErrorMessage).Contains("Synthetic commit failure");
        }
    }

    private MainWindowViewModel GetViewModel() =>
        HeadlessRuntime.Dispatch(() =>
            Session.Inner.MainWindow.DataContext as MainWindowViewModel
            ?? throw new InvalidOperationException("Task-spaces window did not expose MainWindowViewModel."));

    [Test, NotInParallel(DesktopUiConstraint)]
    public async Task ScopeChange_CommitFailureKeepsActiveAdjacentAndInactiveEditors()
    {
        var vm = GetViewModel();
        InitializeFeed(vm);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => vm.Feed.IsVaultInitialized),
            ready => ready, timeout: TimeSpan.FromSeconds(40), timeoutMessage: "Space A vault did not initialize.");
        var originalRoot = HeadlessRuntime.Dispatch(() => vm.Feed.VaultRootPath)!;
        var names = new[] { "guard-source.md", "guard-inactive.md", "guard-adjacent.md" };
        foreach (var name in names) await File.WriteAllTextAsync(Path.Combine(originalRoot, name), $"# {name}\n\nOriginal body\n");
        await RunSpaceUiAsync(() => vm.OpenWorkspaceLocationAsync(WorkspaceLocation.ForNote(names[0], "Source")));
        await RunSpaceUiAsync(() => vm.OpenWorkspaceLocationAsync(WorkspaceLocation.ForNote(names[1], "Inactive"), WorkspaceOpenDisposition.AdjacentPane));
        await RunSpaceUiAsync(() => vm.OpenWorkspaceLocationAsync(WorkspaceLocation.ForNote(names[2], "Adjacent"), WorkspaceOpenDisposition.NewTab));
        var editors = HeadlessRuntime.Dispatch(() => names.Select(name => vm.Feed.DocumentWorkspace.Find(name)!.MarkdownEditor).ToArray());
        var retryCommit = editors[1].CommitBlockAsync;
        var before = HeadlessRuntime.Dispatch(() => vm.WorkspaceNavigation.Panes.SelectMany(pane => pane.Tabs)
            .Select(tab => (tab.Id, tab.CurrentIndex, Entries: tab.History.Count)).ToArray());
        HeadlessRuntime.Dispatch(() =>
        {
            foreach (var editor in editors)
            {
                var block = editor.Blocks.First(block => block.IsEditable && block.PreviewText.Contains("Original body", StringComparison.Ordinal));
                editor.BeginEdit(block);
                block.EditorText += " unsaved guarded text";
            }
            editors[1].CommitBlockAsync = (_, _) => Task.FromResult(MarkdownBlockCommitResult.Rejected("Inactive editor refuses save"));
        });
        var selector = HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants().OfType<ComboBox>()
            .Single(combo => AutomationProperties.GetAutomationId(combo) == "TaskSpaceSelector"));
        var spaceB = HeadlessRuntime.Dispatch(() => vm.Settings.TaskSpaces.Single(space => space.DisplayName == "Space B"));
        HeadlessRuntime.Dispatch(() => selector.SelectedItem = spaceB);
        WaitUntil(() => HeadlessRuntime.Dispatch(() => editors[1].ActiveBlock?.ErrorMessage),
            error => error?.Contains("Inactive editor refuses save", StringComparison.Ordinal) == true,
            timeout: TimeSpan.FromSeconds(20), timeoutMessage: "Scope change did not attempt the inactive editor guard.");
        WaitUntil(() => HeadlessRuntime.Dispatch(() => !vm.Settings.IsTaskSpaceSwitching),
            ready => ready, timeout: TimeSpan.FromSeconds(20), timeoutMessage: "Rejected scope change did not settle.");
        await Assert.That(HeadlessRuntime.Dispatch(() => vm.Settings.TaskSpaces.Single(space => space.DisplayName == "Space A").IsActive)).IsTrue();
        await Assert.That(HeadlessRuntime.Dispatch(() => vm.Feed.VaultRootPath)).IsEqualTo(originalRoot);
        await Assert.That(HeadlessRuntime.Dispatch(() => vm.WorkspaceNavigation.Panes.SelectMany(pane => pane.Tabs)
            .Select(tab => (tab.Id, tab.CurrentIndex, Entries: tab.History.Count)).SequenceEqual(before))).IsTrue();
        await Assert.That(HeadlessRuntime.Dispatch(() => editors[1].ActiveBlock!.EditorText)).Contains("unsaved guarded text");
        await Assert.That(HeadlessRuntime.Dispatch(() => editors[1].ActiveBlock!.ErrorMessage)).Contains("Inactive editor refuses save");
        foreach (var name in names)
            await Assert.That(HeadlessRuntime.Dispatch(() => vm.Feed.DocumentWorkspace.Find(name)!.MarkdownEditor.Blocks
                .Any(block => block.PreviewText.Contains("unsaved guarded text", StringComparison.Ordinal) ||
                    block.EditorText.Contains("unsaved guarded text", StringComparison.Ordinal)))).IsTrue();
        if (Environment.GetEnvironmentVariable("UNLIMOTION_RENDERED_HEADLESS_SCREENSHOTS") == "1")
        {
            var directory = Environment.GetEnvironmentVariable("UNLIMOTION_WORKSPACE_SCREENSHOT_DIR") ?? AppContext.BaseDirectory;
            var image = Session.Inner.CaptureScreenshot(Path.Combine(directory, "scope-guard", Guid.NewGuid().ToString("N"), "scope-commit-error.png"));
            Console.WriteLine($"Headless screenshot: {image}");
        }
        HeadlessRuntime.Dispatch(() => editors[1].CommitBlockAsync = retryCommit);
        var retrySpaceB = HeadlessRuntime.Dispatch(() => vm.Settings.TaskSpaces.Single(space => space.SourceId == spaceB.SourceId));
        HeadlessRuntime.Dispatch(() => selector.SelectedItem = retrySpaceB);
        await Assert.That(HeadlessRuntime.Dispatch(() => ReferenceEquals(selector.SelectedItem, retrySpaceB))).IsTrue()
            .Because("ReloadTaskSpaces recreates option instances after a rejected switch; the retry must select the live UI item.");
        WaitUntil(() => HeadlessRuntime.Dispatch(() => !vm.Settings.IsTaskSpaceSwitching &&
                vm.Settings.TaskSpaces.Single(space => space.DisplayName == "Space B").IsActive && vm.Feed.IsVaultInitialized),
            ready => ready, timeout: TimeSpan.FromSeconds(40), timeoutMessage: "Retry did not enter Space B after committing every editor.");
        await Assert.That(GetOnlyTaskTitle(vm)).IsEqualTo(UnlimotionAutomationScenarioData.TaskSpacesSpaceBTitle);
        await Assert.That(HeadlessRuntime.Dispatch(() => vm.WorkspaceNavigation.Panes.Sum(pane => pane.Tabs.Count))).IsEqualTo(1);
        await Assert.That(HeadlessRuntime.Dispatch(() => vm.Feed.VaultRootPath)).IsNotEqualTo(originalRoot);
        await Assert.That(HeadlessRuntime.Dispatch(() => vm.Feed.DocumentWorkspace.Documents.Count)).IsEqualTo(0);
        foreach (var name in names) await Assert.That(await File.ReadAllTextAsync(Path.Combine(originalRoot, name))).Contains("unsaved guarded text");
    }

    private static Task RunSpaceUiAsync(Func<Task<bool>> action) =>
        HeadlessRuntime.Session.Dispatch(action, CancellationToken.None).ContinueWith(task => task.GetAwaiter().GetResult(),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

    private static void InitializeFeed(MainWindowViewModel vm)
    {
        HeadlessRuntime.Dispatch(() =>
        {
            vm.Feed.IsExternalVaultSupported = true;
            vm.Feed.TaskOwner = vm;
            vm.Feed.TaskResolver = taskId => vm.taskRepository?.Tasks.Items.FirstOrDefault(
                task => string.Equals(task.Id, taskId, StringComparison.Ordinal));
            _ = vm.Feed.InitializeVaultAsync(vm.Settings.NoteVaultRootPath);
        });
    }

    private static string GetOnlyTaskTitle(MainWindowViewModel vm) =>
        HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return vm.taskRepository?.Tasks.Items.Single().Title
                ?? throw new InvalidOperationException("The active task space did not contain exactly one task.");
        });

}
