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
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

internal static class FilterResetUiContract
{
    public static async Task<FilterResetScenarioResult> AssertFilterResetScenarioAsync()
    {
        var result = await ExecuteFilterResetScenarioAsync();

        await AssertFilterResetScenarioResultAsync(result);

        return result;
    }

    public static async Task<FilterResetScenarioResult> ExecuteFilterResetScenarioAsync()
    {
        var result = new FilterResetScenarioResult();

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
                var defaultShowWanted = vm.ShowWanted;
                var notificationManager = (NotificationManagerWrapperMock)vm.ManagerWrapper;
                notificationManager.AskResult = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                result.MainControlOpened = true;

                await ExecuteAllTasksResetAsync(
                    window,
                    view,
                    vm,
                    notificationManager,
                    defaultShowCompleted,
                    defaultShowArchived,
                    result);

                await ExecuteLastCreatedDateResetAsync(
                    window,
                    view,
                    vm,
                    notificationManager,
                    defaultShowCompleted,
                    defaultShowArchived,
                    result);

                await ExecuteUnlockedResetAsync(
                    window,
                    view,
                    vm,
                    notificationManager,
                    defaultShowCompleted,
                    defaultShowArchived,
                    defaultShowWanted,
                    result);
            }
            finally
            {
                await DrainUiThrottlesAsync();
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);

