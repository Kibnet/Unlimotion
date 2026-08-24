using System;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Notes.Operations;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.Test;

public sealed class TaskStorageFeedTaskCreationTargetTests
{
    [Test]
    public async Task LocalRepositoryAdapterPersistsClassificationAndIsIdempotentByStableTaskId()
    {
        var storage = new InMemoryStorage();
        using var repository = new UnifiedTaskStorage(new TaskTreeManager(storage));
        await repository.Init();
        var target = new TaskStorageFeedTaskCreationTarget(() => repository);
        var draft = new FeedTaskDraft(
            "feed-operation1",
            "operation1",
            "Задача из Ленты",
            "Контекст Markdown",
            true,
            ["work", "project"]);

        var first = await target.CreateOrGetAsync(draft);
        var retry = await target.CreateOrGetAsync(draft);
        var stored = await storage.Load(draft.TaskId);

        await Assert.That(first.TaskId).IsEqualTo(draft.TaskId);
        await Assert.That(retry.TaskId).IsEqualTo(draft.TaskId);
        await Assert.That(repository.Tasks.Items.Count(task => task.Id == draft.TaskId)).IsEqualTo(1);
        await Assert.That(stored).IsNotNull();
        await Assert.That(stored!.Title).IsEqualTo(draft.Title);
        await Assert.That(stored.Description).IsEqualTo(draft.Description);
        await Assert.That(stored.IsGoal).IsTrue();
        await Assert.That(stored.AreaIds).IsEquivalentTo(draft.AreaIds);
        await Assert.That(stored.PlannedBeginDateTime).IsNull();
        await Assert.That(stored.PlannedEndDateTime).IsNull();
    }

    [Test]
    public async Task UnsupportedTaskStorageRejectsConversionBeforeCreatingTask()
    {
        var storage = new UnsupportedClassificationStorage();
        using var repository = new UnifiedTaskStorage(new TaskTreeManager(storage));
        await repository.Init();
        var target = new TaskStorageFeedTaskCreationTarget(() => repository);
        var draft = new FeedTaskDraft(
            "feed-unsupported",
            "operation-unsupported",
            "Задача",
            string.Empty,
            true,
            ["work"]);

        await Assert.That(() => target.CreateOrGetAsync(draft))
            .Throws<InvalidOperationException>();
        await Assert.That(target.SupportsClassification).IsFalse();
        await Assert.That(await storage.Load(draft.TaskId)).IsNull();
    }

    private sealed class UnsupportedClassificationStorage : InMemoryStorage, ITaskClassificationCapabilityProvider
    {
        public bool SupportsTaskClassification => false;
    }
}
