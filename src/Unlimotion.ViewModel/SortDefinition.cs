using System.Collections.Generic;
using DynamicData.Binding;

namespace Unlimotion.ViewModel;

public class SortDefinition
{
    public string Name { get; set; }

    public IComparer<TaskWrapperViewModel> Comparer {get; set; }

    public override string ToString()
    {
        return Name;
    }

    public static IEnumerable<SortDefinition> GetDefinitions()
    {
        yield return new SortDefinition
        {
            Name = "Comfort",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.CompletedDateTime, SortDirection.Ascending),
                new(w => w.TaskItem.ArchiveDateTime, SortDirection.Ascending),
                new(w => w.TaskItem.UnlockedDateTime, SortDirection.Ascending),
                new(w => w.TaskItem.CreatedDateTime, SortDirection.Ascending),
            }
        };
        yield return new SortDefinition
        {
            Name = "Created Ascending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.CreatedDateTime, SortDirection.Ascending)
            }
        };
        yield return new SortDefinition
        {
            Name = "Created Descending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.CreatedDateTime, SortDirection.Descending)
            }
        };
        yield return new SortDefinition
        {
            Name = "Unlocked Ascending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.UnlockedDateTime, SortDirection.Ascending)
            }
        };
        yield return new SortDefinition
        {
            Name = "Unlocked Descending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.UnlockedDateTime, SortDirection.Descending)
            }
        };
        yield return new SortDefinition
        {
            Name = "Archive Ascending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.ArchiveDateTime, SortDirection.Ascending)
            }
        };
        yield return new SortDefinition
        {
            Name = "Archive Descending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.ArchiveDateTime, SortDirection.Descending)
            }
        };
        yield return new SortDefinition
        {
            Name = "Completed Ascending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.CompletedDateTime, SortDirection.Ascending)
            }
        };
        yield return new SortDefinition
        {
            Name = "Completed Descending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.CompletedDateTime, SortDirection.Descending)
            }
        };
        yield return new SortDefinition
        {
            Name = "IsCompleted Ascending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.IsCompleted, SortDirection.Ascending)
            }
        };
        yield return new SortDefinition
        {
            Name = "IsCompleted Descending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.IsCompleted, SortDirection.Descending)
            }
        };
        yield return new SortDefinition
        {
            Name = "IsCanBeCompleted Ascending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.IsCanBeCompleted, SortDirection.Ascending)
            }
        };
        yield return new SortDefinition
        {
            Name = "IsCanBeCompleted Descending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.IsCanBeCompleted, SortDirection.Descending)
            }
        };
    }
}