using System.Text.Json.Serialization;

namespace Unlimotion.Cli;

public sealed record NightAgentSnapshotSelection
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
    [JsonRequired]
    public NightAgentSnapshotSelect Select { get; init; } = new();
    public string Context { get; init; } = "night-v1";
    public string MissingSelection { get; init; } = "error";
    public IReadOnlyList<string> Include { get; init; } = ["details", "criteria"];
}

public sealed record NightAgentSnapshotSelect
{
    [JsonRequired]
    public string Mode { get; init; } = "unlocked";
    public IReadOnlyList<string> RootIds { get; init; } = [];
    public IReadOnlyList<string> TaskIds { get; init; } = [];
    public IReadOnlyList<string> Statuses { get; init; } = [];
}

public sealed record NightAgentSnapshotArtifact
{
    [JsonRequired]
    public int SnapshotFormatVersion { get; init; } = 1;
    public required string SnapshotId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ArtifactHash { get; init; }
    public required NightAgentSnapshotSource Source { get; init; }
    public required NightAgentSnapshotContract Contract { get; init; }
    public required NightAgentSnapshotSelection Selection { get; init; }
    public required string ScopeHash { get; init; }
    public required NightAgentSnapshotAcquisition Acquisition { get; init; }
    public required NightAgentSnapshotCompleteness Completeness { get; init; }
    public required IReadOnlyList<NightAgentSnapshotCatalogEntry> Catalog { get; init; }
    public required IReadOnlyList<NightAgentSnapshotTarget> Targets { get; init; }
    public required IReadOnlyList<NightAgentSnapshotNode> Nodes { get; init; }
    public DateTimeOffset? NextEvaluationAt { get; init; }
}

public sealed record NightAgentSnapshotSource(string StorageKind, string SourceKey, string SourceNamespace);
public sealed record NightAgentSnapshotContract(int SchemaVersion, string ClosureVersion, string EtagVersion,
    string AvailabilityVersion, string SearchNormalizationVersion, string TimeZoneId, string TimeZoneOffset);
public sealed record NightAgentSnapshotAcquisition(DateTimeOffset StartedAt, DateTimeOffset CompletedAt,
    DateTimeOffset EvaluatedAt, string Consistency, bool Atomic, int Attempts, int TaskParseCount,
    int VerifiedFileCount, long SourceBytes);
public sealed record NightAgentSnapshotCompleteness(bool Complete, int PayloadCount, int TargetCount,
    int CatalogCount, IReadOnlyDictionary<string, string> Sections, IReadOnlyList<string> MissingRootIds,
    IReadOnlyList<string> MissingTaskIds, IReadOnlyList<string> Warnings);
public sealed record NightAgentSnapshotTarget(string Id, IReadOnlyList<string> ContextIds, string ContextHash);
public sealed record NightAgentSnapshotEdge(string Kind, string From, string To);
public sealed record NightAgentSnapshotBoundaryEdge(string Kind, string From, string To, bool OutsidePayload);
public sealed record NightAgentSnapshotReason(string Kind, string SubjectId, string? SourceTaskId, string? CriterionId);
public sealed record NightAgentSnapshotAvailability(bool CanStart, bool CanComplete, bool IsCanBeCompleted,
    bool CompletionCriteriaSatisfied, bool PlannedBeginIsFuture, IReadOnlyList<NightAgentSnapshotReason> Reasons);
public sealed record NightAgentSnapshotCatalogEntry(string Id, string Etag,
    IReadOnlyDictionary<string, string> SectionHashes, IReadOnlyList<NightAgentSnapshotEdge> Edges,
    NightAgentSnapshotAvailability Availability, string AvailabilityHash, Unlimotion.Domain.TaskStatus Status);

public record NightAgentSnapshotNode
{
    public required string Id { get; init; }
    public required string Etag { get; init; }
    public required TaskSummary Task { get; init; }
    public TaskDetailsOutput? Details { get; init; }
    public IReadOnlyList<TaskCriterionOutput>? Criteria { get; init; }
    public IReadOnlyList<TaskHistoryOutput>? History { get; init; }
    public ExecutionDetailsOutput? Execution { get; init; }
    public required IReadOnlyDictionary<string, string> Sections { get; init; }
    public required IReadOnlyList<string> ContainsTasks { get; init; }
    public required IReadOnlyList<string> ParentTasks { get; init; }
    public required IReadOnlyList<string> BlocksTasks { get; init; }
    public required IReadOnlyList<string> BlockedByTasks { get; init; }
    public required IReadOnlyList<NightAgentSnapshotBoundaryEdge> Edges { get; init; }
    public required NightAgentSnapshotAvailability Availability { get; init; }
}

public sealed record NightAgentSnapshotTargetNode : NightAgentSnapshotNode
{
    public required IReadOnlyList<string> ContextIds { get; init; }
    public required string ContextHash { get; init; }

    public static NightAgentSnapshotTargetNode From(NightAgentSnapshotNode node, NightAgentSnapshotTarget target) => new()
    {
        Id = node.Id, Etag = node.Etag, Task = node.Task, Details = node.Details, Criteria = node.Criteria,
        History = node.History, Execution = node.Execution, Sections = node.Sections, ContainsTasks = node.ContainsTasks,
        ParentTasks = node.ParentTasks, BlocksTasks = node.BlocksTasks, BlockedByTasks = node.BlockedByTasks,
        Edges = node.Edges, Availability = node.Availability, ContextIds = target.ContextIds, ContextHash = target.ContextHash
    };
}

public sealed record NightAgentSnapshotPage(int SchemaVersion, string SnapshotId, string ArtifactHash,
    string View, int TotalCount, IReadOnlyList<object> Items, string? NextCursor, bool Complete);
public sealed record NightAgentSnapshotDeltaPage(int SchemaVersion, string BeforeSnapshotId,
    string AfterSnapshotId, string ScopeHash, string Coverage, IReadOnlyList<NightAgentSnapshotDelta> Items,
    int TotalCount, string? NextCursor, bool BaselineUsable);
public sealed record NightAgentSnapshotDelta
{
    public required string Kind { get; init; }
    public required string TaskId { get; init; }
    public string? BeforeEtag { get; init; }
    public string? AfterEtag { get; init; }
    public IReadOnlyList<string>? BeforeRoles { get; init; }
    public IReadOnlyList<string>? AfterRoles { get; init; }
    public string? Evidence { get; init; }
    public IReadOnlyList<string>? ChangedSections { get; init; }
    public bool? UnprojectedDataChanged { get; init; }
    public NightAgentSnapshotEdge? Relation { get; init; }
    public NightAgentSnapshotAvailability? BeforeAvailability { get; init; }
    public NightAgentSnapshotAvailability? AfterAvailability { get; init; }
    public IReadOnlyList<string>? Reasons { get; init; }
    public IReadOnlyList<string>? ChangedContextIds { get; init; }
}

public sealed class NightAgentSnapshotException(string kind, string message, int exitCode = 1) : Exception(message)
{
    public string Kind { get; } = kind;
    public int ExitCode { get; } = exitCode;
}
