using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel.Localization;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class MainControlTabsOverflowUiTests
{
    private static readonly string[] MainTabAutomationIds =
    [
        "AllTasksTabItem",
        "LastCreatedTabItem",
        "LastUpdatedTabItem",
        "UnlockedTabItem",
        "InProgressTabItem",
        "CompletedTabItem",
        "ArchivedTabItem",
        "LastOpenedTabItem",
        "RoadmapTabItem",
        "SettingsTabItem"
    ];

    [Test]
    public async Task MainTabs_DesktopWidth_ShowsAllTabsWithoutOverflow()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, 2200, 760);
                window = createdWindow;

                var tabs = GetMainTabItems(view);
                var overflowButton = FindControlByAutomationId<Button>(view, "MainTabsOverflowButton");

                AssertAllTabsVisibleAndArranged(tabs);
                await Assert.That(IsVisibleAndArranged(overflowButton)).IsFalse();
                AssertVisibleTabsStayOnSingleRow(view, tabs);
            }
            finally
            {
                CloseWindow(window);
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task MainTabs_DesktopWidthWithOpenDetailsPane_UsesSplitViewContentWidthForOverflow()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(
                    fixture,
                    1368,
                    768,
                    detailsAreOpen: true);
                window = createdWindow;
                SelectTab(view, "SettingsTabItem");

                var tabs = GetMainTabItems(view);
                var visibleTabs = tabs.Where(IsVisibleAndArranged).ToArray();
                var selectedTab = FindMainTabItem(view, "SettingsTabItem");
                var overflowButton = FindControlByAutomationId<Button>(view, "MainTabsOverflowButton");
                var visibleControls = visibleTabs.Cast<Control>().Append(overflowButton).ToArray();

                await Assert.That(IsVisibleAndArranged(overflowButton)).IsTrue();
                await Assert.That(IsVisibleAndArranged(selectedTab)).IsTrue();
                await Assert.That(tabs.Count(tab => !tab.IsVisible)).IsGreaterThan(0);
                await Assert.That(visibleTabs.Contains(selectedTab)).IsTrue();
                AssertHiddenTabsCollapsed(tabs.Where(tab => !tab.IsVisible).ToArray());
                AssertOverflowButtonUsesHorizontalLineIcon(overflowButton);
                AssertVisibleTabsStayOnSingleRow(view, visibleControls);
                AssertControlsFitSplitViewContent(view, visibleControls);
            }
            finally
            {
                CloseWindow(window);
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task MainTabs_IntermediateWidth_MovesInactiveTabsToOverflowAndKeepsCurrentVisible()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, 760, 760);
                window = createdWindow;
                SelectTab(view, "SettingsTabItem");

                var tabs = GetMainTabItems(view);
                var visibleTabs = tabs.Where(IsVisibleAndArranged).ToArray();
                var hiddenTabs = tabs.Where(tab => !tab.IsVisible).ToArray();
                var selectedTab = FindMainTabItem(view, "SettingsTabItem");
                var overflowButton = FindControlByAutomationId<Button>(view, "MainTabsOverflowButton");

                await Assert.That(IsVisibleAndArranged(overflowButton)).IsTrue();
                await Assert.That(IsVisibleAndArranged(selectedTab)).IsTrue();
                await Assert.That(hiddenTabs.Length).IsGreaterThan(0);
                await Assert.That(visibleTabs.Contains(selectedTab)).IsTrue();
                AssertHiddenTabsCollapsed(hiddenTabs);
                AssertOverflowButtonUsesHorizontalLineIcon(overflowButton);
                AssertVisibleTabsStayOnSingleRow(view, visibleTabs.Cast<Control>().Append(overflowButton).ToArray());
                AssertOverflowButtonFollowsVisibleTabHeaders(view, visibleTabs, overflowButton);
                AssertNoHiddenTabFitsRemainingHeaderSpace(view, hiddenTabs, overflowButton);
                AssertMainTabsContentSpansHostWhenOverflowVisible(view);
            }
            finally
            {
                CloseWindow(window);
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task MainTabs_PhoneWidth_KeepsCurrentTabVisibleWithOverflowButton()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, 360, 760);
                window = createdWindow;
                SelectTab(view, "SettingsTabItem");

                var tabs = GetMainTabItems(view);
                var hiddenTabs = tabs.Where(tab => !tab.IsVisible).ToArray();
                var selectedTab = FindMainTabItem(view, "SettingsTabItem");
                var overflowButton = FindControlByAutomationId<Button>(view, "MainTabsOverflowButton");

                await Assert.That(IsVisibleAndArranged(overflowButton)).IsTrue();
                await Assert.That(IsVisibleAndArranged(selectedTab)).IsTrue();
                await Assert.That(hiddenTabs.Length).IsGreaterThan(0);
                AssertHiddenTabsCollapsed(hiddenTabs);
                AssertVisibleTabsStayOnSingleRow(
                    view,
                    tabs.Where(IsVisibleAndArranged).Cast<Control>().Append(overflowButton).ToArray());
            }
            finally
            {
                CloseWindow(window);
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task MainTabs_PhoneWidth_KeepsLongCurrentTabVisibleWithOverflowButton()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, 360, 760);
                window = createdWindow;
                SelectTab(view, "LastUpdatedTabItem");

                var tabs = GetMainTabItems(view);
                var hiddenTabs = tabs.Where(tab => !tab.IsVisible).ToArray();
                var visibleTabs = tabs.Where(IsVisibleAndArranged).ToArray();
                var selectedTab = FindMainTabItem(view, "LastUpdatedTabItem");
                var overflowButton = FindControlByAutomationId<Button>(view, "MainTabsOverflowButton");

                await Assert.That(IsVisibleAndArranged(overflowButton)).IsTrue();
                await Assert.That(IsVisibleAndArranged(selectedTab)).IsTrue();
                await Assert.That(hiddenTabs.Length).IsGreaterThan(0);
                await Assert.That(visibleTabs.Contains(selectedTab)).IsTrue();
                AssertHiddenTabsCollapsed(hiddenTabs);
                AssertVisibleTabsStayOnSingleRow(view, visibleTabs.Cast<Control>().Append(overflowButton).ToArray());
            }
            finally
            {
                CloseWindow(window);
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task MainTabs_OverflowMenu_SelectsHiddenTabAndMakesItVisible()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, 420, 760);
                window = createdWindow;

                await SelectOverflowItemAsync(window, view, "SettingsTabItem");
                RunLayoutJobs();

                var selectedTab = FindMainTabItem(view, "SettingsTabItem");
                var settingsRoot = FindControlByAutomationId<Control>(view, "SettingsRoot");

                await Assert.That(selectedTab.IsSelected).IsTrue();
                await Assert.That(IsVisibleAndArranged(selectedTab)).IsTrue();
                await Assert.That(IsVisibleAndArranged(settingsRoot)).IsTrue();
            }
            finally
            {
                CloseWindow(window);
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task MainTabs_LanguageChangeWhileOverflowActive_RecalculatesHiddenTabWidths()
    {
        var previousLocalization = LocalizationService.Current;
        var culture = CultureSnapshot.Capture();

        try
        {
            var localization = new LocalizationService(new FakeSystemCultureProvider("en-US"));
            LocalizationService.Current = localization;
            localization.SetLanguage(LocalizationService.EnglishLanguage);

            await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
            await session.DispatchAsync(async () =>
            {
                var fixture = new MainWindowViewModelFixture();
                Window? window = null;

                try
                {
                    var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, 600, 760);
                    window = createdWindow;
                    SelectTab(view, "LastUpdatedTabItem");

                    localization.SetLanguage(LocalizationService.RussianLanguage);
                    RunLayoutJobs();

                    var tabs = GetMainTabItems(view);
                    var selectedTab = FindMainTabItem(view, "LastUpdatedTabItem");
                    var overflowButton = FindControlByAutomationId<Button>(view, "MainTabsOverflowButton");
                    var visibleControls = tabs
                        .Where(IsVisibleAndArranged)
                        .Cast<Control>()
                        .Append(overflowButton)
                        .ToArray();

                    await Assert.That(IsVisibleAndArranged(overflowButton)).IsTrue();
                    await Assert.That(IsVisibleAndArranged(selectedTab)).IsTrue();
                    await Assert.That(selectedTab.Header?.ToString()).IsEqualTo("Последние измененные");
                    await Assert.That(tabs.Count(tab => !tab.IsVisible)).IsGreaterThan(0);
                    AssertHiddenTabsCollapsed(tabs.Where(tab => !tab.IsVisible).ToArray());
                    AssertVisibleTabsStayOnSingleRow(view, visibleControls);
                    AssertControlsFitWindow(window, visibleControls);
                }
                finally
                {
                    localization.SetLanguage(LocalizationService.EnglishLanguage);
                    RunLayoutJobs();
                    CloseWindow(window);
                    fixture.CleanTasks();
                }
            }, CancellationToken.None);
        }
        finally
        {
            LocalizationService.Current = previousLocalization;
            culture.Restore();
        }
    }

    [Test]
    public async Task MainTabs_ResizeFromPhoneToDesktop_RestoresAllTabs()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, 360, 760);
                window = createdWindow;
                SelectTab(view, "SettingsTabItem");

                await Assert.That(GetMainTabItems(view).Any(tab => !tab.IsVisible)).IsTrue();

                window.Width = 2200;
                RunLayoutJobs();

                var tabs = GetMainTabItems(view);
                var overflowButton = FindControlByAutomationId<Button>(view, "MainTabsOverflowButton");

                AssertAllTabsVisibleAndArranged(tabs);
                await Assert.That(IsVisibleAndArranged(overflowButton)).IsFalse();
                AssertVisibleTabsStayOnSingleRow(view, tabs);
            }
            finally
            {
                CloseWindow(window);
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    private static async Task<(MainControl View, Window Window)> CreateArrangedMainControlAsync(
        MainWindowViewModelFixture fixture,
        double width,
        double height,
        bool detailsAreOpen = false)
    {
        var vm = fixture.MainWindowViewModelTest;
        await vm.Connect();
        vm.AllTasksMode = true;
        vm.DetailsAreOpen = detailsAreOpen;

        var view = new MainControl { DataContext = vm };
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = view
        };

        window.Show();
        RunLayoutJobs();
        return (view, window);
    }

    private static void SelectTab(MainControl view, string automationId)
    {
        var tabControl = FindControlByAutomationId<TabControl>(view, "MainTabs");
        tabControl.SelectedItem = FindMainTabItem(view, automationId);
        RunLayoutJobs();
    }

    private static async Task SelectOverflowItemAsync(Window window, MainControl view, string tabAutomationId)
    {
        var overflowButton = FindControlByAutomationId<Button>(view, "MainTabsOverflowButton");
        if (overflowButton.Flyout is not MenuFlyout menuFlyout)
        {
            throw new InvalidOperationException("Main tabs overflow flyout was not available.");
        }

        var menuItem = menuFlyout.Items
            .OfType<MenuItem>()
            .FirstOrDefault(item => string.Equals(
                AutomationProperties.GetAutomationId(item),
                $"MainTabsOverflow{tabAutomationId}",
                StringComparison.Ordinal));

        if (menuItem == null)
        {
            throw new InvalidOperationException($"Overflow item for '{tabAutomationId}' was not found.");
        }

        await ClickControlAsync(window, overflowButton);
        RunLayoutJobs();

        if (!IsVisibleAndArranged(menuItem))
        {
            throw new InvalidOperationException(
                $"Overflow item for '{tabAutomationId}' was not visible after clicking the overflow button.");
        }

        await ClickControlAsync(window, menuItem);
        menuFlyout.Hide();
        RunLayoutJobs();
    }

    private static TabItem[] GetMainTabItems(MainControl view)
    {
        return MainTabAutomationIds
            .Select(id => FindMainTabItem(view, id))
            .ToArray();
    }

    private static TabItem FindMainTabItem(MainControl view, string automationId)
    {
        return FindControlByAutomationId<TabItem>(view, automationId);
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

        return control ?? throw new InvalidOperationException(
            $"Control with AutomationId '{automationId}' was not found.");
    }

    private static T FindControlByName<T>(Control root, string name)
        where T : Control
    {
        var control = root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, name, StringComparison.Ordinal));

        return control ?? throw new InvalidOperationException(
            $"Control with Name '{name}' was not found.");
    }

    private static bool IsVisibleAndArranged(Control control)
    {
        return control.IsVisible &&
               control.Bounds.Width > 0 &&
               control.Bounds.Height > 0;
    }

    private static void AssertAllTabsVisibleAndArranged(IReadOnlyCollection<TabItem> tabs)
    {
        var missingTabs = tabs
            .Where(tab => !IsVisibleAndArranged(tab))
            .Select(tab =>
                $"{AutomationProperties.GetAutomationId(tab)} visible={tab.IsVisible} bounds={tab.Bounds}")
            .ToArray();

        if (missingTabs.Length > 0)
        {
            throw new InvalidOperationException(
                $"Expected every main tab to be visible and arranged. Missing: {string.Join("; ", missingTabs)}");
        }
    }

    private static void AssertVisibleTabsStayOnSingleRow(MainControl view, IReadOnlyCollection<Control> controls)
    {
        if (controls.Count == 0)
        {
            throw new InvalidOperationException("No visible tab controls were provided for row assertion.");
        }

        var mainTabs = FindControlByAutomationId<TabControl>(view, "MainTabs");
        var bounds = controls
            .Select(control => GetBoundsRelativeTo(mainTabs, control))
            .ToArray();
        var minTop = bounds.Min(bound => bound.Top);
        var maxBottom = bounds.Max(bound => bound.Bottom);
        var maxHeight = bounds.Max(bound => bound.Height);

        if (maxBottom - minTop > maxHeight + 1)
        {
            throw new InvalidOperationException(
                $"Visible main tabs are not on one row. Top={minTop}, Bottom={maxBottom}, MaxHeight={maxHeight}.");
        }
    }

    private static void AssertMainTabsContentSpansHostWhenOverflowVisible(MainControl view)
    {
        var mainTabsHost = FindControlByName<Grid>(view, "MainTabsHost");
        var mainTabs = FindControlByAutomationId<TabControl>(view, "MainTabs");
        var hostBounds = GetBoundsRelativeTo(view, mainTabsHost);
        var tabsBounds = GetBoundsRelativeTo(view, mainTabs);

        if (tabsBounds.Width < hostBounds.Width - 1)
        {
            throw new InvalidOperationException(
                $"Main tab content must span the host when overflow is visible. TabsWidth={tabsBounds.Width}, HostWidth={hostBounds.Width}.");
        }
    }

    private static void AssertOverflowButtonFollowsVisibleTabHeaders(
        MainControl view,
        IReadOnlyCollection<TabItem> visibleTabs,
        Button overflowButton)
    {
        var mainTabs = FindControlByAutomationId<TabControl>(view, "MainTabs");
        var maxTabRight = visibleTabs
            .Select(tab => GetBoundsRelativeTo(mainTabs, tab).Right)
            .Max();
        var buttonBounds = GetBoundsRelativeTo(mainTabs, overflowButton);
        var gap = buttonBounds.Left - maxTabRight;

        if (gap < -1 || gap > 12)
        {
            throw new InvalidOperationException(
                $"Main tabs overflow button must follow visible tab headers. Gap={gap}, ButtonBounds={buttonBounds}.");
        }
    }

    private static void AssertNoHiddenTabFitsRemainingHeaderSpace(
        MainControl view,
        IReadOnlyCollection<TabItem> hiddenTabs,
        Button overflowButton)
    {
        if (hiddenTabs.Count == 0)
        {
            return;
        }

        var mainTabs = FindControlByAutomationId<TabControl>(view, "MainTabs");
        var buttonBounds = GetBoundsRelativeTo(mainTabs, overflowButton);
        var remainingWidth = Math.Max(0, mainTabs.Bounds.Width - buttonBounds.Right);
        var tabWidths = GetMainTabWidthCache(view);
        var fittingTabs = hiddenTabs
            .Select(tab =>
            {
                var automationId = AutomationProperties.GetAutomationId(tab) ?? string.Empty;
                return new
                {
                    AutomationId = automationId,
                    Width = tabWidths.TryGetValue(automationId, out var width) ? width : 0
                };
            })
            .Where(tab => tab.Width > 0 && tab.Width <= remainingWidth + 1)
            .Select(tab => $"{tab.AutomationId} width={tab.Width}")
            .ToArray();

        if (fittingTabs.Length > 0)
        {
            throw new InvalidOperationException(
                $"Main tabs overflow left unused header space while hidden tabs still fit. RemainingWidth={remainingWidth}; FittingTabs={string.Join("; ", fittingTabs)}.");
        }
    }

    private static IReadOnlyDictionary<string, double> GetMainTabWidthCache(MainControl view)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var field = typeof(MainControl).GetField("_mainTabWidthCache", flags);
        if (field?.GetValue(view) is not IReadOnlyDictionary<string, double> tabWidths)
        {
            throw new InvalidOperationException("Main tab width cache was not available for overflow layout assertion.");
        }

        return tabWidths;
    }

    private static void AssertOverflowButtonUsesHorizontalLineIcon(Button overflowButton)
    {
        if (overflowButton.Content is not Grid)
        {
            throw new InvalidOperationException(
                $"Expected main tabs overflow button content to be an icon grid without a visual frame, got {overflowButton.Content?.GetType().Name ?? "null"}.");
        }

        var lines = overflowButton.GetVisualDescendants()
            .OfType<Border>()
            .Where(border =>
                (AutomationProperties.GetAutomationId(border) ?? string.Empty)
                .StartsWith("MainTabsOverflowIconLine", StringComparison.Ordinal))
            .ToArray();

        if (lines.Length != 3)
        {
            throw new InvalidOperationException(
                $"Expected main tabs overflow icon to contain three horizontal lines, found {lines.Length}.");
        }

        foreach (var line in lines)
        {
            if (line.Bounds.Width <= line.Bounds.Height)
            {
                throw new InvalidOperationException(
                    $"Expected overflow icon line to be horizontal. Bounds={line.Bounds}.");
            }
        }
    }

    private static void AssertHiddenTabsCollapsed(IReadOnlyCollection<TabItem> tabs)
    {
        var expandedTabs = tabs
            .Where(tab => tab.Width != 0 || tab.MinWidth != 0 || tab.MaxWidth != 0 || tab.Padding != new Thickness(0))
            .Select(tab =>
                $"{AutomationProperties.GetAutomationId(tab)} width={tab.Width} min={tab.MinWidth} max={tab.MaxWidth} padding={tab.Padding}")
            .ToArray();

        if (expandedTabs.Length > 0)
        {
            throw new InvalidOperationException(
                $"Expected hidden main tab headers to be collapsed. Expanded: {string.Join("; ", expandedTabs)}");
        }
    }

    private static void AssertControlsFitWindow(Window window, IReadOnlyCollection<Control> controls)
    {
        if (controls.Count == 0)
        {
            throw new InvalidOperationException("No visible tab controls were provided for width assertion.");
        }

        var maxRight = controls
            .Select(control => GetBoundsRelativeTo(window, control))
            .Max(bound => bound.Right);

        if (maxRight > window.ClientSize.Width + 1)
        {
            throw new InvalidOperationException(
                $"Visible main tabs overflow the window width. Right={maxRight}, WindowWidth={window.ClientSize.Width}.");
        }
    }

    private static void AssertControlsFitSplitViewContent(MainControl view, IReadOnlyCollection<Control> controls)
    {
        if (controls.Count == 0)
        {
            throw new InvalidOperationException("No visible tab controls were provided for content width assertion.");
        }

        var splitView = view.GetVisualDescendants().OfType<SplitView>().First();
        var splitViewBounds = GetBoundsRelativeTo(view, splitView);
        var paneWidth = splitView.IsPaneOpen ? splitView.OpenPaneLength : splitView.CompactPaneLength;
        var contentRight = splitViewBounds.Right - paneWidth;
        var maxRight = controls
            .Select(control => GetBoundsRelativeTo(view, control))
            .Max(bound => bound.Right);

        if (maxRight > contentRight + 1)
        {
            throw new InvalidOperationException(
                $"Visible main tabs overflow SplitView content. Right={maxRight}, ContentRight={contentRight}.");
        }
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
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(point.Value, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        await Task.CompletedTask;
    }

    private static void CloseWindow(Window? window)
    {
        if (window == null)
        {
            return;
        }

        window.Content = null;
        RunLayoutJobs();
        window.Close();
        RunLayoutJobs();
    }

    private sealed class FakeSystemCultureProvider : ILocalizationSystemCultureProvider
    {
        public FakeSystemCultureProvider(string cultureName)
        {
            SystemUICulture = CultureInfo.GetCultureInfo(cultureName);
        }

        public CultureInfo SystemUICulture { get; }
    }

    private sealed class CultureSnapshot
    {
        private readonly CultureInfo _currentCulture;
        private readonly CultureInfo _currentUiCulture;
        private readonly CultureInfo? _defaultThreadCurrentCulture;
        private readonly CultureInfo? _defaultThreadCurrentUiCulture;

        private CultureSnapshot()
        {
            _currentCulture = Thread.CurrentThread.CurrentCulture;
            _currentUiCulture = Thread.CurrentThread.CurrentUICulture;
            _defaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentCulture;
            _defaultThreadCurrentUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        }

        public static CultureSnapshot Capture() => new();

        public void Restore()
        {
            CultureInfo.DefaultThreadCurrentCulture = _defaultThreadCurrentCulture;
            CultureInfo.DefaultThreadCurrentUICulture = _defaultThreadCurrentUiCulture;
            Thread.CurrentThread.CurrentCulture = _currentCulture;
            Thread.CurrentThread.CurrentUICulture = _currentUiCulture;
        }
    }

    private static void RunLayoutJobs()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }
}
