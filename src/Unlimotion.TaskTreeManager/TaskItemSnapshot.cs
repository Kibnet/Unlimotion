using Unlimotion.Domain;

namespace Unlimotion.TaskTree;

public static class TaskItemSnapshot
{
    public static TaskItem Clone(TaskItem task) => task with
    {
        StatusHistory = task.StatusHistory?.Select(CloneStatusHistoryEntry).ToList() ?? [],
        CompletionCriteria = task.CompletionCriteria?.Select(CloneCriterion).ToList() ?? [],
        ContainsTasks = task.ContainsTasks?.ToList() ?? [],
        ParentTasks = task.ParentTasks?.ToList() ?? [],
        BlocksTasks = task.BlocksTasks?.ToList() ?? [],
        BlockedByTasks = task.BlockedByTasks?.ToList() ?? [],
        Repeater = CloneRepeater(task.Repeater),
        ExtensionData = task.ExtensionData?.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value == null ? null! : pair.Value.DeepClone())
    };

    private static TaskCompletionCriterion CloneCriterion(TaskCompletionCriterion criterion) =>
        criterion == null
            ? null!
            : new TaskCompletionCriterion
            {
                Id = criterion.Id,
                Text = criterion.Text,
                IsSatisfied = criterion.IsSatisfied,
                ExtensionData = criterion.ExtensionData?.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value == null ? null! : pair.Value.DeepClone())
            };

    private static TaskStatusHistoryEntry CloneStatusHistoryEntry(TaskStatusHistoryEntry entry) =>
        entry == null
            ? null!
            : new TaskStatusHistoryEntry
            {
                Status = entry.Status,
                ChangedAt = entry.ChangedAt,
                Author = entry.Author,
                ExtensionData = entry.ExtensionData?.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value == null ? null! : pair.Value.DeepClone())
            };

    private static RepeaterPattern? CloneRepeater(RepeaterPattern? repeater) =>
        repeater == null
            ? null
            : new RepeaterPattern
            {
                Type = repeater.Type,
                Period = repeater.Period,
                AfterComplete = repeater.AfterComplete,
                Pattern = repeater.Pattern?.ToList()!,
                ExtensionData = repeater.ExtensionData?.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value == null ? null! : pair.Value.DeepClone())
            };
}
