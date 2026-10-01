using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unlimotion.Domain;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Unlimotion.Notes.Operations;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class TaskStorageFeedTaskCreationTargetTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ExistingTaskWithoutMatchingOwnership_IsNeverReused(bool cached, bool foreignMarker)
    {
        var storage = new InMemoryStorage();
        using var repository = new UnifiedTaskStorage(new TaskTreeManager(storage));
        await repository.Init();
        var existing = new TaskItem { Id = "feed-collision", Title = "Чужая задача",
            ExtensionData = foreignMarker ? new Dictionary<string, JToken>
            { ["unlimotionFeedOperationId"] = JValue.CreateString("foreign") } : null };
        await storage.Save(existing);
        if (cached) await repository.Update(existing);
        var target = new TaskStorageFeedTaskCreationTarget(() => repository);
        var draft = new FeedTaskDraft(existing.Id, "ours", "Новая задача", "Контекст", false, []);
        await Assert.That(() => target.CreateOrGetAsync(draft)).Throws<InvalidDataException>();
        await Assert.That(() => target.FindOwnedAsync(draft)).Throws<InvalidDataException>();
        await Assert.That((await storage.Load(existing.Id))!.Title).IsEqualTo("Чужая задача");
    }

    [Test]
    public async Task BackgroundConversion_PublishesRepositoryChangesOnOwningUiContext()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var repository = new UnifiedTaskStorage(new TaskTreeManager(new InMemoryStorage()));
            await repository.Init();
            var wrongThread = false;
            using var subscription = repository.Tasks.Connect().Subscribe(_ =>
            {
                if (!Dispatcher.UIThread.CheckAccess()) wrongThread = true;
            });
            var target = new TaskStorageFeedTaskCreationTarget(() => repository);
            await Task.Run(() => target.CreateOrGetAsync(new FeedTaskDraft(
                "feed-background", "background", "Фоновая задача", "", false, [])));
            await Assert.That(wrongThread).IsFalse();
            await Assert.That(repository.Tasks.Count).IsEqualTo(1);
        }, CancellationToken.None);
    }

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
