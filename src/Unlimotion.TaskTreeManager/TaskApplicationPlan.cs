using Unlimotion.Domain;

namespace Unlimotion.TaskTree;

/// <summary>The common, complete staging result used by preview and guarded commit.</summary>
public sealed record TaskApplicationPlan(
    IReadOnlyDictionary<string, TaskItem> Before,
    IReadOnlyDictionary<string, TaskItem> After,
    IReadOnlyDictionary<string, TaskItem> AfterExplicit,
    DateTimeOffset EvaluatedAt,
    string SourceManifestHash,
    IReadOnlySet<string> UnstableTaskIds,
    IReadOnlyList<string> ChangedTaskIds,
    IReadOnlyList<string> CreatedTaskIds);
