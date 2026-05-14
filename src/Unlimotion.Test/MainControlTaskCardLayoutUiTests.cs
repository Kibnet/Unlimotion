using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.Views;

namespace Unlimotion.Test;

[ParallelLimiter<SharedUiStateParallelLimit>]
public class MainControlTaskCardLayoutUiTests
{
    private static readonly string[] SectionAutomationIds =
    [
        "CurrentTaskCard",
        "CurrentTaskHeader",
        "CurrentTaskCommandBar",
        "CurrentTaskDescriptionSection",
        "CurrentTaskPlanningSection",
        "CurrentTaskRepeaterSection",
        "CurrentTaskRelationsSection"
    ];

    private static readonly string[] KeyControlAutomationIds =
    [
        "CurrentTaskCompletedCheckBox",
        "CurrentTaskTitleTextBox",
        "CurrentTaskWantedCheckBox",
        "CurrentTaskImportanceInput",
        "CurrentTaskArchiveButton",
        "CurrentTaskDescriptionTextBox",
        "CurrentTaskPlannedBeginPicker",
        "CurrentTaskPlannedDurationTextBox",
        "CurrentTaskPlannedEndPicker"
    ];

    [Test]
    public async Task CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, 1400, 900);
                window = createdWindow;

                foreach (var automationId in SectionAutomationIds.Concat(KeyControlAutomationIds))
                {
                    var control = FindControlByAutomationId<Control>(view, automationId);
                    await Assert.That(IsVisibleAndArranged(control)).IsTrue();
                }

                var relations = FindControlByAutomationId<Control>(view, "CurrentTaskRelationsSection");
                var parentsAddButton = FindControlByAutomationId<Button>(relations, "CurrentTaskParentsRelationAddButton");
                var parentsTree = FindControlByAutomationId<TreeView>(relations, "CurrentItemParentsTree");

                await Assert.That(IsVisibleAndArranged(parentsAddButton)).IsTrue();
                await Assert.That(parentsTree).IsNotNull();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CurrentTaskCard_PhoneWidthLayout_DoesNotOverflowAndKeepsRelationEditorUsable()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, 390, 844);
                window = createdWindow;

                var scrollViewer = FindControlByAutomationId<ScrollViewer>(view, "CurrentTaskDetailsScrollViewer");
                var card = FindControlByAutomationId<Control>(view, "CurrentTaskCard");

                AssertNoHorizontalOverflow(scrollViewer, card);

                foreach (var automationId in KeyControlAutomationIds)
                {
                    var control = FindControlByAutomationId<Control>(card, automationId);
                    await Assert.That(control.Bounds.Width).IsGreaterThan(0);
                    await Assert.That(control.Bounds.Height).IsGreaterThan(0);
                }

                var parentsAddButton = FindControlByAutomationId<Button>(card, "CurrentTaskParentsRelationAddButton");
                parentsAddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                RunLayoutJobs();

                var relationInput = FindControlByAutomationId<TextBox>(card, "CurrentTaskParentsRelationAddInput");
                var relationSuggestions = FindControlByAutomationId<ListBox>(card, "CurrentTaskParentsRelationSuggestions");
                var relationCancel = FindControlByAutomationId<Button>(card, "CurrentTaskParentsRelationAddCancelButton");

                await Assert.That(IsVisibleAndArranged(relationInput)).IsTrue();
                await Assert.That(IsVisibleAndArranged(relationSuggestions)).IsTrue();
                await Assert.That(IsVisibleAndArranged(relationCancel)).IsTrue();

                AssertNoHorizontalOverflow(scrollViewer, card);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    private static async Task<(MainControl View, Window Window)> CreateArrangedMainControlAsync(
        MainWindowViewModelFixture fixture,
        double width,
        double height)
    {
        var vm = fixture.MainWindowViewModelTest;
        await vm.Connect();
        vm.AllTasksMode = true;
        vm.DetailsAreOpen = true;
        TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RootTask2Id);

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

    private static void AssertNoHorizontalOverflow(Visual relativeTo, Control root)
    {
        var overflowingControls = root.GetVisualDescendants()
            .OfType<Control>()
            .Where(IsVisibleAndArranged)
            .Where(control => !IsTemplatePartInsideInputControl(control))
            .Select(control => new
            {
                Control = control,
                RightEdge = GetRightEdge(relativeTo, control)
            })
            .Where(item => item.RightEdge > ((Control)relativeTo).Bounds.Width + 1)
            .Select(item =>
                $"{item.Control.GetType().Name}:{item.Control.Name} " +
                $"right={item.RightEdge:F1} width={item.Control.Bounds.Width:F1}")
            .ToList();

        if (overflowingControls.Count > 0)
        {
            throw new InvalidOperationException(
                "Visible task card controls overflow the phone-width details pane: " +
                string.Join("; ", overflowingControls));
        }
    }

    private static bool IsTemplatePartInsideInputControl(Control control)
    {
        if (control is TextBox or ComboBox or NumericUpDown or CalendarDatePicker or DropDownButton)
        {
            return false;
        }

        return control.GetVisualAncestors()
            .Any(ancestor => ancestor is TextBox or ComboBox or NumericUpDown or CalendarDatePicker or DropDownButton);
    }

    private static bool IsVisibleAndArranged(Control control)
    {
        return control.IsVisible &&
               control.Bounds.Width > 0 &&
               control.Bounds.Height > 0;
    }

    private static double GetRightEdge(Visual relativeTo, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width, 0), relativeTo);
        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate point for control {control.GetType().Name}.");
        }

        return point.Value.X;
    }

    private static void RunLayoutJobs()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }
}
