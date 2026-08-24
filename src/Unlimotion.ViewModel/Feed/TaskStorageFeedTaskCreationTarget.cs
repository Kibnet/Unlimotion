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

public sealed class TaskStorageFeedTaskCreationTarget(Func<ITaskStorage?> storageProvider) : IFeedTaskCreationTarget
{
    private const string OperationMetadataKey = "unlimotionFeedOperationId";

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
        if (!SupportsClassificationFor(repository))
        {
            throw new InvalidOperationException(
                "The active task storage does not support goal and area classification.");
        }

        var cached = repository.Tasks.Lookup(draft.TaskId);
        if (cached.HasValue)
        {
            EnsureOperationOwnership(cached.Value.Model, draft);
            return new FeedCreatedTask(cached.Value.Id, cached.Value.Title);
        }

        var stored = await repository.TaskTreeManager.Storage.Load(draft.TaskId).ConfigureAwait(false);
        if (stored is not null)
        {
            EnsureOperationOwnership(stored, draft);
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
        var viewModel = await repository.Update(created).ConfigureAwait(false);
        if (viewModel is null || !string.Equals(viewModel.Id, draft.TaskId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Task storage could not reconcile the converted task into its repository cache.");
        }

        return new FeedCreatedTask(viewModel.Id, viewModel.Title);
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

        if (task.ExtensionData?.TryGetValue(OperationMetadataKey, out var operation) == true
            && !string.Equals(operation.Value<string>(), draft.OperationId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The stable feed task ID belongs to another conversion operation.");
        }
    }
}
