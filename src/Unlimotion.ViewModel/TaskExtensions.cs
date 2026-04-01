using System.Collections.Generic;
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

    public static IReadOnlyList<TaskWrapperViewModel> NormalizeForDeleteBatch(
        this IEnumerable<TaskWrapperViewModel>? wrappers)
    {
        return DistinctByWrapperPath(wrappers);
    }

    public static IReadOnlyList<TaskWrapperViewModel> NormalizeForMoveBatch(
        this IEnumerable<TaskWrapperViewModel>? wrappers)
    {
        return DistinctByWrapperPath(RemoveDescendantsWithSelectedAncestors(wrappers));
    }

    public static IReadOnlyList<TaskWrapperViewModel> NormalizeForNonMoveBatch(
        this IEnumerable<TaskWrapperViewModel>? wrappers)
    {
        var normalized = RemoveDescendantsWithSelectedAncestors(wrappers);
        return normalized
            .Where(static wrapper => wrapper?.TaskItem != null && !string.IsNullOrWhiteSpace(wrapper.TaskItem.Id))
            .GroupBy(static wrapper => wrapper.TaskItem.Id)
            .Select(static group => group.First())
            .ToList();
    }

    public static int GetWrapperDepth(this TaskWrapperViewModel? wrapper)
    {
        var depth = 0;
        for (var current = wrapper?.Parent; current != null; current = current.Parent)
        {
            depth++;
        }

        return depth;
    }

    public static string GetWrapperPathKey(this TaskWrapperViewModel? wrapper)
    {
        if (wrapper == null)
        {
            return string.Empty;
        }

        var path = new Stack<string>();
        for (var current = wrapper; current != null; current = current.Parent)
        {
            path.Push(current.TaskItem?.Id ?? string.Empty);
        }

        return string.Join("/", path);
    }

    public static bool CanCreateBlockingRelation(this TaskItemViewModel blockedTask, TaskItemViewModel blockingTask)
    {
        return blockedTask != null &&
               blockingTask != null &&
               !string.IsNullOrWhiteSpace(blockedTask.Id) &&
               !string.IsNullOrWhiteSpace(blockingTask.Id) &&
               blockedTask.Id != blockingTask.Id &&
               !blockedTask.BlockedBy.Contains(blockingTask.Id) &&
               !blockedTask.Blocks.Contains(blockingTask.Id);
    }

    private static IReadOnlyList<TaskWrapperViewModel> RemoveDescendantsWithSelectedAncestors(
        IEnumerable<TaskWrapperViewModel>? wrappers)
    {
        var distinct = DistinctByWrapperPath(wrappers);
        if (distinct.Count <= 1)
        {
            return distinct;
        }

        var selectedPaths = distinct
            .Select(static wrapper => wrapper.GetWrapperPathKey())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet();

        return distinct
            .Where(wrapper => !HasSelectedAncestor(wrapper, selectedPaths))
            .ToList();
    }

    private static bool HasSelectedAncestor(
        TaskWrapperViewModel? wrapper,
        IReadOnlySet<string> selectedPaths)
    {
        for (var current = wrapper?.Parent; current != null; current = current.Parent)
        {
            if (selectedPaths.Contains(current.GetWrapperPathKey()))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<TaskWrapperViewModel> DistinctByWrapperPath(
        IEnumerable<TaskWrapperViewModel>? wrappers)
    {
        return wrappers?
            .Where(static wrapper => wrapper != null && wrapper.TaskItem != null &&
                                     !string.IsNullOrWhiteSpace(wrapper.TaskItem.Id))
            .GroupBy(static wrapper => wrapper.GetWrapperPathKey())
            .Select(static group => group.First())
            .ToList()
            ?? [];
    }
}