        return result;
    }

    public static async Task AssertFilterResetScenarioResultAsync(FilterResetScenarioResult result)
    {
        await Assert.That(result.MainControlOpened).IsTrue();
        await Assert.That(result.FilterPanelOpened).IsTrue();
        await Assert.That(result.ConfirmationAsked).IsTrue();
        await Assert.That(result.SearchReset).IsTrue();
        await Assert.That(result.StatusFiltersReset).IsTrue();
        await Assert.That(result.DateFilterReset).IsTrue();
        await Assert.That(result.DurationFiltersReset).IsTrue();
        await Assert.That(result.WantedFilterReset).IsTrue();
        await Assert.That(result.EmojiFiltersReset).IsTrue();
    }

    private static async Task ExecuteAllTasksResetAsync(
        Window window,
        MainControl view,
        MainWindowViewModel vm,
        NotificationManagerWrapperMock notificationManager,
        bool defaultShowCompleted,
        bool defaultShowArchived,
        FilterResetScenarioResult result)
    {
        SetActiveFilters(vm);
        notificationManager.ClearMessages();

        SelectTab(view, 0);
        var resetButton = OpenFilterPanelAndFindResetButton(
            view,
            "AllTasksFiltersButton",
            "AllTasksResetFiltersButton");
        result.FilterPanelOpened = true;

        await ClickControlAsync(window, resetButton);
        Dispatcher.UIThread.RunJobs();

        result.ConfirmationAsked = notificationManager.AskCount == 1;
        result.SearchReset = vm.Search.SearchText == string.Empty;
        result.EmojiFiltersReset = ToggleFiltersReset(vm.EmojiFilters) &&
                                   ToggleFiltersReset(vm.EmojiExcludeFilters);
        result.StatusFiltersReset = CompletionVisibilityMatchesDefaults(
            vm,
            defaultShowCompleted,
            defaultShowArchived);
    }

    private static async Task ExecuteLastCreatedDateResetAsync(
        Window window,
        MainControl view,
        MainWindowViewModel vm,
        NotificationManagerWrapperMock notificationManager,
        bool defaultShowCompleted,
        bool defaultShowArchived,
        FilterResetScenarioResult result)
    {
        SetActiveFilters(vm);
        notificationManager.ClearMessages();

        SelectTab(view, 1);
        var resetButton = OpenFilterPanelAndFindResetButton(
            view,
            "LastCreatedFiltersButton",
            "LastCreatedResetFiltersButton");

        await ClickControlAsync(window, resetButton);
        Dispatcher.UIThread.RunJobs();

        result.ConfirmationAsked &= notificationManager.AskCount == 1;
        result.StatusFiltersReset &= CompletionVisibilityMatchesDefaults(
            vm,
            defaultShowCompleted,
            defaultShowArchived);
        result.DateFilterReset = DateFilterIsDefault(vm.LastCreatedDateFilter) &&
                                 DateFilterRemainsCustom(vm.CompletedDateFilter) &&
                                 DateFilterRemainsCustom(vm.ArchivedDateFilter) &&
                                 DateFilterRemainsCustom(vm.LastUpdatedDateFilter);
    }

    private static async Task ExecuteUnlockedResetAsync(
        Window window,
        MainControl view,
        MainWindowViewModel vm,
        NotificationManagerWrapperMock notificationManager,
        bool defaultShowCompleted,
        bool defaultShowArchived,
        bool? defaultShowWanted,
        FilterResetScenarioResult result)
    {
        SetActiveFilters(vm);
        notificationManager.ClearMessages();

        SelectTab(view, 3);
        var resetButton = OpenFilterPanelAndFindResetButton(
            view,
            "UnlockedFiltersButton",
            "UnlockedResetFiltersButton");

        await ClickControlAsync(window, resetButton);
        Dispatcher.UIThread.RunJobs();

        result.ConfirmationAsked &= notificationManager.AskCount == 1;
        result.StatusFiltersReset &= CompletionVisibilityMatchesDefaults(
            vm,
            defaultShowCompleted,
            defaultShowArchived);
        result.DurationFiltersReset = ToggleFiltersReset(vm.DurationFilters) &&
                                      ToggleFiltersReset(vm.UnlockedTimeFilters);
        result.WantedFilterReset = vm.ShowWanted == defaultShowWanted;
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

    private static bool CompletionVisibilityMatchesDefaults(
        MainWindowViewModel vm,
        bool defaultShowCompleted,
        bool defaultShowArchived)
    {
        return StatusFilterSelected(vm, DomainTaskStatus.NotReady) &&
               StatusFilterSelected(vm, DomainTaskStatus.Prepared) &&
               StatusFilterSelected(vm, DomainTaskStatus.InProgress) &&
               StatusFilterSelected(vm, DomainTaskStatus.Completed) == defaultShowCompleted &&
               StatusFilterSelected(vm, DomainTaskStatus.Archived) == defaultShowArchived &&
               vm.ShowCompleted == defaultShowCompleted &&
               vm.ShowArchived == defaultShowArchived;
    }

    private static bool StatusFilterSelected(MainWindowViewModel vm, DomainTaskStatus status)
    {
        return vm.StatusFilters.Single(filter => filter.Status == status).ShowTasks;
    }

    private static bool ToggleFiltersReset(IEnumerable<EmojiFilter> filters)
    {
        return filters.All(static filter => !filter.ShowTasks);
    }

    private static bool ToggleFiltersReset(IEnumerable<UnlockedTimeFilter> filters)
    {
        return filters.All(static filter => !filter.ShowTasks);
    }

    private static bool ToggleFiltersReset(IEnumerable<DurationFilter> filters)
    {
        return filters.All(static filter => !filter.ShowTasks);
    }

    private static bool DateFilterIsDefault(DateFilter filter)
    {
        return !filter.IsCustom &&
               filter.CurrentOption.Id == DateFilterDefinition.Today.Id &&
               filter.From == DateTime.Today &&
               filter.To == DateTime.Today;
    }

    private static bool DateFilterRemainsCustom(DateFilter filter)
    {
        return filter.IsCustom &&
               filter.CurrentOption.Id == DateFilterDefinition.AllTime.Id &&
               filter.From == DateTime.Today.AddDays(-7) &&
               filter.To == DateTime.Today.AddDays(-1);
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
            throw new InvalidOperationException(
                $"Filter button '{filtersButtonAutomationId}' flyout content was not found.");
        }

        return FindControlInDetachedContent<Button>(flyoutContent, resetButtonAutomationId)
               ?? throw new InvalidOperationException(
                   $"Reset button '{resetButtonAutomationId}' was not found in the filter flyout.");
    }

    private static T FindControlByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        return root.GetVisualDescendants()
                   .OfType<T>()
                   .FirstOrDefault(candidate =>
                       string.Equals(
                           AutomationProperties.GetAutomationId(candidate),
                           automationId,
                           StringComparison.Ordinal))
               ?? throw new InvalidOperationException($"Control with AutomationId '{automationId}' was not found.");
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

    private static void SelectTab(MainControl view, int index)
    {
        var tabControl = view.GetVisualDescendants().OfType<TabControl>().First();
        tabControl.SelectedIndex = index;
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task ClickControlAsync(Window window, Control control)
    {
        var point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            window);
        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate point for control {control.GetType().Name}.");
        }

        window.MouseDown(point.Value, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point.Value, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        await Task.CompletedTask;
    }

    private static async Task DrainUiThrottlesAsync()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(SearchDefinition.DefaultThrottleMs + 100));
        Dispatcher.UIThread.RunJobs();
    }
}

internal sealed class FilterResetScenarioResult
{
    public bool MainControlOpened { get; set; }

    public bool FilterPanelOpened { get; set; }

    public bool ConfirmationAsked { get; set; }

    public bool SearchReset { get; set; }

    public bool StatusFiltersReset { get; set; }

    public bool DateFilterReset { get; set; }

    public bool DurationFiltersReset { get; set; }

    public bool WantedFilterReset { get; set; }

    public bool EmojiFiltersReset { get; set; }
}