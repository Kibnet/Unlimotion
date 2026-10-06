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
using Unlimotion.ViewModel.Workspace;
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

        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
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

                var view = new MainScreen { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                result.WorkspaceOpened = true;
                await Assert.That(view.GetVisualDescendants().OfType<MainControl>().Any()).IsFalse();

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
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);

        return result;
    }

    public static async Task AssertFilterResetScenarioResultAsync(FilterResetScenarioResult result)
    {
        await Assert.That(result.WorkspaceOpened).IsTrue();
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
        MainScreen view,
        MainWindowViewModel vm,
        NotificationManagerWrapperMock notificationManager,
        bool defaultShowCompleted,
        bool defaultShowArchived,
        FilterResetScenarioResult result)
    {
        var document = await OpenDocumentAsync(window, view, vm, TaskListKind.AllTasks);
        SetActiveFilters(vm, vm.StatusFilters, TaskListKind.AllTasks);
        notificationManager.ClearMessages();

        var resetButton = await OpenFilterPanelAndFindResetButtonAsync(
            window, document,
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
            vm.StatusFilters,
            defaultShowCompleted,
            defaultShowArchived) &&
            vm.ShowCompleted == defaultShowCompleted &&
            vm.ShowArchived == defaultShowArchived;
        HideFilterFlyout(document, "AllTasksFiltersButton");
    }

    private static async Task ExecuteLastCreatedDateResetAsync(
        Window window,
        MainScreen view,
        MainWindowViewModel vm,
        NotificationManagerWrapperMock notificationManager,
        bool defaultShowCompleted,
        bool defaultShowArchived,
        FilterResetScenarioResult result)
    {
        var document = await OpenDocumentAsync(window, view, vm, TaskListKind.LastCreated);
        SetActiveFilters(vm, vm.LastCreatedStatusFilters, TaskListKind.LastCreated);
        notificationManager.ClearMessages();

        var resetButton = await OpenFilterPanelAndFindResetButtonAsync(
            window, document,
            "LastCreatedFiltersButton",
            "LastCreatedResetFiltersButton");

        await ClickControlAsync(window, resetButton);
        Dispatcher.UIThread.RunJobs();

        result.ConfirmationAsked &= notificationManager.AskCount == 1;
        result.StatusFiltersReset &= CompletionVisibilityMatchesDefaults(
            vm.LastCreatedStatusFilters,
            defaultShowCompleted,
            defaultShowArchived);
        result.DateFilterReset = DateFilterIsDefault(vm.LastCreatedDateFilter) &&
                                 DateFilterRemainsCustom(vm.CompletedDateFilter) &&
                                 DateFilterRemainsCustom(vm.ArchivedDateFilter) &&
                                 DateFilterRemainsCustom(vm.LastUpdatedDateFilter);
        result.SearchReset &= vm.LastCreatedFilter.Search.SearchText == string.Empty;
        result.EmojiFiltersReset &= ToggleFiltersReset(vm.LastCreatedFilter.EmojiFilters) &&
                                   ToggleFiltersReset(vm.LastCreatedFilter.EmojiExcludeFilters);
        HideFilterFlyout(document, "LastCreatedFiltersButton");
    }

    private static async Task ExecuteUnlockedResetAsync(
        Window window,
        MainScreen view,
        MainWindowViewModel vm,
        NotificationManagerWrapperMock notificationManager,
        bool defaultShowCompleted,
        bool defaultShowArchived,
        bool? defaultShowWanted,
        FilterResetScenarioResult result)
    {
        var document = await OpenDocumentAsync(window, view, vm, TaskListKind.Unlocked);
        SetActiveFilters(vm, vm.UnlockedStatusFilters, TaskListKind.Unlocked);
        notificationManager.ClearMessages();

        var resetButton = await OpenFilterPanelAndFindResetButtonAsync(
            window, document,
            "UnlockedFiltersButton",
            "UnlockedResetFiltersButton");

        await ClickControlAsync(window, resetButton);
        Dispatcher.UIThread.RunJobs();

        result.ConfirmationAsked &= notificationManager.AskCount == 1;
        result.StatusFiltersReset &= CompletionVisibilityMatchesDefaults(
            vm.UnlockedStatusFilters,
            defaultShowCompleted,
            defaultShowArchived);
        result.DurationFiltersReset = ToggleFiltersReset(vm.DurationFilters) &&
                                      ToggleFiltersReset(vm.UnlockedTimeFilters);
        result.WantedFilterReset = vm.ShowWanted == defaultShowWanted;
        result.SearchReset &= vm.UnlockedSearch.SearchText == string.Empty;
        result.EmojiFiltersReset &= ToggleFiltersReset(vm.UnlockedEmojiFilters) &&
                                   ToggleFiltersReset(vm.UnlockedEmojiExcludeFilters);
        HideFilterFlyout(document, "UnlockedFiltersButton");
    }

    private static void SetActiveFilters(
        MainWindowViewModel vm,
        IEnumerable<TaskStatusFilter> statusFilters,
        TaskListKind kind)
    {
        vm.Search.SearchText = "Task";
        vm.ShowCompleted = true;
        vm.ShowArchived = true;
        vm.ShowWanted = true;
        vm.Graph.OnlyUnlocked = true;

        foreach (var filter in statusFilters)
        {
            filter.ShowTasks = false;
        }

        SetFirstFilter(vm.EmojiFilters);
        SetFirstFilter(vm.EmojiExcludeFilters);
        if (kind == TaskListKind.LastCreated)
        {
            vm.LastCreatedFilter.Search.SearchText = "Created filter draft";
            SetFirstFilter(vm.LastCreatedFilter.EmojiFilters);
            SetFirstFilter(vm.LastCreatedFilter.EmojiExcludeFilters);
        }
        if (kind == TaskListKind.Unlocked)
        {
            vm.UnlockedSearch.SearchText = "Unlocked filter draft";
            SetFirstFilter(vm.UnlockedEmojiFilters);
            SetFirstFilter(vm.UnlockedEmojiExcludeFilters);
        }
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
        IEnumerable<TaskStatusFilter> statusFilters,
        bool defaultShowCompleted,
        bool defaultShowArchived)
    {
        return StatusFilterSelected(statusFilters, DomainTaskStatus.NotReady) &&
               StatusFilterSelected(statusFilters, DomainTaskStatus.Prepared) &&
               StatusFilterSelected(statusFilters, DomainTaskStatus.InProgress) &&
               StatusFilterSelected(statusFilters, DomainTaskStatus.Completed) == defaultShowCompleted &&
               StatusFilterSelected(statusFilters, DomainTaskStatus.Archived) == defaultShowArchived;
    }

    private static bool StatusFilterSelected(
        IEnumerable<TaskStatusFilter> statusFilters,
        DomainTaskStatus status)
    {
        return statusFilters.Single(filter => filter.Status == status).ShowTasks;
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

    private static async Task<Button> OpenFilterPanelAndFindResetButtonAsync(
        Window window,
        TaskListDocumentView view,
        string filtersButtonAutomationId,
        string resetButtonAutomationId)
    {
        var filtersButton = FindControlByAutomationId<DropDownButton>(view, filtersButtonAutomationId);
        if (filtersButton.Flyout is not Flyout flyout)
        {
            throw new InvalidOperationException($"Filter button '{filtersButtonAutomationId}' must use a Flyout.");
        }

        await ClickControlAsync(window, filtersButton);
        await WaitForUiAsync(() => flyout.IsOpen);

        if (flyout.Content is not Control flyoutContent)
        {
            throw new InvalidOperationException(
                $"Filter button '{filtersButtonAutomationId}' flyout content was not found.");
        }

        var reset = FindControlInDetachedContent<Button>(flyoutContent, resetButtonAutomationId)
               ?? throw new InvalidOperationException(
                   $"Reset button '{resetButtonAutomationId}' was not found in the filter flyout.");
        await WaitForUiAsync(() => reset.IsEffectivelyVisible && reset.Bounds.Width > 0 && reset.Bounds.Height > 0);
        return reset;
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

    private static async Task<TaskListDocumentView> OpenDocumentAsync(
        Window window, MainScreen view, MainWindowViewModel vm, TaskListKind kind)
    {
        await Assert.That(await vm.OpenWorkspaceLocationAsync(WorkspaceLocation.ForTaskList(kind),
            WorkspaceOpenDisposition.NewTab)).IsTrue();
        await WaitForUiAsync(() => view.GetVisualDescendants().OfType<TaskListDocumentView>()
            .Any(document => document.Kind == kind && document.IsEffectivelyVisible));
        window.UpdateLayout();
        return view.GetVisualDescendants().OfType<TaskListDocumentView>()
            .Single(document => document.Kind == kind && document.IsEffectivelyVisible);
    }

    private static void HideFilterFlyout(TaskListDocumentView document, string id) =>
        FindControlByAutomationId<DropDownButton>(document, id).Flyout?.Hide();

    private static async Task WaitForUiAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return;
            await Task.Delay(20);
        }
        throw new TimeoutException("The independent filter document did not reach the expected UI state.");
    }

    private static async Task ClickControlAsync(Window window, Control control)
    {
        control.BringIntoView();
        await Task.Delay(20);
        Dispatcher.UIThread.RunJobs();
        // Flyout buttons can belong to a separate PopupRoot. Injecting their
        // coordinates into the main Window misses the physical button.
        var inputRoot = TopLevel.GetTopLevel(control) ?? window;
        inputRoot.UpdateLayout();
        var point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            inputRoot);
        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate point for control {control.GetType().Name}.");
        }

        inputRoot.MouseDown(point.Value, MouseButton.Left, RawInputModifiers.None);
        inputRoot.MouseUp(point.Value, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(20);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task DrainUiThrottlesAsync()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(SearchDefinition.DefaultThrottleMs + 100));
        Dispatcher.UIThread.RunJobs();
    }
}

internal sealed class FilterResetScenarioResult
{
    public bool WorkspaceOpened { get; set; }

    public bool FilterPanelOpened { get; set; }

    public bool ConfirmationAsked { get; set; }

    public bool SearchReset { get; set; }

    public bool StatusFiltersReset { get; set; }

    public bool DateFilterReset { get; set; }

    public bool DurationFiltersReset { get; set; }

    public bool WantedFilterReset { get; set; }

    public bool EmojiFiltersReset { get; set; }
}
