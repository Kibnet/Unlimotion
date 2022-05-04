using System.Linq;

namespace Unlimotion.ViewModel;

public static class TaskExtensions
{
    public static bool CanMoveInto(this TaskWrapperViewModel current, TaskWrapperViewModel destination)
    {
        return CanMoveInto(current.TaskItem, destination.TaskItem);
    }

    public static bool CanMoveInto(this TaskItemViewModel current, TaskItemViewModel destination)
    {
        return destination != null &&
               destination != current &&
               destination.GetAllParents().All(m => m.Id != current.Id) &&
               !destination.Contains.Contains(current.Id);
    }
}