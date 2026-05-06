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

namespace Unlimotion.Test;

[ParallelLimiter<SharedUiStateParallelLimit>]
public class MainControlFilterToolbarResponsiveUiTests
{
    private static readonly (int TabIndex, string ResetButtonAutomationId)[] TaskTabs =
    [
        (0, "AllTasksResetFiltersButton"),
        (1, "LastCreatedResetFiltersButton"),
        (2, "LastUpdatedResetFiltersButton"),
        (3, "UnlockedResetFiltersButton"),
        (4, "CompletedResetFiltersButton"),
        (5, "ArchivedResetFiltersButton"),
        (6, "LastOpenedResetFiltersButton")
    ];

    [Test]
    public async Task MainControlFilterToolbar_NarrowViewport_StacksSearchAboveFilters()
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
                vm.DetailsAreOpen = false;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view, 320, 760);
                window.Show();
                RunLayoutJobs();

                foreach (var (tabIndex, resetButtonAutomationId) in TaskTabs)
                {
                    SelectTab(view, tabIndex);

                    var toolbar = FindVisibleFilterToolbar(view);
                    var searchBar = FindVisibleToolbarChild<SearchBar>(toolbar);
                    var filterItems = FindVisibleToolbarChild<WrapPanel>(toolbar);
                    var resetButton = FindVisibleControlByAutomationId<Button>(view, resetButtonAutomationId);

                    var searchBounds = GetBoundsRelativeTo(toolbar, searchBar);
                    var filterBounds = GetBoundsRelativeTo(toolbar, filterItems);

                    await Assert.That(resetButton.Bounds.Width).IsGreaterThan(0);
                    await Assert.That(resetButton.Bounds.Height).IsGreaterThan(0);
                    await Assert.That(searchBounds.Right).IsLessThanOrEqualTo(toolbar.Bounds.Width + 1);
                    await Assert.That(searchBounds.Width).IsLessThanOrEqualTo(toolbar.Bounds.Width + 1);
                    await Assert.That(filterBounds.Top).IsGreaterThanOrEqualTo(searchBounds.Bottom - 1);
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
    public async Task MainControlFilterToolbar_WideViewport_KeepsSearchBesideFilters()
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
                vm.DetailsAreOpen = false;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view, 1000, 760);
                window.Show();
                RunLayoutJobs();

                var toolbar = FindVisibleFilterToolbar(view);
                var searchBar = FindVisibleToolbarChild<SearchBar>(toolbar);
                var filterItems = FindVisibleToolbarChild<WrapPanel>(toolbar);

                var searchBounds = GetBoundsRelativeTo(toolbar, searchBar);
                var filterBounds = GetBoundsRelativeTo(toolbar, filterItems);

                await Assert.That(Math.Abs(searchBounds.Top - filterBounds.Top)).IsLessThanOrEqualTo(1);
                await Assert.That(searchBounds.Left).IsGreaterThan(filterBounds.Left);
                await Assert.That(searchBounds.Right).IsLessThanOrEqualTo(toolbar.Bounds.Width + 1);
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
                vm.DetailsAreOpen = false;
                TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RootTask2Id);

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view, 760, 760);
                window.Show();
                RunLayoutJobs();

                var wideToolbar = FindVisibleFilterToolbar(view);
                var wideSearchBar = FindVisibleToolbarChild<SearchBar>(wideToolbar);
                var wideFilterItems = FindVisibleToolbarChild<WrapPanel>(wideToolbar);
                var wideSearchBounds = GetBoundsRelativeTo(wideToolbar, wideSearchBar);
                var wideFilterBounds = GetBoundsRelativeTo(wideToolbar, wideFilterItems);

                await Assert.That(wideSearchBounds.Left).IsGreaterThan(wideFilterBounds.Left);

                vm.DetailsAreOpen = true;
                RunLayoutJobs();

                var narrowToolbar = FindVisibleFilterToolbar(view);
                var narrowSearchBar = FindVisibleToolbarChild<SearchBar>(narrowToolbar);
                var narrowFilterItems = FindVisibleToolbarChild<WrapPanel>(narrowToolbar);
                var narrowSearchBounds = GetBoundsRelativeTo(narrowToolbar, narrowSearchBar);
                var narrowFilterBounds = GetBoundsRelativeTo(narrowToolbar, narrowFilterItems);

                await Assert.That(narrowToolbar.Bounds.Width).IsLessThanOrEqualTo(520);
                await Assert.That(narrowSearchBounds.Right).IsLessThanOrEqualTo(narrowToolbar.Bounds.Width + 1);
                await Assert.That(narrowFilterBounds.Top).IsGreaterThanOrEqualTo(narrowSearchBounds.Bottom - 1);
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
}
