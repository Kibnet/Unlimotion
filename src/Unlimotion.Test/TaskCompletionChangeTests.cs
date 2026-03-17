using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;

namespace Unlimotion.Test
{
    public class TaskCompletionChangeTests
    {
        [Test]
        public async Task HandleTaskCompletionChange_CompletedTask_SetsCompletedDateTime()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);

            var task = new TaskItem
            {
                Id = "test-task",
                IsCompleted = true,
                CompletedDateTime = null
            };
            
            // Act
            var result = await manager.HandleTaskCompletionChange(task);

            // Assert
            await Assert.That(task.CompletedDateTime).IsNotNull();
            await Assert.That(task.ArchiveDateTime).IsNull();
            await Assert.That(result).Contains(task);
        }

        [Test]
        public async Task HandleTaskCompletionChange_UncompletedTask_ClearsDates()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);

            var task = new TaskItem
            {
                Id = "test-task",
                IsCompleted = false,
                CompletedDateTime = DateTimeOffset.UtcNow,
                ArchiveDateTime = DateTimeOffset.UtcNow
            };
            
            // Act
            var result = await manager.HandleTaskCompletionChange(task);

            // Assert
            await Assert.That(task.CompletedDateTime).IsNull();
            await Assert.That(task.ArchiveDateTime).IsNull();
            await Assert.That(result).Contains(task);
        }

        [Test]
        public async Task HandleTaskCompletionChange_ArchivedTask_SetsArchiveDateTime()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);

            var task = new TaskItem
            {
                Id = "test-task",
                IsCompleted = null,
                ArchiveDateTime = null
            };
            
            // Act
            var result = await manager.HandleTaskCompletionChange(task);

            // Assert
            await Assert.That(task.ArchiveDateTime).IsNotNull();
            await Assert.That(task.CompletedDateTime).IsNull();
            await Assert.That(result).Contains(task);
        }

        [Test]
        public async Task HandleTaskCompletionChange_CompletedTaskWithRepeater_CreatesClone()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);

            var task = new TaskItem
            {
                Id = "test-task",
                IsCompleted = true,
                CompletedDateTime = null,
                Repeater = new RepeaterPattern
                {
                    Type = RepeaterType.Daily,
                    Period = 1
                },
                PlannedBeginDateTime = DateTimeOffset.UtcNow.AddDays(-1),
                PlannedEndDateTime = DateTimeOffset.UtcNow,
                ContainsTasks = new List<string> { "child1" },
                BlocksTasks = new List<string> { "blocked1" },
                BlockedByTasks = new List<string> { "blocker1" },
                Description = "Test task",
                Title = "Test Task"
            };
            
            // Act
            var result = await manager.HandleTaskCompletionChange(task);

            var clonedTask = result.Last();

            // Assert
            await Assert.That(task.CompletedDateTime).IsNotNull();
            await Assert.That(task.ArchiveDateTime).IsNull();
            await Assert.That(result).Contains(task);
            await Assert.That(clonedTask.Id).IsNotNull();
            await Assert.That(clonedTask.Title).IsEqualTo(task.Title);
            await Assert.That(clonedTask.Description).IsEqualTo(task.Description);
            await Assert.That(clonedTask.ContainsTasks).IsEqualTo(task.ContainsTasks);
            await Assert.That(clonedTask.BlocksTasks).IsEqualTo(task.BlocksTasks);
            await Assert.That(clonedTask.BlockedByTasks).IsEqualTo(task.BlockedByTasks);
            await Assert.That(result).Contains(clonedTask);
        }

        [Test]
        public async Task HandleTaskCompletionChange_CompletedTaskWithRepeater_ShouldSyncCloneRelationsAndAvailability()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);

            var child = new TaskItem
            {
                Id = "child",
                IsCompleted = false,
                ParentTasks = new List<string> { "source" }
            };

            var blocker = new TaskItem
            {
                Id = "blocker",
                IsCompleted = false,
                BlocksTasks = new List<string> { "source" }
            };

            var blocked = new TaskItem
            {
                Id = "blocked",
                IsCompleted = false,
                IsCanBeCompleted = true,
                UnlockedDateTime = DateTimeOffset.UtcNow,
                BlockedByTasks = new List<string> { "source" }
            };

            var source = new TaskItem
            {
                Id = "source",
                IsCompleted = true,
                CompletedDateTime = null,
                Repeater = new RepeaterPattern
                {
                    Type = RepeaterType.Daily,
                    Period = 1
                },
                PlannedBeginDateTime = DateTimeOffset.UtcNow.AddDays(-1),
                PlannedEndDateTime = DateTimeOffset.UtcNow,
                ContainsTasks = new List<string> { "child" },
                BlocksTasks = new List<string> { "blocked" },
                BlockedByTasks = new List<string> { "blocker" },
                Description = "Source description",
                Title = "Source title"
            };

            await storage.Save(child);
            await storage.Save(blocker);
            await storage.Save(blocked);
            await storage.Save(source);

            // Act
            var result = await manager.HandleTaskCompletionChange(source);

            // Assert
            var clone = result.FirstOrDefault(t => t.Id != source.Id && t.Title == source.Title);
            await Assert.That(clone).IsNotNull();

            var cloneFromStorage = await storage.Load(clone.Id);
            var childFromStorage = await storage.Load(child.Id);
            var blockerFromStorage = await storage.Load(blocker.Id);
            var blockedFromStorage = await storage.Load(blocked.Id);

            await Assert.That(cloneFromStorage).IsNotNull();
            await Assert.That(cloneFromStorage.ContainsTasks).Contains(child.Id);
            await Assert.That(cloneFromStorage.BlockedByTasks).Contains(blocker.Id);
            await Assert.That(cloneFromStorage.BlocksTasks).Contains(blocked.Id);

            await Assert.That(childFromStorage).IsNotNull();
            await Assert.That(childFromStorage.ParentTasks).Contains(clone.Id);

            await Assert.That(blockerFromStorage).IsNotNull();
            await Assert.That(blockerFromStorage.BlocksTasks).Contains(clone.Id);

            await Assert.That(blockedFromStorage).IsNotNull();
            await Assert.That(blockedFromStorage.BlockedByTasks).Contains(clone.Id);
            await Assert.That(blockedFromStorage.IsCanBeCompleted).IsFalse();

            await Assert.That(cloneFromStorage.IsCanBeCompleted).IsFalse();
            await Assert.That(cloneFromStorage.UnlockedDateTime).IsNull();
        }

        [Test]
        public async Task HandleTaskCompletionChange_UpdateTask_SetUpdatedDateTime()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);

            var task = new TaskItem
            {
                Id = "test-task",
                Title = "v1",
                Description = "d1",
                IsCompleted = false
            };

            await storage.Save(task);

            // Act 1
            task.Title = "v2";
            await manager.UpdateTask(task);
            var firstUpdated = task.UpdatedDateTime;

            // Act 2
            task.Description = "d2";
            await manager.UpdateTask(task);
            var secondUpdated = task.UpdatedDateTime;

            // Assert
            await Assert.That(firstUpdated).IsNotNull();
            await Assert.That(secondUpdated).IsNotNull();
            await Assert.That(secondUpdated > firstUpdated).IsTrue();
        }
    }
}