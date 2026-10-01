using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TUnit;
using Avalonia.Threading;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.UiTests.Headless.Tests;

public sealed class TaskSpacesHeadlessTests
    : UiTestBase<MainWindowHeadlessTests.HeadlessRuntimeSession, MainWindowPage>
{
    protected override MainWindowHeadlessTests.HeadlessRuntimeSession LaunchSession() =>
        new(
            DesktopAppSession.Launch(
                UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
                    UnlimotionAutomationScenario.TaskSpaces)));

    protected override MainWindowPage CreatePage(MainWindowHeadlessTests.HeadlessRuntimeSession session) =>
        new(new HeadlessControlResolver(session.Inner.MainWindow));

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Spaces_render_selector_and_settings_management_controls()
    {
        await Assert.That(Page.TaskSpaceSelector.AutomationId).IsEqualTo("TaskSpaceSelector");
        var vm = GetViewModel();
        await Assert.That(vm.Settings.TaskSpaces.Select(space => space.DisplayName))
            .IsEquivalentTo(["Space A", "Space B"]);
        await Assert.That(GetOnlyTaskTitle(vm)).IsEqualTo(
            UnlimotionAutomationScenarioData.TaskSpacesSpaceATitle);

        HeadlessRuntime.Dispatch(() =>
        {
            vm.SettingsMode = true;
            Dispatcher.UIThread.RunJobs();
        });
        await Assert.That(Page.TaskSpacesSection.AutomationId).IsEqualTo("TaskSpacesSection");
        await Assert.That(Page.TaskSpacesList.AutomationId).IsEqualTo("TaskSpacesList");
        await Assert.That(Page.AddTaskSpaceButton.AutomationId).IsEqualTo("AddTaskSpaceButton");
        await Assert.That(Page.RenameTaskSpaceButton.AutomationId).IsEqualTo("RenameTaskSpaceButton");
        await Assert.That(Page.RemoveTaskSpaceButton.AutomationId).IsEqualTo("RemoveTaskSpaceButton");
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
