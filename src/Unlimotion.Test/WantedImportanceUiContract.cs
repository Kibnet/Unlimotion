using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion.Test;

internal static class WantedImportanceUiContract
{
    public static async Task<WantedImportanceScenarioResult> ExecuteWantedImportanceScenarioAsync()
    {
        var result = new WantedImportanceScenarioResult();
        var session = HeadlessUnitTestSession.StartNew(typeof(App));

        try
        {
            await session.DispatchAsync(async () =>
            {
                var fixture = new MainWindowViewModelFixture();
                Window? window = null;

                try
                {
                    var vm = fixture.MainWindowViewModelTest;
                    await vm.Connect();
                    vm.AllTasksMode = true;
                    vm.DetailsAreOpen = true;
                    vm.ShowWanted = null;

                    foreach (var task in vm.taskRepository!.Tasks.Items)
                    {
                        task.Wanted = false;
                        task.Importance = 0;
                    }

                    var currentTask = await vm.taskRepository.Add();
                    currentTask.Title = "Wanted Importance BDD target";
                    currentTask.Wanted = false;
                    currentTask.Importance = 0;
                    vm.CurrentTaskItem = currentTask;
                    vm.SelectCurrentTask();

                    var view = new MainControl { DataContext = vm };
                    window = CreateWindow(view);
                    window.Show();
                    window.Activate();
                    RunLayoutJobs();
                    result.MainControlOpened = true;

                    var wantedCheckBox = FindControlByAutomationId<CheckBox>(
                        view,
                        "CurrentTaskWantedCheckBox");
                    var importanceInput = FindControlByAutomationId<NumericUpDown>(
                        view,
                        "CurrentTaskImportanceInput");

                    result.ControlsAvailable = wantedCheckBox is not null && importanceInput is not null;
                    result.ControlsBoundToCurrentTask =
                        ReferenceEquals(wantedCheckBox!.DataContext, currentTask) &&
                        ReferenceEquals(importanceInput!.DataContext, currentTask);

                    wantedCheckBox!.IsChecked = true;
                    RunLayoutJobs();
                    result.WantedCheckboxUpdatesTask = await WaitUntilAsync(() =>
                        currentTask.Wanted && wantedCheckBox.IsChecked == true);

                    importanceInput!.Value = 42;
                    RunLayoutJobs();
                    result.ImportanceInputUpdatesTask = await WaitUntilAsync(() =>
                        currentTask.Importance == 42);

                    var titleTextBlock = WaitForTaskTitleTextBlock(
                        view,
                        currentTask.Id,
                        "AllTasksTree",
                        currentTask.Title);
                    result.WantedTitleUsesBoldPresentation =
                        titleTextBlock.FontWeight == FontWeight.Bold;

                    vm.AllTasksMode = false;
                    vm.GraphMode = true;
                    vm.Graph.OnlyUnlocked = true;
                    var graphControl = OpenRoadmapTabAndWaitForGraphControl(view);

                    vm.ShowWanted = true;
                    RunLayoutJobs();
                    result.WantedFilterIncludesOnlyWantedTasks = await WaitUntilAsync(() =>
                        graphControl.RoadmapNodes.Any(node => node.Id == currentTask.Id) &&
                        graphControl.RoadmapNodes.All(node => node.TaskItem.Wanted));

                    vm.ShowWanted = false;
                    RunLayoutJobs();
                    result.NotWantedFilterExcludesWantedTask = await WaitUntilAsync(() =>
                        graphControl.RoadmapNodes.Any() &&
                        graphControl.RoadmapNodes.All(node => !node.TaskItem.Wanted) &&
                        graphControl.RoadmapNodes.All(node => node.Id != currentTask.Id));

                    result.ImportanceSortDefinitionsAvailable =
                        vm.SortDefinitions.Any(definition => definition.Id == "importance-ascending") &&
                        vm.SortDefinitions.Any(definition => definition.Id == "importance-descending");
                }
                finally
                {
                    await CloseWindowAndDrainAsync(window);
                    await fixture.CleanTasksAsync();
                    await DrainUiThreadAsync();
                }
            }, CancellationToken.None);
        }
        finally
        {
            await session.DisposeIgnoringHeadlessTeardownNullReferenceAsync();
        }

        return result;
    }

    public static async Task AssertWantedImportanceScenarioResultAsync(
        WantedImportanceScenarioResult result)
    {
        await Assert.That(result.MainControlOpened).IsTrue();
        await Assert.That(result.ControlsAvailable).IsTrue();
        await Assert.That(result.ControlsBoundToCurrentTask).IsTrue();
        await Assert.That(result.WantedCheckboxUpdatesTask).IsTrue();
        await Assert.That(result.ImportanceInputUpdatesTask).IsTrue();
        await Assert.That(result.WantedTitleUsesBoldPresentation).IsTrue();
        await Assert.That(result.WantedFilterIncludesOnlyWantedTasks).IsTrue();
        await Assert.That(result.NotWantedFilterExcludesWantedTask).IsTrue();
        await Assert.That(result.ImportanceSortDefinitionsAvailable).IsTrue();
    }

