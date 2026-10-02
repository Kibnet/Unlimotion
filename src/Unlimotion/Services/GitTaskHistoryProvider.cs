using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unlimotion.ViewModel.Localization;

namespace Unlimotion.Services;

public sealed class GitTaskHistoryProvider : ITaskHistoryProvider
{
    private const long MaxBlobBytes = 8 * 1024 * 1024;
    private const int MaxVisitedCommitsPerPage = 1000;
    private readonly object _sessionGate = new();
    private HistorySession? _session;
    private long _sessionGeneration;

    public Task<TaskHistoryPage> GetPageAsync(TaskHistoryRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => GetPage(request, cancellationToken), cancellationToken);

    public Task<string> ReadValueAsync(TaskHistoryValueReference reference, CancellationToken cancellationToken) =>
        Task.Run(() => ReadValue(reference, cancellationToken), cancellationToken);

    public void ResetSession()
    {
        HistorySession? previous;
        lock (_sessionGate)
        {
            _sessionGeneration++;
            previous = _session;
            _session = null;
        }

        previous?.Dispose();
    }

    private TaskHistoryPage GetPage(TaskHistoryRequest request, CancellationToken cancellationToken)
    {
        HistorySession? session = null;
        try
        {
            session = request.Cursor is null
                ? StartSession(request, cancellationToken)
                : TakeSession(request);

            if (session.UnbornPage is not null)
                return session.UnbornPage;

            var page = GetPageFromSession(session, request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (page.NextCursor is not null)
            {
                lock (_sessionGate)
                {
                    if (session.Generation == _sessionGeneration)
                    {
                        _session = session;
                        session = null;
                    }
                }
            }

            return page;
        }
        finally
        {
            session?.Dispose();
        }
    }

    private HistorySession TakeSession(TaskHistoryRequest request)
    {
        lock (_sessionGate)
        {
            if (_session is null ||
                !string.Equals(_session.Id, request.Cursor, StringComparison.Ordinal) ||
                !string.Equals(_session.StoragePath, request.StoragePath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_session.SourceId, request.SourceId, StringComparison.Ordinal) ||
                !string.Equals(_session.TaskId, request.TaskId, StringComparison.Ordinal))
                throw new InvalidOperationException(Localization.Get("TaskHistoryInvalidCursor"));

            var session = _session;
            _session = null;
            return session;
        }
    }

    private HistorySession StartSession(TaskHistoryRequest request, CancellationToken cancellationToken)
    {
        ResetSession();
        if (string.IsNullOrWhiteSpace(request.StoragePath))
            return HistorySession.Unavailable(request, _sessionGeneration, Unavailable("TaskHistoryUnavailableServer"));

        var storagePath = Path.GetFullPath(request.StoragePath);
        if (!Directory.Exists(storagePath))
            return HistorySession.Unavailable(request, _sessionGeneration, Unavailable("TaskHistoryUnavailableFolder"));

        var discoveredRepository = Repository.Discover(storagePath);
        if (string.IsNullOrWhiteSpace(discoveredRepository))
            return HistorySession.Unavailable(request, _sessionGeneration, Unavailable("TaskHistoryUnavailableGit"));

        var repository = new Repository(discoveredRepository);
        try
        {
        var workTreeRoot = repository.Info.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(workTreeRoot))
            return HistorySession.Unavailable(request, _sessionGeneration, Unavailable("TaskHistoryUnavailableBare"));

        var relativeStoragePath = Path.GetRelativePath(workTreeRoot, storagePath);
        if (relativeStoragePath == ".." ||
            relativeStoragePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relativeStoragePath))
            return HistorySession.Unavailable(request, _sessionGeneration, Unavailable("TaskHistoryUnavailableOutsideRepository"));

        var workingSnapshot = FindWorkingTreeSnapshot(storagePath, workTreeRoot, request.TaskId, cancellationToken);
        var repositoryHead = repository.Head.Tip;
        if (repositoryHead is null)
        {
            var unbornEntries = workingSnapshot is null
                ? Array.Empty<TaskHistoryEntry>()
                : [CreateWorkingTreeEntry(repository, workTreeRoot, null, workingSnapshot, cancellationToken)];
            return HistorySession.Unavailable(request, _sessionGeneration, new TaskHistoryPage(
                unbornEntries,
                null,
                unbornEntries.Length == 0
                    ? Localization.Get("TaskHistoryEmpty")
                    : Localization.Get("TaskHistoryUncommittedOnly")));
        }

        var session = new HistorySession(
            request, _sessionGeneration, repository, repositoryHead,
            workTreeRoot, relativeStoragePath, workingSnapshot);
        repository = null!;
        return session;
        }
        finally
        {
            repository?.Dispose();
        }
    }

    private static TaskHistoryPage GetPageFromSession(
        HistorySession session,
        TaskHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var repository = session.Repository!;
        var head = session.Head!;
        var workTreeRoot = session.WorkTreeRoot!;
        var relativeStoragePath = session.RelativeStoragePath!;
        var workingSnapshot = session.WorkingSnapshot;
        var discoveredRepository = repository.Info.Path;

        var entries = new List<TaskHistoryEntry>();
        var partial = session.IsPartial;
        if (request.Cursor is null && workingSnapshot is not null)
        {
            try
            {
                var headSnapshot = FindCommitSnapshot(
                    head, relativeStoragePath, request.TaskId, cancellationToken, workingSnapshot.RelativePath);
                if (!SnapshotsEqual(headSnapshot, workingSnapshot))
                    entries.Add(CreateWorkingTreeEntry(repository, workTreeRoot, headSnapshot, workingSnapshot, cancellationToken));
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                partial = true;
                entries.Add(CreateUnreadableWorkingTreeEntry(ex));
            }
        }

        var visited = 0;
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var targetCount = pageSize + (request.Cursor is null && entries.Count > 0 ? 1 : 0);
        if (!session.Started)
        {
            session.HasCurrentCommit = session.Enumerator!.MoveNext();
            session.Started = true;
        }
        var knownTaskPath = session.KnownTaskPath;
        while (session.HasCurrentCommit && entries.Count < targetCount && visited < MaxVisitedCommitsPerPage)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var commit = session.Enumerator!.Current;
            try
            {
                var parent = commit.Parents.FirstOrDefault();
                var treeChanges = parent is null
                    ? null
                    : repository.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree);
                var currentCandidates = GetChangedTaskPaths(treeChanges, relativeStoragePath, useOldPath: false);
                var previousCandidates = GetChangedTaskPaths(treeChanges, relativeStoragePath, useOldPath: true);

                var currentSnapshot = FindCommitSnapshot(
                    commit,
                    relativeStoragePath,
                    request.TaskId,
                    cancellationToken,
                    knownTaskPath,
                    currentCandidates);
                if (currentSnapshot is not null)
                    knownTaskPath = currentSnapshot.RelativePath;

                var previousExpectedPath = ResolvePreviousPath(
                    treeChanges,
                    currentSnapshot?.RelativePath ?? knownTaskPath,
                    previousCandidates);
                if (!string.IsNullOrWhiteSpace(previousExpectedPath))
                    knownTaskPath = previousExpectedPath;
                var previousSnapshot = parent is null
                    ? null
                    : FindCommitSnapshot(
                        parent,
                        relativeStoragePath,
                        request.TaskId,
                        cancellationToken,
                        previousExpectedPath,
                        previousCandidates);
                if (previousSnapshot is not null)
                    knownTaskPath = previousSnapshot.RelativePath;

                if (!SnapshotsEqual(previousSnapshot, currentSnapshot))
                {
                    var changes = TaskHistoryDiffBuilder.Build(
                        previousSnapshot,
                        currentSnapshot,
                        discoveredRepository,
                        workTreeRoot,
                        parent?.Sha,
                        commit.Sha);
                    if (changes.Count > 0)
                    {
                        entries.Add(new TaskHistoryEntry(
                            commit.Sha,
                            string.IsNullOrWhiteSpace(commit.Author.Name)
                                ? Localization.Get("TaskHistoryUnknownAuthor")
                                : commit.Author.Name,
                            commit.Author.When,
                            Localization.Format("TaskHistoryGitSource", commit.Sha[..7]),
                            commit.Message?.Trim() ?? string.Empty,
                            changes,
                            IsPartial: commit.Parents.Skip(1).Any(),
                            Notice: commit.Parents.Skip(1).Any()
                                ? Localization.Get("TaskHistoryMergeFirstParent")
                                : string.Empty));
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                partial = true;
                entries.Add(new TaskHistoryEntry(
                    commit.Sha,
                    string.IsNullOrWhiteSpace(commit.Author.Name)
                        ? Localization.Get("TaskHistoryUnknownAuthor")
                        : commit.Author.Name,
                    commit.Author.When,
                    Localization.Format("TaskHistoryGitSource", commit.Sha[..7]),
                    commit.Message?.Trim() ?? string.Empty,
                    [new TaskHistoryFieldChange(
                        string.Empty,
                        Localization.Get("TaskHistoryUnreadableRevision"),
                        Localization.Get("TaskHistoryUnavailableValue"),
                        Localization.Get("TaskHistoryUnavailableValue"),
                        TaskHistoryChangeType.Modified,
                        IsMetadata: false)],
                    IsPartial: true,
                    Notice: ex.Message));
            }

            visited++;
            session.HasCurrentCommit = session.Enumerator.MoveNext();
        }

        session.KnownTaskPath = knownTaskPath;
        session.IsPartial = partial;
        var hasMore = session.HasCurrentCommit;
        var nextCursor = hasMore ? session.Id : null;
        var status = partial
            ? Localization.Get("TaskHistoryPartial")
            : entries.Count == 0 && !hasMore
                ? Localization.Get("TaskHistoryEmpty")
                : hasMore && visited >= MaxVisitedCommitsPerPage
                    ? Localization.Get("TaskHistoryContinueSearch")
                    : string.Empty;

        return new TaskHistoryPage(entries, nextCursor, status, IsPartial: partial);
    }

    private static TaskHistoryPage Unavailable(string messageKey) =>
        new([], null, Localization.Get(messageKey), IsUnavailable: true);

    private static TaskHistoryEntry CreateWorkingTreeEntry(
        Repository repository,
        string workTreeRoot,
        TaskFileSnapshot? oldSnapshot,
        TaskFileSnapshot workingSnapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var changes = TaskHistoryDiffBuilder.Build(
            oldSnapshot,
            workingSnapshot,
            repository.Info.Path,
            workTreeRoot,
            repository.Head.Tip?.Sha,
            null);

        return new TaskHistoryEntry(
            null,
            string.Empty,
            null,
            Localization.Get("TaskHistoryWorkingTree"),
            Localization.Get("TaskHistoryWorkingTreeHint"),
            changes,
            IsWorkingTree: true);
    }

    private static TaskHistoryEntry CreateUnreadableWorkingTreeEntry(Exception exception) =>
        new(
            null,
            string.Empty,
            null,
            Localization.Get("TaskHistoryWorkingTree"),
            Localization.Get("TaskHistoryWorkingTreeHint"),
            [new TaskHistoryFieldChange(
                string.Empty,
                Localization.Get("TaskHistoryUnreadableRevision"),
                Localization.Get("TaskHistoryUnavailableValue"),
                Localization.Get("TaskHistoryUnavailableValue"),
                TaskHistoryChangeType.Modified,
                IsMetadata: false)],
            IsWorkingTree: true,
            IsPartial: true,
            Notice: exception.Message);

    private static TaskFileSnapshot? FindWorkingTreeSnapshot(
        string storagePath,
        string workTreeRoot,
        string taskId,
        CancellationToken cancellationToken)
    {
        var matches = new List<TaskFileSnapshot>();
        foreach (var filePath in Directory.EnumerateFiles(storagePath, "*", SearchOption.TopDirectoryOnly)
                     .Where(IsTaskFile)
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = new FileInfo(filePath);
            if (file.Length == 0 || file.Length > MaxBlobBytes)
                continue;

            try
            {
                var content = File.ReadAllBytes(filePath);
                var json = ParseJson(content);
                if (string.Equals(json.Value<string>("Id"), taskId, StringComparison.Ordinal))
                {
                    matches.Add(new TaskFileSnapshot(
                        json,
                        Path.GetRelativePath(workTreeRoot, filePath).Replace(Path.DirectorySeparatorChar, '/'),
                        filePath,
                        IsWorkingTree: true,
                        ContentHash: Convert.ToHexString(SHA256.HashData(content))));
                }
            }
            catch (JsonException)
            {
            }
        }

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(Localization.Format("TaskHistoryDuplicateTaskFiles", taskId))
        };
    }

    private static IReadOnlyCollection<string>? GetChangedTaskPaths(
        TreeChanges? changes,
        string relativeStoragePath,
        bool useOldPath)
    {
        if (changes is null)
            return null;

        var normalizedStorage = relativeStoragePath.Replace(Path.DirectorySeparatorChar, '/').Trim('/');
        return changes
            .Where(change => useOldPath
                ? change.Status != ChangeKind.Added
                : change.Status != ChangeKind.Deleted)
            .Select(change => useOldPath && !string.IsNullOrWhiteSpace(change.OldPath)
                ? change.OldPath
                : change.Path)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path!.Replace('\\', '/'))
            .Where(path => IsDirectStoragePath(path, normalizedStorage))
            .Where(path => IsTaskFile(Path.GetFileName(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolvePreviousPath(
        TreeChanges? changes,
        string? currentPath,
        IReadOnlyCollection<string>? previousCandidates)
    {
        if (changes is null || string.IsNullOrWhiteSpace(currentPath))
            return currentPath;

        var normalizedCurrent = currentPath.Replace('\\', '/');
        foreach (var change in changes)
        {
            var newPath = change.Path?.Replace('\\', '/');
            if (!string.Equals(newPath, normalizedCurrent, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrWhiteSpace(change.OldPath) &&
                !string.Equals(change.OldPath.Replace('\\', '/'), normalizedCurrent, StringComparison.OrdinalIgnoreCase))
                return change.OldPath.Replace('\\', '/');

            var alternativePaths = previousCandidates?
                .Where(path => !string.Equals(path, normalizedCurrent, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return alternativePaths is { Length: 1 }
                ? alternativePaths[0]
                : normalizedCurrent;
        }

        return normalizedCurrent;
    }

    private static bool IsDirectStoragePath(string path, string normalizedStorage)
    {
        var relative = string.IsNullOrWhiteSpace(normalizedStorage) || normalizedStorage == "."
            ? path
            : path.StartsWith(normalizedStorage + "/", StringComparison.OrdinalIgnoreCase)
                ? path[(normalizedStorage.Length + 1)..]
                : string.Empty;
        return !string.IsNullOrWhiteSpace(relative) && !relative.Contains('/');
    }
    private static TaskFileSnapshot? FindCommitSnapshot(
        Commit commit,
        string relativeStoragePath,
        string taskId,
        CancellationToken cancellationToken,
        string? expectedRelativePath = null,
        IReadOnlyCollection<string>? candidateRelativePaths = null)
    {
        Tree tree;
        if (string.IsNullOrWhiteSpace(relativeStoragePath) || relativeStoragePath == ".")
        {
            tree = commit.Tree;
        }
        else
        {
            var normalizedPath = relativeStoragePath.Replace(Path.DirectorySeparatorChar, '/');
            var storageEntry = commit.Tree[normalizedPath];
            if (storageEntry?.TargetType != TreeEntryTargetType.Tree)
                return null;
            tree = (Tree)storageEntry.Target;
        }

        if (!string.IsNullOrWhiteSpace(expectedRelativePath))
        {
            var expectedName = Path.GetFileName(expectedRelativePath);
            var expectedEntry = tree[expectedName];
            if (expectedEntry?.TargetType == TreeEntryTargetType.Blob)
            {
                var expectedBlob = (Blob)expectedEntry.Target;
                if (expectedBlob.Size == 0 || expectedBlob.Size > MaxBlobBytes)
                    throw new InvalidDataException(Localization.Get("TaskHistoryBlobTooLarge"));

                var expectedJson = ParseJson(ReadBlobBytes(expectedBlob, cancellationToken));
                if (string.Equals(expectedJson.Value<string>("Id"), taskId, StringComparison.Ordinal))
                {
                    var expectedPath = string.IsNullOrWhiteSpace(relativeStoragePath) || relativeStoragePath == "."
                        ? expectedEntry.Name
                        : $"{relativeStoragePath.Replace(Path.DirectorySeparatorChar, '/')}/{expectedEntry.Name}";
                    return new TaskFileSnapshot(expectedJson, expectedPath, expectedPath, IsWorkingTree: false);
                }
            }
        }

        var entriesToInspect = candidateRelativePaths is null
            ? tree.AsEnumerable()
            : candidateRelativePaths
                .Select(path => tree[Path.GetFileName(path)])
                .Where(static entry => entry is not null)
                .Cast<TreeEntry>()
                .DistinctBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase);

        TaskFileSnapshot? match = null;
        Exception? unreadableCandidate = null;
        foreach (var entry in entriesToInspect)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.TargetType != TreeEntryTargetType.Blob || !IsTaskFile(entry.Name))
                continue;

            var blob = (Blob)entry.Target;
            if (blob.Size == 0 || blob.Size > MaxBlobBytes)
            {
                if (candidateRelativePaths is not null)
                    unreadableCandidate ??= new InvalidDataException(Localization.Get("TaskHistoryBlobTooLarge"));
                continue;
            }

            try
            {
                var json = ParseJson(ReadBlobBytes(blob, cancellationToken));
                if (!string.Equals(json.Value<string>("Id"), taskId, StringComparison.Ordinal))
                    continue;

                if (match is not null)
                    throw new InvalidOperationException(Localization.Format("TaskHistoryDuplicateTaskFiles", taskId));

                var relativePath = string.IsNullOrWhiteSpace(relativeStoragePath) || relativeStoragePath == "."
                    ? entry.Name
                    : $"{relativeStoragePath.Replace(Path.DirectorySeparatorChar, '/')}/{entry.Name}";
                match = new TaskFileSnapshot(json, relativePath, relativePath, IsWorkingTree: false);
            }
            catch (JsonException ex)
            {
                if (candidateRelativePaths is not null)
                    unreadableCandidate ??= ex;
            }
        }

        if (match is null && unreadableCandidate is not null)
            throw unreadableCandidate;

        return match;
    }

    private static bool IsTaskFile(string path)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith(".", StringComparison.Ordinal))
            return false;
        var extension = Path.GetExtension(name);
        return extension.Length == 0 || extension.Equals(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] ReadBlobBytes(Blob blob, CancellationToken cancellationToken)
    {
        using var input = blob.GetContentStream();
        using var output = new MemoryStream((int)Math.Min(blob.Size, int.MaxValue));
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.Write(buffer, 0, read);
            if (output.Length > MaxBlobBytes)
                throw new InvalidDataException(Localization.Get("TaskHistoryBlobTooLarge"));
        }

        return output.ToArray();
    }

    private static JObject ParseJson(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        using var jsonReader = new JsonTextReader(reader)
        {
            MaxDepth = 64,
            DateParseHandling = DateParseHandling.None
        };
        return JObject.Load(jsonReader);
    }

    private static bool SnapshotsEqual(TaskFileSnapshot? left, TaskFileSnapshot? right)
    {
        if (left is null || right is null)
            return left is null && right is null;
        return JToken.DeepEquals(left.Json, right.Json) &&
               string.Equals(left.RelativePath, right.RelativePath, StringComparison.Ordinal);
    }

    private static string ReadValue(TaskHistoryValueReference reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        JObject json;
        if (reference.IsWorkingTree)
        {
            var fileInfo = new FileInfo(reference.FilePath);
            if (!fileInfo.Exists || fileInfo.Length > MaxBlobBytes)
                throw new InvalidDataException(Localization.Get("TaskHistoryUnavailableValue"));
            var content = File.ReadAllBytes(reference.FilePath);
            if (!string.Equals(
                    Convert.ToHexString(SHA256.HashData(content)),
                    reference.ExpectedContentHash,
                    StringComparison.Ordinal))
                throw new InvalidDataException(Localization.Get("TaskHistoryValueChangedRefresh"));
            json = ParseJson(content);
        }
        else
        {
            using var repository = new Repository(reference.RepositoryPath);
            var commit = repository.Lookup<Commit>(reference.CommitSha)
                         ?? throw new InvalidDataException(Localization.Get("TaskHistoryUnavailableValue"));
            var entry = commit.Tree[reference.FilePath];
            if (entry?.TargetType != TreeEntryTargetType.Blob)
                throw new InvalidDataException(Localization.Get("TaskHistoryUnavailableValue"));
            json = ParseJson(ReadBlobBytes((Blob)entry.Target, cancellationToken));
        }

        var token = string.IsNullOrWhiteSpace(reference.JsonPath)
            ? json
            : json.SelectToken(reference.JsonPath, errorWhenNoMatch: false);
        return token is null
            ? Localization.Get("TaskHistoryMissingValue")
            : TaskHistoryDiffBuilder.FormatFullValue(token);
    }

    private sealed class HistorySession : IDisposable
    {
        private HistorySession(TaskHistoryRequest request, long generation, TaskHistoryPage unavailablePage)
        {
            StoragePath = request.StoragePath;
            SourceId = request.SourceId;
            TaskId = request.TaskId;
            Generation = generation;
            UnbornPage = unavailablePage;
        }

        public HistorySession(
            TaskHistoryRequest request,
            long generation,
            Repository repository,
            Commit head,
            string workTreeRoot,
            string relativeStoragePath,
            TaskFileSnapshot? workingSnapshot)
        {
            StoragePath = request.StoragePath;
            SourceId = request.SourceId;
            TaskId = request.TaskId;
            Generation = generation;
            Repository = repository;
            Head = head;
            WorkTreeRoot = workTreeRoot;
            RelativeStoragePath = relativeStoragePath;
            WorkingSnapshot = workingSnapshot;
            KnownTaskPath = workingSnapshot?.RelativePath;
            Enumerator = repository.Commits.QueryBy(new CommitFilter
            {
                IncludeReachableFrom = head,
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time
            }).GetEnumerator();
        }

        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string? StoragePath { get; }
        public string SourceId { get; }
        public string TaskId { get; }
        public long Generation { get; }
        public TaskHistoryPage? UnbornPage { get; }
        public Repository? Repository { get; }
        public Commit? Head { get; }
        public string? WorkTreeRoot { get; }
        public string? RelativeStoragePath { get; }
        public TaskFileSnapshot? WorkingSnapshot { get; }
        public IEnumerator<Commit>? Enumerator { get; }
        public string? KnownTaskPath { get; set; }
        public bool Started { get; set; }
        public bool HasCurrentCommit { get; set; }
        public bool IsPartial { get; set; }

        public static HistorySession Unavailable(
            TaskHistoryRequest request, long generation, TaskHistoryPage page) =>
            new(request, generation, page);

        public void Dispose()
        {
            Enumerator?.Dispose();
            Repository?.Dispose();
        }
    }

    internal sealed record TaskFileSnapshot(
        JObject Json,
        string RelativePath,
        string FilePath,
        bool IsWorkingTree,
        string? ContentHash = null);
}

