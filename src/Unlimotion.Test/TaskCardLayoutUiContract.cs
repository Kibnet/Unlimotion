using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Localization;
using Unlimotion.Views;

namespace Unlimotion.Test;

internal sealed class TaskCardLayoutScenarioResult
{
    public bool DesktopCardIsReadableAndControllable { get; set; }

    public bool DesktopParentRelationEditorIsUsable { get; set; }

    public bool NarrowLayoutsStayHorizontallyContained { get; set; }

    public bool NarrowParentRelationEditorIsUsable { get; set; }
}

internal static class TaskCardLayoutUiContract
{
    private static readonly string[] DesktopAutomationIds =
    [
        "CurrentTaskCard",
        "CurrentTaskHeader",
        "CurrentTaskDescriptionSection",
        "CurrentTaskPlanningSection",
        "CurrentTaskRelationsSection",
        "CurrentTaskCompletionCriteriaSection",
        "CurrentTaskStatusHistorySection",
        "CurrentTaskTitleTextBox",
        "CurrentTaskDescriptionTextBox",
        "CurrentTaskParentsRelationAddButton"
    ];

    private static readonly string[] NarrowAutomationIds =
    [
        "CurrentTaskCard",
        "CurrentTaskTitleTextBox",
        "CurrentTaskDescriptionTextBox",
        "CurrentTaskParentsRelationAddButton"
    ];

    private static readonly string[] RelationEditorAutomationIds =
    [
        "CurrentTaskParentsRelationAddInput",
        "CurrentTaskParentsRelationSuggestions",
        "CurrentTaskParentsRelationAddCancelButton",
        "CurrentTaskParentsRelationAddConfirmButton"
    ];

