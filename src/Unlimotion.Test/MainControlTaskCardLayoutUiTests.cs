using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.Domain;
using Unlimotion.ViewModel;
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
        "CurrentTaskIdTextBlock",
        "CurrentTaskDescriptionTextBox",
        "CurrentTaskPlannedBeginPicker",
        "CurrentTaskSetBeginButton",
        "CurrentTaskPlannedDurationTextBox",
        "CurrentTaskSetDurationButton",
        "CurrentTaskPlannedEndPicker",
        "CurrentTaskSetEndButton",
        "CurrentTaskRepeaterSelector"
    ];

    private static readonly string[] PlanningControlAutomationIds =
    [
        "CurrentTaskPlannedBeginPicker",
        "CurrentTaskSetBeginButton",
        "CurrentTaskPlannedDurationTextBox",
        "CurrentTaskSetDurationButton",
        "CurrentTaskPlannedEndPicker",
        "CurrentTaskSetEndButton"
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
                    AssertVisibleAndArranged(control, automationId);
                }

                var createButton = FindControlByAutomationId<Button>(view, "CurrentTaskCreateButton");
                var removeButton = FindControlByAutomationId<Button>(view, "CurrentTaskRemoveButton");
                var archiveButton = FindControlByAutomationId<Button>(view, "CurrentTaskArchiveButton");
                var descriptionTextBox = FindControlByAutomationId<TextBox>(view, "CurrentTaskDescriptionTextBox");
                var setBeginButton = FindControlByAutomationId<DropDownButton>(view, "CurrentTaskSetBeginButton");
                var setDurationButton = FindControlByAutomationId<DropDownButton>(view, "CurrentTaskSetDurationButton");
                var setEndButton = FindControlByAutomationId<DropDownButton>(view, "CurrentTaskSetEndButton");

                AssertHasClass(createButton, "TaskCommandPrimaryButton");
                AssertHasClass(removeButton, "TaskCommandDangerButton");
                AssertHasClass(archiveButton, "TaskHeaderArchiveButton");
                AssertHasClass(descriptionTextBox, "TaskDescriptionEditor");
                AssertHasClass(setBeginButton, "TaskPlanningQuickAction");
                AssertHasClass(setDurationButton, "TaskPlanningQuickAction");
                AssertHasClass(setEndButton, "TaskPlanningQuickAction");

                AssertDesktopPlanningGroupsStayCompactRow(view);
                AssertDesktopRepeaterControlsStayCompact(view);

                var relations = FindControlByAutomationId<Control>(view, "CurrentTaskRelationsSection");
                var parentsAddButton = FindControlByAutomationId<Button>(relations, "CurrentTaskParentsRelationAddButton");
                var parentsTree = FindControlByAutomationId<TreeView>(relations, "CurrentItemParentsTree");

                await Assert.That(IsVisibleAndArranged(parentsAddButton)).IsTrue();
                AssertHasClass(parentsAddButton, "RelationAddButton");
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
    public async Task CurrentTaskCard_DesktopRepeaterLayout_UsesCompactControls()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(
                    fixture,
                    1400,
                    900,
                    MainWindowViewModelFixture.RepeateTask9Id,
                    task =>
                    {
                        task.Repeater!.Type = RepeaterType.Weekly;
                        task.Repeater.WorkDays = true;
                    });
                window = createdWindow;

                AssertDesktopRepeaterControlsStayCompact(view, requireWeekdayToggles: true);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments(360)]
    [Arguments(390)]
    [Arguments(430)]
    public async Task CurrentTaskCard_PhoneWidthLayout_DoesNotOverflowAndKeepsRelationEditorUsable(double width)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var (view, createdWindow) = await CreateArrangedMainControlAsync(fixture, width, 844);
                window = createdWindow;

                var scrollViewer = FindControlByAutomationId<ScrollViewer>(view, "CurrentTaskDetailsScrollViewer");
                var card = FindControlByAutomationId<Control>(view, "CurrentTaskCard");
                var commandBar = FindControlByAutomationId<Control>(view, "CurrentTaskCommandBar");
                var header = FindControlByAutomationId<Control>(card, "CurrentTaskHeader");
                var title = FindControlByAutomationId<Control>(card, "CurrentTaskTitleTextBox");
                var createButton = FindControlByAutomationId<Button>(view, "CurrentTaskCreateButton");
                var removeButton = FindControlByAutomationId<Button>(view, "CurrentTaskRemoveButton");
                var moreActions = FindControlByAutomationId<Control>(view, "CurrentTaskMoreActionsButton");

                AssertNoHorizontalOverflow(scrollViewer, card);
                AssertFirstPhoneViewportShowsHeader(scrollViewer, commandBar, header, title);
                AssertHasClass(createButton, "TaskCommandPrimaryButton");
                await Assert.That(IsVisibleAndArranged(moreActions)).IsTrue();
                await Assert.That(IsVisibleAndArranged(removeButton)).IsFalse();

                foreach (var automationId in KeyControlAutomationIds)
                {
                    var control = FindControlByAutomationId<Control>(card, automationId);
                    await Assert.That(control.Bounds.Width).IsGreaterThan(0);
                    await Assert.That(control.Bounds.Height).IsGreaterThan(0);
                    AssertHorizontallyContained(scrollViewer, control);
                }

                var parentsAddButton = FindControlByAutomationId<Button>(card, "CurrentTaskParentsRelationAddButton");
                AssertHasClass(parentsAddButton, "RelationAddButton");
                parentsAddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                RunLayoutJobs();

                var relationInput = FindControlByAutomationId<TextBox>(card, "CurrentTaskParentsRelationAddInput");
                var relationSuggestions = FindControlByAutomationId<ListBox>(card, "CurrentTaskParentsRelationSuggestions");
                var relationCancel = FindControlByAutomationId<Button>(card, "CurrentTaskParentsRelationAddCancelButton");
                var relationConfirm = FindControlByAutomationId<Button>(card, "CurrentTaskParentsRelationAddConfirmButton");

                await Assert.That(IsVisibleAndArranged(relationInput)).IsTrue();
                await Assert.That(IsVisibleAndArranged(relationSuggestions)).IsTrue();
                await Assert.That(IsVisibleAndArranged(relationCancel)).IsTrue();
                await Assert.That(IsVisibleAndArranged(relationConfirm)).IsTrue();
                AssertHorizontallyContained(scrollViewer, relationInput);
                AssertHorizontallyContained(scrollViewer, relationSuggestions);
                AssertHorizontallyContained(scrollViewer, relationCancel);
                AssertHorizontallyContained(scrollViewer, relationConfirm);

                AssertNoHorizontalOverflow(scrollViewer, card);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    private static void AssertFirstPhoneViewportShowsHeader(
        ScrollViewer scrollViewer,
        Control commandBar,
        Control header,
        Control title)
    {
        var commandBarBottom = GetBottomEdge(scrollViewer, commandBar);
        var headerTop = GetTopEdge(scrollViewer, header);
        var titleTop = GetTopEdge(scrollViewer, title);

        if (commandBarBottom > 72)
        {
            throw new InvalidOperationException(
                $"Phone command bar consumes too much first viewport height: bottom={commandBarBottom:F1}.");
        }

        if (headerTop >= scrollViewer.Bounds.Height || titleTop >= scrollViewer.Bounds.Height)
        {
            throw new InvalidOperationException(
                $"Task card header is not visible in the first phone viewport: " +
                $"headerTop={headerTop:F1}; titleTop={titleTop:F1}; viewport={scrollViewer.Bounds.Height:F1}.");
        }
    }

    private static async Task<(MainControl View, Window Window)> CreateArrangedMainControlAsync(
        MainWindowViewModelFixture fixture,
        double width,
        double height,
        string selectedTaskId = MainWindowViewModelFixture.RootTask2Id,
        Action<TaskItemViewModel>? configureCurrentTask = null)
    {
        var vm = fixture.MainWindowViewModelTest;
        await vm.Connect();
        vm.AllTasksMode = true;
        vm.DetailsAreOpen = true;
        var currentTask = TestHelpers.SetCurrentTask(vm, selectedTaskId);
        configureCurrentTask?.Invoke(currentTask);

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

    private static void AssertHasClass(Control control, string className)
    {
        if (!control.Classes.Contains(className))
        {
            throw new InvalidOperationException(
                $"{control.GetType().Name}:{AutomationProperties.GetAutomationId(control)} " +
                $"does not have expected class '{className}'.");
        }
    }

    private static void AssertVisibleAndArranged(Control control, string automationId)
    {
        if (!IsVisibleAndArranged(control))
        {
            throw new InvalidOperationException(
                $"{control.GetType().Name}:{automationId} is not visible and arranged: " +
                $"visible={control.IsVisible}; bounds={control.Bounds}.");
        }
    }

    private static void AssertDesktopPlanningGroupsStayCompactRow(Control root)
    {
        var planningControls = PlanningControlAutomationIds
            .Select(automationId => FindControlByAutomationId<Control>(root, automationId))
            .ToArray();

        var topEdges = planningControls
            .Select(control => GetTopEdge(root, control))
            .ToArray();
        var bottomEdges = planningControls
            .Select(control => GetBottomEdge(root, control))
            .ToArray();
        var widths = planningControls
            .Select(control => control.Bounds.Width)
            .ToArray();

        var beginTop = topEdges[0];
        var durationTop = topEdges[2];
        var endTop = topEdges[4];
        if (Math.Abs(beginTop - durationTop) > 2 || Math.Abs(beginTop - endTop) > 2)
        {
            throw new InvalidOperationException(
                "Desktop planning fields should stay in one compact row: " +
                $"beginTop={beginTop:F1}; durationTop={durationTop:F1}; endTop={endTop:F1}.");
        }

        var maxPlanningControlWidth = widths.Max();
        if (maxPlanningControlWidth > 260)
        {
            throw new InvalidOperationException(
                "Desktop planning controls are too wide for a compact three-column row: " +
                string.Join("; ", PlanningControlAutomationIds.Zip(widths, (id, width) => $"{id}={width:F1}")));
        }

        var beginActionTop = topEdges[1];
        var durationActionTop = topEdges[3];
        var endActionTop = topEdges[5];
        if (beginActionTop <= bottomEdges[0] || durationActionTop <= bottomEdges[2] || endActionTop <= bottomEdges[4])
        {
            throw new InvalidOperationException("Desktop planning quick actions should sit below their matching value controls.");
        }
    }

    private static void AssertDesktopRepeaterControlsStayCompact(Control root, bool requireWeekdayToggles = false)
    {
        var repeaterSelector = FindControlByAutomationId<ComboBox>(root, "CurrentTaskRepeaterSelector");
        if (repeaterSelector.Bounds.Width > 300)
        {
            throw new InvalidOperationException(
                $"Desktop repeater selector is too wide: width={repeaterSelector.Bounds.Width:F1}.");
        }

        var weekdayToggles = root.GetVisualDescendants()
            .OfType<ToggleButton>()
            .Where(static toggle => toggle.Classes.Contains("WeekdayToggle"))
            .Where(IsVisibleAndArranged)
            .ToArray();

        if (requireWeekdayToggles && weekdayToggles.Length != 7)
        {
            throw new InvalidOperationException(
                $"Expected seven visible desktop weekday toggles, found {weekdayToggles.Length}.");
        }

        if (requireWeekdayToggles)
        {
            var firstTop = GetTopEdge(root, weekdayToggles[0]);
            var wrappedToggle = weekdayToggles
                .Select(toggle => new { Toggle = toggle, Top = GetTopEdge(root, toggle) })
                .FirstOrDefault(item => Math.Abs(item.Top - firstTop) > 2);
            if (wrappedToggle is not null)
            {
                throw new InvalidOperationException(
                    $"Desktop weekday toggles should stay in one compact row: " +
                    $"content={wrappedToggle.Toggle.Content}; firstTop={firstTop:F1}; top={wrappedToggle.Top:F1}.");
            }
        }

        foreach (var toggle in weekdayToggles)
        {
            if (toggle.Bounds.Width > 64 || toggle.Bounds.Height > 36)
            {
                throw new InvalidOperationException(
                    $"Desktop weekday toggle is too large: content={toggle.Content}; bounds={toggle.Bounds}.");
            }
        }
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

    private static void AssertHorizontallyContained(Visual relativeTo, Control control)
    {
        var leftEdge = GetLeftEdge(relativeTo, control);
        var rightEdge = GetRightEdge(relativeTo, control);
        var viewportWidth = ((Control)relativeTo).Bounds.Width;

        if (leftEdge < -1 || rightEdge > viewportWidth + 1)
        {
            throw new InvalidOperationException(
                $"{control.GetType().Name}:{AutomationProperties.GetAutomationId(control)} is not fully contained " +
                $"in the phone-width details pane: left={leftEdge:F1}; right={rightEdge:F1}; viewport={viewportWidth:F1}.");
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

    private static double GetLeftEdge(Visual relativeTo, Control control)
    {
        var point = control.TranslatePoint(new Point(0, 0), relativeTo);
        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate left edge for control {control.GetType().Name}.");
        }

        return point.Value.X;
    }

    private static double GetTopEdge(Visual relativeTo, Control control)
    {
        var point = control.TranslatePoint(new Point(0, 0), relativeTo);
        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate top edge for control {control.GetType().Name}.");
        }

        return point.Value.Y;
    }

    private static double GetBottomEdge(Visual relativeTo, Control control)
    {
        var point = control.TranslatePoint(new Point(0, control.Bounds.Height), relativeTo);
        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate bottom edge for control {control.GetType().Name}.");
        }

        return point.Value.Y;
    }

    private static void RunLayoutJobs()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }
}
