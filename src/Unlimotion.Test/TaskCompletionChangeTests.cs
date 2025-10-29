using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Telerik.JustMock;
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
            var storageMock = Mock.Create<IStorage>();
            var taskTreeManager = new TaskTreeManager(storageMock);
            
            var task = new TaskItem
            {
                Id = "test-task",
                IsCompleted = true,
                CompletedDateTime = null
            };
            
            // Setup storage mock to return the task when saved
            Mock.Arrange(() => storageMock.Save(Arg.IsAny<TaskItem>()))
                .DoInstead<TaskItem>(t => {
                    // Update the task with the saved values
                    task.CompletedDateTime = t.CompletedDateTime;
                    task.ArchiveDateTime = t.ArchiveDateTime;
                })
                .Returns(Task.FromResult(true));

            // Act
            var result = await taskTreeManager.HandleTaskCompletionChange(task, false);

            // Assert
            Assert.NotNull(task.CompletedDateTime);
            Assert.Null(task.ArchiveDateTime);
            Assert.Contains(task, result);
        }

        [Fact]
        public async Task HandleTaskCompletionChange_UncompletedTask_ClearsDates()
        {
            // Arrange
            var storageMock = Mock.Create<IStorage>();
            var taskTreeManager = new TaskTreeManager(storageMock);
            
            var task = new TaskItem
            {
                Id = "test-task",
                IsCompleted = false,
                CompletedDateTime = DateTimeOffset.UtcNow,
                ArchiveDateTime = DateTimeOffset.UtcNow
            };
            
            // Setup storage mock to return the task when saved
            Mock.Arrange(() => storageMock.Save(Arg.IsAny<TaskItem>()))
                .DoInstead<TaskItem>(t => {
                    // Update the task with the saved values
                    task.CompletedDateTime = t.CompletedDateTime;
                    task.ArchiveDateTime = t.ArchiveDateTime;
                })
                .Returns(Task.FromResult(true));

            // Act
            var result = await taskTreeManager.HandleTaskCompletionChange(task, true);

            // Assert
            Assert.Null(task.CompletedDateTime);
            Assert.Null(task.ArchiveDateTime);
            Assert.Contains(task, result);
        }

        [Fact]
        public async Task HandleTaskCompletionChange_ArchivedTask_SetsArchiveDateTime()
        {
            // Arrange
            var storageMock = Mock.Create<IStorage>();
            var taskTreeManager = new TaskTreeManager(storageMock);
            
            var task = new TaskItem
            {
                Id = "test-task",
                IsCompleted = null,
                ArchiveDateTime = null
            };
            
            // Setup storage mock to return the task when saved
            Mock.Arrange(() => storageMock.Save(Arg.IsAny<TaskItem>()))
                .DoInstead<TaskItem>(t => {
                    // Update the task with the saved values
                    task.CompletedDateTime = t.CompletedDateTime;
                    task.ArchiveDateTime = t.ArchiveDateTime;
                })
                .Returns(Task.FromResult(true));

            // Act
            var result = await taskTreeManager.HandleTaskCompletionChange(task, false);

            // Assert
            Assert.NotNull(task.ArchiveDateTime);
            Assert.Null(task.CompletedDateTime);
            Assert.Contains(task, result);
        }

        [Fact]
        public async Task HandleTaskCompletionChange_CompletedTaskWithRepeater_CreatesClone()
        {
            // Arrange
            var storageMock = Mock.Create<IStorage>();
            var taskTreeManager = new TaskTreeManager(storageMock);
            
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
            
            var clonedTask = new TaskItem();
            
            // Setup storage mock to capture the cloned task
            Mock.Arrange(() => storageMock.Save(Arg.IsAny<TaskItem>()))
                .DoInstead<TaskItem>(t => {
                    if (t.Id != task.Id)
                    {
                        // This is the cloned task
                        clonedTask = t;
                        clonedTask.Id = "cloned-task";
                    }
                    else
                    {
                        // This is the original task
                        task.CompletedDateTime = t.CompletedDateTime;
                        task.ArchiveDateTime = t.ArchiveDateTime;
                    }
                })
                .Returns(Task.FromResult(true));

            // Act
            var result = await taskTreeManager.HandleTaskCompletionChange(task, false);

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
    }
}