    public static async Task<TaskCardLayoutScenarioResult> ExecuteTaskCardLayoutScenarioAsync()
    {
        var result = new TaskCardLayoutScenarioResult();

        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            ResetSharedUiState();

            result.DesktopCardIsReadableAndControllable = await VerifyDesktopLayoutAsync();
            result.DesktopParentRelationEditorIsUsable = true;

            var narrowLayoutsStayContained = true;
            var narrowRelationEditorsStayUsable = true;
            foreach (var width in new[] { 360d, 390d, 430d })
            {
                var phoneResult = await VerifyNarrowLayoutAsync(width);
                narrowLayoutsStayContained &= phoneResult.CardIsContained;
                narrowRelationEditorsStayUsable &= phoneResult.RelationEditorIsUsable;
            }

            result.NarrowLayoutsStayHorizontallyContained = narrowLayoutsStayContained;
            result.NarrowParentRelationEditorIsUsable = narrowRelationEditorsStayUsable;
        }, CancellationToken.None);

        return result;
    }

    public static async Task AssertTaskCardLayoutScenarioResultAsync(TaskCardLayoutScenarioResult result)
    {
        await Assert.That(result.DesktopCardIsReadableAndControllable).IsTrue();
        await Assert.That(result.DesktopParentRelationEditorIsUsable).IsTrue();
        await Assert.That(result.NarrowLayoutsStayHorizontallyContained).IsTrue();
        await Assert.That(result.NarrowParentRelationEditorIsUsable).IsTrue();
    }

    private static async Task<bool> VerifyDesktopLayoutAsync()
    {
        var fixture = new MainWindowViewModelFixture();
        Window? window = null;

        try
        {
            var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, 1400, 900);
            window = createdWindow;

            foreach (var automationId in DesktopAutomationIds)
            {
                AssertVisibleAndArranged(FindControlByAutomationId<Control>(view, automationId), automationId);
            }

            OpenAndAssertParentRelationEditor(view, relativeTo: null);
            return true;
        }
        finally
        {
            CloseWindow(window);
            await fixture.CleanTasksAsync();
        }
    }

    private static async Task<NarrowLayoutResult> VerifyNarrowLayoutAsync(double width)
    {
        var fixture = new MainWindowViewModelFixture();
        Window? window = null;

        try
        {
            var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, width, 844);
            window = createdWindow;

            var scrollViewer = FindControlByAutomationId<ScrollViewer>(view, "CurrentTaskDetailsScrollViewer");
            foreach (var automationId in NarrowAutomationIds)
            {
                var control = FindControlByAutomationId<Control>(view, automationId);
                AssertVisibleAndArranged(control, automationId);
                AssertHorizontallyContained(scrollViewer, control, automationId);
            }

            OpenAndAssertParentRelationEditor(view, scrollViewer);
            return new NarrowLayoutResult(CardIsContained: true, RelationEditorIsUsable: true);
        }
        finally
        {
            CloseWindow(window);
            await fixture.CleanTasksAsync();
        }
    }

    private static void OpenAndAssertParentRelationEditor(MainControl view, Control? relativeTo)
    {
        var parentsAddButton = FindControlByAutomationId<Button>(view, "CurrentTaskParentsRelationAddButton");
        parentsAddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        RunLayoutJobs();

        foreach (var automationId in RelationEditorAutomationIds)
        {
            var control = FindControlByAutomationId<Control>(view, automationId);
            AssertVisibleAndArranged(control, automationId);
            if (relativeTo is not null)
            {
                AssertHorizontallyContained(relativeTo, control, automationId);
            }
        }
    }

    private static async Task<(MainControl View, Window Window)> CreateArrangedMainControlAsync(
        MainWindowViewModelFixture fixture,
        double width,
        double height)
    {
        var vm = fixture.MainWindowViewModelTest;
        vm.Settings.LanguageMode = LocalizationService.EnglishLanguage;
        vm.Settings.FontSize = AppearanceSettings.DefaultFontSize;
        await vm.Connect();
        vm.AllTasksMode = true;
        vm.DetailsAreOpen = true;
        TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RootTask2Id);

        var view = new MainControl
        {
            DataContext = vm,
            Width = width,
            Height = height
        };
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = view
        };

        window.Show();
        try
        {
            ArrangeMainControlForTest(window, view, width, height);
            EnsureDetailsPaneArranged(window, view, width, height);
        }
        catch
        {
            window.Close();
            throw;
        }

        return (view, window);
    }

    private static void ResetSharedUiState()
    {
        var localization = new LocalizationService(new FakeSystemCultureProvider("en-US"));
        LocalizationService.Current = localization;
        localization.SetLanguage(LocalizationService.EnglishLanguage);

        if (Application.Current is not { } application)
        {
            return;
        }

        foreach (var key in LocalizationService.Current.GetResourceKeys(CultureInfo.InvariantCulture))
        {
            application.Resources[key] = LocalizationService.Current.Get(key);
        }

        application.Resources["AppFontSize"] = AppearanceSettings.DefaultFontSize;
        application.Resources["AppSmallFontSize"] = AppearanceSettings.DefaultSmallFontSize;
        application.Resources["AppTabFontSize"] = AppearanceSettings.DefaultTabFontSize;
        application.Resources["AppTabMinHeight"] = AppearanceSettings.DefaultTabMinHeight;
        application.Resources["AppSearchControlHeight"] = AppearanceSettings.DefaultSearchControlHeight;
        application.Resources["AppSearchClearButtonSize"] = AppearanceSettings.DefaultSearchClearButtonSize;
        application.Resources["AppSearchClearIconFontSize"] = AppearanceSettings.DefaultSearchClearIconFontSize;
        application.Resources["AppSearchBarMinWidth"] = AppearanceSettings.DefaultSearchBarMinWidth;
        application.Resources["AppFloatingControlMinHeight"] = AppearanceSettings.DefaultFloatingControlMinHeight;
    }

    private static void ArrangeMainControlForTest(Window window, MainControl view, double width, double height)
    {
        window.MinWidth = width;
        window.MinHeight = height;
        window.MaxWidth = width;
        window.MaxHeight = height;
        window.Width = width;
        window.Height = height;
        view.Width = width;
        view.Height = height;

        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        RunLayoutJobs();
    }

    private static void EnsureDetailsPaneArranged(Window window, MainControl view, double width, double height)
    {
        var splitView = view.GetVisualDescendants().OfType<SplitView>().FirstOrDefault();
        var scrollViewer = FindControlByAutomationId<ScrollViewer>(view, "CurrentTaskDetailsScrollViewer");
        ApplyDetailsPaneTestWidth(scrollViewer, width);
        UpdateTaskDetailsLayoutForTest(view);

        if (splitView is not null)
        {
            splitView.OpenPaneLength = Math.Min(width, 600d);
            splitView.IsPaneOpen = true;
        }

        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (scrollViewer.Bounds.Width > 100)
            {
                return;
            }

            if (view.DataContext is MainWindowViewModel viewModel)
            {
                viewModel.DetailsAreOpen = true;
            }

            ApplyDetailsPaneTestWidth(scrollViewer, width);
            if (splitView is not null)
            {
                splitView.OpenPaneLength = Math.Min(width, 600d);
                splitView.IsPaneOpen = true;
            }

            ArrangeMainControlForTest(window, view, width, height);
            UpdateTaskDetailsLayoutForTest(view);
        }

        throw new InvalidOperationException(
            $"Task details pane did not arrange to an open width: bounds={scrollViewer.Bounds}.");
    }

    private static void UpdateTaskDetailsLayoutForTest(MainControl view)
    {
        typeof(MainControl)
            .GetMethod("UpdateTaskDetailsLayout", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(view, null);
        RunLayoutJobs();
    }

    private static void ApplyDetailsPaneTestWidth(ScrollViewer scrollViewer, double width)
    {
        var detailsWidth = Math.Max(0d, Math.Min(width, 600d) - 20d);
        if (detailsWidth <= 100)
        {
            return;
        }

        scrollViewer.Width = detailsWidth;
        scrollViewer.MinWidth = detailsWidth;
        scrollViewer.MaxWidth = detailsWidth;
    }

    private static T FindControlByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        return root.GetVisualDescendants()
                   .OfType<T>()
                   .FirstOrDefault(candidate => string.Equals(
                       AutomationProperties.GetAutomationId(candidate),
                       automationId,
                       StringComparison.Ordinal))
               ?? throw new InvalidOperationException($"Control with AutomationId '{automationId}' was not found.");
    }

    private static void AssertVisibleAndArranged(Control control, string automationId)
    {
        if (!control.IsVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
        {
            throw new InvalidOperationException(
                $"{automationId} is not visible and arranged: visible={control.IsVisible}; bounds={control.Bounds}.");
        }
    }

    private static void AssertHorizontallyContained(Control relativeTo, Control control, string automationId)
    {
        var origin = control.TranslatePoint(new Point(0, 0), relativeTo)
                     ?? throw new InvalidOperationException($"Cannot translate {automationId} into the task details viewport.");
        var right = origin.X + control.Bounds.Width;

        if (origin.X < -1 || right > relativeTo.Bounds.Width + 1)
        {
            throw new InvalidOperationException(
                $"{automationId} overflows the task details viewport: left={origin.X:F1}; right={right:F1}; " +
                $"viewport={relativeTo.Bounds.Width:F1}.");
        }
    }

    private static void RunLayoutJobs()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void CloseWindow(Window? window)
    {
        if (window is null)
        {
            return;
        }

        window.Content = null;
        RunLayoutJobs();
        window.Close();
        RunLayoutJobs();
    }

    private sealed record NarrowLayoutResult(bool CardIsContained, bool RelationEditorIsUsable);

    private sealed class FakeSystemCultureProvider(string cultureName) : ILocalizationSystemCultureProvider
    {
        public CultureInfo SystemUICulture { get; } = CultureInfo.GetCultureInfo(cultureName);
    }
}
