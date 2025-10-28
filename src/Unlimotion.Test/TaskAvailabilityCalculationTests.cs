using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Xunit;

namespace Unlimotion.Test
{
    public class TaskAvailabilityCalculationTests
    {
        private class InMemoryStorage : IStorage
        {
            private readonly Dictionary<string, TaskItem> _tasks = new();

            public Task<TaskItem> Load(string id)
            {
                return Task.FromResult(_tasks.TryGetValue(id, out var task) ? task : null);
            }

            public Task<bool> Save(TaskItem taskItem)
            {
                _tasks[taskItem.Id] = taskItem;
                return Task.FromResult(true);
            }

            public Task<bool> Remove(string id)
            {
                _tasks.Remove(id);
                return Task.FromResult(true);
            }

            public void Clear() => _tasks.Clear();
        }

        [Fact]
        public async Task TaskWithNoDependencies_ShouldBeAvailable()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);
            var task = new TaskItem
            {
                Id = "task1",
                Title = "Test Task",
                IsCompleted = false,
                ContainsTasks = new List<string>(),
                BlockedByTasks = new List<string>()
            };

            // Act
            var results = await manager.CalculateAndUpdateAvailability(task);

            // Assert
            Assert.True(task.IsCanBeCompleted);
            Assert.NotNull(task.UnlockedDateTime);
        }

        [Fact]
        public async Task TaskWithCompletedChild_ShouldBeAvailable()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);
            
            var childTask = new TaskItem
            {
                Id = "child1",
                Title = "Child Task",
                IsCompleted = true
            };
            await storage.Save(childTask);

            var parentTask = new TaskItem
            {
                Id = "parent1",
                Title = "Parent Task",
                IsCompleted = false,
                ContainsTasks = new List<string> { "child1" },
                BlockedByTasks = new List<string>()
            };

            // Act
            var results = await manager.CalculateAndUpdateAvailability(parentTask);

            // Assert
            Assert.True(parentTask.IsCanBeCompleted);
            Assert.NotNull(parentTask.UnlockedDateTime);
        }

        [Fact]
        public async Task TaskWithIncompleteChild_ShouldNotBeAvailable()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);
            
            var childTask = new TaskItem
            {
                Id = "child1",
                Title = "Child Task",
                IsCompleted = false
            };
            await storage.Save(childTask);

            var parentTask = new TaskItem
            {
                Id = "parent1",
                Title = "Parent Task",
                IsCompleted = false,
                IsCanBeCompleted = true, // Initially available
                UnlockedDateTime = DateTimeOffset.UtcNow,
                ContainsTasks = new List<string> { "child1" },
                BlockedByTasks = new List<string>()
            };

            // Act
            var results = await manager.CalculateAndUpdateAvailability(parentTask);

            // Assert
            Assert.False(parentTask.IsCanBeCompleted);
            Assert.Null(parentTask.UnlockedDateTime);
        }

        [Fact]
        public async Task TaskWithArchivedChild_ShouldBeAvailable()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);
            
            var childTask = new TaskItem
            {
                Id = "child1",
                Title = "Child Task",
                IsCompleted = null // Archived
            };
            await storage.Save(childTask);

            var parentTask = new TaskItem
            {
                Id = "parent1",
                Title = "Parent Task",
                IsCompleted = false,
                ContainsTasks = new List<string> { "child1" },
                BlockedByTasks = new List<string>()
            };

            // Act
            var results = await manager.CalculateAndUpdateAvailability(parentTask);

            // Assert
            Assert.True(parentTask.IsCanBeCompleted);
            Assert.NotNull(parentTask.UnlockedDateTime);
        }

        [Fact]
        public async Task TaskWithCompletedBlocker_ShouldBeAvailable()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);
            
            var blockerTask = new TaskItem
            {
                Id = "blocker1",
                Title = "Blocker Task",
                IsCompleted = true
            };
            await storage.Save(blockerTask);

            var blockedTask = new TaskItem
            {
                Id = "blocked1",
                Title = "Blocked Task",
                IsCompleted = false,
                ContainsTasks = new List<string>(),
                BlockedByTasks = new List<string> { "blocker1" }
            };

            // Act
            var results = await manager.CalculateAndUpdateAvailability(blockedTask);

            // Assert
            Assert.True(blockedTask.IsCanBeCompleted);
            Assert.NotNull(blockedTask.UnlockedDateTime);
        }

        [Fact]
        public async Task TaskWithIncompleteBlocker_ShouldNotBeAvailable()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);
            
            var blockerTask = new TaskItem
            {
                Id = "blocker1",
                Title = "Blocker Task",
                IsCompleted = false
            };
            await storage.Save(blockerTask);

            var blockedTask = new TaskItem
            {
                Id = "blocked1",
                Title = "Blocked Task",
                IsCompleted = false,
                IsCanBeCompleted = true,
                UnlockedDateTime = DateTimeOffset.UtcNow,
                ContainsTasks = new List<string>(),
                BlockedByTasks = new List<string> { "blocker1" }
            };

            // Act
            var results = await manager.CalculateAndUpdateAvailability(blockedTask);

            // Assert
            Assert.False(blockedTask.IsCanBeCompleted);
            Assert.Null(blockedTask.UnlockedDateTime);
        }

        [Fact]
        public async Task TaskWithMixedDependencies_OneIncomplete_ShouldNotBeAvailable()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);
            
            var child1 = new TaskItem { Id = "child1", IsCompleted = true };
            var child2 = new TaskItem { Id = "child2", IsCompleted = false };
            var blocker1 = new TaskItem { Id = "blocker1", IsCompleted = true };
            
            await storage.Save(child1);
            await storage.Save(child2);
            await storage.Save(blocker1);

            var task = new TaskItem
            {
                Id = "task1",
                Title = "Task",
                IsCompleted = false,
                ContainsTasks = new List<string> { "child1", "child2" },
                BlockedByTasks = new List<string> { "blocker1" }
            };

            // Act
            var results = await manager.CalculateAndUpdateAvailability(task);

            // Assert
            Assert.False(task.IsCanBeCompleted);
        }

        [Fact]
        public async Task UnlockedDateTime_ShouldBeSetWhenTaskBecomesAvailable()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);
            
            var task = new TaskItem
            {
                Id = "task1",
                Title = "Task",
                IsCompleted = false,
                IsCanBeCompleted = false,
                UnlockedDateTime = null,
                ContainsTasks = new List<string>(),
                BlockedByTasks = new List<string>()
            };

            var beforeCalculation = DateTimeOffset.UtcNow;

            // Act
            await manager.CalculateAndUpdateAvailability(task);

            // Assert
            Assert.True(task.IsCanBeCompleted);
            Assert.NotNull(task.UnlockedDateTime);
            Assert.True(task.UnlockedDateTime >= beforeCalculation);
        }

        [Fact]
        public async Task UnlockedDateTime_ShouldBeClearedWhenTaskBecomesBlocked()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);
            
            var blocker = new TaskItem { Id = "blocker1", IsCompleted = false };
            await storage.Save(blocker);

            var task = new TaskItem
            {
                Id = "task1",
                Title = "Task",
                IsCompleted = false,
                IsCanBeCompleted = true,
                UnlockedDateTime = DateTimeOffset.UtcNow,
                ContainsTasks = new List<string>(),
                BlockedByTasks = new List<string> { "blocker1" }
            };

            // Act
            await manager.CalculateAndUpdateAvailability(task);

            // Assert
            Assert.False(task.IsCanBeCompleted);
            Assert.Null(task.UnlockedDateTime);
        }

        [Fact]
        public async Task AddChildTask_ShouldRecalculateParentAvailability()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);
            
            var parentTask = new TaskItem
            {
                Id = "parent1",
                Title = "Parent",
                IsCompleted = false,
                IsCanBeCompleted = true,
                ContainsTasks = new List<string>(),
                ParentTasks = new List<string>()
            };
            await storage.Save(parentTask);

            var childTask = new TaskItem
            {
                Id = "child1",
                Title = "Child",
                IsCompleted = false,
                ContainsTasks = new List<string>(),
                ParentTasks = new List<string>()
            };

            // Act
            var results = await manager.AddChildTask(childTask, parentTask);

            // Assert
            var updatedParent = results.FirstOrDefault(t => t.Id == "parent1");
            Assert.NotNull(updatedParent);
            Assert.False(updatedParent.IsCanBeCompleted);
        }

        [Fact]
        public async Task CreateBlockingRelation_ShouldRecalculateBlockedTaskAvailability()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);
            
            var blockerTask = new TaskItem
            {
                Id = "blocker1",
                Title = "Blocker",
                IsCompleted = false,
                BlocksTasks = new List<string>()
            };

            var taskToBlock = new TaskItem
            {
                Id = "blocked1",
                Title = "Blocked",
                IsCompleted = false,
                IsCanBeCompleted = true,
                BlockedByTasks = new List<string>()
            };

            // Act
            var results = await manager.BlockTask(taskToBlock, blockerTask);

            // Assert
            var updatedBlocked = results.FirstOrDefault(t => t.Id == "blocked1");
            Assert.NotNull(updatedBlocked);
            Assert.False(updatedBlocked.IsCanBeCompleted);
        }

        [Fact]
        public async Task BreakBlockingRelation_ShouldRecalculateBlockedTaskAvailability()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);
            
            var blockerTask = new TaskItem
            {
                Id = "blocker1",
                Title = "Blocker",
                IsCompleted = false,
                BlocksTasks = new List<string> { "blocked1" }
            };
            await storage.Save(blockerTask);

            var taskToUnblock = new TaskItem
            {
                Id = "blocked1",
                Title = "Blocked",
                IsCompleted = false,
                IsCanBeCompleted = false,
                BlockedByTasks = new List<string> { "blocker1" }
            };
            await storage.Save(taskToUnblock);

            // Act
            var results = await manager.UnblockTask(taskToUnblock, blockerTask);

            // Assert
            var updatedUnblocked = results.FirstOrDefault(t => t.Id == "blocked1");
            Assert.NotNull(updatedUnblocked);
            Assert.True(updatedUnblocked.IsCanBeCompleted);
        }

        [Fact]
        public async Task UpdateTask_WithIsCompletedChange_ShouldRecalculateAffectedTasks()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);
            
            // Create a child task
            var childTask = new TaskItem
            {
                Id = "child1",
                Title = "Child",
                IsCompleted = false,
                ParentTasks = new List<string> { "parent1" }
            };
            await storage.Save(childTask);

            // Create a parent task that depends on the child
            var parentTask = new TaskItem
            {
                Id = "parent1",
                Title = "Parent",
                IsCompleted = false,
                IsCanBeCompleted = false,
                ContainsTasks = new List<string> { "child1" }
            };
            await storage.Save(parentTask);

            // Complete the child task
            childTask.IsCompleted = true;

            // Act
            await manager.UpdateTask(childTask);

            // Assert - parent should now be available
            var updatedParent = await storage.Load("parent1");
            Assert.True(updatedParent.IsCanBeCompleted);
        }
    }
}
