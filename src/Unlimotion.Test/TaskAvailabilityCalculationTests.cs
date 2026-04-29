using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;

namespace Unlimotion.Test
{
    public class TaskAvailabilityCalculationTests
    {
                [Test]
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
            await Assert.That(task.IsCanBeCompleted).IsTrue();
            await Assert.That(task.UnlockedDateTime).IsNotNull();
        }

        [Test]
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
            await Assert.That(parentTask.IsCanBeCompleted).IsTrue();
            await Assert.That(parentTask.UnlockedDateTime).IsNotNull();
        }

        [Test]
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
            await Assert.That(parentTask.IsCanBeCompleted).IsFalse();
            await Assert.That(parentTask.UnlockedDateTime).IsNull();
        }

        [Test]
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
            await Assert.That(parentTask.IsCanBeCompleted).IsTrue();
            await Assert.That(parentTask.UnlockedDateTime).IsNotNull();
        }

        [Test]
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
            await Assert.That(blockedTask.IsCanBeCompleted).IsTrue();
            await Assert.That(blockedTask.UnlockedDateTime).IsNotNull();
        }

        [Test]
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
            await Assert.That(blockedTask.IsCanBeCompleted).IsFalse();
            await Assert.That(blockedTask.UnlockedDateTime).IsNull();
        }

        [Test]
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
            await Assert.That(task.IsCanBeCompleted).IsFalse();
        }

        [Test]
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
            await Assert.That(task.IsCanBeCompleted).IsTrue();
            await Assert.That(task.UnlockedDateTime).IsNotNull();
            await Assert.That(task.UnlockedDateTime >= beforeCalculation).IsTrue();
        }

        [Test]
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
            await Assert.That(task.IsCanBeCompleted).IsFalse();
            await Assert.That(task.UnlockedDateTime).IsNull();
        }

        [Test]
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
            await Assert.That(updatedParent).IsNotNull();
            await Assert.That(updatedParent.IsCanBeCompleted).IsFalse();
        }

        [Test]
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
            await Assert.That(updatedBlocked).IsNotNull();
            await Assert.That(updatedBlocked.IsCanBeCompleted).IsFalse();
        }

        [Test]
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
            await Assert.That(updatedUnblocked).IsNotNull();
            await Assert.That(updatedUnblocked.IsCanBeCompleted).IsTrue();
        }

        [Test]
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
            await Assert.That(updatedParent.IsCanBeCompleted).IsTrue();
        }

        [Test]
        public async Task MoveTaskWithChildToParent_ShouldMaintainBlockedState()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);

            // Create a child task that is not completed (blocking its parent)
            var childTask = new TaskItem
            {
                Id = "child1",
                Title = "Child Task",
                IsCompleted = false // Not completed, so it blocks its parent
            };
            await storage.Save(childTask);

            // Create original parent task
            var originalParentTask = new TaskItem
            {
                Id = "originalParent",
                Title = "Original Parent",
                IsCompleted = false,
                IsCanBeCompleted = true, // Initially available
                UnlockedDateTime = DateTimeOffset.UtcNow,
                ContainsTasks = new List<string>() { "child1" }, // Add the child relationship
                ParentTasks = new List<string>() // Initialize parent tasks
            };
            await storage.Save(originalParentTask);

            // Create new parent task that will receive the child
            var newParentTask = new TaskItem
            {
                Id = "newParent",
                Title = "New Parent",
                IsCompleted = false,
                IsCanBeCompleted = true, // Initially available
                UnlockedDateTime = DateTimeOffset.UtcNow,
                ContainsTasks = new List<string>(), // Initialize contains tasks
                ParentTasks = new List<string>() // Initialize parent tasks
            };
            await storage.Save(newParentTask);

            // Make sure child has correct parent relationship
            childTask.ParentTasks = new List<string> { "originalParent" };
            await storage.Save(childTask);

            // Verify initial state - original parent should be blocked because child is not completed
            var initialResult = await manager.CalculateAndUpdateAvailability(originalParentTask);
            var updatedOriginalParent = initialResult.FirstOrDefault(t => t.Id == "originalParent") ?? originalParentTask;
            await Assert.That(updatedOriginalParent.IsCanBeCompleted).IsFalse();
            await Assert.That(updatedOriginalParent.UnlockedDateTime).IsNull();

            // Act - Move child from original parent to new parent
            var moveResult = await manager.MoveTaskToNewParent(childTask, newParentTask, originalParentTask);

            // Reload tasks to get updated state after move
            var updatedChild = await storage.Load("child1");
            updatedOriginalParent = await storage.Load("originalParent");
            var updatedNewParent = await storage.Load("newParent");

            // Assert - Both parents should maintain correct blocked state
            // Original parent should now be unblocked (no children)
            await Assert.That(updatedOriginalParent.IsCanBeCompleted).IsTrue();
            await Assert.That(updatedOriginalParent.UnlockedDateTime).IsNotNull();

            // New parent should be blocked (has incomplete child)
            await Assert.That(updatedNewParent.IsCanBeCompleted).IsFalse();
            await Assert.That(updatedNewParent.UnlockedDateTime).IsNull();

            // Child relationships should be correct
            await Assert.That(updatedChild.ParentTasks).DoesNotContain("originalParent");
            await Assert.That(updatedChild.ParentTasks).Contains("newParent");
            await Assert.That(updatedOriginalParent.ContainsTasks).DoesNotContain("child1");
            await Assert.That(updatedNewParent.ContainsTasks).Contains("child1");
        }

        [Test]
        public async Task ParentTaskWithIncompleteChildAndBlocked_ShouldRemainNotAvailable()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);
            
            // Create a parent task
            var parentTask = new TaskItem
            {
                Id = "parent1",
                Title = "Parent Task",
                IsCompleted = false,
            };
            
            // Create the parent task in storage
            await storage.Save(parentTask);
            
            // Create an incomplete child task
            var childTask = new TaskItem
            {
                Id = "child1",
                Title = "Child Task",
                IsCompleted = false
            };
            
            // Create the child task in storage
            await storage.Save(childTask);

            // Create parent-child relationship
            await manager.AddChildTask(childTask, parentTask);
            var updatedParent = await storage.Load(parentTask.Id);
            await Assert.That(updatedParent.IsCanBeCompleted).IsFalse();
            await Assert.That(updatedParent.UnlockedDateTime).IsNull();

            // Create a blocker task
            var blockerTask = new TaskItem
            {
                Id = "blocker1",
                Title = "Blocker Task",
                IsCompleted = false
            };
            await storage.Save(blockerTask);

            // Act - Create blocking relation
            var results = await manager.BlockTask(parentTask, blockerTask);

            // Assert - Parent task should remain not available
            var updatedParentAfterBlock = results.FirstOrDefault(t => t.Id == "parent1");
            await Assert.That(updatedParentAfterBlock).IsNotNull();
            await Assert.That(updatedParentAfterBlock.IsCanBeCompleted).IsFalse();
            await Assert.That(updatedParentAfterBlock.UnlockedDateTime).IsNull();
        }

        [Test]
        public async Task CompletionImpact_ShouldPropagateTransitivelyThroughBlockingChain()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);

            // c blocks b, and b blocks a.
            // If c is incomplete, both b and a must end up unavailable.
            var taskC = new TaskItem
            {
                Id = "c",
                IsCompleted = false,
                BlocksTasks = new List<string> { "b" }
            };
            var taskB = new TaskItem
            {
                Id = "b",
                IsCompleted = false,
                IsCanBeCompleted = true,
                UnlockedDateTime = DateTimeOffset.UtcNow,
                BlockedByTasks = new List<string> { "c" },
                BlocksTasks = new List<string> { "a" }
            };
            var taskA = new TaskItem
            {
                Id = "a",
                IsCompleted = false,
                IsCanBeCompleted = true,
                UnlockedDateTime = DateTimeOffset.UtcNow,
                BlockedByTasks = new List<string> { "b" }
            };

            await storage.Save(taskC);
            await storage.Save(taskB);
            await storage.Save(taskA);

            // Act
            await manager.CalculateAndUpdateAvailability(taskC);

            // Assert
            var updatedB = await storage.Load("b");
            var updatedA = await storage.Load("a");

            await Assert.That(updatedB).IsNotNull();
            await Assert.That(updatedA).IsNotNull();
            await Assert.That(updatedB.IsCanBeCompleted).IsFalse();
            await Assert.That(updatedB.UnlockedDateTime).IsNull();
            await Assert.That(updatedA.IsCanBeCompleted).IsFalse();
            await Assert.That(updatedA.UnlockedDateTime).IsNull();
        }

        [Test]
        public async Task ChildTask_ShouldBecomeUnavailable_WhenParentHasIncompleteBlocker()
        {
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);

            var blocker = new TaskItem
            {
                Id = "blocker",
                IsCompleted = false,
                BlocksTasks = new List<string> { "parent" }
            };
            var parent = new TaskItem
            {
                Id = "parent",
                IsCompleted = false,
                IsCanBeCompleted = true,
                UnlockedDateTime = DateTimeOffset.UtcNow,
                ContainsTasks = new List<string> { "child" },
                BlockedByTasks = new List<string> { "blocker" }
            };
            var child = new TaskItem
            {
                Id = "child",
                IsCompleted = false,
                IsCanBeCompleted = true,
                UnlockedDateTime = DateTimeOffset.UtcNow,
                ParentTasks = new List<string> { "parent" }
            };

            await storage.Save(blocker);
            await storage.Save(parent);
            await storage.Save(child);

            await manager.CalculateAndUpdateAvailability(parent);

            var updatedChild = await storage.Load("child");

            await Assert.That(updatedChild).IsNotNull();
            await Assert.That(updatedChild.IsCanBeCompleted).IsFalse();
            await Assert.That(updatedChild.UnlockedDateTime).IsNull();
            await Assert.That(updatedChild.BlockedByTasks).IsEmpty();
        }

        [Test]
        public async Task Grandchild_ShouldInheritIncompleteBlockerFromAncestor()
        {
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);

            var blocker = new TaskItem
            {
                Id = "blocker",
                IsCompleted = false,
                BlocksTasks = new List<string> { "parent" }
            };
            var parent = new TaskItem
            {
                Id = "parent",
                IsCompleted = false,
                IsCanBeCompleted = true,
                UnlockedDateTime = DateTimeOffset.UtcNow,
                ContainsTasks = new List<string> { "child" },
                BlockedByTasks = new List<string> { "blocker" }
            };
            var child = new TaskItem
            {
                Id = "child",
                IsCompleted = false,
                IsCanBeCompleted = true,
                UnlockedDateTime = DateTimeOffset.UtcNow,
                ContainsTasks = new List<string> { "grandchild" },
                ParentTasks = new List<string> { "parent" }
            };
            var grandchild = new TaskItem
            {
                Id = "grandchild",
                IsCompleted = false,
                IsCanBeCompleted = true,
                UnlockedDateTime = DateTimeOffset.UtcNow,
                ParentTasks = new List<string> { "child" }
            };

            await storage.Save(blocker);
            await storage.Save(parent);
            await storage.Save(child);
            await storage.Save(grandchild);

            await manager.CalculateAndUpdateAvailability(parent);

            var updatedChild = await storage.Load("child");
            var updatedGrandchild = await storage.Load("grandchild");

            await Assert.That(updatedChild).IsNotNull();
            await Assert.That(updatedGrandchild).IsNotNull();
            await Assert.That(updatedChild.IsCanBeCompleted).IsFalse();
            await Assert.That(updatedGrandchild.IsCanBeCompleted).IsFalse();
            await Assert.That(updatedChild.UnlockedDateTime).IsNull();
            await Assert.That(updatedGrandchild.UnlockedDateTime).IsNull();
        }

        [Test]
        public async Task Sibling_ShouldRemainAvailable_WhenParentIsUnavailableOnlyBecauseOfAnotherIncompleteChild()
        {
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);

            var parent = new TaskItem
            {
                Id = "parent",
                IsCompleted = false,
                IsCanBeCompleted = true,
                UnlockedDateTime = DateTimeOffset.UtcNow,
                ContainsTasks = new List<string> { "blocking-child", "sibling" }
            };
            var blockingChild = new TaskItem
            {
                Id = "blocking-child",
                IsCompleted = false,
                ParentTasks = new List<string> { "parent" }
            };
            var sibling = new TaskItem
            {
                Id = "sibling",
                IsCompleted = false,
                IsCanBeCompleted = false,
                UnlockedDateTime = null,
                ParentTasks = new List<string> { "parent" }
            };

            await storage.Save(parent);
            await storage.Save(blockingChild);
            await storage.Save(sibling);

            await manager.CalculateAndUpdateAvailability(parent);

            var updatedParent = await storage.Load("parent");
            var updatedSibling = await storage.Load("sibling");

            await Assert.That(updatedParent).IsNotNull();
            await Assert.That(updatedSibling).IsNotNull();
            await Assert.That(updatedParent.IsCanBeCompleted).IsFalse();
            await Assert.That(updatedSibling.IsCanBeCompleted).IsTrue();
            await Assert.That(updatedSibling.UnlockedDateTime).IsNotNull();
        }

        [Test]
        public async Task Descendants_ShouldBecomeAvailable_WhenAncestorBlockerIsCompleted()
        {
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);

            var blocker = new TaskItem
            {
                Id = "blocker",
                IsCompleted = false,
                BlocksTasks = new List<string> { "parent" }
            };
            var parent = new TaskItem
            {
                Id = "parent",
                IsCompleted = false,
                IsCanBeCompleted = true,
                UnlockedDateTime = DateTimeOffset.UtcNow,
                ContainsTasks = new List<string> { "child" },
                BlockedByTasks = new List<string> { "blocker" }
            };
            var child = new TaskItem
            {
                Id = "child",
                IsCompleted = false,
                IsCanBeCompleted = true,
                UnlockedDateTime = DateTimeOffset.UtcNow,
                ParentTasks = new List<string> { "parent" }
            };

            await storage.Save(blocker);
            await storage.Save(parent);
            await storage.Save(child);

            await manager.CalculateAndUpdateAvailability(parent);

            blocker.IsCompleted = true;
            await manager.UpdateTask(blocker);

            var updatedChild = await storage.Load("child");

            await Assert.That(updatedChild).IsNotNull();
            await Assert.That(updatedChild.IsCanBeCompleted).IsTrue();
            await Assert.That(updatedChild.UnlockedDateTime).IsNotNull();
        }

        [Test]
        public async Task MultiParentTask_ShouldBecomeUnavailable_WhenAnyAncestorHasIncompleteBlocker()
        {
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);

            var blocker = new TaskItem
            {
                Id = "blocker",
                IsCompleted = false,
                BlocksTasks = new List<string> { "parent-a" }
            };
            var blockedParent = new TaskItem
            {
                Id = "parent-a",
                IsCompleted = false,
                ContainsTasks = new List<string> { "shared-child" },
                BlockedByTasks = new List<string> { "blocker" }
            };
            var freeParent = new TaskItem
            {
                Id = "parent-b",
                IsCompleted = false,
                ContainsTasks = new List<string> { "shared-child" }
            };
            var sharedChild = new TaskItem
            {
                Id = "shared-child",
                IsCompleted = false,
                IsCanBeCompleted = true,
                UnlockedDateTime = DateTimeOffset.UtcNow,
                ParentTasks = new List<string> { "parent-a", "parent-b" }
            };

            await storage.Save(blocker);
            await storage.Save(blockedParent);
            await storage.Save(freeParent);
            await storage.Save(sharedChild);

            await manager.CalculateAndUpdateAvailability(blockedParent);

            var updatedSharedChild = await storage.Load("shared-child");

            await Assert.That(updatedSharedChild).IsNotNull();
            await Assert.That(updatedSharedChild.IsCanBeCompleted).IsFalse();
            await Assert.That(updatedSharedChild.UnlockedDateTime).IsNull();
        }

        [Test]
        public async Task DeleteTask_WithoutStorageDelete_ShouldUnblockChild_WhenStorageLoadsDetachedInstances()
        {
            var storage = new DetachedLoadStorage();
            var manager = new TaskTreeManager(storage);

            var blocker = new TaskItem
            {
                Id = "blocker",
                IsCompleted = false,
                BlocksTasks = new List<string> { "parent" }
            };
            var parent = new TaskItem
            {
                Id = "parent",
                IsCompleted = false,
                ContainsTasks = new List<string> { "child" },
                BlockedByTasks = new List<string> { "blocker" }
            };
            var child = new TaskItem
            {
                Id = "child",
                IsCompleted = false,
                IsCanBeCompleted = true,
                UnlockedDateTime = DateTimeOffset.UtcNow,
                ParentTasks = new List<string> { "parent" }
            };

            await storage.Save(blocker);
            await storage.Save(parent);
            await storage.Save(child);

            await manager.CalculateAndUpdateAvailability(parent);
            var blockedChild = await storage.Load("child");

            await Assert.That(blockedChild).IsNotNull();
            await Assert.That(blockedChild.IsCanBeCompleted).IsFalse();

            await manager.DeleteTask(parent, deleteInStorage: false);

            var updatedChild = await storage.Load("child");

            await Assert.That(updatedChild).IsNotNull();
            await Assert.That(updatedChild.ParentTasks).IsEmpty();
            await Assert.That(updatedChild.IsCanBeCompleted).IsTrue();
            await Assert.That(updatedChild.UnlockedDateTime).IsNotNull();
        }

        [Test]
        public async Task DeleteTask_ShouldCascadeDeleteContainedTasks_WhenDeletingFromStorage()
        {
            var storage = new DetachedLoadStorage();
            var manager = new TaskTreeManager(storage);

            var parent = new TaskItem
            {
                Id = "parent",
                IsCompleted = false,
                ContainsTasks = new List<string> { "child" }
            };
            var externalParent = new TaskItem
            {
                Id = "external-parent",
                IsCompleted = false,
                ContainsTasks = new List<string> { "child" }
            };
            var child = new TaskItem
            {
                Id = "child",
                IsCompleted = false,
                ParentTasks = new List<string> { "parent", "external-parent" },
                ContainsTasks = new List<string> { "grandchild" }
            };
            var grandchild = new TaskItem
            {
                Id = "grandchild",
                IsCompleted = false,
                ParentTasks = new List<string> { "child" }
            };

            await storage.Save(parent);
            await storage.Save(externalParent);
            await storage.Save(child);
            await storage.Save(grandchild);

            await manager.DeleteTask(parent);

            var deletedParent = await storage.Load("parent");
            var deletedChild = await storage.Load("child");
            var deletedGrandchild = await storage.Load("grandchild");
            var updatedExternalParent = await storage.Load("external-parent");

            await Assert.That(deletedParent).IsNull();
            await Assert.That(deletedChild).IsNull();
            await Assert.That(deletedGrandchild).IsNull();
            await Assert.That(updatedExternalParent).IsNotNull();
            await Assert.That(updatedExternalParent.ContainsTasks).DoesNotContain("child");
        }

        [Test]
        public async Task DeleteTask_ShouldRemoveContainedTaskById_WhenContainedTaskLoadFails()
        {
            var storage = new DetachedLoadStorage();
            var manager = new TaskTreeManager(storage);

            var parent = new TaskItem
            {
                Id = "parent",
                IsCompleted = false,
                ContainsTasks = new List<string> { "child" }
            };
            var child = new TaskItem
            {
                Id = "child",
                IsCompleted = false,
                ParentTasks = new List<string> { "parent" }
            };

            await storage.Save(parent);
            await storage.Save(child);
            storage.FailLoadFor("child");

            await manager.DeleteTask(parent);

            await Assert.That(storage.ContainsStoredTask("parent")).IsFalse();
            await Assert.That(storage.ContainsStoredTask("child")).IsFalse();
        }

        [Test]
        public async Task AddNewParentToTask_WithSameTask_ShouldNotCreateSelfParentRelation()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);
            var task = new TaskItem
            {
                Id = "self-parent",
                IsCompleted = false,
                ContainsTasks = new List<string>(),
                ParentTasks = new List<string>()
            };
            await storage.Save(task);

            // Act
            var results = await manager.AddNewParentToTask(task, task);
            var stored = await storage.Load(task.Id);

            // Assert
            await Assert.That(stored).IsNotNull();
            await Assert.That(stored.ContainsTasks).IsEmpty();
            await Assert.That(stored.ParentTasks).IsEmpty();
            await Assert.That(stored.ContainsTasks).DoesNotContain(stored.Id);
            await Assert.That(stored.ParentTasks).DoesNotContain(stored.Id);
        }

        [Test]
        public async Task BlockTask_WithSameTask_ShouldNotCreateSelfBlockingRelation()
        {
            // Arrange
            var storage = new InMemoryStorage();
            var manager = new TaskTreeManager(storage);
            var task = new TaskItem
            {
                Id = "self-block",
                IsCompleted = false,
                BlocksTasks = new List<string>(),
                BlockedByTasks = new List<string>()
            };
            await storage.Save(task);

            // Act
            var results = await manager.BlockTask(task, task);
            var stored = await storage.Load(task.Id);

            // Assert
            await Assert.That(stored).IsNotNull();
            await Assert.That(stored.BlocksTasks).IsEmpty();
            await Assert.That(stored.BlockedByTasks).IsEmpty();
            await Assert.That(stored.BlocksTasks).DoesNotContain(stored.Id);
            await Assert.That(stored.BlockedByTasks).DoesNotContain(stored.Id);
        }

        private sealed class DetachedLoadStorage : IStorage
        {
            private readonly Dictionary<string, TaskItem> _tasks = new(StringComparer.Ordinal);
            private readonly HashSet<string> _failedLoadTaskIds = new(StringComparer.Ordinal);

            public event EventHandler<TaskStorageUpdateEventArgs> Updating
            {
                add { }
                remove { }
            }

            public event Action<Exception?>? OnConnectionError;

            public Task<TaskItem> Save(TaskItem item)
            {
                var clone = CloneTask(item);
                clone.Id ??= Guid.NewGuid().ToString();
                item.Id = clone.Id;
                _tasks[clone.Id] = clone;
                return Task.FromResult(CloneTask(clone));
            }

            public Task<bool> Remove(string itemId)
            {
                _tasks.Remove(itemId);
                return Task.FromResult(true);
            }

            public Task<TaskItem?> Load(string itemId)
            {
                if (_failedLoadTaskIds.Contains(itemId))
                {
                    return Task.FromResult<TaskItem?>(null);
                }

                return Task.FromResult(_tasks.TryGetValue(itemId, out var task) ? CloneTask(task) : null);
            }

            public void FailLoadFor(string taskId)
            {
                _failedLoadTaskIds.Add(taskId);
            }

            public bool ContainsStoredTask(string taskId)
            {
                return _tasks.ContainsKey(taskId);
            }

            public async IAsyncEnumerable<TaskItem> GetAll()
            {
                foreach (var task in _tasks.Values)
                {
                    yield return CloneTask(task);
                }
            }

            public async Task BulkInsert(IEnumerable<TaskItem> taskItems)
            {
                foreach (var taskItem in taskItems)
                {
                    await Save(taskItem);
                }
            }

            public Task<bool> Connect()
            {
                return Task.FromResult(true);
            }

            public Task Disconnect()
            {
                return Task.CompletedTask;
            }

            private static TaskItem CloneTask(TaskItem task)
            {
                return new TaskItem
                {
                    Id = task.Id,
                    UserId = task.UserId,
                    Title = task.Title,
                    Description = task.Description,
                    IsCompleted = task.IsCompleted,
                    IsCanBeCompleted = task.IsCanBeCompleted,
                    CreatedDateTime = task.CreatedDateTime,
                    UpdatedDateTime = task.UpdatedDateTime,
                    UnlockedDateTime = task.UnlockedDateTime,
                    CompletedDateTime = task.CompletedDateTime,
                    ArchiveDateTime = task.ArchiveDateTime,
                    PlannedBeginDateTime = task.PlannedBeginDateTime,
                    PlannedEndDateTime = task.PlannedEndDateTime,
                    PlannedDuration = task.PlannedDuration,
                    ContainsTasks = task.ContainsTasks?.ToList() ?? new List<string>(),
                    ParentTasks = task.ParentTasks?.ToList() ?? new List<string>(),
                    BlocksTasks = task.BlocksTasks?.ToList() ?? new List<string>(),
                    BlockedByTasks = task.BlockedByTasks?.ToList() ?? new List<string>(),
                    Repeater = task.Repeater,
                    Importance = task.Importance,
                    Wanted = task.Wanted,
                    Version = task.Version
                };
            }
        }
    }
}
