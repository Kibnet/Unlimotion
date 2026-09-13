using System;
using System.Collections.Generic;

namespace Unlimotion.Domain;

public sealed record AgentExecutionRecord
{
    public string AgentId { get; set; } = string.Empty;
    public string LeaseId { get; set; } = string.Empty;
    public AgentExecutionState State { get; set; } = AgentExecutionState.Active;
    public DateTimeOffset ClaimedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<AgentExecutionQuestion> Questions { get; set; } = new();
    public AgentExecutionResult? Result { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public string? ReleaseReason { get; set; }
    public List<AgentExecutionAttempt> PreviousAttempts { get; set; } = new();
    public bool AuditTruncated { get; set; }
}

public sealed record AgentExecutionQuestion
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset AskedAt { get; set; }
    public string? Answer { get; set; }
    public DateTimeOffset? AnsweredAt { get; set; }
}

public sealed record AgentExecutionResult
{
    public string Summary { get; set; } = string.Empty;
    public List<string> Links { get; set; } = new();
    public DateTimeOffset RecordedAt { get; set; }
}

public sealed record AgentExecutionAttempt
{
    public string AgentId { get; set; } = string.Empty;
    public string LeaseId { get; set; } = string.Empty;
    public AgentExecutionState State { get; set; }
    public DateTimeOffset ClaimedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public string? ReleaseReason { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public enum AgentExecutionState
{
    Active,
    AwaitingInput,
    Released,
    Completed
}
