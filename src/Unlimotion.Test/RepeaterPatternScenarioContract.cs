using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DynamicData;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion.Test;

internal static class RepeaterPatternScenarioContract
{
    private static readonly RepeaterType[] ExpectedTypes =
    [
        RepeaterType.None,
        RepeaterType.Daily,
        RepeaterType.Weekly,
        RepeaterType.Monthly,
        RepeaterType.Yearly
    ];

    private static readonly string ExpectedTypeKey = CreateTypeKey(ExpectedTypes);

    public static Task<RepeaterPatternScenarioResult> ExecuteRepeaterPatternScenarioAsync()
    {
        var result = new RepeaterPatternScenarioResult
        {
            RepeaterTypeOptionsExposeAllSupportedTypes = RepeaterTypeOptionsExposeAllSupportedTypes(),
            TaskCardRepeaterOptionsExposeAllSupportedModes = TaskCardRepeaterOptionsExposeAllSupportedModes(),
            ModelRoundTripPreservesPattern = ModelRoundTripPreservesPattern(),
            WeeklyWorkDaysShortcutMapsToPattern = WeeklyWorkDaysShortcutMapsToPattern(),
            DomainOccurrencesMatchSupportedTypes = DomainOccurrencesMatchSupportedTypes(),
            ViewModelOccurrencesMatchSupportedTypes = ViewModelOccurrencesMatchSupportedTypes(),
            AfterCompleteDomainModeSupported = AfterCompleteDomainModeSupported(),
            AfterCompleteViewModelModeSupported = AfterCompleteViewModelModeSupported(),
            ActiveRepeaterMarkerBehaviorMatchesMode = ActiveRepeaterMarkerBehaviorMatchesMode()
        };

        return Task.FromResult(result);
    }

    public static async Task AssertRepeaterPatternScenarioResultAsync(
        RepeaterPatternScenarioResult result)
    {
        await Assert.That(result.RepeaterTypeOptionsExposeAllSupportedTypes).IsTrue();
        await Assert.That(result.TaskCardRepeaterOptionsExposeAllSupportedModes).IsTrue();
        await Assert.That(result.ModelRoundTripPreservesPattern).IsTrue();
        await Assert.That(result.WeeklyWorkDaysShortcutMapsToPattern).IsTrue();
        await Assert.That(result.DomainOccurrencesMatchSupportedTypes).IsTrue();
        await Assert.That(result.ViewModelOccurrencesMatchSupportedTypes).IsTrue();
        await Assert.That(result.AfterCompleteDomainModeSupported).IsTrue();
        await Assert.That(result.AfterCompleteViewModelModeSupported).IsTrue();
        await Assert.That(result.ActiveRepeaterMarkerBehaviorMatchesMode).IsTrue();
    }

    private static bool RepeaterTypeOptionsExposeAllSupportedTypes()
    {
        var actualKey = CreateTypeKey(RepeaterTypeOption.Definitions.Select(option => option.Value));
        return string.Equals(actualKey, ExpectedTypeKey, StringComparison.Ordinal);
    }

    private static bool TaskCardRepeaterOptionsExposeAllSupportedModes()
    {
        using var task = CreateTask(null);
        var actualKey = CreateRepeaterOptionKey(task.Repeaters);
        var expectedKey = CreateRepeaterOptionKey(
        [
            new RepeaterPatternViewModel { Type = RepeaterType.None },
            new RepeaterPatternViewModel { Type = RepeaterType.Daily },
            new RepeaterPatternViewModel { Type = RepeaterType.Weekly, WorkDays = true },
            new RepeaterPatternViewModel { Type = RepeaterType.Weekly },
            new RepeaterPatternViewModel { Type = RepeaterType.Monthly },
            new RepeaterPatternViewModel { Type = RepeaterType.Yearly }
        ]);

        return string.Equals(actualKey, expectedKey, StringComparison.Ordinal);
    }

    private static bool ModelRoundTripPreservesPattern()
    {
        var source = new RepeaterPattern
        {
            Type = RepeaterType.Weekly,
            Period = 2,
            AfterComplete = true,
            Pattern = [0, 2, 4]
        };

        var viewModel = new RepeaterPatternViewModel(source);
        var model = viewModel.Model;

        return source.Equals(model) &&
               viewModel.Monday &&
               !viewModel.Tuesday &&
               viewModel.Wednesday &&
               !viewModel.Thursday &&
               viewModel.Friday &&
               !viewModel.Saturday &&
               !viewModel.Sunday;
    }

