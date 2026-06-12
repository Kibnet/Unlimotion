using System;

namespace Unlimotion.Domain;

public record TaskStatusHistoryEntry
{
    public TaskStatus Status { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public string Author { get; set; } = "System";
}
