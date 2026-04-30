using System;
using System.Collections.Generic;
using System.Linq;
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
using Unlimotion.ViewModel;
using Unlimotion.Views;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Test;

[ParallelLimiter<SharedUiStateParallelLimit>]
public class MainControlResetFiltersUiTests
{
    private static readonly (int TabIndex, string ButtonAutomationId)[] TaskTabs =
    [
        (0, "AllTasksResetFiltersButton"),
        (1, "LastCreatedResetFiltersButton"),
        (2, "LastUpdatedResetFiltersButton"),
        (3, "UnlockedResetFiltersButton"),
        (4, "CompletedResetFiltersButton"),
        (5, "ArchivedResetFiltersButton"),
        (6, "LastOpenedResetFiltersButton"),
        (7, "RoadmapResetFiltersButton")
    ];

    [Test]
    public async Task ResetFiltersButton_IsAvailableOnTaskTabs()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                AssertResetButtonsOnTaskTabs(view);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ResetFiltersButton_AsksConfirmation_AndCancelKeepsFilters()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                SetActiveFilters(vm);

                var notificationManager = (NotificationManagerWrapperMock)vm.ManagerWrapper;
                notificationManager.ClearMessages();
                notificationManager.AskResult = false;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                SelectTab(view, 0);
                var resetButton = FindControlByAutomationId<Button>(view, "AllTasksResetFiltersButton");
                await ClickControlAsync(window, resetButton);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(notificationManager.AskCount).IsEqualTo(1);
                await Assert.That(notificationManager.LastAskHeader).IsEqualTo(L10n.Get("ResetFiltersConfirmHeader"));
                await Assert.That(notificationManager.LastAskMessage).IsEqualTo(L10n.Get("ResetFiltersConfirmMessage"));
                await AssertActiveFilters(vm);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task AllTasksResetFilters_AfterConfirmation_ResetsOnlyAllTasksFiltersToDefaults()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                var defaultShowCompleted = vm.ShowCompleted;
                var defaultShowArchived = vm.ShowArchived;
                SetActiveFilters(vm);

                var notificationManager = (NotificationManagerWrapperMock)vm.ManagerWrapper;
                notificationManager.ClearMessages();
                notificationManager.AskResult = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                SelectTab(view, 0);
                var resetButton = FindControlByAutomationId<Button>(view, "AllTasksResetFiltersButton");
                await ClickControlAsync(window, resetButton);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(notificationManager.AskCount).IsEqualTo(1);
                await Assert.That(vm.Search.SearchText).IsEqualTo(string.Empty);
                await Assert.That(vm.ShowCompleted).IsEqualTo(defaultShowCompleted);
                await Assert.That(vm.ShowArchived).IsEqualTo(defaultShowArchived);
                await Assert.That(vm.ShowWanted).IsEqualTo(true);
                await Assert.That(vm.Graph.OnlyUnlocked).IsTrue();
                await AssertToggleFiltersReset(vm.EmojiFilters);
                await AssertToggleFiltersReset(vm.EmojiExcludeFilters);
                await AssertFirstFilterActive(vm.UnlockedTimeFilters);
                await AssertFirstFilterActive(vm.DurationFilters);
                await AssertCustomDateFilter(vm.CompletedDateFilter);
                await AssertCustomDateFilter(vm.ArchivedDateFilter);
                await AssertCustomDateFilter(vm.LastCreatedDateFilter);
                await AssertCustomDateFilter(vm.LastUpdatedDateFilter);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task LastCreatedResetFilters_AfterConfirmation_ResetsCurrentDateFilterToDefault()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                var defaultShowCompleted = vm.ShowCompleted;
                var defaultShowArchived = vm.ShowArchived;
                SetActiveFilters(vm);

                var notificationManager = (NotificationManagerWrapperMock)vm.ManagerWrapper;
                notificationManager.ClearMessages();
                notificationManager.AskResult = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                SelectTab(view, 1);
                var resetButton = FindControlByAutomationId<Button>(view, "LastCreatedResetFiltersButton");
                await ClickControlAsync(window, resetButton);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(notificationManager.AskCount).IsEqualTo(1);
                await Assert.That(vm.Search.SearchText).IsEqualTo(string.Empty);
                await Assert.That(vm.ShowCompleted).IsEqualTo(defaultShowCompleted);
                await Assert.That(vm.ShowArchived).IsEqualTo(defaultShowArchived);
                await Assert.That(vm.ShowWanted).IsEqualTo(true);
                await Assert.That(vm.Graph.OnlyUnlocked).IsTrue();
                await AssertToggleFiltersReset(vm.EmojiFilters);
                await AssertToggleFiltersReset(vm.EmojiExcludeFilters);
                await AssertDateFilterDefault(vm.LastCreatedDateFilter);
                await AssertCustomDateFilter(vm.CompletedDateFilter);
                await AssertCustomDateFilter(vm.ArchivedDateFilter);
                await AssertCustomDateFilter(vm.LastUpdatedDateFilter);
                await AssertFirstFilterActive(vm.UnlockedTimeFilters);
                await AssertFirstFilterActive(vm.DurationFilters);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapResetFilters_WhenCompletionFiltersAreVisible_DoesNotResetHiddenWantedFilter()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                var defaultShowCompleted = vm.ShowCompleted;
                var defaultShowArchived = vm.ShowArchived;
                SetActiveFilters(vm);
                vm.Graph.OnlyUnlocked = false;

