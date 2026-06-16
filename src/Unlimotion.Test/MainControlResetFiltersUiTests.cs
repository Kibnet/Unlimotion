using System;
using System.Collections;
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
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class MainControlResetFiltersUiTests
{
    private static readonly (int TabIndex, string FiltersButtonAutomationId, string ResetButtonAutomationId)[] TaskTabs =
    [
        (0, "AllTasksFiltersButton", "AllTasksResetFiltersButton"),
        (1, "LastCreatedFiltersButton", "LastCreatedResetFiltersButton"),
        (2, "LastUpdatedFiltersButton", "LastUpdatedResetFiltersButton"),
        (3, "UnlockedFiltersButton", "UnlockedResetFiltersButton"),
        (4, "InProgressFiltersButton", "InProgressResetFiltersButton"),
        (5, "CompletedFiltersButton", "CompletedResetFiltersButton"),
        (6, "ArchivedFiltersButton", "ArchivedResetFiltersButton"),
        (7, "LastOpenedFiltersButton", "LastOpenedResetFiltersButton"),
        (8, "RoadmapFiltersButton", "RoadmapResetFiltersButton")
    ];

    private static readonly (int TabIndex, string FiltersButtonAutomationId, string StatusFilterAutomationId)[] StatusFilterTabs =
    [
        (0, "AllTasksFiltersButton", "AllTasksStatusFilterComboBox"),
        (1, "LastCreatedFiltersButton", "LastCreatedStatusFilterComboBox"),
        (2, "LastUpdatedFiltersButton", "LastUpdatedStatusFilterComboBox"),
        (3, "UnlockedFiltersButton", "UnlockedStatusFilterComboBox"),
        (4, "InProgressFiltersButton", "InProgressStatusFilterComboBox"),
        (5, "CompletedFiltersButton", "CompletedStatusFilterComboBox"),
        (6, "ArchivedFiltersButton", "ArchivedStatusFilterComboBox"),
        (7, "LastOpenedFiltersButton", "LastOpenedStatusFilterComboBox"),
        (8, "RoadmapFiltersButton", "RoadmapStatusFilterComboBox")
    ];

    private static readonly (int TabIndex, string FiltersButtonAutomationId, string ResetButtonAutomationId, int ForcedVisibleStatus)[] StatusResetTabs =
    [
        (3, "UnlockedFiltersButton", "UnlockedResetFiltersButton", -1),
        (4, "InProgressFiltersButton", "InProgressResetFiltersButton", (int)DomainTaskStatus.InProgress),
        (5, "CompletedFiltersButton", "CompletedResetFiltersButton", (int)DomainTaskStatus.Completed),
        (6, "ArchivedFiltersButton", "ArchivedResetFiltersButton", (int)DomainTaskStatus.Archived),
        (7, "LastOpenedFiltersButton", "LastOpenedResetFiltersButton", -1)
    ];

    [Test]
    public async Task ResetFiltersButton_IsAvailableOnTaskTabs()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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
                await DrainUiThrottlesAsync();
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task StatusFilterComboBox_IsAvailableOnEveryTaskTab()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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

                var expectedStatuses = Enum.GetValues<DomainTaskStatus>();
                foreach (var (tabIndex, filtersButtonAutomationId, statusFilterAutomationId) in StatusFilterTabs)
                {
                    SelectTab(view, tabIndex);
                    var filtersButton = FindControlByAutomationId<DropDownButton>(view, filtersButtonAutomationId);
                    var flyout = filtersButton.Flyout as Flyout
                                  ?? throw new InvalidOperationException(
                                      $"Filter button '{filtersButtonAutomationId}' must use a Flyout.");

                    flyout.ShowAt(filtersButton);
                    Dispatcher.UIThread.RunJobs();

                    var flyoutContent = flyout.Content as Control
                                        ?? throw new InvalidOperationException(
                                            $"Filter button '{filtersButtonAutomationId}' flyout content was not found.");
                    var statusFilter = FindControlInDetachedContent<ComboBox>(
                        flyoutContent,
                        statusFilterAutomationId);

                    await Assert.That(statusFilter).IsNotNull();
                    await Assert.That(ReadStatusFilterStatuses(statusFilter!)).IsEquivalentTo(expectedStatuses);

                    flyout.Hide();
                    Dispatcher.UIThread.RunJobs();
                }
            }
            finally
            {
                await DrainUiThrottlesAsync();
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task WantedFilterComboBox_IsAvailableOnUnlockedAndRoadmapTabs_WithDefaultAllOption()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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

                await Assert.That(vm.ShowWanted).IsNull();

                SelectTab(view, 3);
                var unlockedWantedFilter = OpenFilterPanelAndFindComboBox(
                    view,
                    "UnlockedFiltersButton",
                    "UnlockedWantedFilterComboBox");
                await AssertWantedFilterComboBox(unlockedWantedFilter, vm, expectedShowWanted: null);
                unlockedWantedFilter.SelectedItem = vm.WantedFilterDefinitions.Single(option => option.Value == true);
                Dispatcher.UIThread.RunJobs();
                await Assert.That(vm.ShowWanted).IsTrue();
                HideFilterPanel(view, "UnlockedFiltersButton");

                SelectTab(view, 8);
                vm.Graph.OnlyUnlocked = true;
                Dispatcher.UIThread.RunJobs();
                var roadmapWantedFilter = OpenFilterPanelAndFindComboBox(
                    view,
                    "RoadmapFiltersButton",
                    "RoadmapWantedFilterComboBox");
                await AssertWantedFilterComboBox(roadmapWantedFilter, vm, expectedShowWanted: true);
                vm.ShowWanted = null;
                Dispatcher.UIThread.RunJobs();
                await AssertWantedFilterComboBox(roadmapWantedFilter, vm, expectedShowWanted: null);
                vm.ShowWanted = true;
                Dispatcher.UIThread.RunJobs();
                await AssertWantedFilterComboBox(roadmapWantedFilter, vm, expectedShowWanted: true);
                roadmapWantedFilter.SelectedItem = vm.WantedFilterDefinitions.Single(option => option.Value == false);
                Dispatcher.UIThread.RunJobs();
                await Assert.That(vm.ShowWanted).IsFalse();
                roadmapWantedFilter.SelectedItem = vm.WantedFilterDefinitions.Single(option => option.Value == null);
                Dispatcher.UIThread.RunJobs();
                await Assert.That(vm.ShowWanted).IsNull();
            }
            finally
            {
                await DrainUiThrottlesAsync();
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ResetFiltersButton_OnStatusFilteredTabs_ResetsStatusFilters()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                var defaultShowCompleted = vm.ShowCompleted;
                var defaultShowArchived = vm.ShowArchived;

                var notificationManager = (NotificationManagerWrapperMock)vm.ManagerWrapper;
                notificationManager.AskResult = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                foreach (var (tabIndex, filtersButtonAutomationId, resetButtonAutomationId, forcedVisibleStatus) in StatusResetTabs)
                {
                    notificationManager.ClearMessages();
                    SetAllStatusFilters(vm, false);

                    SelectTab(view, tabIndex);
                    var resetButton = OpenFilterPanelAndFindResetButton(
                        view,
                        filtersButtonAutomationId,
                        resetButtonAutomationId);
                    await ClickControlAsync(window, resetButton);
                    Dispatcher.UIThread.RunJobs();

                    var forcedStatus = forcedVisibleStatus >= 0
                        ? (DomainTaskStatus)forcedVisibleStatus
                        : (DomainTaskStatus?)null;
                    await Assert.That(notificationManager.AskCount).IsEqualTo(1);
                    await AssertStatusFiltersReset(vm, defaultShowCompleted, defaultShowArchived, forcedStatus);

                    HideFilterPanel(view, filtersButtonAutomationId);
                }
            }
            finally
            {
                await DrainUiThrottlesAsync();
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ResetFiltersButton_AsksConfirmation_AndCancelKeepsFilters()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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
                var resetButton = OpenFilterPanelAndFindResetButton(
                    view,
                    "AllTasksFiltersButton",
                    "AllTasksResetFiltersButton");
                await ClickControlAsync(window, resetButton);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(notificationManager.AskCount).IsEqualTo(1);
                await Assert.That(notificationManager.LastAskHeader).IsEqualTo(L10n.Get("ResetFiltersConfirmHeader"));
                await Assert.That(notificationManager.LastAskMessage).IsEqualTo(L10n.Get("ResetFiltersConfirmMessage"));
                await AssertActiveFilters(vm);
            }
            finally
            {
                await DrainUiThrottlesAsync();
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task AllTasksResetFilters_AfterConfirmation_ResetsOnlyAllTasksFiltersToDefaults()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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
                var resetButton = OpenFilterPanelAndFindResetButton(
                    view,
                    "AllTasksFiltersButton",
                    "AllTasksResetFiltersButton");
                await ClickControlAsync(window, resetButton);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(notificationManager.AskCount).IsEqualTo(1);
                await Assert.That(vm.Search.SearchText).IsEqualTo(string.Empty);
                await Assert.That(vm.ShowCompleted).IsEqualTo(defaultShowCompleted);
                await Assert.That(vm.ShowArchived).IsEqualTo(defaultShowArchived);
                await Assert.That(vm.ShowWanted).IsTrue();
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
                await DrainUiThrottlesAsync();
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task LastCreatedResetFilters_AfterConfirmation_ResetsCurrentDateFilterToDefault()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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
                var resetButton = OpenFilterPanelAndFindResetButton(
                    view,
                    "LastCreatedFiltersButton",
                    "LastCreatedResetFiltersButton");
                await ClickControlAsync(window, resetButton);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(notificationManager.AskCount).IsEqualTo(1);
                await Assert.That(vm.Search.SearchText).IsEqualTo(string.Empty);
                await Assert.That(vm.ShowCompleted).IsEqualTo(defaultShowCompleted);
                await Assert.That(vm.ShowArchived).IsEqualTo(defaultShowArchived);
                await Assert.That(vm.ShowWanted).IsTrue();
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
                await DrainUiThrottlesAsync();
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapResetFilters_WhenCompletionFiltersAreVisible_DoesNotResetHiddenWantedFilter()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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

                SelectTab(view, 8);
                var resetButton = OpenFilterPanelAndFindResetButton(
                    view,
                    "RoadmapFiltersButton",
                    "RoadmapResetFiltersButton");
                await ClickControlAsync(window, resetButton);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(notificationManager.AskCount).IsEqualTo(1);
                await Assert.That(vm.Search.SearchText).IsEqualTo(string.Empty);
                await Assert.That(vm.ShowCompleted).IsEqualTo(defaultShowCompleted);
                await Assert.That(vm.ShowArchived).IsEqualTo(defaultShowArchived);
                await Assert.That(vm.ShowWanted).IsTrue();
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
                await DrainUiThrottlesAsync();
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapResetFilters_WhenWantedFilterIsVisible_DoesNotResetHiddenCompletionFilters()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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

                SelectTab(view, 8);
                var resetButton = OpenFilterPanelAndFindResetButton(
                    view,
                    "RoadmapFiltersButton",
                    "RoadmapResetFiltersButton");
                await ClickControlAsync(window, resetButton);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(notificationManager.AskCount).IsEqualTo(1);
                await Assert.That(vm.Search.SearchText).IsEqualTo(string.Empty);
                await Assert.That(vm.ShowCompleted).IsTrue();
                await Assert.That(vm.ShowArchived).IsTrue();
                await Assert.That(vm.ShowWanted).IsNull();
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
                await DrainUiThrottlesAsync();
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

    private static void SetAllStatusFilters(MainWindowViewModel vm, bool selected)
    {
        foreach (var filter in vm.StatusFilters)
        {
            filter.ShowTasks = selected;
        }
    }

    private static void AssertResetButtonsOnTaskTabs(MainControl view)
    {
        foreach (var (tabIndex, filtersButtonAutomationId, resetButtonAutomationId) in TaskTabs)
        {
            SelectTab(view, tabIndex);
            var resetButton = OpenFilterPanelAndFindResetButton(
                view,
                filtersButtonAutomationId,
                resetButtonAutomationId);

            if (!resetButton.Classes.Contains("FilterPanelResetButton"))
            {
                throw new InvalidOperationException($"Reset button '{resetButtonAutomationId}' must be styled as a filter panel action.");
            }

            var filtersButton = FindControlByAutomationId<DropDownButton>(view, filtersButtonAutomationId);
            ((Flyout)filtersButton.Flyout!).Hide();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task AssertActiveFilters(MainWindowViewModel vm)
    {
        await Assert.That(vm.Search.SearchText).IsEqualTo("Task");
        await Assert.That(vm.ShowCompleted).IsTrue();
        await Assert.That(vm.ShowArchived).IsTrue();
        await Assert.That(vm.ShowWanted).IsTrue();
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

    private static async Task AssertStatusFiltersReset(
        MainWindowViewModel vm,
        bool defaultShowCompleted,
        bool defaultShowArchived,
        DomainTaskStatus? forcedVisibleStatus)
    {
        await AssertStatusFilterSelected(vm, DomainTaskStatus.NotReady, true);
        await AssertStatusFilterSelected(vm, DomainTaskStatus.Prepared, true);
        await AssertStatusFilterSelected(vm, DomainTaskStatus.InProgress, true);

        var expectedShowCompleted = forcedVisibleStatus == DomainTaskStatus.Completed || defaultShowCompleted;
        var expectedShowArchived = forcedVisibleStatus == DomainTaskStatus.Archived || defaultShowArchived;

        await AssertStatusFilterSelected(vm, DomainTaskStatus.Completed, expectedShowCompleted);
        await AssertStatusFilterSelected(vm, DomainTaskStatus.Archived, expectedShowArchived);
        await Assert.That(vm.ShowCompleted).IsEqualTo(expectedShowCompleted);
        await Assert.That(vm.ShowArchived).IsEqualTo(expectedShowArchived);
    }

    private static async Task AssertStatusFilterSelected(
        MainWindowViewModel vm,
        DomainTaskStatus status,
        bool expected)
    {
        var filter = vm.StatusFilters.Single(item => item.Status == status);
        if (expected)
        {
            await Assert.That(filter.ShowTasks).IsTrue();
            return;
        }

        await Assert.That(filter.ShowTasks).IsFalse();
    }

    private static async Task AssertWantedFilterComboBox(
        ComboBox comboBox,
        MainWindowViewModel vm,
        bool? expectedShowWanted)
    {
        var itemsSource = comboBox.ItemsSource ??
                          throw new InvalidOperationException("Wanted filter combo box must be bound to an ItemsSource.");
        var selectedDefinition = vm.WantedFilterDefinitions.Single(option => option.Value == expectedShowWanted);
        await Assert.That(itemsSource).IsSameReferenceAs(vm.WantedFilterDefinitions);
        await Assert.That(comboBox.SelectedItem).IsEqualTo(selectedDefinition);
        await Assert.That(ReadRenderedTextValues(comboBox)).Contains(selectedDefinition.Title);
        await Assert.That(ReadWantedFilterValues(comboBox)).IsEquivalentTo(new bool?[] { null, true, false });
        await Assert.That(ReadWantedFilterTitles(comboBox)).IsEquivalentTo([
            L10n.Get("WantedFilterAll"),
            L10n.Get("WantedFilterWanted"),
            L10n.Get("WantedFilterNotWanted")
        ]);
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

    private static async Task DrainUiThrottlesAsync()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(SearchDefinition.DefaultThrottleMs + 100));
        Dispatcher.UIThread.RunJobs();
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

    private static Button OpenFilterPanelAndFindResetButton(
        MainControl view,
        string filtersButtonAutomationId,
        string resetButtonAutomationId)
    {
        var filtersButton = FindControlByAutomationId<DropDownButton>(view, filtersButtonAutomationId);
        if (filtersButton.Flyout is not Flyout flyout)
        {
            throw new InvalidOperationException($"Filter button '{filtersButtonAutomationId}' must use a Flyout.");
        }

        flyout.ShowAt(filtersButton);
        Dispatcher.UIThread.RunJobs();

        if (flyout.Content is not Control flyoutContent)
        {
            throw new InvalidOperationException($"Filter button '{filtersButtonAutomationId}' flyout content was not found.");
        }

        var resetButton = FindControlInDetachedContent<Button>(flyoutContent, resetButtonAutomationId);
        if (resetButton == null)
        {
            throw new InvalidOperationException($"Reset button '{resetButtonAutomationId}' was not found in the filter flyout.");
        }

        Dispatcher.UIThread.RunJobs();
        return resetButton;
    }

    private static ComboBox OpenFilterPanelAndFindComboBox(
        MainControl view,
        string filtersButtonAutomationId,
        string comboBoxAutomationId)
    {
        var filtersButton = FindControlByAutomationId<DropDownButton>(view, filtersButtonAutomationId);
        if (filtersButton.Flyout is not Flyout flyout)
        {
            throw new InvalidOperationException($"Filter button '{filtersButtonAutomationId}' must use a Flyout.");
        }

        flyout.ShowAt(filtersButton);
        Dispatcher.UIThread.RunJobs();

        if (flyout.Content is not Control flyoutContent)
        {
            throw new InvalidOperationException($"Filter button '{filtersButtonAutomationId}' flyout content was not found.");
        }

        var comboBox = FindControlInDetachedContent<ComboBox>(flyoutContent, comboBoxAutomationId);
        if (comboBox == null)
        {
            throw new InvalidOperationException($"ComboBox '{comboBoxAutomationId}' was not found in the filter flyout.");
        }

        Dispatcher.UIThread.RunJobs();
        return comboBox;
    }

    private static void HideFilterPanel(MainControl view, string filtersButtonAutomationId)
    {
        var filtersButton = FindControlByAutomationId<DropDownButton>(view, filtersButtonAutomationId);
        ((Flyout)filtersButton.Flyout!).Hide();
        Dispatcher.UIThread.RunJobs();
    }

    private static T? FindControlInDetachedContent<T>(Control root, string automationId)
        where T : Control
    {
        if (root is T typedRoot &&
            string.Equals(AutomationProperties.GetAutomationId(root), automationId, StringComparison.Ordinal))
        {
            return typedRoot;
        }

        return root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(candidate =>
                string.Equals(
                    AutomationProperties.GetAutomationId(candidate),
                    automationId,
                    StringComparison.Ordinal));
    }

    private static IReadOnlyList<DomainTaskStatus> ReadStatusFilterStatuses(ComboBox comboBox)
    {
        if (comboBox.ItemsSource is not IEnumerable source)
        {
            throw new InvalidOperationException("Status filter combo box must be bound to an ItemsSource.");
        }

        return source
            .Cast<TaskStatusFilter>()
            .Select(filter => filter.Status)
            .ToList();
    }

    private static IReadOnlyList<string> ReadRenderedTextValues(Control control)
    {
        return control
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(static textBlock => textBlock.Text)
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .Cast<string>()
            .ToList();
    }

    private static IReadOnlyList<bool?> ReadWantedFilterValues(ComboBox comboBox)
    {
        if (comboBox.ItemsSource is not IEnumerable source)
        {
            throw new InvalidOperationException("Wanted filter combo box must be bound to an ItemsSource.");
        }

        return source
            .Cast<WantedFilterOption>()
            .Select(filter => filter.Value)
            .ToList();
    }

    private static IReadOnlyList<string> ReadWantedFilterTitles(ComboBox comboBox)
    {
        if (comboBox.ItemsSource is not IEnumerable source)
        {
            throw new InvalidOperationException("Wanted filter combo box must be bound to an ItemsSource.");
        }

        return source
            .Cast<WantedFilterOption>()
            .Select(filter => filter.Title)
            .ToList();
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