    private static bool WeeklyWorkDaysShortcutMapsToPattern()
    {
        var viewModel = new RepeaterPatternViewModel
        {
            Type = RepeaterType.Weekly,
            WorkDays = true
        };

        return viewModel.WorkDays &&
               viewModel.Model.Pattern.SequenceEqual([0, 1, 2, 3, 4]);
    }

    private static bool DomainOccurrencesMatchSupportedTypes()
    {
        var start = new DateTimeOffset(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);

        return Occurs(new RepeaterPattern { Type = RepeaterType.None, Period = 1 }, start, start) &&
               Occurs(new RepeaterPattern { Type = RepeaterType.Daily, Period = 3 }, start, start.AddDays(3)) &&
               Occurs(new RepeaterPattern { Type = RepeaterType.Weekly, Period = 2, Pattern = [] }, start, start.AddDays(14)) &&
               Occurs(new RepeaterPattern { Type = RepeaterType.Weekly, Period = 1, Pattern = [2] }, start, start.AddDays(2)) &&
               Occurs(new RepeaterPattern { Type = RepeaterType.Monthly, Period = 2 }, start, start.AddMonths(2)) &&
               Occurs(new RepeaterPattern { Type = RepeaterType.Yearly, Period = 1 }, start, start.AddYears(1));
    }

    private static bool ViewModelOccurrencesMatchSupportedTypes()
    {
        var start = new DateTimeOffset(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);

        return Occurs(new RepeaterPatternViewModel { Type = RepeaterType.None, Period = 1 }, start, start) &&
               Occurs(new RepeaterPatternViewModel { Type = RepeaterType.Daily, Period = 3 }, start, start.AddDays(3)) &&
               Occurs(new RepeaterPatternViewModel { Type = RepeaterType.Weekly, Period = 2 }, start, start.AddDays(14)) &&
               Occurs(new RepeaterPatternViewModel { Type = RepeaterType.Weekly, Period = 1, Wednesday = true }, start, start.AddDays(2)) &&
               Occurs(new RepeaterPatternViewModel { Type = RepeaterType.Monthly, Period = 2 }, start, start.AddMonths(2)) &&
               Occurs(new RepeaterPatternViewModel { Type = RepeaterType.Yearly, Period = 1 }, start, start.AddYears(1));
    }

    private static bool AfterCompleteDomainModeSupported()
    {
        var start = new DateTimeOffset(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);
        DateTimeOffset before = DateTimeOffset.Now.Date;
        var actual = new RepeaterPattern
        {
            Type = RepeaterType.Daily,
            Period = 1,
            AfterComplete = true
        }.GetNextOccurrence(start);
        DateTimeOffset after = DateTimeOffset.Now.Date;

        return actual == before.AddDays(1) || actual == after.AddDays(1);
    }

    private static bool AfterCompleteViewModelModeSupported()
    {
        var start = new DateTimeOffset(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);
        DateTimeOffset before = DateTimeOffset.Now.Date;
        var actual = new RepeaterPatternViewModel
        {
            Type = RepeaterType.Daily,
            Period = 1,
            AfterComplete = true
        }.GetNextOccurrence(start);
        DateTimeOffset after = DateTimeOffset.Now.Date;

        return actual == before.AddDays(1) || actual == after.AddDays(1);
    }

    private static bool ActiveRepeaterMarkerBehaviorMatchesMode()
    {
        using var taskWithoutRepeater = CreateTask(null);
        using var taskWithNoneRepeater = CreateTask(new RepeaterPattern
        {
            Type = RepeaterType.None,
            Period = 1
        });
        using var taskWithDailyRepeater = CreateTask(new RepeaterPattern
        {
            Type = RepeaterType.Daily,
            Period = 1
        });

        return !taskWithoutRepeater.IsHaveRepeater &&
               taskWithoutRepeater.RepeaterListMarker == string.Empty &&
               taskWithoutRepeater.RepeaterListMarkerToolTip is null &&
               !taskWithNoneRepeater.IsHaveRepeater &&
               taskWithNoneRepeater.RepeaterListMarker == string.Empty &&
               taskWithNoneRepeater.RepeaterListMarkerToolTip is null &&
               taskWithDailyRepeater.IsHaveRepeater &&
               taskWithDailyRepeater.RepeaterListMarker == "↻" &&
               taskWithDailyRepeater.RepeaterListMarkerToolTip == taskWithDailyRepeater.Repeater!.Title;
    }