                var notificationManager = (NotificationManagerWrapperMock)vm.ManagerWrapper;
                notificationManager.ClearMessages();
                notificationManager.AskResult = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                SelectTab(view, 7);
                var resetButton = FindControlByAutomationId<Button>(view, "RoadmapResetFiltersButton");
                await ClickControlAsync(window, resetButton);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(notificationManager.AskCount).IsEqualTo(1);
                await Assert.That(vm.Search.SearchText).IsEqualTo(string.Empty);
                await Assert.That(vm.ShowCompleted).IsEqualTo(defaultShowCompleted);
                await Assert.That(vm.ShowArchived).IsEqualTo(defaultShowArchived);
                await Assert.That(vm.ShowWanted).IsEqualTo(true);
                await Assert.That(vm.Graph.OnlyUnlocked).IsFalse();
                await AssertToggleFiltersReset(vm.EmojiFilters);
                await AssertToggleFiltersReset(vm.EmojiExcludeFilters);
                await AssertFirstFilterActive(vm.UnlockedTimeFilters);
                await AssertFirstFilterActive(vm.DurationFilters);
                await AssertCustomDateFilter(vm.CompletedDateFilter);
                await AssertCustomDateFilter(vm.ArchivedDateFilter);
                await AssertCustomDateFilter(vm.LastCreatedDateFilter);
                await AssertCustomDateFilter(vm.LastUpdatedDateFilter);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapResetFilters_WhenWantedFilterIsVisible_DoesNotResetHiddenCompletionFilters()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                SetActiveFilters(vm);

                var notificationManager = (NotificationManagerWrapperMock)vm.ManagerWrapper;
                notificationManager.ClearMessages();
                notificationManager.AskResult = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                SelectTab(view, 7);
                var resetButton = FindControlByAutomationId<Button>(view, "RoadmapResetFiltersButton");
                await ClickControlAsync(window, resetButton);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(notificationManager.AskCount).IsEqualTo(1);
                await Assert.That(vm.Search.SearchText).IsEqualTo(string.Empty);
                await Assert.That(vm.ShowCompleted).IsTrue();
                await Assert.That(vm.ShowArchived).IsTrue();
                await Assert.That(vm.ShowWanted).IsEqualTo(false);
                await Assert.That(vm.Graph.OnlyUnlocked).IsFalse();
                await AssertToggleFiltersReset(vm.EmojiFilters);
                await AssertToggleFiltersReset(vm.EmojiExcludeFilters);
                await AssertFirstFilterActive(vm.UnlockedTimeFilters);
                await AssertFirstFilterActive(vm.DurationFilters);
                await AssertCustomDateFilter(vm.CompletedDateFilter);
                await AssertCustomDateFilter(vm.ArchivedDateFilter);
                await AssertCustomDateFilter(vm.LastCreatedDateFilter);
                await AssertCustomDateFilter(vm.LastUpdatedDateFilter);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    private static void SetActiveFilters(MainWindowViewModel vm)
    {
        vm.Search.SearchText = "Task";
        vm.ShowCompleted = true;
        vm.ShowArchived = true;
        vm.ShowWanted = true;
        vm.Graph.OnlyUnlocked = true;

        SetFirstFilter(vm.EmojiFilters);
        SetFirstFilter(vm.EmojiExcludeFilters);
        SetFirstFilter(vm.UnlockedTimeFilters);
        SetFirstFilter(vm.DurationFilters);

        SetCustomDateFilter(vm.CompletedDateFilter);
        SetCustomDateFilter(vm.ArchivedDateFilter);
        SetCustomDateFilter(vm.LastCreatedDateFilter);
        SetCustomDateFilter(vm.LastUpdatedDateFilter);
    }

