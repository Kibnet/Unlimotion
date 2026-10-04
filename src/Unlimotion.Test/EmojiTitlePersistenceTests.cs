using System;
using System.IO;
using System.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class EmojiTitlePersistenceTests
{
    [Test]
    public Task ChangedEmoji_SaveAndReloadPreserveTitleRelationsAndInheritedSequence() =>
        AssertSaveAndReloadPreserveEmojiAsync("🧭", "🐦‍🔥");

    [Test]
    public Task ExistingTaskWizardToJellyfish_SaveAndReloadPreserveTitleRelationsAndInheritedSequence() =>
        AssertSaveAndReloadPreserveEmojiAsync("🧙‍♂️", "🪼");

    private static async Task AssertSaveAndReloadPreserveEmojiAsync(string originalEmoji, string replacementEmoji)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"unlimotion-emoji-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using (var storage = new FileStorage(directory))
            {
                await storage.Save(new TaskItem
                {
                    Id = "emoji-parent",
                    Title = $"{originalEmoji} Проект",
                    Status = DomainTaskStatus.Prepared,
                    ContainsTasks = ["emoji-child"]
                });
                await storage.Save(new TaskItem
                {
                    Id = "emoji-child",
                    Title = "Дочерняя задача",
                    Status = DomainTaskStatus.Prepared,
                    ParentTasks = ["emoji-parent"]
                });
                using var repository = new UnifiedTaskStorage(new TaskTreeManager(storage));
                await repository.Init();
                var parent = repository.Tasks.Lookup("emoji-parent").Value;
                parent.IsInitializedProvider = () => true;
                await Assert.That(parent.Title).IsEqualTo($"{originalEmoji} Проект");
                await Assert.That(parent.Emoji).IsEqualTo(originalEmoji);
                parent.Title = $"{replacementEmoji} Проект";
                await parent.SaveItemCommand.Execute().ToTask();
                await Task.WhenAll(repository.Tasks.Items.Select(task => task.SealPendingSaves()));
                await Assert.That((await storage.Load(parent.Id, forced: true))!.Title).IsEqualTo($"{replacementEmoji} Проект");
            }

            using var reloadedStorage = new FileStorage(directory);
            using var reloaded = new UnifiedTaskStorage(new TaskTreeManager(reloadedStorage));
            await reloaded.Init();
            var savedParent = reloaded.Tasks.Lookup("emoji-parent").Value;
            var child = reloaded.Tasks.Lookup("emoji-child").Value;
            await Assert.That(savedParent.Title).IsEqualTo($"{replacementEmoji} Проект");
            await Assert.That(savedParent.Emoji).IsEqualTo(replacementEmoji);
            await Assert.That(savedParent.ContainsTasks.Select(task => task.Id)).IsEquivalentTo([child.Id]);
            await Assert.That(child.ParentsTasks.Select(task => task.Id)).IsEquivalentTo([savedParent.Id]);
            await Assert.That(child.ParentEmojiTrail).IsEqualTo(replacementEmoji);
            await Assert.That(child.GetAllEmoji).IsEqualTo(replacementEmoji);
            await Task.WhenAll(reloaded.Tasks.Items.Select(task => task.SealPendingSaves()));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
