using System;
using System.Collections.Generic;
using System.Linq;

namespace Unlimotion.Services;

public enum TaskSpaceCatalogIssue
{
    DuplicateOrEmptySourceId,
    OrphanScopedSettings,
    MissingActiveSource,
    InvalidSourceConfiguration,
    ConflictingSourceOwnership
}

public sealed class TaskSpaceCatalogException : InvalidOperationException
{
    public TaskSpaceCatalogException(
        TaskSpaceCatalogIssue issue,
        IEnumerable<string?> problemSourceIds,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Issue = issue;
        ProblemSourceIds = problemSourceIds
            .Select(sourceId => string.IsNullOrWhiteSpace(sourceId) ? "<empty>" : sourceId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public TaskSpaceCatalogIssue Issue { get; }

    public IReadOnlyList<string> ProblemSourceIds { get; }
}
