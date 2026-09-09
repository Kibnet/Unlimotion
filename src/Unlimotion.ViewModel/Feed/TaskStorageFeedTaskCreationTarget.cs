using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DynamicData;
using Newtonsoft.Json.Linq;
using Unlimotion.Domain;
using Unlimotion.Notes.Operations;

namespace Unlimotion.ViewModel.Feed;

public interface ITaskClassificationCapabilityProvider
{
    bool SupportsTaskClassification { get; }
}

public sealed class TaskStorageFeedTaskCreationTarget(
    Func<ITaskStorage?> storageProvider,
    Func<FeedTaskSourceIdentity?>? taskSourceIdentityProvider = null) : IFeedTaskCreationTarget
{
    private const string OperationMetadataKey = "unlimotionFeedOperationId";
    public bool SupportsReadOnlyLookup => true;

    public async Task<FeedCreatedTask?> FindOwnedAsync(FeedTaskDraft draft, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FeedTaskSourceIdentity.RequireCurrent(draft.TaskSourceIdentity, taskSourceIdentityProvider);
        var repository = storageProvider() ?? throw new InvalidOperationException("Task storage is not connected.");
        var stored = await repository.TaskTreeManager.Storage.Load(draft.TaskId).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (stored is null) return null;
        EnsureOperationOwnership(stored, draft);
        return new FeedCreatedTask(stored.Id, stored.Title);
    }

    public bool SupportsClassification
    {
        get
        {
            var repository = storageProvider();
            return repository is not null && SupportsClassificationFor(repository);
        }
    }

    public async Task<FeedCreatedTask> CreateOrGetAsync(
        FeedTaskDraft draft,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var repository = storageProvider()
            ?? throw new InvalidOperationException("Task storage is not connected.");
        EnsureCurrent(repository, draft, cancellationToken);
        if (!SupportsClassificationFor(repository))
        {
            throw new InvalidOperationException(
                "The active task storage does not support goal and area classification.");
        }

        // Resolve all parents before creating anything. A missing/archived root never silently becomes a rootless task.
        foreach (var parentId in (draft.ParentTaskIds ?? []).Distinct(StringComparer.Ordinal))
            await RequireParentAsync(repository, draft, parentId, cancellationToken).ConfigureAwait(false);

        var stored = await repository.TaskTreeManager.Storage.Load(draft.TaskId).ConfigureAwait(false);
        if (stored is not null)
        {
            EnsureOperationOwnership(stored, draft);
            stored = await EnsureParentsAsync(repository, draft, stored, cancellationToken).ConfigureAwait(false);
            EnsureCurrent(repository, draft, cancellationToken);
            var reconciled = await repository.Update(stored).ConfigureAwait(false);
            return new FeedCreatedTask(reconciled.Id, reconciled.Title);
        }

        var task = new TaskItem
        {
            Id = draft.TaskId,
            Title = draft.Title,
            Description = draft.Description,
            IsGoal = draft.IsGoal,
            AreaIds = draft.AreaIds.Distinct(StringComparer.Ordinal).ToList(),
            ExtensionData = new Dictionary<string, JToken>(StringComparer.Ordinal)
            {
                [OperationMetadataKey] = JValue.CreateString(draft.OperationId)
            }
        };
        EnsureCurrent(repository, draft, cancellationToken);
        var createdGraph = await repository.TaskTreeManager.AddTask(task).ConfigureAwait(false);
        var created = createdGraph.FirstOrDefault(value =>
            string.Equals(value.Id, draft.TaskId, StringComparison.Ordinal));
        if (created is null)
        {
            created = await repository.TaskTreeManager.Storage.Load(draft.TaskId).ConfigureAwait(false);
        }

        if (created is null)
        {
            throw new IOException("Task storage did not persist the feed conversion task.");
        }

        EnsureOperationOwnership(created, draft);
        created = await EnsureParentsAsync(repository, draft, created, cancellationToken).ConfigureAwait(false);
        EnsureCurrent(repository, draft, cancellationToken);
        var viewModel = await repository.Update(created).ConfigureAwait(false);
        if (viewModel is null || !string.Equals(viewModel.Id, draft.TaskId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Task storage could not reconcile the converted task into its repository cache.");
        }

        return new FeedCreatedTask(viewModel.Id, viewModel.Title);
    }

    private void EnsureCurrent(ITaskStorage repository, FeedTaskDraft draft, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FeedTaskSourceIdentity.RequireCurrent(draft.TaskSourceIdentity, taskSourceIdentityProvider);
        if (!ReferenceEquals(storageProvider(), repository))
            throw new InvalidOperationException("FeedTaskSourceMismatch");
    }

    private async Task<TaskItem> RequireParentAsync(ITaskStorage repository, FeedTaskDraft draft, string parentId,
        CancellationToken cancellationToken)
    {
        EnsureCurrent(repository, draft, cancellationToken);
        if (string.IsNullOrWhiteSpace(parentId) || parentId == draft.TaskId)
            throw new InvalidOperationException("AreaRootTaskUnavailable");
        var parent = await repository.TaskTreeManager.Storage.Load(parentId).ConfigureAwait(false);
        EnsureCurrent(repository, draft, cancellationToken);
        if (parent is null || parent.IsCompleted is null)
            throw new InvalidOperationException("AreaRootTaskUnavailable");
        return parent;
    }

    private async Task<TaskItem> EnsureParentsAsync(ITaskStorage repository, FeedTaskDraft draft, TaskItem task,
        CancellationToken cancellationToken)
    {
        foreach (var parentId in (draft.ParentTaskIds ?? []).Distinct(StringComparer.Ordinal))
        {
            var parent = await RequireParentAsync(repository, draft, parentId, cancellationToken).ConfigureAwait(false);
            // Retry repairs either side of a partially persisted relation, rather than trusting the task cache.
            if (!task.ParentTasks.Contains(parentId) || !parent.ContainsTasks.Contains(task.Id))
            {
                EnsureCurrent(repository, draft, cancellationToken);
                var graph = await repository.TaskTreeManager.AddNewParentToTask(task, parent).ConfigureAwait(false);
                foreach (var changed in graph)
                {
                    EnsureCurrent(repository, draft, cancellationToken);
                    await repository.Update(changed).ConfigureAwait(false);
                }
            }
            EnsureCurrent(repository, draft, cancellationToken);
            task = await repository.TaskTreeManager.Storage.Load(draft.TaskId).ConfigureAwait(false)
                ?? throw new IOException("Task storage did not persist the feed conversion task.");
            parent = await RequireParentAsync(repository, draft, parentId, cancellationToken).ConfigureAwait(false);
            if (!task.ParentTasks.Contains(parentId) || !parent.ContainsTasks.Contains(task.Id))
                throw new IOException("Task storage did not persist the feed parent relation.");
        }
        return task;
    }

    private static bool SupportsClassificationFor(ITaskStorage repository) =>
        repository.TaskTreeManager.Storage is not ITaskClassificationCapabilityProvider provider
        || provider.SupportsTaskClassification;

    private static void EnsureOperationOwnership(TaskItem task, FeedTaskDraft draft)
    {
        if (!string.Equals(task.Id, draft.TaskId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The resolved task does not match the conversion task ID.");
        }

        if (task.ExtensionData is not { } metadata || !metadata.TryGetValue(OperationMetadataKey, out var operation)
            || !string.Equals(operation.Value<string>(), draft.OperationId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The stable feed task ID belongs to another conversion operation.");
        }
    }
}
