using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Localization;
using Unlimotion.Views;
using static Unlimotion.Test.TaskTabEmojiFilterIsolationUiTests;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class TaskTabEmojiFilterCopyUiTests
{
    [Test]
    [Arguments(0)] [Arguments(1)] [Arguments(2)] [Arguments(3)] [Arguments(4)]
    [Arguments(5)] [Arguments(6)] [Arguments(7)] [Arguments(8)]
    public Task EveryTab_CopiesBothSets_ChangesResults_AndUndoesLocally(int index) => Run(index, async (vm, view, window) =>
    {
        var source = index == 0 ? 3 : 0;
        SelectTab(view, window, source);
        await ToggleAsync(window, FilterControl(view, false), "🚀");
        await ToggleAsync(window, FilterControl(view, true), "🧰");
        SelectTab(view, window, index);
        await ToggleAsync(window, FilterControl(view, false), "🧰");
        await ToggleAsync(window, FilterControl(view, true), "🚀");
        await Results(vm, index, rocket: false, toolbox: true);
        var originalSearch = vm.Search.SearchText;
        var panel = OpenPanel(view, window);
        await PickSource(panel, window, vm.EmojiFilterCopyPanels[source], pointer: index % 2 == 0);
        await Results(vm, index, rocket: true, toolbox: false);
        await Assert.That(vm.EmojiFilterCopyPanels[index].CanUndo).IsTrue();
        await Assert.That(vm.Search.SearchText).IsEqualTo(originalSearch);
        await Assert.That(Selections(view, false)).IsEquivalentTo(new[] { "🚀" });
        await Assert.That(Selections(view, true)).IsEquivalentTo(new[] { "🧰" });
        panel.Flyout.Hide();
        SelectTab(view, window, source);
        await ToggleAsync(window, FilterControl(view, false), "🚀");
        SelectTab(view, window, index);
        await Results(vm, index, rocket: true, toolbox: false);
        panel = OpenPanel(view, window);
        Undo(panel, window);
        await Results(vm, index, rocket: false, toolbox: true);
        await Assert.That(vm.EmojiFilterCopyPanels[index].CanUndo).IsFalse();
        await Assert.That(Selections(view, false)).IsEquivalentTo(new[] { "🧰" });
        await Assert.That(Selections(view, true)).IsEquivalentTo(new[] { "🚀" });
        panel.Flyout.Hide();
    });

    [Test]
    public Task EmptySource_IdenticalCopy_AndRepeatedCopy_HavePredictableUndo() => Run(0, async (vm, view, window) =>
    {
        await ToggleAsync(window, FilterControl(view, false), "🚀");
        await ToggleAsync(window, FilterControl(view, true), "🧰");
        var panel = OpenPanel(view, window);
        await Assert.That(vm.EmojiFilterCopyPanels[1].Preview).IsEqualTo(Localization.Get("EmojiFiltersUnrestricted"));
        await PickSource(panel, window, vm.EmojiFilterCopyPanels[1]);
        await Results(vm, 0, true, true);
        await Assert.That(Selections(view, false)).IsEmpty();
        await Assert.That(Selections(view, true)).IsEmpty();
        await PickSource(panel, window, vm.EmojiFilterCopyPanels[2]); // Identical empty source does not replace first undo.
        Undo(panel, window);
        await Results(vm, 0, true, false);
        await Assert.That(Selections(view, false)).IsEquivalentTo(new[] { "🚀" });
        panel.Flyout.Hide();
        SelectTab(view, window, 1);
        await ToggleAsync(window, FilterControl(view, false), "🧰");
        SelectTab(view, window, 0);
        panel = OpenPanel(view, window);
        await PickSource(panel, window, vm.EmojiFilterCopyPanels[1]);
        await PickSource(panel, window, vm.EmojiFilterCopyPanels[2]);
        Undo(panel, window); // Undo latest copy only, not all earlier transfers.
        await Results(vm, 0, false, true);
        panel.Flyout.Hide();
    });

    [Test]
    public Task All_MasterFlagsAndPartialExclusion_AreCopiedWithoutCrossTalk() => Run(0, async (vm, view, window) =>
    {
        SelectTab(view, window, 1);
        await ToggleAsync(window, FilterControl(view, false), "");
        await ToggleAsync(window, FilterControl(view, true), "🧰");
        SelectTab(view, window, 0);
        var panel = OpenPanel(view, window);
        await PickSource(panel, window, vm.EmojiFilterCopyPanels[1]);
        await Results(vm, 0, true, false);
        await Assert.That(vm.EmojiFilters.All(filter => filter.ShowTasks)).IsTrue();
        await Assert.That(Selections(view, true)).IsEquivalentTo(new[] { "🧰" });
        Undo(panel, window);
        await Results(vm, 0, true, true);
        await Assert.That(vm.EmojiFilters.All(filter => !filter.ShowTasks)).IsTrue();
        await Assert.That(vm.LastCreatedEmoji.IncludeFilters.All(filter => filter.ShowTasks)).IsTrue();
        panel.Flyout.Hide();
    });

    [Test]
    public Task ManualChange_ResetAndReconnect_InvalidateUndo() => Run(0, async (vm, view, window) =>
    {
        SelectTab(view, window, 1);
        await ToggleAsync(window, FilterControl(view, false), "🚀");
        SelectTab(view, window, 0);
        var panel = OpenPanel(view, window);
        await PickSource(panel, window, vm.EmojiFilterCopyPanels[1]);
        panel.Flyout.Hide();
        await ToggleAsync(window, FilterControl(view, true), "🧰");
        await Assert.That(vm.EmojiFilterCopyPanels[0].CanUndo).IsFalse();
        panel = OpenPanel(view, window);
        await PickSource(panel, window, vm.EmojiFilterCopyPanels[2]);
        await Assert.That(vm.EmojiFilterCopyPanels[0].CanUndo).IsTrue();
        vm.ResetCurrentTabFilters();
        await Assert.That(vm.EmojiFilterCopyPanels[0].CanUndo).IsFalse();
        await PickSource(panel, window, vm.EmojiFilterCopyPanels[1]);
        await Assert.That(panel.Copy.IsEffectivelyVisible).IsTrue();
        var detached = vm.EmojiFilters.Single(filter => filter.Emoji == "🚀");
        vm.ClearTaskSpaceSurface();
        Render(window);
        await Assert.That(vm.EmojiFilterCopyPanels.All(item => !item.CanUndo && item.Feedback == "")).IsTrue();
        await Assert.That(panel.Copy.FindControl<DropDownButton>("CopySourceButton")!.IsEnabled).IsFalse();
        vm.EmojiFilterCopyPanels[1].CopyFrom(vm.EmojiFilterCopyPanels[0]);
        await Assert.That(vm.EmojiFilterCopyPanels.All(item => !item.CanUndo && !item.CanCopy)).IsTrue();
        await vm.Connect();
        detached.ShowTasks = false;
        await Assert.That(vm.EmojiFilterCopyPanels.All(item => !item.CanUndo)).IsTrue();
        await Assert.That(vm.EmojiFilterCopyPanels.All(item => item.CanCopy)).IsTrue();
    });

    [Test]
    public Task CatalogRemovalInvalidatesUndo_ButRenameAndUnselectedAdditionDoNot() => Run(0, async (vm, view, window) =>
    {
        await ToggleAsync(window, FilterControl(view, false), "🚀");
        var panel = OpenPanel(view, window);
        await PickSource(panel, window, vm.EmojiFilterCopyPanels[1]);
        panel.Flyout.Hide();
        var sourceTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
        sourceTask.Title = "🚀 Renamed";
        await Assert.That(vm.EmojiFilterCopyPanels[0].CanUndo).IsTrue();
        var extraTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask1Id);
        extraTask.Title = "🪼 New catalog emoji";
        await Assert.That(vm.EmojiFilterCopyPanels[0].CanUndo).IsTrue();
        sourceTask.Title = "No longer an emoji";
        await Assert.That(await TestHelpers.WaitUntilAsync(() => !vm.EmojiFilterCopyPanels[0].CanUndo, TimeSpan.FromSeconds(5))).IsTrue();
    });

    [Test]
    public Task CopyUsesLatestSourceSelection_AndPreservesSearchDatesAndStatuses() => Run(1, async (vm, view, window) =>
    {
        vm.Search.SearchText = "target";
        vm.LastCreatedDateFilter.From = DateTime.Today.AddDays(-1);
        var statuses = vm.LastCreatedStatusFilters.Select(filter => filter.ShowTasks).ToArray();
        var to = vm.LastCreatedDateFilter.To;
        var custom = vm.LastCreatedDateFilter.IsCustom;
        await Results(vm, 1, true, true);
        var panel = OpenPanel(view, window);
        var picker = OpenPicker(panel, window);
        vm.EmojiFilters.Single(filter => filter.Emoji == "🚀").ShowTasks = true;
        vm.EmojiExcludeFilters.Single(filter => filter.Emoji == "🧰").ShowTasks = true;
        Render(window);
        var button = SourceButton(picker, vm.EmojiFilterCopyPanels[0]);
        await Assert.That(string.Join(" ", button.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text))).Contains("🚀");
        await Click(window, button);
        await Results(vm, 1, true, false);
        await Assert.That(vm.Search.SearchText).IsEqualTo("target");
        await Assert.That(vm.LastCreatedDateFilter.From).IsEqualTo(DateTime.Today.AddDays(-1));
        await Assert.That(vm.LastCreatedDateFilter.To).IsEqualTo(to);
        await Assert.That(vm.LastCreatedDateFilter.IsCustom).IsEqualTo(custom);
        await Assert.That(vm.LastCreatedStatusFilters.Select(filter => filter.ShowTasks)).IsEquivalentTo(statuses);
        Undo(panel, window);
        await Results(vm, 1, true, true);
        panel.Flyout.Hide();
    });

    [Test]
    [Arguments("ru", 390)] [Arguments("en", 900)]
    public Task PickerAndFeedback_AreRenderedAndLocalized(string language, int width) => Run(0, async (vm, view, window) =>
    {
        LocalizationService.Current.SetLanguage(language);
        SelectTab(view, window, 1);
        await ToggleAsync(window, FilterControl(view, false), "🚀");
        SelectTab(view, window, 0);
        window.Width = width;
        Render(window);
        var panel = OpenPanel(view, window);
        var picker = OpenPicker(panel, window);
        Capture(window, language + "-" + width + "-sources");
        await Assert.That(panel.Copy.FindControl<DropDownButton>("CopySourceButton")!.Content!.ToString())
            .IsEqualTo(Localization.Get("CopyEmojiFiltersFromTab"));
        await Assert.That(picker.GetVisualDescendants().OfType<Button>().Count(button => button.Classes.Contains("EmojiFilterCopySource"))).IsEqualTo(8);
        var button = SourceButton(picker, vm.EmojiFilterCopyPanels[1]);
        await Click(window, button);
        Render(window);
        await Results(vm, 0, true, false);
        Capture(window, language + "-" + width + "-feedback");
        Undo(panel, window);
        Capture(window, language + "-" + width + "-undone");
        panel.Flyout.Hide();
    });

    private static async Task Run(int index, Func<MainWindowViewModel, MainControl, Window, Task> action)
    {
        if (await RenderedTestProcess.RunIfNeededAsync(TimeSpan.FromMinutes(3))) return;
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(EmojiTitleTestAppBuilder));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = MainControlFilterToolbarResponsiveUiTests.CreateControlledEmojiFixture();
            var vm = fixture.MainWindowViewModelTest;
            await vm.Connect();
            await Task.WhenAll(vm.taskRepository!.Tasks.Items.Select(task => task.SealPendingSaves()));
            await MainControlFilterToolbarResponsiveUiTests.PrepareEmojiFilterData(vm);
            foreach (var task in new[] { TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id), TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask3Id) })
            {
                task.Status = index switch { 4 => DomainTaskStatus.InProgress, 5 => DomainTaskStatus.Completed, 6 => DomainTaskStatus.Archived, _ => DomainTaskStatus.Prepared };
                task.IsCanBeCompleted = true;
                task.PlannedBeginDateTime = null;
                // Match task creation's UTC storage convention; a local-offset value
                // would be shifted twice by the existing date-only projection late at night.
                task.CreatedDateTime = DateTimeOffset.UtcNow;
                task.StartedDateTime = task.UpdatedDateTime = task.CompletedDateTime = task.ArchiveDateTime = DateTimeOffset.UtcNow;
                vm.DetailsAreOpen = true;
                vm.CurrentTaskItem = task;
            }
            vm.DetailsAreOpen = false;
            vm.LastCreatedDateFilter.From = vm.LastUpdatedDateFilter.From = vm.CompletedDateFilter.From = vm.ArchivedDateFilter.From = null;
            var view = new MainControl { DataContext = vm };
            var window = new Window { Content = view, Width = 900, Height = 760 };
            try { window.Show(); SelectTab(view, window, index); await action(vm, view, window); }
            finally { window.Close(); LocalizationService.Current.SetLanguage("en"); }
        }, CancellationToken.None);
    }

    private static string[] Selections(MainControl view, bool excluding) => ((System.Collections.IEnumerable)FilterControl(view, excluding).Filters!)
        .OfType<EmojiFilter>().Where(filter => filter.ShowTasks).Select(filter => filter.Emoji).ToArray();

    private static async Task Results(MainWindowViewModel vm, int index, bool rocket, bool toolbox)
    {
        await Assert.That(await TestHelpers.WaitUntilAsync(() =>
        {
            var tasks = index switch
            {
                0 => vm.CurrentAllTasksItems, 1 => vm.LastCreatedItems, 2 => vm.LastUpdatedItems, 3 => vm.UnlockedItems,
                4 => vm.InProgressItems, 5 => vm.CompletedItems, 6 => vm.ArchivedItems, 7 => vm.LastOpenedItems, _ => vm.Graph.Tasks
            };
            return tasks.Any(task => task.Id == MainWindowViewModelFixture.RootTask2Id) == rocket
                && tasks.Any(task => task.Id == MainWindowViewModelFixture.RootTask3Id) == toolbox;
        }, TimeSpan.FromSeconds(5))).IsTrue();
    }

    private sealed record Panel(Flyout Flyout, EmojiFilterCopyControl Copy);
    private static Panel OpenPanel(MainControl view, Window window)
    {
        var button = view.GetVisualDescendants().OfType<DropDownButton>().Single(button => button.IsEffectivelyVisible && button.Flyout is Flyout flyout
            && flyout.Content is Control content && content.GetLogicalDescendants().OfType<EmojiFilterCopyControl>().Any());
        Press(window, button);
        var flyout = (Flyout)button.Flyout!;
        return new(flyout, ((Control)flyout.Content!).GetLogicalDescendants().OfType<EmojiFilterCopyControl>().Single());
    }

    private static Control OpenPicker(Panel panel, Window window)
    {
        var button = panel.Copy.FindControl<DropDownButton>("CopySourceButton")!;
        Press(window, button);
        return (Control)((Flyout)button.Flyout!).Content!;
    }

    private static Button SourceButton(Control picker, EmojiFilterCopyPanel source) => picker.GetVisualDescendants().OfType<Button>()
        .Single(button => button.Classes.Contains("EmojiFilterCopySource") && ReferenceEquals(button.DataContext, source));

    private static async Task PickSource(Panel panel, Window window, EmojiFilterCopyPanel source, bool pointer = false)
    {
        var picker = OpenPicker(panel, window);
        var button = SourceButton(picker, source);
        if (pointer) await Click(window, button); else Press(window, button);
    }

    private static void Undo(Panel panel, Window window) => Press(window, panel.Copy.GetVisualDescendants().OfType<Button>()
        .Single(button => Avalonia.Automation.AutomationProperties.GetAutomationId(button) == "EmojiFilterCopyUndoButton"));

    private static void Press(Window window, Control control)
    {
        control.Focus();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Render(window);
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("UNLIMOTION_TEST_TRACE_DIRECTORY");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        Render(window);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered copy panel frame.");
        frame.Save(Path.Combine(directory, "emoji-copy-" + name + ".png"));
    }
}
