using System;

namespace Unlimotion.Domain;

public sealed record AgentExecutionRecord
{
    public string AgentId { get; set; } = string.Empty;
    public string LeaseId { get; set; } = string.Empty;
    public AgentExecutionState State { get; set; } = AgentExecutionState.Active;
    public DateTimeOffset ClaimedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum AgentExecutionState
{
    Active
}
