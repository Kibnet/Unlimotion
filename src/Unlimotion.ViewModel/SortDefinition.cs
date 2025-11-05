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
        //Комфорт
        yield return new SortDefinition
        {
            Name = "Comfort",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.CompletedDateTime),
                new(w => w.TaskItem.ArchiveDateTime),
                new(w => w.TaskItem.UnlockedDateTime),
                new(w => w.TaskItem.CreatedDateTime),
            }
        };
        //Эмодзи
        yield return new SortDefinition
        {
            Name = "Emodji",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.GetAllEmoji),
            },
        };
        //По дате создания Asc
        yield return new SortDefinition
        {
            Name = "Created Ascending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.CreatedDateTime)
            }
        };
        //По дате создания Des
        yield return new SortDefinition
        {
            Name = "Created Descending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.CreatedDateTime, SortDirection.Descending)
            }
        };
        //По дате разблокировки Asc
        yield return new SortDefinition
        {
            Name = "Unlocked Ascending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.UnlockedDateTime)
            }
        };
        //По дате разблокировки Des
        yield return new SortDefinition
        {
            Name = "Unlocked Descending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.UnlockedDateTime, SortDirection.Descending)
            }
        };
        //По дате архивации Asc
        yield return new SortDefinition
        {
            Name = "Archive Ascending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.ArchiveDateTime)
            }
        };
        //По дате архивации Des
        yield return new SortDefinition
        {
            Name = "Archive Descending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.ArchiveDateTime, SortDirection.Descending)
            }
        };
        //По дате выполнения Asc
        yield return new SortDefinition
        {
            Name = "Completed Ascending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.CompletedDateTime)
            }
        };
        //По дате выполнения Des
        yield return new SortDefinition
        {
            Name = "Completed Descending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.CompletedDateTime, SortDirection.Descending)
            }
        };
        //По готовности Asc
        yield return new SortDefinition
        {
            Name = "IsCompleted Ascending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.IsCompleted)
            }
        };
        //По готовности Des
        yield return new SortDefinition
        {
            Name = "IsCompleted Descending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.IsCompleted, SortDirection.Descending)
            }
        };
        //It can be compited Asc
        yield return new SortDefinition
        {
            Name = "IsCanBeCompleted Ascending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.IsCanBeCompleted)
            }
        };
        //It can be compited Des
        yield return new SortDefinition
        {
            Name = "IsCanBeCompleted Descending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.IsCanBeCompleted, SortDirection.Descending)
            }
        };
        //По названию Asc
        yield return new SortDefinition
        {
            Name = "Title Ascending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.OnlyTextTitle)
            }
        };
        //По названию Des
        yield return new SortDefinition
        {
            Name = "Title Descending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.OnlyTextTitle, SortDirection.Descending)
            }
        };
        //По приоритету (важности) Asc
        yield return new SortDefinition
        {
            Name = "Importance Ascending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.Importance)
            }
        };
        //По приоритету (важности) Des
        yield return new SortDefinition
        {
            Name = "Importance Descending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.Importance, SortDirection.Descending)
            }
        };
        //По дате планируемого начала выполнения Asc
        yield return new SortDefinition
        {
            Name = "Planned Begin Ascending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.PlannedBeginDateTime)
            }
        };
        //По дате планируемого начала выполнения Des
        yield return new SortDefinition
        {
            Name = "Planned Begin Descending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.PlannedBeginDateTime, SortDirection.Descending)
            }
        };
        //По планируемой длительности Asc
        yield return new SortDefinition
        {
            Name = "Planned Duration Ascending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.PlannedDuration)
            }
        };
        //По планируемой длительности Des
        yield return new SortDefinition
        {
            Name = "Planned Duration Descending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.PlannedDuration, SortDirection.Descending)
            }
        };   
        //По дате планируемого окончания выполнения Asc
        yield return new SortDefinition
        {
            Name = "Planned Finish Ascending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.PlannedEndDateTime)
            }
        };
        //По дате планируемого окончания выполнения Des
        yield return new SortDefinition
        {
            Name = "Planned Finish descending",
            Comparer = new SortExpressionComparer<TaskWrapperViewModel>
            {
                new(w => w.TaskItem.PlannedEndDateTime, SortDirection.Descending)
            }
        };   
    }
}