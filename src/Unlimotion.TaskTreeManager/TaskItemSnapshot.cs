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
        AgentExecution = CloneAgentExecution(task.AgentExecution),
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

    private static AgentExecutionRecord? CloneAgentExecution(AgentExecutionRecord? execution) =>
        execution == null
            ? null
            : new AgentExecutionRecord
            {
                AgentId = execution.AgentId,
                LeaseId = execution.LeaseId,
                State = execution.State,
                ClaimedAt = execution.ClaimedAt,
                UpdatedAt = execution.UpdatedAt,
                Questions = execution.Questions?.Select(static question => new AgentExecutionQuestion
                {
                    Id = question.Id,
                    Text = question.Text,
                    AskedAt = question.AskedAt,
                    Answer = question.Answer,
                    AnsweredAt = question.AnsweredAt
                }).ToList() ?? [],
                Result = execution.Result == null
                    ? null
                    : new AgentExecutionResult
                    {
                        Summary = execution.Result.Summary,
                        Links = execution.Result.Links?.ToList() ?? [],
                        RecordedAt = execution.Result.RecordedAt
                    },
                ReleasedAt = execution.ReleasedAt,
                ReleaseReason = execution.ReleaseReason,
                PreviousAttempts = execution.PreviousAttempts?.Select(static attempt => new AgentExecutionAttempt
                {
                    AgentId = attempt.AgentId,
                    LeaseId = attempt.LeaseId,
                    State = attempt.State,
                    ClaimedAt = attempt.ClaimedAt,
                    UpdatedAt = attempt.UpdatedAt,
                    ReleasedAt = attempt.ReleasedAt,
                    ReleaseReason = attempt.ReleaseReason,
                    CompletedAt = attempt.CompletedAt
                }).ToList() ?? [],
                AuditTruncated = execution.AuditTruncated
            };
}
