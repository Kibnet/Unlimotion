using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.Views;
using Unlimotion.Views.SearchControl;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class MainControlFilterToolbarResponsiveUiTests
{
    private static readonly (int TabIndex, string ResetButtonAutomationId, string? FiltersButtonAutomationId, string? FilterPanelAutomationId, bool ExpectsSortControl)[] TaskTabs =
    [
        (0, "AllTasksResetFiltersButton", "AllTasksFiltersButton", "AllTasksFilterPanel", false),
        (1, "LastCreatedResetFiltersButton", "LastCreatedFiltersButton", "LastCreatedFilterPanel", false),
        (2, "LastUpdatedResetFiltersButton", "LastUpdatedFiltersButton", "LastUpdatedFilterPanel", false),
        (3, "UnlockedResetFiltersButton", "UnlockedFiltersButton", "UnlockedFilterPanel", false),
        (4, "CompletedResetFiltersButton", "CompletedFiltersButton", "CompletedFilterPanel", false),
        (5, "ArchivedResetFiltersButton", "ArchivedFiltersButton", "ArchivedFilterPanel", false),
        (6, "LastOpenedResetFiltersButton", null, null, false)
    ];

    [Test]
    public async Task MainControlFilterToolbar_NarrowViewport_UsesCompactPrimaryActions()
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
                vm.DetailsAreOpen = false;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view, 390, 760);
                window.Show();
                RunLayoutJobs();

                foreach (var tab in TaskTabs)
                {
                    SelectTab(view, tab.TabIndex);

                    var toolbar = FindVisibleFilterToolbar(view);
                    var searchBar = FindVisibleToolbarChild<SearchBar>(toolbar);
                    var primaryActions = FindVisibleToolbarChild<WrapPanel>(toolbar);
                    var resetButton = FindVisibleControlByAutomationId<Button>(view, tab.ResetButtonAutomationId);

                    await AssertCompactPrimaryActions(primaryActions, tab.ExpectsSortControl);
                    await AssertPrimaryActionsUseSingleLine(primaryActions);
                    await AssertSearchPrecedesActionsInLogicalOrder(toolbar, searchBar, primaryActions);
                    await AssertSearchAndActionsShareToolbarRow(toolbar, searchBar, primaryActions);
                    await AssertResetButtonMatchesSearchSize(resetButton, searchBar);
                    await AssertAutomationName(resetButton, "ResetFilters");

                    if (tab.FiltersButtonAutomationId != null)
                    {
                        var filtersButton = FindVisibleControlByAutomationId<DropDownButton>(view, tab.FiltersButtonAutomationId);
                        AssertFilterFlyoutPanel(filtersButton, tab.FilterPanelAutomationId!);
                        await AssertFilterButtonMatchesSearchHeight(filtersButton, searchBar);
                        await AssertAutomationName(filtersButton, "Filters");
                    }
                }
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapFilterToolbar_NarrowViewport_UsesCompactPrimaryActions()
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
                vm.DetailsAreOpen = false;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view, 390, 760);
                window.Show();
                RunLayoutJobs();

                SelectTab(view, 7);

                var toolbar = FindVisibleRoadmapFilterToolbar(view);
                var searchBar = FindVisibleToolbarChild<SearchBar>(toolbar);
                var primaryActions = FindVisibleToolbarChild<WrapPanel>(toolbar);
                var filtersButton = FindVisibleControlByAutomationId<DropDownButton>(view, "RoadmapFiltersButton");
                var resetButton = FindVisibleControlByAutomationId<Button>(view, "RoadmapResetFiltersButton");

                await AssertCompactPrimaryActions(primaryActions, expectsSortControl: false);
                await AssertPrimaryActionsUseSingleLine(primaryActions);
                await AssertSearchPrecedesActionsInLogicalOrder(toolbar, searchBar, primaryActions);
                AssertFilterFlyoutPanel(filtersButton, "RoadmapFilterPanel");
                await AssertSearchAndActionsShareToolbarRow(toolbar, searchBar, primaryActions);
                await AssertFilterButtonMatchesSearchHeight(filtersButton, searchBar);
                await AssertResetButtonMatchesSearchSize(resetButton, searchBar);
                await AssertAutomationName(filtersButton, "Filters");
                await AssertAutomationName(resetButton, "ResetFilters");
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task MainControlFilterToolbar_WideViewport_KeepsSearchBesideFilters()
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
                vm.DetailsAreOpen = false;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view, 1000, 760);
                window.Show();
                RunLayoutJobs();

                var toolbar = FindVisibleFilterToolbar(view);
                var searchBar = FindVisibleToolbarChild<SearchBar>(toolbar);
                var primaryActions = FindVisibleToolbarChild<WrapPanel>(toolbar);

                await AssertCompactPrimaryActions(primaryActions, expectsSortControl: false);
                await AssertPrimaryActionsUseSingleLine(primaryActions);
                await AssertSearchPrecedesActionsInLogicalOrder(toolbar, searchBar, primaryActions);
                await AssertSearchAndActionsShareToolbarRow(toolbar, searchBar, primaryActions);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task MainControlFilterToolbar_DetailsPaneShrink_ReflowsToNarrowLayout()
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
                vm.DetailsAreOpen = false;
                TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RootTask2Id);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view, 760, 760);
                window.Show();
                RunLayoutJobs();

                var wideToolbar = FindVisibleFilterToolbar(view);
                var wideSearchBar = FindVisibleToolbarChild<SearchBar>(wideToolbar);
                var widePrimaryActions = FindVisibleToolbarChild<WrapPanel>(wideToolbar);

                await AssertCompactPrimaryActions(widePrimaryActions, expectsSortControl: false);
                await AssertPrimaryActionsUseSingleLine(widePrimaryActions);
                await AssertSearchPrecedesActionsInLogicalOrder(wideToolbar, wideSearchBar, widePrimaryActions);
                await AssertSearchAndActionsShareToolbarRow(wideToolbar, wideSearchBar, widePrimaryActions);

                vm.DetailsAreOpen = true;
                RunLayoutJobs();

                var narrowToolbar = FindVisibleFilterToolbar(view);
                var narrowSearchBar = FindVisibleToolbarChild<SearchBar>(narrowToolbar);
                var narrowPrimaryActions = FindVisibleToolbarChild<WrapPanel>(narrowToolbar);

                await AssertCompactPrimaryActions(narrowPrimaryActions, expectsSortControl: false);
                await AssertPrimaryActionsUseSingleLine(narrowPrimaryActions);
                await Assert.That(narrowToolbar.Bounds.Width).IsLessThanOrEqualTo(520);
                await AssertSearchPrecedesActionsInLogicalOrder(narrowToolbar, narrowSearchBar, narrowPrimaryActions);
                await AssertSearchAndActionsShareToolbarRow(narrowToolbar, narrowSearchBar, narrowPrimaryActions);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    private static Window CreateWindow(Control content, double width, double height)
    {
        return new Window
        {
            Width = width,
            Height = height,
            Content = content
        };
    }

    private static void RunLayoutJobs()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static Grid FindVisibleFilterToolbar(MainControl view)
    {
        return view.GetVisualDescendants()
            .OfType<Grid>()
            .First(control => control.Classes.Contains("FilterToolbar") && IsVisibleAndArranged(control));
    }

    private static Grid FindVisibleRoadmapFilterToolbar(MainControl view)
    {
        return view.GetVisualDescendants()
            .OfType<Grid>()
            .First(control => control.Classes.Contains("RoadmapFilterToolbar") && IsVisibleAndArranged(control));
    }

    private static T FindVisibleToolbarChild<T>(Grid toolbar)
        where T : Control
    {
        return toolbar.GetVisualDescendants()
            .OfType<T>()
            .First(IsVisibleAndArranged);
    }

    private static void SelectTab(MainControl view, int index)
    {
        var tabControl = view.GetVisualDescendants().OfType<TabControl>().First();
        tabControl.SelectedIndex = index;
        RunLayoutJobs();
    }

    private static T FindVisibleControlByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        return root.GetVisualDescendants()
            .OfType<T>()
            .First(control =>
                AutomationProperties.GetAutomationId(control) == automationId &&
                IsVisibleAndArranged(control));
    }

    private static async Task AssertCompactPrimaryActions(WrapPanel primaryActions, bool expectsSortControl)
    {
        var visibleComboBoxCount = primaryActions.GetVisualDescendants()
            .OfType<ComboBox>()
            .Count(IsVisibleAndArranged);
        var hasVisibleCheckBox = primaryActions.GetVisualDescendants()
            .OfType<CheckBox>()
            .Any(IsVisibleAndArranged);

        await Assert.That(visibleComboBoxCount).IsLessThanOrEqualTo(expectsSortControl ? 1 : 0);
        await Assert.That(hasVisibleCheckBox).IsFalse();
    }

    private static async Task AssertPrimaryActionsUseSingleLine(WrapPanel primaryActions)
    {
        var visibleChildTops = primaryActions.Children
            .OfType<Control>()
            .Where(IsVisibleAndArranged)
            .Select(child => GetBoundsRelativeTo(primaryActions, child).Top)
            .ToArray();

        if (visibleChildTops.Length < 2)
        {
            return;
        }

        await Assert.That(visibleChildTops.Max() - visibleChildTops.Min()).IsLessThanOrEqualTo(8);
    }

    private static async Task AssertSearchAndActionsShareToolbarRow(Grid toolbar, SearchBar searchBar, WrapPanel primaryActions)
    {
        var searchBounds = GetBoundsRelativeTo(toolbar, searchBar);
        var primaryBounds = GetBoundsRelativeTo(toolbar, primaryActions);

        await Assert.That(searchBounds.Right).IsLessThanOrEqualTo(primaryBounds.Left + 1);
        await Assert.That(searchBounds.Width).IsGreaterThan(0);
        await Assert.That(searchBounds.Right).IsLessThanOrEqualTo(toolbar.Bounds.Width + 1);
        await Assert.That(primaryBounds.Height).IsLessThanOrEqualTo(searchBounds.Height + 2);
        await Assert.That(Math.Abs(GetCenterY(searchBounds) - GetCenterY(primaryBounds))).IsLessThanOrEqualTo(2);
        await Assert.That(primaryBounds.Right).IsLessThanOrEqualTo(toolbar.Bounds.Width + 1);
    }

    private static async Task AssertSearchPrecedesActionsInLogicalOrder(
        Grid toolbar,
        SearchBar searchBar,
        WrapPanel primaryActions)
    {
        var children = toolbar.Children.ToArray();
        await Assert.That(Array.IndexOf(children, searchBar)).IsGreaterThanOrEqualTo(0);
        await Assert.That(Array.IndexOf(children, primaryActions)).IsGreaterThan(Array.IndexOf(children, searchBar));
    }

    private static async Task AssertResetButtonMatchesSearchSize(Button button, SearchBar searchBar)
    {
        await Assert.That(button.Bounds.Width).IsGreaterThan(0);
        await Assert.That(button.Bounds.Width).IsLessThanOrEqualTo(searchBar.Bounds.Height + 2);
        await Assert.That(button.Bounds.Width).IsGreaterThanOrEqualTo(searchBar.Bounds.Height - 2);
        await Assert.That(Math.Abs(button.Bounds.Height - searchBar.Bounds.Height)).IsLessThanOrEqualTo(2);
        await Assert.That(button.Content).IsAssignableTo<PathIcon>();
    }

    private static async Task AssertFilterButtonMatchesSearchHeight(DropDownButton button, SearchBar searchBar)
    {
        await Assert.That(button.Bounds.Width).IsGreaterThan(searchBar.Bounds.Height);
        await Assert.That(button.Bounds.Width).IsLessThanOrEqualTo(searchBar.Bounds.Height + 16);
        await Assert.That(Math.Abs(button.Bounds.Height - searchBar.Bounds.Height)).IsLessThanOrEqualTo(2);
        await Assert.That(button.Content).IsAssignableTo<PathIcon>();
        var icon = (PathIcon)button.Content!;
        await Assert.That(icon.Margin.Left).IsGreaterThanOrEqualTo(2);
        await Assert.That(icon.Margin.Right).IsGreaterThanOrEqualTo(2);
    }

    private static async Task AssertAutomationName(Control control, string resourceKey)
    {
        await Assert.That(AutomationProperties.GetName(control)).IsEqualTo(L10n.Get(resourceKey));
    }

    private static void AssertFilterFlyoutPanel(DropDownButton filtersButton, string filterPanelAutomationId)
    {
        if (filtersButton.Flyout is not Flyout flyout)
        {
            throw new InvalidOperationException("Filter button must use a Flyout.");
        }

        if (flyout.Placement != PlacementMode.BottomEdgeAlignedRight)
        {
            throw new InvalidOperationException("Filter flyout must stay aligned to the right edge on narrow screens.");
        }

        if (flyout.Content is not Control flyoutContent)
        {
            throw new InvalidOperationException("Filter flyout content was not found.");
        }

        var panel = FindControlInDetachedContent<Control>(flyoutContent, filterPanelAutomationId);
        if (panel == null)
        {
            throw new InvalidOperationException($"Filter panel '{filterPanelAutomationId}' was not found.");
        }
    }

    private static T? FindControlInDetachedContent<T>(Control root, string automationId)
        where T : Control
    {
        if (root is T typedRoot && AutomationProperties.GetAutomationId(root) == automationId)
        {
            return typedRoot;
        }

        return root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId);
    }

    private static bool IsVisibleAndArranged(Control control)
    {
        return control.IsVisible &&
               control.Bounds.Width > 0 &&
               control.Bounds.Height > 0;
    }

    private static Rect GetBoundsRelativeTo(Visual relativeTo, Control control)
    {
        var topLeft = control.TranslatePoint(new Point(0, 0), relativeTo);
        if (!topLeft.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate point for control {control.GetType().Name}.");
        }

        return new Rect(topLeft.Value, control.Bounds.Size);
    }

    private static double GetCenterY(Rect bounds)
    {
        return bounds.Top + bounds.Height / 2d;
    }
}