    private static Window CreateWindow(Control content)
    {
        return new Window
        {
            Width = 1400,
            Height = 900,
            Content = content
        };
    }

    private static T FindControlByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        var control = root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(candidate => string.Equals(
                AutomationProperties.GetAutomationId(candidate),
                automationId,
                StringComparison.Ordinal));

        if (control is null)
        {
            throw new InvalidOperationException(
                $"Control with automation id '{automationId}' was not found.");
        }

        return control;
    }

    private static TextBlock WaitForTaskTitleTextBlock(
        Control root,
        string taskId,
        string ancestorAutomationId,
        string title,
        int timeoutMilliseconds = 3000)
    {
        TextBlock? textBlock = null;
        var ready = SpinWait.SpinUntil(() =>
        {
            RunLayoutJobs();
            textBlock = root.GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(candidate =>
                    candidate.FindAncestorOfType<Control>(includeSelf: true) is { } control &&
                    HasAncestorWithAutomationId(control, ancestorAutomationId))
                .FirstOrDefault(candidate =>
                    candidate.DataContext is TaskItemViewModel task &&
                    task.Id == taskId &&
                    MatchesTitle(candidate, title));

            return textBlock != null;
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));

        if (!ready || textBlock == null)
        {
            throw new InvalidOperationException($"Title TextBlock for task '{taskId}' was not found.");
        }

        return textBlock;
    }

    private static bool MatchesTitle(TextBlock textBlock, string title)
    {
        if (textBlock.Text == title)
        {
            return true;
        }

        if (textBlock is not EmojiTextBlock emojiTextBlock)
        {
            return false;
        }

        var inlines = emojiTextBlock.Inlines;
        return emojiTextBlock.EmojiText == title ||
               (inlines != null && string.Concat(inlines.OfType<Run>().Select(run => run.Text)) == title);
    }

    private static bool HasAncestorWithAutomationId(Control control, string automationId)
    {
        for (Control? current = control; current != null; current = current.Parent as Control)
        {
            if (AutomationProperties.GetAutomationId(current) == automationId)
            {
                return true;
            }
        }

        return false;
    }

    private static GraphControl OpenRoadmapTabAndWaitForGraphControl(
        MainControl root,
        int timeoutMilliseconds = 3000)
    {
        var roadmapTab = FindControlByAutomationId<TabItem>(
            root,
            "RoadmapTabItem");

        roadmapTab.IsSelected = true;
        GraphControl? graphControl = null;
        var ready = SpinWait.SpinUntil(() =>
        {
            RunLayoutJobs();
            graphControl = root.GetVisualDescendants().OfType<GraphControl>().FirstOrDefault();
            return graphControl != null && graphControl.RoadmapNodes.Count > 0;
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));

        if (!ready || graphControl == null)
        {
            throw new InvalidOperationException("Roadmap graph control was not found.");
        }

        return graphControl;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate)
    {
        return await TestHelpers.WaitUntilAsync(
            () =>
            {
                RunLayoutJobs();
                return predicate();
            },
            TimeSpan.FromSeconds(5));
    }

    private static void RunLayoutJobs()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task CloseWindowAndDrainAsync(Window? window)
    {
        if (window == null)
        {
            return;
        }

        var root = window.Content as Control;
        window.Content = null;
        if (root != null)
        {
            root.DataContext = null;
        }

        RunLayoutJobs();
        window.Close();
        await DrainUiThreadAsync();
    }

    private static async Task DrainUiThreadAsync(int quietMilliseconds = 200)
    {
        var drainUntil = DateTime.UtcNow.AddMilliseconds(quietMilliseconds);
        do
        {
            RunLayoutJobs();
            await Task.Delay(25);
        }
        while (DateTime.UtcNow < drainUntil);

        RunLayoutJobs();
    }
}

internal sealed class WantedImportanceScenarioResult
{
    public bool MainControlOpened { get; set; }

    public bool ControlsAvailable { get; set; }

    public bool ControlsBoundToCurrentTask { get; set; }

    public bool WantedCheckboxUpdatesTask { get; set; }

    public bool ImportanceInputUpdatesTask { get; set; }

    public bool WantedTitleUsesBoldPresentation { get; set; }

    public bool WantedFilterIncludesOnlyWantedTasks { get; set; }

    public bool NotWantedFilterExcludesWantedTask { get; set; }

    public bool ImportanceSortDefinitionsAvailable { get; set; }
}
