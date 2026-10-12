using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData;
using Unlimotion.ViewModel;
using Unlimotion.Views;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class TaskTabEmojiFilterIsolationUiTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    public async Task EveryTab_FiltersItsActualResults_AndOwnsAllAndExclude(int index)
    {
        if (await TaskTabEmojiFilterRenderedProcess.RunIfNeededAsync()) return;
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(EmojiTitleTestAppBuilder));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = MainControlFilterToolbarResponsiveUiTests.CreateControlledEmojiFixture();
            var vm = fixture.MainWindowViewModelTest;
            await vm.Connect();
            await Task.WhenAll(vm.taskRepository!.Tasks.Items.Select(task => task.SealPendingSaves()));
            await MainControlFilterToolbarResponsiveUiTests.PrepareEmojiFilterData(vm);
            var first = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
            var second = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask3Id);
            foreach (var task in new[] { first, second })
            {
                task.Status = index switch
                {
                    4 => DomainTaskStatus.InProgress,
                    5 => DomainTaskStatus.Completed,
                    6 => DomainTaskStatus.Archived,
                    _ => DomainTaskStatus.Prepared
                };
                task.IsCanBeCompleted = true;
                task.PlannedBeginDateTime = null;
                task.StartedDateTime = task.CreatedDateTime = DateTimeOffset.UtcNow;
                task.UpdatedDateTime = task.CompletedDateTime = task.ArchiveDateTime = DateTimeOffset.UtcNow;
            }
            vm.ShowCompleted = vm.ShowArchived = true;
            vm.ShowWanted = null;
            vm.LastCreatedDateFilter.From = vm.LastUpdatedDateFilter.From = null;
            vm.CompletedDateFilter.From = vm.ArchivedDateFilter.From = null;
            vm.DetailsAreOpen = true;
            vm.CurrentTaskItem = first;
            vm.CurrentTaskItem = second;
            vm.DetailsAreOpen = false;
            var view = new MainControl { DataContext = vm };
            var window = new Window { Content = view, Width = 900, Height = 760 };
            try
            {
                window.Show();
                // A different tab has an opposite selection before this tab is initialized lazily.
                var other = index == 0 ? vm.UnlockedEmoji.IncludeFilters : vm.EmojiFilters;
                other.Single(filter => filter.Emoji == "🧰").ShowTasks = true;
                SelectTab(view, window, index);
                await WaitForResults(() => Has(index, vm, first.Id) && Has(index, vm, second.Id));
                foreach (var exclude in new[] { false, true })
                {
                    var control = FilterControl(view, exclude);
                    await Assert.That(FilterItems(control).All(filter => !filter.ShowTasks)).IsTrue();
                    await ToggleAsync(window, control, "🚀");
                    await WaitForResults(() => Has(index, vm, first.Id) != exclude && Has(index, vm, second.Id) == exclude);
                    await Assert.That(other.Single(filter => filter.Emoji == "🧰").ShowTasks).IsTrue();
                    var otherIndex = index == 0 ? 1 : 0;
                    SelectTab(view, window, otherIndex);
                    SelectTab(view, window, index);
                    control = FilterControl(view, exclude);
                    await Assert.That(FilterItems(control).Single(filter => filter.Emoji == "🚀").ShowTasks).IsTrue();
                    await ToggleAsync(window, control, "🚀");
                    await WaitForResults(() => Has(index, vm, first.Id) && Has(index, vm, second.Id));
                    await ToggleAsync(window, control, "");
                    await Assert.That(FilterItems(control).All(filter => filter.ShowTasks)).IsTrue();
                    await Assert.That(other.Single(filter => filter.Emoji == "🧰").ShowTasks).IsTrue();
                    await ToggleAsync(window, control, "");
                    await Assert.That(FilterItems(control).All(filter => !filter.ShowTasks)).IsTrue();
                }
                if (index == 8)
                {
                    vm.Graph.OnlyUnlocked = true;
                    await ToggleAsync(window, FilterControl(view, false), "🚀");
                    await WaitForResults(() => vm.Graph.UnlockedTasks.Any(task => task.Id == first.Id)
                        && vm.Graph.UnlockedTasks.All(task => task.Id != second.Id));
                    await ToggleAsync(window, FilterControl(view, false), "🚀");
                }
                await ToggleAsync(window, FilterControl(view, false), "🚀");
                await ToggleAsync(window, FilterControl(view, true), "🧰");
                var notifications = (NotificationManagerWrapperMock)vm.ManagerWrapper;
                notifications.AskResult = true;
                var prefix = new[] { "AllTasks", "LastCreated", "LastUpdated", "Unlocked", "InProgress", "Completed", "Archived", "LastOpened", "Roadmap" }[index];
                var filtersButton = view.GetVisualDescendants().OfType<DropDownButton>()
                    .Single(button => button.IsEffectivelyVisible && AutomationProperties.GetAutomationId(button) == prefix + "FiltersButton");
                var flyout = (Flyout)filtersButton.Flyout!;
                flyout.ShowAt(filtersButton);
                Render(window);
                var resetButton = ((Control)flyout.Content!).GetLogicalDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetAutomationId(button) == prefix + "ResetFiltersButton");
                resetButton.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Render(window);
                await Assert.That(notifications.AskCount).IsEqualTo(1);
                await Assert.That(FilterItems(FilterControl(view, false)).All(filter => !filter.ShowTasks)).IsTrue();
                await Assert.That(FilterItems(FilterControl(view, true)).All(filter => !filter.ShowTasks)).IsTrue();
                await Assert.That(other.Single(filter => filter.Emoji == "🧰").ShowTasks).IsTrue();
                flyout.Hide();
                await OpenAsync(window, FilterControl(view, false));
                Capture(window, $"tab-{index}-wide");
                Close(window);
                window.Width = 390;
                Render(window);
                await OpenAsync(window, FilterControl(view, false));
                Capture(window, $"tab-{index}-narrow");
                Close(window);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CatalogRefresh_KeepsSelectionsAndLocalHierarchy_ThenReconnectClearsThem()
    {
        if (await TaskTabEmojiFilterRenderedProcess.RunIfNeededAsync()) return;
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(EmojiTitleTestAppBuilder));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = MainControlFilterToolbarResponsiveUiTests.CreateControlledEmojiFixture();
            var vm = fixture.MainWindowViewModelTest;
            await vm.Connect();
            await Task.WhenAll(vm.taskRepository!.Tasks.Items.Select(task => task.SealPendingSaves()));
            await MainControlFilterToolbarResponsiveUiTests.PrepareEmojiFilterData(vm);
            vm.DetailsAreOpen = false;
            var view = new MainControl { DataContext = vm };
            var window = new Window { Content = view, Width = 900, Height = 760 };
            try
            {
                window.Show();
                SelectTab(view, window, 1);
                await ToggleAsync(window, FilterControl(view, false), "🚀");
                var local = vm.LastCreatedEmoji.IncludeFilters.Single(filter => filter.Emoji == "🚀");
                SelectTab(view, window, 3);
                await ToggleAsync(window, FilterControl(view, false), "🧰");
                var parent = vm.UnlockedEmoji.IncludeFilters.Single(filter => filter.Emoji == "🧪");
                var child = vm.UnlockedEmoji.IncludeFilters.Single(filter => filter.Emoji == "🔧");
                await Assert.That(ReferenceEquals(child.HierarchyParent, parent)).IsTrue();
                await OpenAsync(window, FilterControl(view, false));
                var hierarchyButton = FilterControl(view, false).FindControl<ListBox>("PART_List")!
                    .GetVisualDescendants().OfType<Button>().Single(button =>
                        ReferenceEquals(button.DataContext, parent) && button.Classes.Contains("EmojiFilterHierarchyToggle"));
                await Click(window, hierarchyButton);
                await Assert.That(parent.IsHierarchyExpanded).IsFalse();
                Close(window);
                var source = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
                source.Title = "🚀 Renamed launch";
                await WaitForResults(() => local.DisplayTitle == "Renamed launch");
                await Assert.That(local.ShowTasks).IsTrue();
                await Assert.That(vm.UnlockedEmoji.IncludeFilters.Single(filter => filter.Emoji == "🧰").ShowTasks).IsTrue();
                await Assert.That(parent.IsHierarchyExpanded).IsFalse();
                SelectTab(view, window, 1);
                await OpenAsync(window, FilterControl(view, false));
                await Assert.That(vm.LastCreatedEmoji.IncludeFilters.Single(filter => filter.Emoji == "🧪").IsHierarchyExpanded).IsTrue();
                Capture(window, "renamed-catalog");
                Close(window);
                // A cache refresh of the same surviving key must not replace its local filter.
                vm.taskRepository.Tasks.AddOrUpdate(source);
                await Assert.That(ReferenceEquals(local, vm.LastCreatedEmoji.IncludeFilters.Single(filter => filter.Emoji == "🚀"))).IsTrue();
                source.Title = "🪼 Changed emoji";
                await WaitForResults(() => !vm.LastCreatedEmoji.IncludeFilters.Any(filter => filter.Emoji == "🚀")
                    && vm.LastCreatedEmoji.IncludeFilters.Any(filter => filter.Emoji == "🪼"));
                await Assert.That(vm.LastCreatedEmoji.IncludeFilters.Single(filter => filter.Emoji == "🪼").ShowTasks).IsFalse();
                await Assert.That(vm.UnlockedEmoji.IncludeFilters.Single(filter => filter.Emoji == "🧰").ShowTasks).IsTrue();
                source.Title = "Without emoji";
                await WaitForResults(() => !vm.LastCreatedEmoji.IncludeFilters.Any(filter => filter.Emoji == "🪼"));
                var detached = vm.UnlockedEmoji.IncludeFilters.Single(filter => filter.Emoji == "🧰");
                vm.ClearTaskSpaceSurface();
                await Assert.That(vm.UnlockedEmoji.IncludeFilters.Count).IsEqualTo(0);
                await vm.Connect();
                await WaitForResults(() => vm.UnlockedEmoji.IncludeFilters.Count > 0);
                detached.ShowTasks = true;
                foreach (var state in AllStates(vm))
                    await Assert.That(state.IncludeFilters.Concat(state.ExcludeFilters).All(filter => !filter.ShowTasks)).IsTrue();
                await Assert.That(vm.EmojiFilters.Concat(vm.EmojiExcludeFilters).All(filter => !filter.ShowTasks)).IsTrue();
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static TaskTabEmojiFilterState[] AllStates(MainWindowViewModel vm) =>
        [vm.LastCreatedEmoji, vm.LastUpdatedEmoji, vm.UnlockedEmoji, vm.InProgressEmoji,
            vm.CompletedEmoji, vm.ArchivedEmoji, vm.LastOpenedEmoji, vm.RoadmapEmoji];

    private static bool Has(int index, MainWindowViewModel vm, string id) => (index switch
    {
        0 => vm.CurrentAllTasksItems,
        1 => vm.LastCreatedItems,
        2 => vm.LastUpdatedItems,
        3 => vm.UnlockedItems,
        4 => vm.InProgressItems,
        5 => vm.CompletedItems,
        6 => vm.ArchivedItems,
        7 => vm.LastOpenedItems,
        8 => vm.Graph.Tasks,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    }).Any(task => task.Id == id);

    private static async Task WaitForResults(Func<bool> condition) =>
        await Assert.That(await TestHelpers.WaitUntilAsync(condition, TimeSpan.FromSeconds(5))).IsTrue();

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SwitchingTabs_DoesNotReuseTheOtherTabsEmojiSelection(bool exclude)
    {
        if (await TaskTabEmojiFilterRenderedProcess.RunIfNeededAsync()) return;
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(EmojiTitleTestAppBuilder));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = MainControlFilterToolbarResponsiveUiTests.CreateControlledEmojiFixture();
            var vm = fixture.MainWindowViewModelTest;
            await vm.Connect();
            await Task.WhenAll(vm.taskRepository!.Tasks.Items.Select(task => task.SealPendingSaves()));
            await MainControlFilterToolbarResponsiveUiTests.PrepareEmojiFilterData(vm);
            vm.DetailsAreOpen = false;
            var view = new MainControl { DataContext = vm };
            var window = new Window { Content = view, Width = 900, Height = 760 };
            try
            {
                window.Show();
                SelectTab(view, window, 0);
                var all = FilterControl(view, exclude);
                await ToggleAsync(window, all, "🚀");
                await Assert.That(await TestHelpers.WaitUntilAsync(() =>
                    vm.CurrentAllTasksItems.Any(task => task.Id == MainWindowViewModelFixture.RootTask2Id) != exclude,
                    TimeSpan.FromSeconds(5))).IsTrue();
                Capture(window, $"all-selected-{exclude}");

                SelectTab(view, window, 3);
                var unlocked = FilterControl(view, exclude);
                await OpenAsync(window, unlocked);
                Capture(window, $"unlocked-before-own-selection-{exclude}");
                await Assert.That(FilterItems(unlocked).Single(filter => filter.Emoji == "🚀").ShowTasks).IsFalse();
                Close(window);
                await ToggleAsync(window, unlocked, "🧰");
                SelectTab(view, window, 0);
                await OpenAsync(window, FilterControl(view, exclude));
                await Assert.That(FilterItems(all).Single(filter => filter.Emoji == "🚀").ShowTasks).IsTrue();
                await Assert.That(FilterItems(all).Single(filter => filter.Emoji == "🧰").ShowTasks).IsFalse();
                Capture(window, $"all-returned-{exclude}");
                Close(window);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    internal static EmojiFilterMultiSelectSearchBox FilterControl(MainControl view, bool exclude) =>
        view.GetVisualDescendants().OfType<EmojiFilterMultiSelectSearchBox>()
            .Single(control => control.IsEffectivelyVisible && control.IsExclude == exclude);

    private static EmojiFilter[] FilterItems(EmojiFilterMultiSelectSearchBox control) =>
        ((IEnumerable)control.Filters!).OfType<EmojiFilter>().ToArray();

    internal static void SelectTab(MainControl view, Window window, int index)
    {
        view.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = index;
        Render(window);
    }

    private static async Task OpenAsync(Window window, EmojiFilterMultiSelectSearchBox control)
    {
        Render(window);
        control.FindControl<TextBox>("PART_Input")!.Focus();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await Assert.That(await TestHelpers.WaitUntilAsync(() =>
        {
            Render(window);
            return control.FindControl<Popup>("PART_DropDownPopup")!.IsOpen;
        }, TimeSpan.FromSeconds(3))).IsTrue();
    }

    internal static async Task ToggleAsync(Window window, EmojiFilterMultiSelectSearchBox control, string emoji)
    {
        await OpenAsync(window, control);
        var list = control.FindControl<ListBox>("PART_List")!;
        list.SelectedItem = FilterItems(control).Single(filter => filter.Emoji == emoji);
        list.Focus();
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        Render(window);
        Close(window);
    }

    private static void Close(Window window)
    {
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Render(window);
    }

    internal static async Task Click(Window window, Control control)
    {
        TopLevel? root = null;
        control.BringIntoView();
        Point point = default;
        Point? previousPoint = null;
        Visual? hit = null;
        // Layout bounds can precede the compositor's input scene in a full suite.
        // Require two stable positions and a real hit on this button before one click.
        var ready = await TestHelpers.WaitUntilAsync(() =>
        {
            Render(window);
            // A flyout may still belong to the owner window before the popup is
            // attached. Resolve its input root after rendering, not before it.
            var currentRoot = TopLevel.GetTopLevel(control);
            if (currentRoot == null) return false;
            if (!ReferenceEquals(root, currentRoot)) previousPoint = null;
            root = currentRoot;
            root.UpdateLayout();
            var translated = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root);
            if (!translated.HasValue) return false;
            point = translated.Value;
            hit = root.InputHitTest(point) as Visual;
            var onButton = ReferenceEquals(hit, control) ||
                hit?.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, control)) == true;
            var stable = previousPoint == point;
            previousPoint = point;
            return stable && onButton && control.IsEffectivelyVisible && control.IsEffectivelyEnabled;
        }, TimeSpan.FromSeconds(3));
        if (!ready)
            Console.WriteLine($"Emoji click target not ready: root={root?.GetType().Name}/{root?.Bounds}; button={control.Bounds}; point={point}; hit={hit}");
        await Assert.That(ready).IsTrue();
        root!.MouseMove(point, RawInputModifiers.None);
        root.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        root.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Render(window);
    }

    internal static void Render(Window window)
    {
        var size = new Size(window.Width, window.Height);
        window.Measure(size);
        window.Arrange(new Rect(size));
        for (var tick = 0; tick < 5; tick++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("UNLIMOTION_TEST_TRACE_DIRECTORY");
        if (string.IsNullOrEmpty(directory)) return;
        Render(window);
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("Rendered emoji-filter frame is unavailable.");
        frame.Save(Path.Combine(directory, $"tab-emoji-{name}.png"));
    }
}
