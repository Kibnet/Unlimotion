using System.Collections.Generic;
using Unlimotion.ViewModel;

namespace Unlimotion;

public sealed class TaskRelationsIndex : ITaskRelationsIndex
{
    public void Rebuild(IEnumerable<TaskItemViewModel> tasks)
    {
        var lookup = new Dictionary<string, TaskItemViewModel>();
        foreach (var task in tasks)
        {
            if (task?.Id == null)
            {
                continue;
            }

            lookup[task.Id] = task;
        }

        foreach (var task in lookup.Values)
        {
            task.ApplyRelations(
                Resolve(task.Contains, lookup),
                Resolve(task.Parents, lookup),
                Resolve(task.Blocks, lookup),
                Resolve(task.BlockedBy, lookup),
                refreshComputed: false);
        }

        foreach (var task in lookup.Values)
        {
            task.RefreshComputedFields();
        }
    }

    private static IReadOnlyList<TaskItemViewModel> Resolve(
        IEnumerable<string>? relationIds,
        Dictionary<string, TaskItemViewModel> lookup)
    {
        if (relationIds == null)
        {
            return [];
        }

        var unique = new HashSet<string>();
        var result = new List<TaskItemViewModel>();

        foreach (var relationId in relationIds)
        {
            if (string.IsNullOrWhiteSpace(relationId))
            {
                continue;
            }

            if (!unique.Add(relationId))
            {
                continue;
            }

            if (lookup.TryGetValue(relationId, out var relatedTask))
            {
                result.Add(relatedTask);
            }
        }

        return result;
    }
}