    private static bool Occurs(RepeaterPattern repeater, DateTimeOffset start, DateTimeOffset expected) =>
        repeater.GetNextOccurrence(start) == expected;

    private static bool Occurs(RepeaterPatternViewModel repeater, DateTimeOffset start, DateTimeOffset expected) =>
        repeater.GetNextOccurrence(start) == expected;

    private static string CreateTypeKey(IEnumerable<RepeaterType> types)
    {
        return string.Join("|", types.Select(type => $"{type}:{(int)type}"));
    }

    private static string CreateRepeaterOptionKey(IEnumerable<RepeaterPatternViewModel> repeaters)
    {
        return string.Join(
            "|",
            repeaters.Select(repeater => $"{repeater.Type}:{repeater.WorkDays}:{repeater.AfterComplete}"));
    }

    private static TaskItemViewModel CreateTask(RepeaterPattern? repeater)
    {
        var model = new TaskItem
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Task",
            IsCompleted = false,
            IsCanBeCompleted = true,
            CreatedDateTime = DateTimeOffset.UtcNow
        };

        if (repeater != null)
        {
            model.Repeater = repeater;
        }

        return new TaskItemViewModel(model, new StubTaskStorage());
    }

    private sealed class StubTaskStorage : ITaskStorage
    {
        public SourceCache<TaskItemViewModel, string> Tasks { get; } = new(task => task.Id);

        public ITaskRelationsIndex Relations => throw new NotSupportedException();

        public TaskTreeManager TaskTreeManager => throw new NotSupportedException();

        public event EventHandler<EventArgs>? Initiated
        {
            add { }
            remove { }
        }

        public Task Init() => Task.CompletedTask;

        public Task<TaskItemViewModel> Add(TaskItemViewModel? currentTask = null, bool isBlocked = false) =>
            throw new NotSupportedException();

        public Task<TaskItemViewModel> AddChild(TaskItemViewModel currentTask) =>
            throw new NotSupportedException();

        public Task<bool> Delete(TaskItemViewModel change, bool deleteInStorage = true) =>
            throw new NotSupportedException();

        public Task<bool> Delete(TaskItemViewModel change, TaskItemViewModel parent) =>
            throw new NotSupportedException();

        public Task<TaskItemViewModel> Update(TaskItemViewModel change) => Task.FromResult(change);

        public Task<TaskItemViewModel> Update(TaskItem change) =>
            throw new NotSupportedException();

        public Task<TaskItemViewModel> Clone(TaskItemViewModel change, params TaskItemViewModel[]? additionalParents) =>
            throw new NotSupportedException();

        public Task<bool> CopyInto(TaskItemViewModel change, TaskItemViewModel[]? additionalParents) =>
            throw new NotSupportedException();

        public Task<bool> MoveInto(TaskItemViewModel change, TaskItemViewModel[] additionalParents, TaskItemViewModel? currentTask) =>
            throw new NotSupportedException();

        public Task<bool> Unblock(TaskItemViewModel taskToUnblock, TaskItemViewModel blockingTask) =>
            throw new NotSupportedException();

        public Task<bool> Block(TaskItemViewModel change, TaskItemViewModel currentTask) =>
            throw new NotSupportedException();

        public Task RemoveParentChildConnection(TaskItemViewModel parent, TaskItemViewModel child) =>
            throw new NotSupportedException();
    }
}

internal sealed class RepeaterPatternScenarioResult
{
    public bool RepeaterTypeOptionsExposeAllSupportedTypes { get; set; }

    public bool TaskCardRepeaterOptionsExposeAllSupportedModes { get; set; }

    public bool ModelRoundTripPreservesPattern { get; set; }

    public bool WeeklyWorkDaysShortcutMapsToPattern { get; set; }

    public bool DomainOccurrencesMatchSupportedTypes { get; set; }

    public bool ViewModelOccurrencesMatchSupportedTypes { get; set; }

    public bool AfterCompleteDomainModeSupported { get; set; }

    public bool AfterCompleteViewModelModeSupported { get; set; }

    public bool ActiveRepeaterMarkerBehaviorMatchesMode { get; set; }
}