internal static class TaskHistoryDiffBuilder
{
    private const int MaxStructuredDepth = 16;
    private const int PreviewLength = 60;

    private static readonly HashSet<string> MetadataFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Id", "UserId", "CreatedDateTime", "UpdatedDateTime", "UnlockedDateTime",
        "IsCanBeCompleted", "Version", "StatusHistory", "AgentExecution", "SortOrder"
    };

    private static readonly HashSet<string> SetFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "ContainsTasks", "ParentTasks", "BlocksTasks", "BlockedByTasks"
    };

    public static IReadOnlyList<TaskHistoryFieldChange> Build(
        GitTaskHistoryProvider.TaskFileSnapshot? oldSnapshot,
        GitTaskHistoryProvider.TaskFileSnapshot? newSnapshot,
        string repositoryPath,
        string workTreeRoot,
        string? oldCommitSha,
        string? newCommitSha)
    {
        var changes = new List<TaskHistoryFieldChange>();
        var ignored = FindEquivalentLegacyMigrationFields(oldSnapshot?.Json, newSnapshot?.Json);

        Compare(
            oldSnapshot?.Json,
            newSnapshot?.Json,
            string.Empty,
            string.Empty,
            0,
            ignored,
            changes,
            path => CreateReference(oldSnapshot, repositoryPath, oldCommitSha, path),
            path => CreateReference(newSnapshot, repositoryPath, newCommitSha, path));

        foreach (var field in ignored.OrderBy(static field => field, StringComparer.Ordinal))
        {
            var oldValue = oldSnapshot?.Json.Property(field)?.Value;
            var newValue = newSnapshot?.Json.Property(field)?.Value;
            if (JToken.DeepEquals(oldValue, newValue))
                continue;

            changes.Add(new TaskHistoryFieldChange(
                field,
                DisplayName(field),
                oldValue is null ? Localization.Get("TaskHistoryMissingValue") : Preview(FormatFullValue(oldValue)),
                newValue is null ? Localization.Get("TaskHistoryMissingValue") : Preview(FormatFullValue(newValue)),
                oldValue is null
                    ? TaskHistoryChangeType.Added
                    : newValue is null
                        ? TaskHistoryChangeType.Removed
                        : TaskHistoryChangeType.Modified,
                IsMetadata: true,
                CreateReference(oldSnapshot, repositoryPath, oldCommitSha, field),
                CreateReference(newSnapshot, repositoryPath, newCommitSha, field)));
        }
        if (oldSnapshot is not null &&
            newSnapshot is not null &&
            !string.Equals(oldSnapshot.RelativePath, newSnapshot.RelativePath, StringComparison.Ordinal))
        {
            changes.Insert(0, new TaskHistoryFieldChange(
                "@path",
                Localization.Get("TaskHistoryFilePath"),
                oldSnapshot.RelativePath,
                newSnapshot.RelativePath,
                TaskHistoryChangeType.Modified,
                IsMetadata: true));
        }

        return changes;
    }

    public static string FormatFullValue(JToken token) =>
        token.Type switch
        {
            JTokenType.Null => Localization.Get("TaskHistoryNullValue"),
            JTokenType.String when string.IsNullOrEmpty(token.Value<string>()) =>
                Localization.Get("TaskHistoryEmptyString"),
            JTokenType.String => token.Value<string>() ?? string.Empty,
            JTokenType.Date => FormatDate(token),
            JTokenType.Object or JTokenType.Array => token.ToString(Formatting.Indented),
            _ => token.ToString(Formatting.None)
        };

    private static string FormatDate(JToken token) =>
        token is JValue { Value: DateTimeOffset offset }
            ? offset.ToLocalTime().ToString("G")
            : token.Value<DateTime>().ToLocalTime().ToString("G");
    private static void Compare(
        JToken? oldToken,
        JToken? newToken,
        string path,
        string rootProperty,
        int depth,
        IReadOnlySet<string> ignoredRootFields,
        ICollection<TaskHistoryFieldChange> changes,
        Func<string, TaskHistoryValueReference?> oldReference,
        Func<string, TaskHistoryValueReference?> newReference)
    {
        if (JToken.DeepEquals(oldToken, newToken))
            return;

        if (depth >= MaxStructuredDepth)
        {
            AddChange(oldToken, newToken, path, rootProperty, changes, oldReference, newReference);
            return;
        }

        if (oldToken is JObject || newToken is JObject)
        {
            if ((oldToken is not null && oldToken is not JObject) ||
                (newToken is not null && newToken is not JObject))
            {
                AddChange(oldToken, newToken, path, rootProperty, changes, oldReference, newReference);
                return;
            }
            var oldObject = oldToken as JObject ?? new JObject();
            var newObject = newToken as JObject ?? new JObject();
            var propertyNames = oldObject.Properties().Select(static p => p.Name)
                .Union(newObject.Properties().Select(static p => p.Name), StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal);
            foreach (var propertyName in propertyNames)
            {
                if (string.IsNullOrEmpty(path) && ignoredRootFields.Contains(propertyName))
                    continue;

                var nextPath = string.IsNullOrEmpty(path)
                    ? EscapeJsonPathProperty(propertyName)
                    : $"{path}.{EscapeJsonPathProperty(propertyName)}";
                Compare(
                    oldObject.Property(propertyName)?.Value,
                    newObject.Property(propertyName)?.Value,
                    nextPath,
                    string.IsNullOrEmpty(rootProperty) ? propertyName : rootProperty,
                    depth + 1,
                    ignoredRootFields,
                    changes,
                    oldReference,
                    newReference);
            }

            return;
        }

        if (oldToken is JArray || newToken is JArray)
        {
            if ((oldToken is not null && oldToken is not JArray) ||
                (newToken is not null && newToken is not JArray))
            {
                AddChange(oldToken, newToken, path, rootProperty, changes, oldReference, newReference);
                return;
            }
            var oldArray = oldToken as JArray ?? new JArray();
            var newArray = newToken as JArray ?? new JArray();
            if (SetFields.Contains(rootProperty))
            {
                CompareSet(oldArray, newArray, path, rootProperty, changes, oldReference, newReference);
                return;
            }

            if (string.Equals(rootProperty, "CompletionCriteria", StringComparison.OrdinalIgnoreCase))
            {
                CompareCriteria(oldArray, newArray, path, changes, oldReference, newReference);
                return;
            }

            AddChange(oldToken, newToken, path, rootProperty, changes, oldReference, newReference);
            return;
        }

        AddChange(oldToken, newToken, path, rootProperty, changes, oldReference, newReference);
    }

    private static void CompareSet(
        JArray oldArray,
        JArray newArray,
        string path,
        string rootProperty,
        ICollection<TaskHistoryFieldChange> changes,
        Func<string, TaskHistoryValueReference?> oldReference,
        Func<string, TaskHistoryValueReference?> newReference)
    {
        var oldItems = oldArray.Select(FormatFullValue).ToHashSet(StringComparer.Ordinal);
        var newItems = newArray.Select(FormatFullValue).ToHashSet(StringComparer.Ordinal);
        foreach (var removed in oldItems.Except(newItems, StringComparer.Ordinal).Order())
        {
            changes.Add(new TaskHistoryFieldChange(
                path, DisplayName(rootProperty), Preview(removed),
                Localization.Get("TaskHistoryMissingValue"),
                TaskHistoryChangeType.Removed, IsMetadata(rootProperty),
                oldReference(path), newReference(path)));
        }

        foreach (var added in newItems.Except(oldItems, StringComparer.Ordinal).Order())
        {
            changes.Add(new TaskHistoryFieldChange(
                path, DisplayName(rootProperty),
                Localization.Get("TaskHistoryMissingValue"), Preview(added),
                TaskHistoryChangeType.Added, IsMetadata(rootProperty),
                oldReference(path), newReference(path)));
        }
    }

    private static void CompareCriteria(
        JArray oldArray,
        JArray newArray,
        string path,
        ICollection<TaskHistoryFieldChange> changes,
        Func<string, TaskHistoryValueReference?> oldReference,
        Func<string, TaskHistoryValueReference?> newReference)
    {
        var oldById = ToObjectDictionary(oldArray);
        var newById = ToObjectDictionary(newArray);
        if (oldById is null || newById is null)
        {
            AddChange(oldArray, newArray, path, "CompletionCriteria", changes, oldReference, newReference);
            return;
        }

        foreach (var id in oldById.Keys.Union(newById.Keys, StringComparer.Ordinal).Order())
        {
            oldById.TryGetValue(id, out var oldCriterion);
            newById.TryGetValue(id, out var newCriterion);
            if (JToken.DeepEquals(oldCriterion, newCriterion))
                continue;

            var oldDisplay = oldCriterion is null
                ? Localization.Get("TaskHistoryMissingValue")
                : CriterionDisplay(oldCriterion);
            var newDisplay = newCriterion is null
                ? Localization.Get("TaskHistoryMissingValue")
                : CriterionDisplay(newCriterion);
            if (oldCriterion is not null && newCriterion is not null &&
                string.Equals(oldDisplay, newDisplay, StringComparison.Ordinal))
            {
                oldDisplay = oldCriterion.ToString(Formatting.None);
                newDisplay = newCriterion.ToString(Formatting.None);
            }

            changes.Add(new TaskHistoryFieldChange(
                path,
                Localization.Get("TaskHistoryCompletionCriterion"),
                Preview(oldDisplay),
                Preview(newDisplay),
                oldCriterion is null
                    ? TaskHistoryChangeType.Added
                    : newCriterion is null
                        ? TaskHistoryChangeType.Removed
                        : TaskHistoryChangeType.Modified,
                IsMetadata: false,
                oldReference(path),
                newReference(path)));
        }

        var oldOrder = oldArray.OfType<JObject>().Select(item => item.Value<string>("Id")).ToArray();
        var newOrder = newArray.OfType<JObject>().Select(item => item.Value<string>("Id")).ToArray();
        if (!oldOrder.SequenceEqual(newOrder, StringComparer.Ordinal))
        {
            changes.Add(new TaskHistoryFieldChange(
                $"{path}.@order",
                Localization.Get("CompletionCriteria"),
                string.Join(" → ", oldOrder),
                string.Join(" → ", newOrder),
                TaskHistoryChangeType.Modified,
                IsMetadata: false,
                oldReference(path),
                newReference(path)));
        }
    }

    private static Dictionary<string, JObject>? ToObjectDictionary(JArray array)
    {
        var result = new Dictionary<string, JObject>(StringComparer.Ordinal);
        foreach (var token in array)
        {
            if (token is not JObject item ||
                string.IsNullOrWhiteSpace(item.Value<string>("Id")) ||
                !result.TryAdd(item.Value<string>("Id")!, item))
                return null;
        }

        return result;
    }

    private static string CriterionDisplay(JObject criterion)
    {
        var text = criterion.Value<string>("Text") ?? string.Empty;
        var isSatisfied = criterion.Value<bool?>("IsSatisfied") == true;
        return $"{(isSatisfied ? "✓" : "○")} {text}";
    }

    private static void AddChange(
        JToken? oldToken,
        JToken? newToken,
        string path,
        string rootProperty,
        ICollection<TaskHistoryFieldChange> changes,
        Func<string, TaskHistoryValueReference?> oldReference,
        Func<string, TaskHistoryValueReference?> newReference)
    {
        var oldValue = oldToken is null
            ? Localization.Get("TaskHistoryMissingValue")
            : FormatKnownValue(rootProperty, oldToken);
        var newValue = newToken is null
            ? Localization.Get("TaskHistoryMissingValue")
            : FormatKnownValue(rootProperty, newToken);

        changes.Add(new TaskHistoryFieldChange(
            path,
            DisplayName(string.IsNullOrWhiteSpace(path) ? rootProperty : path),
            Preview(oldValue),
            Preview(newValue),
            oldToken is null
                ? TaskHistoryChangeType.Added
                : newToken is null
                    ? TaskHistoryChangeType.Removed
                    : TaskHistoryChangeType.Modified,
            IsMetadata(rootProperty) ||
            ((rootProperty == "CompletedDateTime" || rootProperty == "ArchiveDateTime") &&
             ((oldToken is null && newToken?.Type == JTokenType.Null) ||
              (newToken is null && oldToken?.Type == JTokenType.Null))),
            oldReference(path),
            newReference(path)));
    }

    private static string FormatKnownValue(string rootProperty, JToken value)
    {
        if (string.Equals(rootProperty, "Status", StringComparison.OrdinalIgnoreCase))
            return FormatStatus(value);

        if (string.Equals(rootProperty, "IsCompleted", StringComparison.OrdinalIgnoreCase))
        {
            if (value.Type == JTokenType.Null)
                return Localization.Get("TaskStatusArchived");
            return value.Value<bool>()
                ? Localization.Get("TaskStatusCompleted")
                : Localization.Get("TaskStatusNotReady");
        }

        return FormatFullValue(value);
    }

    private static string FormatStatus(JToken value)
    {
        var raw = value.Type == JTokenType.String ? value.Value<string>() : value.ToString();
        return raw switch
        {
            "0" or "NotReady" => Localization.Get("TaskStatusNotReady"),
            "1" or "Prepared" => Localization.Get("TaskStatusPrepared"),
            "2" or "InProgress" => Localization.Get("TaskStatusInProgress"),
            "3" or "Completed" => Localization.Get("TaskStatusCompleted"),
            "4" or "Archived" => Localization.Get("TaskStatusArchived"),
            _ => raw ?? string.Empty
        };
    }

    private static HashSet<string> FindEquivalentLegacyMigrationFields(JObject? oldObject, JObject? newObject)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (oldObject is null || newObject is null ||
            oldObject.Property("Status") is not null ||
            oldObject.Property("IsCompleted") is null ||
            newObject.Property("Status") is null ||
            newObject.Property("IsCompleted") is not null)
            return ignored;

        var legacy = oldObject["IsCompleted"];
        var modern = newObject["Status"];
        var equivalent = legacy?.Type == JTokenType.Null
            ? modern?.ToString() is "4" or "Archived"
            : legacy?.Value<bool>() == true
                ? modern?.ToString() is "3" or "Completed"
                : modern?.ToString() is "0" or "NotReady";
        if (!equivalent)
            return ignored;

        ignored.Add("IsCompleted");
        ignored.Add("Status");
        return ignored;
    }

    private static TaskHistoryValueReference? CreateReference(
        GitTaskHistoryProvider.TaskFileSnapshot? snapshot,
        string repositoryPath,
        string? commitSha,
        string jsonPath)
    {
        if (snapshot is null)
            return null;

        return snapshot.IsWorkingTree
            ? new TaskHistoryValueReference(
                repositoryPath, null, snapshot.FilePath, jsonPath,
                IsWorkingTree: true, ExpectedContentHash: snapshot.ContentHash)
            : new TaskHistoryValueReference(repositoryPath, commitSha, snapshot.RelativePath, jsonPath, IsWorkingTree: false);
    }

    private static string DisplayName(string path)
    {
        var property = path.Split('.', '[', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? path;
        var key = property switch
        {
            "Title" => "TaskHistoryFieldTitle",
            "Description" => "TaskHistoryFieldDescription",
            "Status" or "IsCompleted" => "TaskHistoryFieldStatus",
            "CompletedDateTime" => "TaskHistoryFieldCompletedAt",
            "ArchiveDateTime" => "TaskHistoryFieldArchivedAt",
            "CompletionCriteria" => "CompletionCriteria",
            "PlannedBeginDateTime" => "TaskHistoryFieldPlannedBegin",
            "PlannedEndDateTime" => "TaskHistoryFieldPlannedEnd",
            "PlannedDuration" => "TaskHistoryFieldPlannedDuration",
            "ContainsTasks" => "TaskHistoryFieldContains",
            "ParentTasks" => "TaskHistoryFieldParents",
            "BlocksTasks" => "TaskHistoryFieldBlocks",
            "BlockedByTasks" => "TaskHistoryFieldBlockedBy",
            "Repeater" => "TaskHistoryFieldRepeater",
            "Importance" => "TaskHistoryFieldImportance",
            "Wanted" => "TaskHistoryFieldWanted",
            _ => string.Empty
        };

        return string.IsNullOrEmpty(key) ? path : Localization.Get(key);
    }

    private static bool IsMetadata(string rootProperty) => MetadataFields.Contains(rootProperty);

    private static string Preview(string value) =>
        value.Length <= PreviewLength ? value : value[..PreviewLength] + "…";

    private static string EscapeJsonPathProperty(string propertyName) =>
        propertyName.All(static c => char.IsLetterOrDigit(c) || c == '_')
            ? propertyName
            : "['" + propertyName.Replace("'", "\\'") + "']";
}
