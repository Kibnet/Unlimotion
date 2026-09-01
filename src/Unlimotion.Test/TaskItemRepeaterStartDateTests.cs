using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion.Test;

public class TaskItemRepeaterStartDateTests
{
    [Test]
    public async Task HydratedRepeater_SelectsStableTemplateWithoutChangingPattern()
    {
        using var storage = new UnifiedTaskStorage(new TaskTreeManager(new InMemoryStorage()));
        var model = CreateModel(RepeaterType.Daily, afterComplete: true);
        model.Repeater!.Period = 3;
        using var task = new TaskItemViewModel(model, storage, () => false);

        var templates = task.Repeaters;
        var dailyTemplate = templates.Single(template => template.Type == RepeaterType.Daily);

        using (Assert.Multiple())
        {
            await Assert.That(task.Repeaters).IsSameReferenceAs(templates);
            await Assert.That(task.SelectedRepeaterTemplate).IsSameReferenceAs(dailyTemplate);
            await Assert.That(task.Repeater!.Period).IsEqualTo(3);
            await Assert.That(task.Repeater.AfterComplete).IsTrue();
        }
    }

    [Test]
    public async Task WeeklyTemplateSelection_DistinguishesExactWorkDaysFromCustomPattern()
    {
        using var storage = new UnifiedTaskStorage(new TaskTreeManager(new InMemoryStorage()));
        var workDaysModel = CreateModel();
        workDaysModel.Repeater!.Pattern = [0, 1, 2, 3, 4];
        using var workDays = new TaskItemViewModel(workDaysModel, storage, () => false);

        var workDaysTemplate = workDays.Repeaters[2];
        await Assert.That(workDays.SelectedRepeaterTemplate).IsSameReferenceAs(workDaysTemplate);

        var customModel = CreateModel();
        customModel.Repeater!.Pattern = [0, 5];
        using var custom = new TaskItemViewModel(customModel, storage, () => false);
        await Assert.That(custom.SelectedRepeaterTemplate).IsSameReferenceAs(custom.Repeaters[3]);
    }

    [Test]
    public async Task SelectingTemplate_CopiesItIntoEditableRepeater()
    {
        using var storage = new UnifiedTaskStorage(new TaskTreeManager(new InMemoryStorage()));
        using var task = new TaskItemViewModel(CreateModel(), storage, () => false);
        var dailyTemplate = task.Repeaters.Single(template => template.Type == RepeaterType.Daily);

        task.SelectedRepeaterTemplate = dailyTemplate;
        task.Repeater!.Period = 4;

        using (Assert.Multiple())
        {
            await Assert.That(task.Repeater).IsNotSameReferenceAs(dailyTemplate);
            await Assert.That(task.Repeater.Type).IsEqualTo(RepeaterType.Daily);
            await Assert.That(dailyTemplate.Period).IsEqualTo(1);
        }
    }

    [Test]
    [Arguments(RepeaterType.Daily, false)]
    [Arguments(RepeaterType.Weekly, true)]
    [Arguments(RepeaterType.None, false)]
    public async Task ClearingStart_DropsEntireRepeater_WithoutChangingOtherPlanningFields(
        RepeaterType type, bool afterComplete)
    {
        using var storage = new UnifiedTaskStorage(new TaskTreeManager(new InMemoryStorage()));
        using var task = new TaskItemViewModel(CreateModel(type, afterComplete), storage, () => false);
        var end = task.PlannedEndDateTime;
        var duration = task.PlannedDuration;
        var previousRepeater = task.Repeater!;

        task.PlannedBeginDateTime = null;

        await Assert.That(task.Repeater).IsNull();
        await Assert.That(task.Model.Repeater).IsNull();
        await Assert.That(task.IsHaveRepeater).IsFalse();
        await Assert.That(task.RepeaterListMarker).IsEqualTo(string.Empty);
        await Assert.That(task.PlannedEndDateTime).IsEqualTo(end);
        await Assert.That(task.PlannedDuration).IsEqualTo(duration);

        var notifications = new List<string?>();
        ((INotifyPropertyChanged)task).PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        previousRepeater.Period = 9;
        await Assert.That(notifications).DoesNotContain(nameof(TaskItemViewModel.RepeaterListMarker));

        task.PlannedBeginDateTime = DateTime.Today.AddDays(2);
        await Assert.That(task.Repeater).IsNull();
    }

    [Test]
    public async Task ClearingStart_WithNoRepeater_IsSafe()
    {
        using var storage = new UnifiedTaskStorage(new TaskTreeManager(new InMemoryStorage()));
        var model = CreateModel();
        model.Repeater = null;
        using var task = new TaskItemViewModel(model, storage, () => false);
        task.Commands.SetBeginNone.Execute(null);
        await Assert.That(task.PlannedBeginDateTime).IsNull();
        await Assert.That(task.Repeater).IsNull();
    }

    [Test]
    public async Task ChangingNonEmptyStart_PreservesRepeater()
    {
        using var storage = new UnifiedTaskStorage(new TaskTreeManager(new InMemoryStorage()));
        var model = CreateModel();
        using var task = new TaskItemViewModel(model, storage, () => false);
        task.PlannedBeginDateTime = DateTime.Today.AddDays(4);
        await Assert.That(task.Repeater!.Model.Equals(model.Repeater)).IsTrue();
    }

    [Test]
    public async Task LoadingAndHydratingLegacyTask_DoesNotErasePersistedRepeater()
    {
        using var storage = new UnifiedTaskStorage(new TaskTreeManager(new InMemoryStorage()));
        var model = CreateModel();
        using var task = new TaskItemViewModel(model, storage, () => false);
        var originalRepeater = task.Repeater;
        model.PlannedBeginDateTime = null;
        task.Update(model);
        await Assert.That(ReferenceEquals(task.Repeater, originalRepeater)).IsTrue();
        await Assert.That(task.Repeater!.Model.Equals(model.Repeater)).IsTrue();

        using var loaded = new TaskItemViewModel(model, storage, () => false);
        await Assert.That(loaded.Repeater!.Model.Equals(model.Repeater)).IsTrue();
        loaded.PlannedBeginDateTime = DateTime.Today;
        await Assert.That(loaded.Repeater!.Model.Equals(model.Repeater)).IsTrue();
    }

    private static TaskItem CreateModel(RepeaterType type = RepeaterType.Weekly, bool afterComplete = true) => new()
    {
        Id = "repeater-start-date",
        Title = "Repeatable task",
        PlannedBeginDateTime = DateTime.Today,
        PlannedEndDateTime = DateTime.Today.AddDays(1),
        PlannedDuration = TimeSpan.FromHours(2),
        Repeater = new RepeaterPattern { Type = type, Period = 3, Pattern = [0, 2, 4], AfterComplete = afterComplete }
    };
}
