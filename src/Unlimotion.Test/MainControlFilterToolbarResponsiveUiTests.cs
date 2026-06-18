using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel;
using Unlimotion.Views;
using Unlimotion.Views.SearchControl;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class MainControlFilterToolbarResponsiveUiTests
{
    private static readonly (int TabIndex, string ResetButtonAutomationId, string FiltersButtonAutomationId, string FilterPanelAutomationId, bool ExpectsSortControl, bool ExpectsEmojiFilters)[] TaskTabs =
    [
        (0, "AllTasksResetFiltersButton", "AllTasksFiltersButton", "AllTasksFilterPanel", false, true),
        (1, "LastCreatedResetFiltersButton", "LastCreatedFiltersButton", "LastCreatedFilterPanel", false, true),
        (2, "LastUpdatedResetFiltersButton", "LastUpdatedFiltersButton", "LastUpdatedFilterPanel", false, true),
        (3, "UnlockedResetFiltersButton", "UnlockedFiltersButton", "UnlockedFilterPanel", false, true),
        (4, "InProgressResetFiltersButton", "InProgressFiltersButton", "InProgressFilterPanel", false, true),
        (5, "CompletedResetFiltersButton", "CompletedFiltersButton", "CompletedFilterPanel", false, true),
        (6, "ArchivedResetFiltersButton", "ArchivedFiltersButton", "ArchivedFilterPanel", false, true),
        (7, "LastOpenedResetFiltersButton", "LastOpenedFiltersButton", "LastOpenedFilterPanel", false, false)
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
                    var filtersButton = FindVisibleControlByAutomationId<DropDownButton>(view, tab.FiltersButtonAutomationId);

                    await AssertCompactPrimaryActions(primaryActions, tab.ExpectsSortControl);
                    await AssertPrimaryActionsUseSingleLine(primaryActions);
                    await AssertActionsPrecedeSearchInLogicalOrder(toolbar, searchBar, primaryActions);
                    await AssertEmojiFilterToolbarPlacement(primaryActions, filtersButton, tab.ExpectsEmojiFilters);
                    await AssertSearchAndActionsShareToolbarRow(toolbar, searchBar, primaryActions);
                    await AssertNestedSearchControlShrinksWithToolbar(toolbar, searchBar);
                    AssertFilterFlyoutPanel(filtersButton, tab.FilterPanelAutomationId, tab.ResetButtonAutomationId);
                    await AssertFilterButtonMatchesSearchHeight(filtersButton, searchBar);
                    await AssertAutomationName(filtersButton, "Filters");
                    await AssertResetButtonIsInsideFlyout(view, filtersButton, tab.ResetButtonAutomationId);
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
    public async Task MainControlFilterToolbar_NarrowWindow_KeepsEmojiSummariesInsideWindow()
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
                await PrepareEmojiFilterData(vm);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view, 500, 760);
                window.Show();
                RunLayoutJobs();
                SelectTab(view, 0);

                foreach (var filter in vm.EmojiFilters
                             .Where(static filter => !string.IsNullOrWhiteSpace(filter.Emoji))
                             .Take(6))
                {
                    filter.ShowTasks = true;
                }

                var toolbar = FindVisibleFilterToolbar(view);
                var searchBar = FindVisibleToolbarChild<SearchBar>(toolbar);
                var (includeControl, excludeControl) = FindVisibleToolbarEmojiFilterControls(toolbar);
                var filtersButton = FindVisibleControlByAutomationId<DropDownButton>(view, "AllTasksFiltersButton");

                await Assert.That(GetEmojiFilterInput(includeControl).Text).Contains("+");
                await Assert.That(GetEmojiFilterInput(includeControl).Bounds.Width).IsLessThanOrEqualTo(includeControl.SummaryWidth + 1);
                await Assert.That(GetEmojiFilterInput(excludeControl).Bounds.Width).IsLessThanOrEqualTo(excludeControl.SummaryWidth + 1);
                await AssertControlStaysInsideWindow(window, filtersButton);
                await AssertControlStaysInsideWindow(window, searchBar);

                foreach (var width in new[] { 508d, 516d, 508d, 500d })
                {
                    window.Width = width;
                    RunLayoutJobs();

                    toolbar = FindVisibleFilterToolbar(view);
                    searchBar = FindVisibleToolbarChild<SearchBar>(toolbar);
                    (includeControl, excludeControl) = FindVisibleToolbarEmojiFilterControls(toolbar);
                    filtersButton = FindVisibleControlByAutomationId<DropDownButton>(view, "AllTasksFiltersButton");

                    await Assert.That(GetEmojiFilterInput(includeControl).Bounds.Width).IsLessThanOrEqualTo(includeControl.SummaryWidth + 1);
                    await Assert.That(GetEmojiFilterInput(excludeControl).Bounds.Width).IsLessThanOrEqualTo(excludeControl.SummaryWidth + 1);
                    await AssertControlStaysInsideWindow(window, filtersButton);
                    await AssertControlStaysInsideWindow(window, searchBar);
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

                SelectTab(view, 8);

                var toolbar = FindVisibleRoadmapFilterToolbar(view);
                var searchBar = FindVisibleToolbarChild<SearchBar>(toolbar);
                var primaryActions = FindVisibleToolbarChild<WrapPanel>(toolbar);
                var filtersButton = FindVisibleControlByAutomationId<DropDownButton>(view, "RoadmapFiltersButton");

                await AssertCompactPrimaryActions(primaryActions, expectsSortControl: false);
                await AssertPrimaryActionsUseSingleLine(primaryActions);
                await AssertActionsPrecedeSearchInLogicalOrder(toolbar, searchBar, primaryActions);
                await AssertEmojiFilterToolbarPlacement(primaryActions, filtersButton, expectsEmojiFilters: true);
                AssertFilterFlyoutPanel(filtersButton, "RoadmapFilterPanel", "RoadmapResetFiltersButton");
                await AssertSearchAndActionsShareToolbarRow(toolbar, searchBar, primaryActions);
                await AssertNestedSearchControlShrinksWithToolbar(toolbar, searchBar);
                await AssertFilterButtonMatchesSearchHeight(filtersButton, searchBar);
                await AssertAutomationName(filtersButton, "Filters");
                await AssertResetButtonIsInsideFlyout(view, filtersButton, "RoadmapResetFiltersButton");
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapFilterToolbar_StandaloneNarrowViewport_ShrinksNestedSearchControl()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            Window? window = null;

            try
            {
                var view = new GraphControl
                {
                    DataContext = new GraphViewModel()
                };
                window = CreateWindow(view, 320, 360);
                window.Show();
                RunLayoutJobs();

                var toolbar = FindVisibleRoadmapFilterToolbar(view);
                var searchBar = FindVisibleToolbarChild<SearchBar>(toolbar);
                var primaryActions = FindVisibleToolbarChild<WrapPanel>(toolbar);

                await AssertSearchAndActionsShareToolbarRow(toolbar, searchBar, primaryActions);
                await AssertNestedSearchControlShrinksWithToolbar(toolbar, searchBar);
            }
            finally
            {
                window?.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Toolbar_EmojiFilters_SummaryChromeDoesNotCoverOrShiftTokens()
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
                await PrepareEmojiFilterData(vm);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view, 390, 760);
                window.Show();
                RunLayoutJobs();
                SelectTab(view, 0);

                var toolbar = FindVisibleFilterToolbar(view);
                var (includeControl, excludeControl) = FindVisibleToolbarEmojiFilterControls(toolbar);
                var includeInput = GetEmojiFilterInput(includeControl);
                var excludeInput = GetEmojiFilterInput(excludeControl);
                var excludeMarker = GetEmojiFilterExcludeMarker(excludeControl);

                await Assert.That(FindEmojiFilterDropDownGlyph(includeControl)).IsNull();
                await Assert.That(FindEmojiFilterDropDownGlyph(excludeControl)).IsNull();
                await Assert.That(GetEmojiFilterExcludeMarker(includeControl).IsVisible).IsFalse();
                await Assert.That(excludeMarker.IsVisible).IsTrue();
                await AssertExcludeMarkerDoesNotShiftInputText(includeInput, excludeInput, excludeMarker);

                vm.EmojiFilters.First(static filter => !string.IsNullOrWhiteSpace(filter.Emoji)).ShowTasks = true;
                vm.EmojiExcludeFilters.First(static filter => !string.IsNullOrWhiteSpace(filter.Emoji)).ShowTasks = true;
                RunLayoutJobs();

                await Assert.That(includeInput.Text).IsNotNull();
                await Assert.That(excludeInput.Text).IsNotNull();
                await Assert.That(includeInput.Padding.Left).IsEqualTo(excludeInput.Padding.Left);
                await Assert.That(includeInput.Padding.Right).IsEqualTo(excludeInput.Padding.Right);
                await AssertExcludeMarkerDoesNotShiftInputText(includeInput, excludeInput, excludeMarker);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Toolbar_EmojiFilters_OpenFullListThenSearchAndToggleWithoutClosing()
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
                await PrepareEmojiFilterData(vm);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view, 390, 760);
                window.Show();
                RunLayoutJobs();
                SelectTab(view, 0);

                var toolbar = FindVisibleFilterToolbar(view);
                var (includeControl, excludeControl) = FindVisibleToolbarEmojiFilterControls(toolbar);
                var includeInput = GetEmojiFilterInput(includeControl);
                var excludeInput = GetEmojiFilterInput(excludeControl);

                await Assert.That(GetEmojiFilterExcludeMarker(includeControl).IsVisible).IsFalse();
                await Assert.That(GetEmojiFilterExcludeMarker(excludeControl).IsVisible).IsTrue();
                await Assert.That(excludeInput.Classes.Contains("Exclude")).IsTrue();
                await Assert.That(includeInput.VerticalContentAlignment).IsEqualTo(VerticalAlignment.Center);
                await Assert.That(GetEmojiFilterPopup(includeControl).ShouldUseOverlayLayer).IsFalse();

                await ClickControlAsync(window, includeInput);
                RunLayoutJobs();

                var includeList = GetEmojiFilterList(includeControl);
                var includeListItems = GetEmojiFilterListItems(includeList);
                await Assert.That(includeListItems.Count).IsEqualTo(vm.EmojiFilters.Count);
                await Assert.That(includeListItems[0].Title).IsEqualTo("All");
                await Assert.That(includeListItems[0].Emoji).IsEqualTo(string.Empty);
                await AssertEmojiRowsMeasureContentAndCenterVertically(includeList);

                var inputBounds = GetBoundsRelativeTo(window, includeInput);
                var dropDown = GetEmojiFilterDropDown(includeControl);
                var dropDownBounds = GetBoundsRelativeTo(window, dropDown);
                await Assert.That(Math.Abs(dropDownBounds.Left - inputBounds.Left)).IsLessThanOrEqualTo(2);
                await Assert.That(Math.Abs(dropDownBounds.Top - inputBounds.Bottom)).IsLessThanOrEqualTo(2);
                await Assert.That(dropDown.CornerRadius).IsEqualTo(new CornerRadius(4));
                AssertVisibleItemsStayInsideDropDown(dropDown, includeList);

                await ClickControlAsync(window, includeInput);
                RunLayoutJobs();

                await Assert.That(AutomationProperties.GetAutomationId(includeInput)).IsEqualTo("IncludeEmojiFilterSearchBox");
                await Assert.That(includeInput.Text).IsEqualTo(string.Empty);
                await Assert.That(includeInput.PlaceholderText).IsEqualTo(L10n.Get("EmojiFilterSearchWatermark"));

                var libraryFilter = vm.EmojiFilters.First(filter =>
                    filter.SearchText.Contains("library", StringComparison.OrdinalIgnoreCase));
                TypeText(window, "library");
                RunLayoutJobs();

                await Assert.That(includeInput.Text).IsEqualTo("library");
                var filteredItems = GetEmojiFilterListItems(includeList);
                await Assert.That(filteredItems.Count).IsEqualTo(1);
                await Assert.That(filteredItems[0].SearchText).IsEqualTo(libraryFilter.SearchText);
                await Assert.That(GetVisibleEmojiItemTexts(includeList).Any(text => text == libraryFilter.Emoji)).IsTrue();
                await Assert.That(GetVisibleEmojiItemTitleTexts(includeList)).IsEquivalentTo([libraryFilter.DisplayTitle]);
                await Assert.That(libraryFilter.ShowTasks).IsFalse();

                var libraryCheckBox = FindVisibleCheckBox(includeList);
                await ClickControlAsync(window, libraryCheckBox);
                RunLayoutJobs();

                await Assert.That(libraryFilter.ShowTasks).IsTrue();
                await Assert.That(IsEmojiDropDownOpen(includeControl)).IsTrue();

                var currentInputBounds = GetBoundsRelativeTo(window, includeInput);
                var sideDismissPoint = new Point(
                    Math.Min(window.Bounds.Width - 6, currentInputBounds.Right + 24),
                    GetCenterY(currentInputBounds));
                window.MouseDown(sideDismissPoint, MouseButton.Left);
                RunLayoutJobs();
                window.MouseUp(sideDismissPoint, MouseButton.Left);
                RunLayoutJobs();

                await Assert.That(IsEmojiDropDownOpen(includeControl)).IsFalse();

                await ClickControlAsync(window, excludeInput);
                RunLayoutJobs();

                await Assert.That(IsEmojiDropDownOpen(excludeControl)).IsTrue();
                var excludeList = GetEmojiFilterList(excludeControl);
                await Assert.That(GetEmojiFilterListItems(excludeList).Count).IsEqualTo(vm.EmojiExcludeFilters.Count);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Toolbar_EmojiFilters_AllItemTogglesEveryEmojiFilter()
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
                await PrepareEmojiFilterData(vm);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view, 390, 760);
                window.Show();
                RunLayoutJobs();
                SelectTab(view, 0);

                var toolbar = FindVisibleFilterToolbar(view);
                var (includeControl, _) = FindVisibleToolbarEmojiFilterControls(toolbar);
                await ClickControlAsync(window, GetEmojiFilterInput(includeControl));
                RunLayoutJobs();

                var includeList = GetEmojiFilterList(includeControl);
                var allFilter = GetEmojiFilterListItems(includeList)[0];
                await Assert.That(allFilter.Title).IsEqualTo("All");

                includeList.SelectedItem = allFilter;
                includeList.Focus();
                RunLayoutJobs();
                await Assert.That(includeList.SelectedItem).IsEqualTo(allFilter);

                PressKey(window, Key.Space, PhysicalKey.Space);
                RunLayoutJobs();

                await Assert.That(vm.EmojiFilters.Where(static filter => !string.IsNullOrWhiteSpace(filter.Emoji)).All(static filter => filter.ShowTasks)).IsTrue();

                includeList.SelectedItem = allFilter;
                includeList.Focus();
                RunLayoutJobs();

                PressKey(window, Key.Space, PhysicalKey.Space);
                RunLayoutJobs();

                await Assert.That(vm.EmojiFilters.All(static filter => !filter.ShowTasks)).IsTrue();
                await Assert.That(IsEmojiDropDownOpen(includeControl)).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Toolbar_EmojiFilters_SummaryShowsSelectedEmojiAndOverflowInListOrder()
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
                await PrepareEmojiFilterData(vm);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view, 390, 760);
                window.Show();
                RunLayoutJobs();
                SelectTab(view, 0);

                var toolbar = FindVisibleFilterToolbar(view);
                var (includeControl, _) = FindVisibleToolbarEmojiFilterControls(toolbar);
                var includeInput = GetEmojiFilterInput(includeControl);

                var selectedFilters = vm.EmojiFilters
                    .Where(static filter => !string.IsNullOrWhiteSpace(filter.Emoji))
                    .Take(6)
                    .ToArray();

                foreach (var filter in selectedFilters)
                {
                    filter.ShowTasks = true;
                }

                RunLayoutJobs();

                var summaryParts = includeInput.Text!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                await Assert.That(summaryParts[0]).IsEqualTo(selectedFilters[0].Emoji);
                await Assert.That(summaryParts[^1]).StartsWith("+");
                await Assert.That(summaryParts.Length).IsLessThan(selectedFilters.Length);
                await Assert.That(includeInput.Bounds.Width).IsLessThanOrEqualTo(includeControl.SummaryWidth + 1);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Toolbar_EmojiFilters_NoMatchesShowsWarningAndKeepsFullList()
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
                await PrepareEmojiFilterData(vm);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view, 390, 760);
                window.Show();
                RunLayoutJobs();
                SelectTab(view, 0);

                var toolbar = FindVisibleFilterToolbar(view);
                var (includeControl, _) = FindVisibleToolbarEmojiFilterControls(toolbar);
                var includeInput = GetEmojiFilterInput(includeControl);
                await ClickControlAsync(window, includeInput);
                await ClickControlAsync(window, includeInput);
                RunLayoutJobs();

                includeInput.Text = "zzzz-no-tag";
                RunLayoutJobs();

                var includeList = GetEmojiFilterList(includeControl);
                var noMatchesPanel = GetEmojiFilterNoMatchesPanel(includeControl);
                var noMatches = GetEmojiFilterNoMatches(includeControl);

                await Assert.That(noMatchesPanel.IsVisible).IsTrue();
                await Assert.That(noMatchesPanel.Background).IsNotNull();
                await Assert.That(noMatchesPanel.Background).IsAssignableTo<ISolidColorBrush>();
                await Assert.That(((ISolidColorBrush)noMatchesPanel.Background!).Color.A).IsEqualTo(byte.MaxValue);
                await Assert.That(noMatches.Text).IsEqualTo(L10n.Get("EmojiFilterNoMatches"));
                await Assert.That(includeList.IsVisible).IsTrue();
                await Assert.That(GetEmojiFilterListItems(includeList).Count).IsEqualTo(vm.EmojiFilters.Count);
                await Assert.That(vm.EmojiFilters.Any(static filter => filter.ShowTasks)).IsFalse();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Toolbar_EmojiFilters_RespondsToLargeFontResources()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                ApplyApplicationFontResources(24d);

                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();
                vm.AllTasksMode = true;
                vm.DetailsAreOpen = false;
                await PrepareEmojiFilterData(vm);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view, 390, 760);
                window.Show();
                RunLayoutJobs();
                SelectTab(view, 0);

                var toolbar = FindVisibleFilterToolbar(view);
                var (includeControl, _) = FindVisibleToolbarEmojiFilterControls(toolbar);
                var includeInput = GetEmojiFilterInput(includeControl);

                await ClickControlAsync(window, includeInput);
                RunLayoutJobs();

                var includeList = GetEmojiFilterList(includeControl);
                await Assert.That(includeInput.FontSize).IsEqualTo(24d);
                await Assert.That(includeList.FontSize).IsEqualTo(24d);
                await Assert.That(includeInput.Bounds.Height).IsGreaterThan(AppearanceSettings.DefaultSearchControlHeight);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
                ApplyApplicationFontResources(AppearanceSettings.DefaultFontSize);
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Toolbar_EmojiFilters_KeyboardFlowOpensSearchTogglesAndClosesPopup()
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
                await PrepareEmojiFilterData(vm);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view, 390, 760);
                window.Show();
                RunLayoutJobs();
                SelectTab(view, 0);

                var toolbar = FindVisibleFilterToolbar(view);
                var (includeControl, _) = FindVisibleToolbarEmojiFilterControls(toolbar);
                var includeInput = GetEmojiFilterInput(includeControl);
                includeInput.Focus();
                PressKey(window, Key.Enter, PhysicalKey.Enter);
                RunLayoutJobs();

                await Assert.That(IsEmojiDropDownOpen(includeControl)).IsTrue();

                PressKey(window, Key.F2, PhysicalKey.F2);
                RunLayoutJobs();

                await Assert.That(AutomationProperties.GetAutomationId(includeInput)).IsEqualTo("IncludeEmojiFilterSearchBox");
                includeInput.Text = "launch";
                RunLayoutJobs();

                var includeList = GetEmojiFilterList(includeControl);
                includeList.Focus();
                includeList.SelectedIndex = 0;
                var launchFilter = GetEmojiFilterListItems(includeList).Single();
                PressKey(window, Key.Space, PhysicalKey.Space);
                RunLayoutJobs();

                await Assert.That(launchFilter.ShowTasks).IsTrue();
                await Assert.That(IsEmojiDropDownOpen(includeControl)).IsTrue();

                includeInput.Focus();
                PressKey(window, Key.Escape, PhysicalKey.Escape);
                RunLayoutJobs();

                await Assert.That(includeInput.Text).IsEqualTo(string.Empty);
                await Assert.That(IsEmojiDropDownOpen(includeControl)).IsTrue();

                PressKey(window, Key.Escape, PhysicalKey.Escape);
                RunLayoutJobs();

                await Assert.That(IsEmojiDropDownOpen(includeControl)).IsFalse();
                await Assert.That(AutomationProperties.GetAutomationId(includeInput)).IsEqualTo("IncludeEmojiFilterSummaryBox");
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Toolbar_EmojiFilters_PopupStaysVisibleInNarrowViewport()
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
                await PrepareEmojiFilterData(vm);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view, 320, 360);
                window.Show();
                RunLayoutJobs();
                SelectTab(view, 0);

                var toolbar = FindVisibleFilterToolbar(view);
                var (includeControl, _) = FindVisibleToolbarEmojiFilterControls(toolbar);
                var includeInput = GetEmojiFilterInput(includeControl);
                await ClickControlAsync(window, includeInput);
                RunLayoutJobs();

                var dropDown = GetEmojiFilterDropDown(includeControl);
                var inputBounds = GetBoundsRelativeTo(window, includeInput);
                var dropDownBounds = GetBoundsRelativeTo(window, dropDown);

                await Assert.That(dropDownBounds.Left).IsGreaterThanOrEqualTo(-1);
                await Assert.That(dropDownBounds.Top).IsGreaterThanOrEqualTo(-1);
                await Assert.That(dropDownBounds.Right).IsLessThanOrEqualTo(window.Bounds.Width + 1);
                await Assert.That(dropDownBounds.Bottom).IsLessThanOrEqualTo(window.Bounds.Height + 1);
                await Assert.That(dropDownBounds.Left).IsLessThanOrEqualTo(inputBounds.Right);
                await Assert.That(dropDownBounds.Right).IsGreaterThanOrEqualTo(inputBounds.Left);
                await Assert.That(dropDown.MaxHeight).IsLessThanOrEqualTo(260);
                await Assert.That(GetEmojiFilterList(includeControl).MaxHeight).IsLessThanOrEqualTo(dropDown.MaxHeight);
                AssertVisibleItemsStayInsideDropDown(dropDown, GetEmojiFilterList(includeControl));
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RoadmapToolbar_EmojiFilters_UsesSearchableMultiSelectDropdown()
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
                await PrepareEmojiFilterData(vm);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view, 390, 760);
                window.Show();
                RunLayoutJobs();
                SelectTab(view, 8);

                var toolbar = FindVisibleRoadmapFilterToolbar(view);
                var (includeControl, excludeControl) = FindVisibleToolbarEmojiFilterControls(toolbar);
                await ClickControlAsync(window, GetEmojiFilterInput(includeControl));
                RunLayoutJobs();

                await Assert.That(GetEmojiFilterListItems(GetEmojiFilterList(includeControl))).IsNotEmpty();
                await Assert.That(GetEmojiFilterInput(excludeControl).Classes.Contains("Exclude")).IsTrue();
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
                await AssertActionsPrecedeSearchInLogicalOrder(toolbar, searchBar, primaryActions);
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
                await AssertActionsPrecedeSearchInLogicalOrder(wideToolbar, wideSearchBar, widePrimaryActions);
                await AssertSearchAndActionsShareToolbarRow(wideToolbar, wideSearchBar, widePrimaryActions);

                vm.DetailsAreOpen = true;
                RunLayoutJobs();

                var narrowToolbar = FindVisibleFilterToolbar(view);
                var narrowSearchBar = FindVisibleToolbarChild<SearchBar>(narrowToolbar);
                var narrowPrimaryActions = FindVisibleToolbarChild<WrapPanel>(narrowToolbar);

                await AssertCompactPrimaryActions(narrowPrimaryActions, expectsSortControl: false);
                await AssertPrimaryActionsUseSingleLine(narrowPrimaryActions);
                await Assert.That(narrowToolbar.Bounds.Width).IsLessThanOrEqualTo(520);
                await AssertActionsPrecedeSearchInLogicalOrder(narrowToolbar, narrowSearchBar, narrowPrimaryActions);
                await AssertSearchAndActionsShareToolbarRow(narrowToolbar, narrowSearchBar, narrowPrimaryActions);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task MainControlFilterToolbar_DetailsPaneMediumNarrow_KeepsEmptyEmojiSummariesSquare()
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
                window = CreateWindow(view, 810, 760);
                window.Show();
                RunLayoutJobs();

                var wideToolbar = FindVisibleFilterToolbar(view);
                var (wideIncludeControl, wideExcludeControl) = FindVisibleToolbarEmojiFilterControls(wideToolbar);
                var wideFiltersButton = FindVisibleControlByAutomationId<DropDownButton>(view, "AllTasksFiltersButton");

                await AssertEmojiFilterInputIsEmptySquare(wideIncludeControl);
                await AssertEmojiFilterInputIsEmptySquare(wideExcludeControl);
                await Assert.That(GetEmojiFilterInput(wideIncludeControl).Bounds.Width)
                    .IsLessThanOrEqualTo(wideFiltersButton.Bounds.Width + 1);

                vm.DetailsAreOpen = true;
                RunLayoutJobs();

                var toolbar = FindVisibleFilterToolbar(view);
                var searchBar = FindVisibleToolbarChild<SearchBar>(toolbar);
                var primaryActions = FindVisibleToolbarChild<WrapPanel>(toolbar);
                var (includeControl, excludeControl) = FindVisibleToolbarEmojiFilterControls(toolbar);

                await AssertEmojiFilterInputIsEmptySquare(includeControl);
                await AssertEmojiFilterInputIsEmptySquare(excludeControl);
                await AssertPrimaryActionsUseSingleLine(primaryActions);
                await AssertActionsPrecedeSearchInLogicalOrder(toolbar, searchBar, primaryActions);
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
    public async Task FilterFlyouts_CompactViewport_ConstrainPopupSizeAndScrollVertically()
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
                window = CreateWindow(view, 320, 360);
                window.Show();
                RunLayoutJobs();

                foreach (var tab in TaskTabs)
                {
                    SelectTab(view, tab.TabIndex);

                    var filtersButton = FindVisibleControlByAutomationId<DropDownButton>(view, tab.FiltersButtonAutomationId);
                    await AssertFilterFlyoutViewportContract(window, filtersButton, tab.FilterPanelAutomationId);
                }

                SelectTab(view, 8);

                var roadmapFiltersButton = FindVisibleControlByAutomationId<DropDownButton>(view, "RoadmapFiltersButton");
                await AssertFilterFlyoutViewportContract(window, roadmapFiltersButton, "RoadmapFilterPanel");
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

    private static void ApplyApplicationFontResources(double fontSize)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        var normalized = AppearanceSettings.NormalizeFontSize(fontSize);
        application.Resources["AppFontSize"] = normalized;
        application.Resources["AppSmallFontSize"] = AppearanceSettings.GetFloatingWatermarkFontSize(normalized);
        application.Resources["AppTabFontSize"] = AppearanceSettings.GetTabFontSize(normalized);
        application.Resources["AppTabMinHeight"] = AppearanceSettings.GetTabMinHeight(normalized);
        application.Resources["AppSearchControlHeight"] = AppearanceSettings.GetSearchControlHeight(normalized);
        application.Resources["AppSearchClearButtonSize"] = AppearanceSettings.GetSearchClearButtonSize(normalized);
        application.Resources["AppSearchClearIconFontSize"] = AppearanceSettings.GetSearchClearIconFontSize(normalized);
        application.Resources["AppSearchBarMinWidth"] = AppearanceSettings.GetSearchBarMinWidth(normalized);
        application.Resources["AppFloatingControlMinHeight"] = AppearanceSettings.GetFloatingControlMinHeight(normalized);
    }

    private static Grid FindVisibleFilterToolbar(MainControl view)
    {
        return view.GetVisualDescendants()
            .OfType<Grid>()
            .First(control => control.Classes.Contains("FilterToolbar") && IsVisibleAndArranged(control));
    }

    private static Grid FindVisibleRoadmapFilterToolbar(Control view)
    {
        return view.GetVisualDescendants()
            .OfType<Grid>()
            .First(control => control.Classes.Contains("RoadmapFilterToolbar") && IsVisibleAndArranged(control));
    }

    private static T FindVisibleToolbarChild<T>(Grid toolbar)
        where T : Control
    {
        var child = toolbar.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(IsVisibleAndArranged);

        if (child != null)
        {
            return child;
        }

        var descendants = toolbar.GetVisualDescendants()
            .OfType<Control>()
            .Select(control =>
                $"{control.GetType().Name} visible={control.IsVisible} bounds={control.Bounds.Width:0.##}x{control.Bounds.Height:0.##} classes={string.Join(",", control.Classes)}")
            .Take(40);

        throw new InvalidOperationException(
            $"Visible arranged toolbar child '{typeof(T).Name}' was not found. Toolbar bounds={toolbar.Bounds.Width:0.##}x{toolbar.Bounds.Height:0.##}. Descendants: {string.Join(" | ", descendants)}");
    }

    private static (EmojiFilterMultiSelectSearchBox IncludeControl, EmojiFilterMultiSelectSearchBox ExcludeControl)
        FindVisibleToolbarEmojiFilterControls(Grid toolbar)
    {
        var controls = toolbar.GetVisualDescendants()
            .OfType<EmojiFilterMultiSelectSearchBox>()
            .Where(IsVisibleAndArranged)
            .ToArray();

        if (controls.Length != 2)
        {
            throw new InvalidOperationException($"Expected exactly 2 visible toolbar emoji filters, found {controls.Length}.");
        }

        return (
            controls.Single(static control => !control.IsExclude),
            controls.Single(static control => control.IsExclude));
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

    private static async Task AssertEmojiFilterToolbarPlacement(
        WrapPanel primaryActions,
        DropDownButton filtersButton,
        bool expectsEmojiFilters)
    {
        var visibleChildren = primaryActions.Children
            .OfType<Control>()
            .Where(IsVisibleAndArranged)
            .ToArray();
        var emojiControls = visibleChildren
            .OfType<EmojiFilterMultiSelectSearchBox>()
            .ToArray();

        if (!expectsEmojiFilters)
        {
            await Assert.That(emojiControls).IsEmpty();
            return;
        }

        await Assert.That(emojiControls.Length).IsEqualTo(2);

        var includeControl = emojiControls.Single(static control => !control.IsExclude);
        var excludeControl = emojiControls.Single(static control => control.IsExclude);
        var includeIndex = Array.IndexOf(visibleChildren, includeControl);
        var excludeIndex = Array.IndexOf(visibleChildren, excludeControl);
        var filtersButtonIndex = Array.IndexOf(visibleChildren, filtersButton);

        await Assert.That(includeIndex).IsGreaterThanOrEqualTo(0);
        await Assert.That(excludeIndex).IsEqualTo(includeIndex + 1);
        await Assert.That(filtersButtonIndex).IsEqualTo(excludeIndex + 1);
        await Assert.That(GetEmojiFilterInput(includeControl).Bounds.Width)
            .IsLessThanOrEqualTo(includeControl.SummaryWidth + 1);
        await Assert.That(GetEmojiFilterInput(excludeControl).Bounds.Width)
            .IsLessThanOrEqualTo(excludeControl.SummaryWidth + 1);
    }

    private static async Task AssertEmojiFilterInputIsEmptySquare(EmojiFilterMultiSelectSearchBox control)
    {
        var input = GetEmojiFilterInput(control);

        await Assert.That(input.Text).IsEqualTo("🙂");
        await Assert.That(Math.Abs(input.Bounds.Width - input.Bounds.Height)).IsLessThanOrEqualTo(8);
        await Assert.That(input.Bounds.Width).IsLessThanOrEqualTo(control.SummaryMinWidth + 8);
    }

    private static async Task AssertExcludeMarkerDoesNotShiftInputText(
        TextBox includeInput,
        TextBox excludeInput,
        TextBlock excludeMarker)
    {
        await Assert.That(excludeInput.Padding.Left).IsEqualTo(includeInput.Padding.Left);
        await Assert.That(excludeInput.Padding.Right).IsEqualTo(includeInput.Padding.Right);

        var parent = excludeInput.GetVisualParent() ??
                     throw new InvalidOperationException("Emoji filter input visual parent was not found.");
        var inputBounds = GetBoundsRelativeTo(parent, excludeInput);
        var markerBounds = GetBoundsRelativeTo(parent, excludeMarker);
        await Assert.That(markerBounds.Left).IsLessThanOrEqualTo(inputBounds.Left + excludeInput.Padding.Left);
    }

    private static async Task AssertControlStaysInsideWindow(Window window, Control control)
    {
        var bounds = GetBoundsRelativeTo(window, control);
        await Assert.That(bounds.Left).IsGreaterThanOrEqualTo(-1);
        await Assert.That(bounds.Right).IsLessThanOrEqualTo(window.Bounds.Width + 1);
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

        await Assert.That(primaryBounds.Right).IsLessThanOrEqualTo(searchBounds.Left + 1);
        await Assert.That(searchBounds.Width).IsGreaterThan(0);
        await Assert.That(searchBounds.Right).IsLessThanOrEqualTo(toolbar.Bounds.Width + 1);
        await Assert.That(primaryBounds.Height).IsLessThanOrEqualTo(searchBounds.Height + 2);
        await Assert.That(Math.Abs(GetCenterY(searchBounds) - GetCenterY(primaryBounds))).IsLessThanOrEqualTo(2);
        await Assert.That(primaryBounds.Left).IsGreaterThanOrEqualTo(-1);
    }

    private static async Task AssertActionsPrecedeSearchInLogicalOrder(
        Grid toolbar,
        SearchBar searchBar,
        WrapPanel primaryActions)
    {
        var children = toolbar.Children.ToArray();
        await Assert.That(Array.IndexOf(children, searchBar)).IsGreaterThanOrEqualTo(0);
        await Assert.That(Array.IndexOf(children, primaryActions)).IsLessThan(Array.IndexOf(children, searchBar));
    }

    private static async Task AssertNestedSearchControlShrinksWithToolbar(Grid toolbar, SearchBar searchBar)
    {
        var searchControl = searchBar.GetVisualDescendants()
            .OfType<SearchControl>()
            .First(IsVisibleAndArranged);
        var searchControlBounds = GetBoundsRelativeTo(toolbar, searchControl);

        await Assert.That(searchControl.MinWidth).IsEqualTo(0);
        await Assert.That(searchControl.Bounds.Width).IsLessThanOrEqualTo(searchBar.Bounds.Width + 1);
        await Assert.That(searchControlBounds.Right).IsLessThanOrEqualTo(toolbar.Bounds.Width + 1);
    }

    private static async Task AssertFilterButtonMatchesSearchHeight(DropDownButton button, SearchBar searchBar)
    {
        await Assert.That(button.Bounds.Width).IsGreaterThan(searchBar.Bounds.Height);
        await Assert.That(button.Bounds.Width).IsLessThanOrEqualTo(searchBar.Bounds.Height + 16);
        await Assert.That(Math.Abs(button.Bounds.Height - searchBar.Bounds.Height)).IsLessThanOrEqualTo(2);
        await Assert.That(button.Content).IsAssignableTo<PathIcon>();
        var icon = (PathIcon)button.Content!;
        await Assert.That(icon.Classes.Contains("FilterToolbarFiltersIcon")).IsTrue();
        await Assert.That(icon.Margin.Left).IsGreaterThanOrEqualTo(2);
        await Assert.That(icon.Margin.Right).IsGreaterThanOrEqualTo(2);
    }

    private static async Task AssertAutomationName(Control control, string resourceKey)
    {
        await Assert.That(AutomationProperties.GetName(control)).IsEqualTo(L10n.Get(resourceKey));
    }

    private static void AssertFilterFlyoutPanel(
        DropDownButton filtersButton,
        string filterPanelAutomationId,
        string resetButtonAutomationId)
    {
        if (filtersButton.Flyout is not Flyout flyout)
        {
            throw new InvalidOperationException("Filter button must use a Flyout.");
        }

        if (flyout.Placement != PlacementMode.BottomEdgeAlignedLeft)
        {
            throw new InvalidOperationException("Filter flyout must stay aligned to the left edge of the filter button.");
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

        if (FindControlInDetachedContent<Button>(flyoutContent, resetButtonAutomationId) == null)
        {
            throw new InvalidOperationException($"Reset button '{resetButtonAutomationId}' was not found in the filter flyout.");
        }

        if (flyoutContent.GetVisualDescendants().OfType<EmojiFilterMultiSelectSearchBox>().Any() ||
            flyoutContent.GetLogicalDescendants().OfType<EmojiFilterMultiSelectSearchBox>().Any())
        {
            throw new InvalidOperationException("Emoji filter controls must stay outside the filter flyout.");
        }
    }

    private static async Task AssertResetButtonIsInsideFlyout(
        Control root,
        DropDownButton filtersButton,
        string resetButtonAutomationId)
    {
        var visibleToolbarResetButtons = root.GetVisualDescendants()
            .OfType<Button>()
            .Where(control =>
                AutomationProperties.GetAutomationId(control) == resetButtonAutomationId &&
                IsVisibleAndArranged(control))
            .ToArray();

        await Assert.That(visibleToolbarResetButtons).IsEmpty();

        if (filtersButton.Flyout is not Flyout flyout)
        {
            throw new InvalidOperationException("Filter flyout content was not found.");
        }

        flyout.ShowAt(filtersButton);
        RunLayoutJobs();

        try
        {
            if (flyout.Content is not Control flyoutContent)
            {
                throw new InvalidOperationException("Filter flyout content was not found.");
            }

            var resetButton = FindControlInDetachedContent<Button>(flyoutContent, resetButtonAutomationId);
            if (resetButton == null)
            {
                throw new InvalidOperationException($"Reset button '{resetButtonAutomationId}' was not found in the filter flyout.");
            }

            await Assert.That(resetButton.Classes.Contains("FilterPanelResetButton")).IsTrue();
            await AssertAutomationName(resetButton, "ResetFilters");
            await Assert.That(resetButton.Content).IsAssignableTo<StackPanel>();

            var content = (StackPanel)resetButton.Content!;
            await Assert.That(content.Children.OfType<PathIcon>().Any()).IsTrue();
            await Assert.That(content.Children.OfType<TextBlock>().Any(textBlock =>
                string.Equals(textBlock.Text, L10n.Get("ResetFilters"), StringComparison.Ordinal))).IsTrue();
            await AssertFilterButtonIconStyleDoesNotLeakToFlyout(flyoutContent);
        }
        finally
        {
            flyout.Hide();
            RunLayoutJobs();
        }
    }

    private static async Task AssertFilterButtonIconStyleDoesNotLeakToFlyout(Control flyoutContent)
    {
        var leakedIcons = flyoutContent.GetVisualDescendants()
            .OfType<PathIcon>()
            .Where(static icon =>
                !icon.Classes.Contains("FilterToolbarFiltersIcon") &&
                !icon.Classes.Contains("FilterPanelResetIcon") &&
                icon.Margin.Left == 2 &&
                icon.Margin.Top == 0 &&
                icon.Margin.Right == 2 &&
                icon.Margin.Bottom == 0)
            .ToArray();

        await Assert.That(leakedIcons).IsEmpty();
    }

    private static async Task AssertFilterFlyoutViewportContract(
        Window window,
        DropDownButton filtersButton,
        string filterPanelAutomationId)
    {
        if (filtersButton.Flyout is not Flyout flyout)
        {
            throw new InvalidOperationException("Filter button must use a Flyout.");
        }

        flyout.ShowAt(filtersButton);
        RunLayoutJobs();

        try
        {
            if (flyout.Content is not Control flyoutContent)
            {
                throw new InvalidOperationException("Filter flyout content was not found.");
            }

            var panel = FindControlInDetachedContent<Border>(flyoutContent, filterPanelAutomationId);
            if (panel == null)
            {
                throw new InvalidOperationException($"Filter panel '{filterPanelAutomationId}' was not found.");
            }

            var scrollViewer = panel.GetVisualDescendants()
                .OfType<ScrollViewer>()
                .FirstOrDefault(static control => control.Classes.Contains("FilterPanelScrollViewer"));
            if (scrollViewer == null)
            {
                throw new InvalidOperationException($"Filter panel '{filterPanelAutomationId}' must use a scroll viewer.");
            }

            var buttonLeft = filtersButton.TranslatePoint(new Point(0, 0), window)?.X ?? 0;
            var buttonBottom = filtersButton.TranslatePoint(new Point(0, filtersButton.Bounds.Height), window)?.Y ?? filtersButton.Bounds.Height;
            var availableWidth = Math.Max(0, window.Bounds.Width - buttonLeft - 8);
            var availableHeight = Math.Max(0, window.Bounds.Height - buttonBottom - 8);

            await Assert.That(panel.MinWidth).IsEqualTo(0);
            await Assert.That(panel.MaxWidth).IsLessThanOrEqualTo(availableWidth + 1);
            await Assert.That(panel.Bounds.Width).IsLessThanOrEqualTo(panel.MaxWidth + 1);
            await Assert.That(panel.MaxHeight).IsLessThanOrEqualTo(availableHeight + 1);
            await Assert.That(panel.Bounds.Height).IsLessThanOrEqualTo(panel.MaxHeight + 1);
            await Assert.That(scrollViewer.VerticalScrollBarVisibility).IsEqualTo(ScrollBarVisibility.Auto);
            await Assert.That(scrollViewer.HorizontalScrollBarVisibility).IsEqualTo(ScrollBarVisibility.Disabled);
            await Assert.That(scrollViewer.MaxHeight).IsLessThanOrEqualTo(panel.MaxHeight + 1);
        }
        finally
        {
            flyout.Hide();
            RunLayoutJobs();
        }
    }

    private static async Task PrepareEmojiFilterData(MainWindowViewModel vm)
    {
        var titlesById = new (string TaskId, string Title)[]
        {
            (MainWindowViewModelFixture.RootTask2Id, "\ud83d\ude80 Alpha launch target"),
            (MainWindowViewModelFixture.RootTask3Id, "\ud83e\uddf0 Beta tools target"),
            (MainWindowViewModelFixture.RootTask4Id, "\ud83e\uddea Delta assay target"),
            (MainWindowViewModelFixture.RootTask5Id, "\ud83d\udcda Epsilon library target"),
            (MainWindowViewModelFixture.RootTask6Id, "\u274C Gamma blocked target"),
            (MainWindowViewModelFixture.RootTask7Id, "\u2705 Zeta done target")
        };

        foreach (var (taskId, title) in titlesById)
        {
            TestHelpers.GetTask(vm, taskId).Title = title;
        }

        var filtersReady = WaitFor(() =>
            vm.EmojiFilters.Count(static filter => !string.IsNullOrWhiteSpace(filter.Emoji)) >= titlesById.Length &&
            vm.EmojiExcludeFilters.Count(static filter => !string.IsNullOrWhiteSpace(filter.Emoji)) >= titlesById.Length &&
            vm.EmojiFilters.Any(static filter => filter.SearchText.Contains("library", StringComparison.OrdinalIgnoreCase)) &&
            vm.EmojiFilters.Any(static filter => filter.SearchText.Contains("launch", StringComparison.OrdinalIgnoreCase)));

        await Assert.That(filtersReady).IsTrue();
    }

    private static TextBox GetEmojiFilterInput(EmojiFilterMultiSelectSearchBox control)
    {
        return control.FindControl<TextBox>("PART_Input") ??
               throw new InvalidOperationException("Emoji filter input was not found.");
    }

    private static PathIcon? FindEmojiFilterDropDownGlyph(EmojiFilterMultiSelectSearchBox control)
    {
        return control.FindControl<PathIcon>("PART_DropDownGlyph");
    }

    private static TextBlock GetEmojiFilterExcludeMarker(EmojiFilterMultiSelectSearchBox control)
    {
        return control.FindControl<TextBlock>("PART_ExcludeMarker") ??
               throw new InvalidOperationException("Emoji filter exclude marker was not found.");
    }

    private static Border GetEmojiFilterDropDown(EmojiFilterMultiSelectSearchBox control)
    {
        return control.FindControl<Border>("PART_DropDown") ??
               throw new InvalidOperationException("Emoji filter dropdown was not found.");
    }

    private static ListBox GetEmojiFilterList(EmojiFilterMultiSelectSearchBox control)
    {
        return control.FindControl<ListBox>("PART_List") ??
               throw new InvalidOperationException("Emoji filter list was not found.");
    }

    private static TextBlock GetEmojiFilterNoMatches(EmojiFilterMultiSelectSearchBox control)
    {
        return control.FindControl<TextBlock>("PART_NoMatches") ??
               throw new InvalidOperationException("Emoji filter no-matches text was not found.");
    }

    private static Border GetEmojiFilterNoMatchesPanel(EmojiFilterMultiSelectSearchBox control)
    {
        return control.FindControl<Border>("PART_NoMatchesPanel") ??
               throw new InvalidOperationException("Emoji filter no-matches panel was not found.");
    }

    private static IReadOnlyList<EmojiFilter> GetEmojiFilterListItems(ListBox list)
    {
        return ((IEnumerable?)list.ItemsSource ?? list.Items)
            .OfType<EmojiFilter>()
            .ToArray();
    }

    private static IReadOnlyList<ListBoxItem> GetVisibleEmojiListBoxItems(ListBox list)
    {
        return list.GetVisualDescendants()
            .OfType<ListBoxItem>()
            .Where(IsVisibleAndArranged)
            .ToArray();
    }

    private static void AssertVisibleItemsStayInsideDropDown(Border dropDown, ListBox list)
    {
        var visibleItems = GetVisibleEmojiListBoxItems(list);
        if (visibleItems.Count == 0)
        {
            throw new InvalidOperationException("Expected visible emoji filter rows.");
        }

        var listBounds = GetBoundsRelativeTo(dropDown, list);
        foreach (var item in visibleItems)
        {
            var itemBounds = GetBoundsRelativeTo(dropDown, item);
            if (itemBounds.Bottom <= listBounds.Top + 1 ||
                itemBounds.Top >= listBounds.Bottom - 1)
            {
                continue;
            }

            if (itemBounds.Top < listBounds.Top - 1 ||
                itemBounds.Bottom > listBounds.Bottom + 1 ||
                itemBounds.Top < -1 ||
                itemBounds.Bottom > dropDown.Bounds.Height + 1)
            {
                throw new InvalidOperationException(
                    $"Emoji filter row is clipped by dropdown bounds: " +
                    $"itemTop={itemBounds.Top:F1}; itemBottom={itemBounds.Bottom:F1}; " +
                    $"listTop={listBounds.Top:F1}; listBottom={listBounds.Bottom:F1}; dropdownHeight={dropDown.Bounds.Height:F1}.");
            }
        }
    }

    private static async Task AssertEmojiRowsMeasureContentAndCenterVertically(ListBox list)
    {
        var visibleItems = GetVisibleEmojiListBoxItems(list);
        await Assert.That(visibleItems).IsNotEmpty();

        foreach (var item in visibleItems)
        {
            var rowControls = item.GetVisualDescendants()
                .OfType<Control>()
                .Where(static control =>
                    control is CheckBox ||
                    control.Classes.Contains("EmojiFilterItemEmoji") ||
                    control.Classes.Contains("EmojiFilterItemTitle"))
                .Where(IsVisibleAndArranged)
                .ToArray();
            await Assert.That(rowControls).IsNotEmpty();

            var rowCenterY = item.Bounds.Height / 2d;
            var tallestContentHeight = rowControls.Max(static control => control.Bounds.Height);
            await Assert.That(item.Bounds.Height).IsGreaterThanOrEqualTo(tallestContentHeight);

            foreach (var control in rowControls)
            {
                var bounds = GetBoundsRelativeTo(item, control);
                await Assert.That(bounds.Top).IsGreaterThanOrEqualTo(-1);
                await Assert.That(bounds.Bottom).IsLessThanOrEqualTo(item.Bounds.Height + 1);
                await Assert.That(Math.Abs(GetCenterY(bounds) - rowCenterY)).IsLessThanOrEqualTo(2);
            }
        }
    }

    private static IReadOnlyList<string?> GetVisibleEmojiItemTexts(ListBox list)
    {
        return list.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(textBlock => textBlock.Classes.Contains("EmojiFilterItemEmoji") && IsVisibleAndArranged(textBlock))
            .Select(textBlock => textBlock.Text)
            .ToArray();
    }

    private static IReadOnlyList<string> GetVisibleEmojiItemTitleTexts(ListBox list)
    {
        return list.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(textBlock => textBlock.Classes.Contains("EmojiFilterItemTitle") && IsVisibleAndArranged(textBlock))
            .Select(textBlock => textBlock.Text ?? string.Empty)
            .ToArray();
    }

    private static CheckBox FindVisibleCheckBox(Control root)
    {
        return root.GetVisualDescendants()
            .OfType<CheckBox>()
            .First(IsVisibleAndArranged);
    }

    private static bool IsEmojiDropDownOpen(EmojiFilterMultiSelectSearchBox control)
    {
        return GetEmojiFilterPopup(control).IsOpen;
    }

    private static Popup GetEmojiFilterPopup(EmojiFilterMultiSelectSearchBox control)
    {
        return control.FindControl<Popup>("PART_DropDownPopup") ??
               throw new InvalidOperationException("Emoji filter dropdown popup was not found.");
    }

    private static async Task ClickControlAsync(
        Window window,
        Control control,
        MouseButton button = MouseButton.Left,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate click point for {control.GetType().Name}.");
        }

        window.MouseDown(point.Value, button, modifiers);
        RunLayoutJobs();
        window.MouseUp(point.Value, button, modifiers);
        RunLayoutJobs();
        await Task.Yield();
    }

    private static void PressKey(
        Window window,
        Key key,
        PhysicalKey physicalKey,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physicalKey, null);
        RunLayoutJobs();
    }

    private static void TypeText(Window window, string text)
    {
        window.KeyTextInput(text);
        RunLayoutJobs();
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
            .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId) ??
            root.GetLogicalDescendants()
                .OfType<T>()
                .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId);
    }

    private static bool IsVisibleAndArranged(Control control)
    {
        return control.IsVisible &&
               control.Bounds.Width > 0 &&
               control.Bounds.Height > 0;
    }

    private static bool WaitFor(Func<bool> predicate, int timeoutMilliseconds = 2000)
    {
        var start = DateTime.UtcNow;

        while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMilliseconds)
        {
            if (predicate())
            {
                return true;
            }

            RunLayoutJobs();
            Thread.Sleep(10);
        }

        return predicate();
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
