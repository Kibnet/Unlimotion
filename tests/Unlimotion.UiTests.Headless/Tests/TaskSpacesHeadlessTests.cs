using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TUnit;
using AppAutomation.Abstractions;
using Avalonia.Threading;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;
using Unlimotion.ViewModel;
using LibGit2Sharp;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.VisualTree;
using Unlimotion;
using Unlimotion.UiTests.Headless.Infrastructure;

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
        using var presentation = new HeadlessWindowPresentation(Session.Inner.MainWindow);
        await Assert.That(Page.TaskSpaceSelector.AutomationId).IsEqualTo("TaskSpaceSelector");
        var vm = GetViewModel();
        await Assert.That(vm.Settings.TaskSpaces.Select(space => space.DisplayName))
            .IsEquivalentTo(["Space A", "Space B"]);
        await Assert.That(GetOnlyTaskTitle(vm)).IsEqualTo(
            UnlimotionAutomationScenarioData.TaskSpacesSpaceATitle);

        Page.SelectTabItem(static page => page.SettingsTabItem, timeoutMs: 10_000);
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
    public async Task Git_indicator_follows_real_task_space_switch_and_rejects_old_operation()
    {
        if (await GitStatusIndicatorHeadlessTests.RunRenderedChildIfNeeded("/*/*/TaskSpacesHeadlessTests/Git_indicator*")) return;
        using var presentation = new HeadlessWindowPresentation(Session.Inner.MainWindow);
        var vm = GetViewModel();
        var pathA = HeadlessRuntime.Dispatch(() => ((FileStorage)vm.taskRepository!.TaskTreeManager.Storage).Path);
        Repository.Init(pathA);
        File.WriteAllText(Path.Combine(pathA, "git-indicator-untracked"), "fixture");
        HeadlessRuntime.Dispatch(() => vm.Settings.GitBackupEnabled = true);
        try { await UiAssert.TextEqualsAsync(() => HeadlessRuntime.Dispatch(() => { Dispatcher.UIThread.RunJobs(); return vm.GitStatus.State.ToString(); }), "Dirty"); }
        catch { Console.WriteLine("Git fixture path: " + pathA + "\n" + HeadlessRuntime.Dispatch(() => vm.GitStatus.Details + "\nRequest: " + typeof(GitStatusViewModel).GetField("_request", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(vm.GitStatus) + "\nRead error: " + typeof(GitStatusViewModel).GetField("_readError", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(vm.GitStatus))); throw; }
        var sourceA = HeadlessRuntime.Dispatch(() => vm.GitStatus.LastKnown!.Request.SourceId);
        var evidence = Path.Combine(AppContext.BaseDirectory, "artifacts", "git-indicator", "source-switch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        Session.Inner.CaptureScreenshot(Path.Combine(evidence, "source-A-dirty.png"));
        var target = HeadlessRuntime.Dispatch(() => vm.Settings.TaskSpaces.Single(space => space.DisplayName == "Space B"));
        HeadlessRuntime.Dispatch(() => Session.Inner.MainWindow.GetVisualDescendants().OfType<ComboBox>()
            .Single(combo => AutomationProperties.GetAutomationId(combo) == "TaskSpaceSelector").SelectedItem = target);
        await UiAssert.TextEqualsAsync(() => HeadlessRuntime.Dispatch(() => vm.Settings.HeaderTaskSpace!.SourceId), target.SourceId);
        await UiAssert.TextEqualsAsync(() => GetOnlyTaskTitle(vm), UnlimotionAutomationScenarioData.TaskSpacesSpaceBTitle);
        // Candidate task binding precedes runtime publication. Wait for the complete
        // active settings projection, rather than the intermediate hidden Unknown A.
        await UiAssert.TextEqualsAsync(() => HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return (!vm.Settings.IsTaskSpaceSwitching && vm.Settings.GitProjection?.SourceId == target.SourceId).ToString();
        }), "True");
        await UiAssert.TextEqualsAsync(() => HeadlessRuntime.Dispatch(() => vm.GitStatus.State.ToString()), "Unknown");
        await Assert.That(HeadlessRuntime.Dispatch(() => vm.GitStatus.IsIndicatorVisible)).IsFalse();
        await Assert.That(HeadlessRuntime.Dispatch(() => vm.GitStatus.LastKnown)).IsNull();
        Console.WriteLine(HeadlessRuntime.Dispatch(() => "Before old operation: A=" + sourceA + "; B=" + target.SourceId +
            "; projection=" + vm.Settings.GitProjection + "; request=" + typeof(GitStatusViewModel)
                .GetField("_request", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(vm.GitStatus) +
            "; busy=" + vm.GitStatus.IsBusy + "; switching=" + vm.Settings.IsTaskSpaceSwitching));
        HeadlessRuntime.Dispatch(() => vm.GitStatus.ApplyDiagnostic(new(sourceA, pathA, Guid.NewGuid(), true)));
        await Assert.That(HeadlessRuntime.Dispatch(() => vm.GitStatus.IsBusy)).IsFalse();
        await Assert.That(GetOnlyTaskTitle(vm)).IsEqualTo(UnlimotionAutomationScenarioData.TaskSpacesSpaceBTitle);
        Session.Inner.CaptureScreenshot(Path.Combine(evidence, "source-B-disabled.png"));
        HeadlessRuntime.Dispatch(() => vm.Settings.GitBackupEnabled = true);
        await UiAssert.TextEqualsAsync(() => HeadlessRuntime.Dispatch(() => { Dispatcher.UIThread.RunJobs(); return vm.GitStatus.IsIndicatorVisible.ToString(); }), "True");
        await Assert.That(HeadlessRuntime.Dispatch(() => vm.GitStatus.State)).IsEqualTo(GitIndicatorState.Unknown);
        Session.Inner.CaptureScreenshot(Path.Combine(evidence, "source-B-enabled-unknown.png"));
        await File.WriteAllTextAsync(Path.Combine(evidence, "flow-trace.txt"), "Actual TaskSpaceSelector: measured Dirty enabled A=" + sourceA + " -> disabled B=" + target.SourceId + " -> enabled B Unknown (no repo); old A Started ignored; B task title preserved\n");
    }

    private MainWindowViewModel GetViewModel() =>
        HeadlessRuntime.Dispatch(() =>
            Session.Inner.MainWindow.DataContext as MainWindowViewModel
            ?? throw new InvalidOperationException("Task-spaces window did not expose MainWindowViewModel."));

    private static string GetOnlyTaskTitle(MainWindowViewModel vm) =>
        HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return vm.taskRepository?.Tasks.Items.Single().Title
                ?? throw new InvalidOperationException("The active task space did not contain exactly one task.");
        });

}