    private static void SetFirstFilter(IEnumerable<EmojiFilter> filters)
    {
        filters.First().ShowTasks = true;
    }

    private static void SetFirstFilter(IEnumerable<UnlockedTimeFilter> filters)
    {
        filters.First().ShowTasks = true;
    }

    private static void SetFirstFilter(IEnumerable<DurationFilter> filters)
    {
        filters.First().ShowTasks = true;
    }

    private static void SetCustomDateFilter(DateFilter filter)
    {
        filter.CurrentOption = DateFilterDefinition.AllTime;
        filter.IsCustom = true;
        filter.From = DateTime.Today.AddDays(-7);
        filter.To = DateTime.Today.AddDays(-1);
    }

    private static void AssertResetButtonsOnTaskTabs(MainControl view)
    {
        foreach (var (tabIndex, buttonAutomationId) in TaskTabs)
        {
            SelectTab(view, tabIndex);
            FindControlByAutomationId<Button>(view, buttonAutomationId);
        }
    }

    private static async Task AssertActiveFilters(MainWindowViewModel vm)
    {
        await Assert.That(vm.Search.SearchText).IsEqualTo("Task");
        await Assert.That(vm.ShowCompleted).IsTrue();
        await Assert.That(vm.ShowArchived).IsTrue();
        await Assert.That(vm.ShowWanted).IsEqualTo(true);
        await Assert.That(vm.Graph.OnlyUnlocked).IsTrue();

        await AssertFirstFilterActive(vm.EmojiFilters);
        await AssertFirstFilterActive(vm.EmojiExcludeFilters);
        await AssertFirstFilterActive(vm.UnlockedTimeFilters);
        await AssertFirstFilterActive(vm.DurationFilters);
        await AssertCustomDateFilter(vm.CompletedDateFilter);
        await AssertCustomDateFilter(vm.ArchivedDateFilter);
        await AssertCustomDateFilter(vm.LastCreatedDateFilter);
        await AssertCustomDateFilter(vm.LastUpdatedDateFilter);
    }

    private static async Task AssertFirstFilterActive(IEnumerable<EmojiFilter> filters)
    {
        await Assert.That(filters.First().ShowTasks).IsTrue();
    }

    private static async Task AssertFirstFilterActive(IEnumerable<UnlockedTimeFilter> filters)
    {
        await Assert.That(filters.First().ShowTasks).IsTrue();
    }

    private static async Task AssertFirstFilterActive(IEnumerable<DurationFilter> filters)
    {
        await Assert.That(filters.First().ShowTasks).IsTrue();
    }

    private static async Task AssertToggleFiltersReset(IEnumerable<EmojiFilter> filters)
    {
        await Assert.That(filters.All(static filter => !filter.ShowTasks)).IsTrue();
    }

    private static async Task AssertCustomDateFilter(DateFilter filter)
    {
        await Assert.That(filter.IsCustom).IsTrue();
        await Assert.That(filter.CurrentOption.Id).IsEqualTo(DateFilterDefinition.AllTime.Id);
        await Assert.That(filter.From).IsEqualTo(DateTime.Today.AddDays(-7));
        await Assert.That(filter.To).IsEqualTo(DateTime.Today.AddDays(-1));
    }

    private static async Task AssertDateFilterDefault(DateFilter filter)
    {
        DateTime? today = DateTime.Today;
        await Assert.That(filter.IsCustom).IsFalse();
        await Assert.That(filter.CurrentOption.Id).IsEqualTo(DateFilterDefinition.Today.Id);
        await Assert.That(filter.From).IsEqualTo(today);
        await Assert.That(filter.To).IsEqualTo(today);
    }

    private static Window CreateWindow(Control content)
    {
        return new Window
        {
            Width = 1800,
            Height = 1000,
            Content = content
        };
    }

    private static T FindControlByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        var control = root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(candidate =>
                string.Equals(
                    AutomationProperties.GetAutomationId(candidate),
                    automationId,
                    StringComparison.Ordinal));

        return control ?? throw new InvalidOperationException($"Control with AutomationId '{automationId}' was not found.");
    }

    private static void SelectTab(MainControl view, int index)
    {
        var tabControl = view.GetVisualDescendants().OfType<TabControl>().First();
        tabControl.SelectedIndex = index;
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task ClickControlAsync(Window window, Control control)
    {
        var point = GetControlCenterPoint(window, control);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        await Task.CompletedTask;
    }

    private static Point GetControlCenterPoint(Visual relativeTo, Control control)
    {
        var point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            relativeTo);

        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate point for control {control.GetType().Name}.");
        }

        return point.Value;
    }
}
