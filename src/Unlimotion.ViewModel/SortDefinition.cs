using System.Collections.Generic;
using DynamicData.Binding;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel;

public class SortDefinition
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string ResourceKey { get; set; } = string.Empty;

    public IComparer<TaskWrapperViewModel> Comparer { get; set; } = null!;

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(ResourceKey) ? Name : L10n.Get(ResourceKey);
    }

    public bool MatchesPersistedValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               (string.Equals(Id, value, System.StringComparison.Ordinal) ||
                string.Equals(Name, value, System.StringComparison.Ordinal));
    }

    public static IEnumerable<SortDefinition> GetDefinitions()
    {
        yield return Create("comfort", "Comfort", "SortComfort", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.CompletedDateTime),
            new(w => w.TaskItem.ArchiveDateTime),
            new(w => w.TaskItem.UnlockedDateTime),
            new(w => w.TaskItem.CreatedDateTime),
            new(w => w.TaskItem.UpdatedDateTime),
        });

        yield return Create("emoji", "Emodji", "SortEmoji", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.GetAllEmoji),
        });

        yield return Create("created-ascending", "Created Ascending", "SortCreatedAscending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.CreatedDateTime)
        });
        yield return Create("created-descending", "Created Descending", "SortCreatedDescending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.CreatedDateTime, SortDirection.Descending)
        });
        yield return Create("updated-ascending", "Updated Ascending", "SortUpdatedAscending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.UpdatedDateTime)
        });
        yield return Create("updated-descending", "Updated Descending", "SortUpdatedDescending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.UpdatedDateTime, SortDirection.Descending)
        });
        yield return Create("unlocked-ascending", "Unlocked Ascending", "SortUnlockedAscending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.UnlockedDateTime)
        });
        yield return Create("unlocked-descending", "Unlocked Descending", "SortUnlockedDescending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.UnlockedDateTime, SortDirection.Descending)
        });
        yield return Create("archive-ascending", "Archive Ascending", "SortArchiveAscending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.ArchiveDateTime)
        });
        yield return Create("archive-descending", "Archive Descending", "SortArchiveDescending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.ArchiveDateTime, SortDirection.Descending)
        });
        yield return Create("completed-ascending", "Completed Ascending", "SortCompletedAscending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.CompletedDateTime)
        });
        yield return Create("completed-descending", "Completed Descending", "SortCompletedDescending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.CompletedDateTime, SortDirection.Descending)
        });
        yield return Create("is-completed-ascending", "IsCompleted Ascending", "SortIsCompletedAscending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.IsCompleted)
        });
        yield return Create("is-completed-descending", "IsCompleted Descending", "SortIsCompletedDescending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.IsCompleted, SortDirection.Descending)
        });
        yield return Create("is-can-be-completed-ascending", "IsCanBeCompleted Ascending", "SortIsCanBeCompletedAscending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.IsCanBeCompleted)
        });
        yield return Create("is-can-be-completed-descending", "IsCanBeCompleted Descending", "SortIsCanBeCompletedDescending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.IsCanBeCompleted, SortDirection.Descending)
        });
        yield return Create("title-ascending", "Title Ascending", "SortTitleAscending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.OnlyTextTitle)
        });
        yield return Create("title-descending", "Title Descending", "SortTitleDescending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.OnlyTextTitle, SortDirection.Descending)
        });
        yield return Create("importance-ascending", "Importance Ascending", "SortImportanceAscending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.Importance)
        });
        yield return Create("importance-descending", "Importance Descending", "SortImportanceDescending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.Importance, SortDirection.Descending)
        });
        yield return Create("planned-begin-ascending", "Planned Begin Ascending", "SortPlannedBeginAscending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.PlannedBeginDateTime)
        });
        yield return Create("planned-begin-descending", "Planned Begin Descending", "SortPlannedBeginDescending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.PlannedBeginDateTime, SortDirection.Descending)
        });
        yield return Create("planned-duration-ascending", "Planned Duration Ascending", "SortPlannedDurationAscending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.PlannedDuration)
        });
        yield return Create("planned-duration-descending", "Planned Duration Descending", "SortPlannedDurationDescending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.PlannedDuration, SortDirection.Descending)
        });
        yield return Create("planned-finish-ascending", "Planned Finish Ascending", "SortPlannedFinishAscending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.PlannedEndDateTime)
        });
        yield return Create("planned-finish-descending", "Planned Finish descending", "SortPlannedFinishDescending", new SortExpressionComparer<TaskWrapperViewModel>
        {
            new(w => w.TaskItem.PlannedEndDateTime, SortDirection.Descending)
        });
    }

    private static SortDefinition Create(
        string id,
        string legacyName,
        string resourceKey,
        IComparer<TaskWrapperViewModel> comparer)
    {
        return new SortDefinition
        {
            Id = id,
            Name = legacyName,
            ResourceKey = resourceKey,
            Comparer = comparer
        };
    }
}
