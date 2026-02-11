using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Xunit;

namespace Unlimotion.Test
{
    public class TaskCompletionChangeTests
    {
        [Fact]
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
            Assert.NotNull(task.CompletedDateTime);
            Assert.Null(task.ArchiveDateTime);
            Assert.Contains(task, result);
        }

        [Fact]
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
            Assert.Null(task.CompletedDateTime);
            Assert.Null(task.ArchiveDateTime);
            Assert.Contains(task, result);
        }

        [Fact]
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
            Assert.NotNull(task.ArchiveDateTime);
            Assert.Null(task.CompletedDateTime);
            Assert.Contains(task, result);
        }

        [Fact]
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
            Assert.NotNull(task.CompletedDateTime);
            Assert.Null(task.ArchiveDateTime);
            Assert.Contains(task, result);
            Assert.NotNull(clonedTask.Id);
            Assert.Equal(task.Title, clonedTask.Title);
            Assert.Equal(task.Description, clonedTask.Description);
            Assert.Equal(task.ContainsTasks, clonedTask.ContainsTasks);
            Assert.Equal(task.BlocksTasks, clonedTask.BlocksTasks);
            Assert.Equal(task.BlockedByTasks, clonedTask.BlockedByTasks);
            Assert.Contains(clonedTask, result);
        }

        [Fact]
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
            Assert.NotNull(clone);

            var cloneFromStorage = await storage.Load(clone.Id);
            var childFromStorage = await storage.Load(child.Id);
            var blockerFromStorage = await storage.Load(blocker.Id);
            var blockedFromStorage = await storage.Load(blocked.Id);

            Assert.NotNull(cloneFromStorage);
            Assert.Contains(child.Id, cloneFromStorage.ContainsTasks);
            Assert.Contains(blocker.Id, cloneFromStorage.BlockedByTasks);
            Assert.Contains(blocked.Id, cloneFromStorage.BlocksTasks);

            Assert.NotNull(childFromStorage);
            Assert.Contains(clone.Id, childFromStorage.ParentTasks);

            Assert.NotNull(blockerFromStorage);
            Assert.Contains(clone.Id, blockerFromStorage.BlocksTasks);

            Assert.NotNull(blockedFromStorage);
            Assert.Contains(clone.Id, blockedFromStorage.BlockedByTasks);
            Assert.False(blockedFromStorage.IsCanBeCompleted);

            Assert.False(cloneFromStorage.IsCanBeCompleted);
            Assert.Null(cloneFromStorage.UnlockedDateTime);
        }
    }
}
