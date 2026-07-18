using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Unlimotion.Domain;
using Unlimotion.Server.ServiceModel;
using Unlimotion.Server.ServiceModel.Molds.Tasks;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public sealed class ServerStorageStatusCommandTests
{
    [Test]
    public async Task ReadGraph_UsesOneGetAllRequestAndMapsTasks()
    {
        var requestCount = 0;
        var storage = CreateStorage(request =>
        {
            requestCount++;
            return Task.FromResult(new TaskItemPage
            {
                Tasks =
                [
                    new TaskItemMold
                    {
                        Id = "server-task",
                        Title = "Server task",
                        Status = DomainTaskStatus.Prepared,
                        IsCanBeCompleted = true
                    }
                ]
            });
        });

        var graph = await storage.ReadGraphAsync();

        using (Assert.Multiple())
        {
            await Assert.That(requestCount).IsEqualTo(1);
            await Assert.That(graph.Tasks.Count).IsEqualTo(1);
            await Assert.That(graph.Tasks[0].Id).IsEqualTo("server-task");
            await Assert.That(graph.Tasks[0].Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(graph.LoadErrors).IsEmpty();
            await Assert.That(graph.DuplicateIdIssues).IsEmpty();
        }
    }

    [Test]
    public async Task ReadGraph_PropagatesGetAllFailure()
    {
        var requestCount = 0;
        var expected = new IOException("server graph unavailable");
        var storage = CreateStorage(request =>
        {
            requestCount++;
            return Task.FromException<TaskItemPage>(expected);
        });

        await Assert.That(async () => await storage.ReadGraphAsync())
            .Throws<IOException>();
        await Assert.That(requestCount).IsEqualTo(1);
    }

    [Test]
    public async Task SameStatusCommand_UsesOneGetAllAndDoesNotRequireWriteLock()
    {
        var requestCount = 0;
        var storage = CreateStorage(request =>
        {
            requestCount++;
            return Task.FromResult(new TaskItemPage
            {
                Tasks =
                [
                    new TaskItemMold
                    {
                        Id = "server-task",
                        Title = "Server task",
                        Status = DomainTaskStatus.Prepared,
                        IsCanBeCompleted = true
                    }
                ]
            });
        });

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync("server-task", DomainTaskStatus.Prepared, "tester");

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.ChangedTasks).IsEmpty();
            await Assert.That(requestCount).IsEqualTo(1);
            await Assert.That(storage is ITaskGraphDiagnosticStorage).IsTrue();
            await Assert.That(storage is ITaskGraphWriteLock).IsFalse();
        }
    }

    [Test]
    public async Task AllowedCommand_VerifiesPersistedStatusWithinThreeGetAllRequests()
    {
        var persisted = CreateTask("server-task", DomainTaskStatus.Prepared);
        var requestCount = 0;
        var saveCount = 0;
        var storage = CreateStorage(
            request =>
            {
                requestCount++;
                return Task.FromResult(CreatePage(persisted));
            },
            item =>
            {
                saveCount++;
                persisted = CloneTask(item);
                return Task.FromResult(item);
            },
            id => Task.FromResult<TaskItem?>(
                string.Equals(id, persisted.Id, StringComparison.Ordinal)
                    ? CloneTask(persisted)
                    : null));

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync(persisted.Id, DomainTaskStatus.InProgress, "tester");

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.AuthoritativeTask?.Status)
                .IsEqualTo(DomainTaskStatus.InProgress);
            await Assert.That(persisted.Status).IsEqualTo(DomainTaskStatus.InProgress);
            await Assert.That(saveCount > 0).IsTrue();
            await Assert.That(requestCount >= 2 && requestCount <= 3).IsTrue();
        }
    }

    [Test]
    public async Task UnarchiveCommand_ResolvesAuthoritativeHistoryAndVerifiesWithinThreeGetAllRequests()
    {
        var persisted = CreateTask("server-unarchive", DomainTaskStatus.Archived);
        persisted.StatusHistory =
        [
            new TaskStatusHistoryEntry
            {
                Status = DomainTaskStatus.Completed,
                ChangedAt = DateTimeOffset.UtcNow.AddHours(-2),
                Author = "server"
            },
            new TaskStatusHistoryEntry
            {
                Status = DomainTaskStatus.Archived,
                ChangedAt = DateTimeOffset.UtcNow.AddHours(-1),
                Author = "server"
            }
        ];
        var requestCount = 0;
        var saveCount = 0;
        var storage = CreateStorage(
            request =>
            {
                requestCount++;
                return Task.FromResult(CreatePage(persisted));
            },
            item =>
            {
                saveCount++;
                persisted = CloneTask(item);
                return Task.FromResult(item);
            },
            id => Task.FromResult<TaskItem?>(
                string.Equals(id, persisted.Id, StringComparison.Ordinal)
                    ? CloneTask(persisted)
                    : null));

        var result = await new TaskGraphCommandService(storage)
            .TryUnarchiveAsync(persisted.Id, "tester");

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.AuthoritativeTask?.Status).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(persisted.Status).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(persisted.StatusHistory[^1].Author).IsEqualTo("tester");
            await Assert.That(saveCount > 0).IsTrue();
            await Assert.That(requestCount >= 2 && requestCount <= 3).IsTrue();
        }
    }

    [Test]
    public async Task UnarchiveCommand_AuthoritativeNonArchivedStatusFailsPreconditionWithOneGetAll()
    {
        var persisted = CreateTask("server-already-unarchived", DomainTaskStatus.Prepared);
        var requestCount = 0;
        var saveCount = 0;
        var storage = CreateStorage(
            request =>
            {
                requestCount++;
                return Task.FromResult(CreatePage(persisted));
            },
            item =>
            {
                saveCount++;
                return Task.FromResult(item);
            });

        var result = await new TaskGraphCommandService(storage)
            .TryUnarchiveAsync(persisted.Id, "tester");

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.DeniedReason?.Kind)
                .IsEqualTo(TaskOperationDeniedKind.StatusPreconditionFailed);
            await Assert.That(result.AuthoritativeTask?.Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(requestCount).IsEqualTo(1);
            await Assert.That(saveCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task UnarchiveCommand_PostVerifyMismatchReturnsOutcomeUnknownWithinThreeGetAllRequests()
    {
        var persisted = CreateTask("server-unarchive-mismatch", DomainTaskStatus.Archived);
        persisted.StatusHistory =
        [
            new TaskStatusHistoryEntry
            {
                Status = DomainTaskStatus.InProgress,
                ChangedAt = DateTimeOffset.UtcNow.AddHours(-2),
                Author = "server"
            },
            new TaskStatusHistoryEntry
            {
                Status = DomainTaskStatus.Archived,
                ChangedAt = DateTimeOffset.UtcNow.AddHours(-1),
                Author = "server"
            }
        ];
        var requestCount = 0;
        var saveCount = 0;
        var storage = CreateStorage(
            request =>
            {
                requestCount++;
                return Task.FromResult(CreatePage(persisted));
            },
            item =>
            {
                saveCount++;
                return Task.FromResult(item);
            },
            id => Task.FromResult<TaskItem?>(CloneTask(persisted)));

        var result = await new TaskGraphCommandService(storage)
            .TryUnarchiveAsync(persisted.Id, "tester");

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.DeniedReason?.Kind)
                .IsEqualTo(TaskOperationDeniedKind.OutcomeUnknown);
            await Assert.That(persisted.Status).IsEqualTo(DomainTaskStatus.Archived);
            await Assert.That(saveCount > 0).IsTrue();
            await Assert.That(requestCount >= 2 && requestCount <= 3).IsTrue();
        }
    }

    [Test]
    public async Task PostVerifyMismatch_ReturnsOutcomeUnknownWithinThreeGetAllRequests()
    {
        var persisted = CreateTask("server-task", DomainTaskStatus.Prepared);
        var requestCount = 0;
        var saveCount = 0;
        var storage = CreateStorage(
            request =>
            {
                requestCount++;
                return Task.FromResult(CreatePage(persisted));
            },
            item =>
            {
                saveCount++;
                return Task.FromResult(item);
            },
            id => Task.FromResult<TaskItem?>(
                string.Equals(id, persisted.Id, StringComparison.Ordinal)
                    ? CloneTask(persisted)
                    : null));

        var result = await new TaskGraphCommandService(storage)
            .TrySetStatusAsync(persisted.Id, DomainTaskStatus.InProgress, "tester");

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.DeniedReason?.Kind)
                .IsEqualTo(TaskOperationDeniedKind.OutcomeUnknown);
            await Assert.That(result.AuthoritativeTask).IsNull();
            await Assert.That(persisted.Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(saveCount > 0).IsTrue();
            await Assert.That(requestCount >= 2 && requestCount <= 3).IsTrue();
        }
    }

    [Test]
    public async Task ReadGraph_ReportsNullTaskElementsAsWriteUnsafe()
    {
        var requestCount = 0;
        var storage = CreateStorage(request =>
        {
            requestCount++;
            var page = new TaskItemPage();
            page.Tasks.Add(null!);
            return Task.FromResult(page);
        });

        var graph = await storage.ReadGraphAsync();
        var validation = TaskGraphValidationReport.From(graph);

        using (Assert.Multiple())
        {
            await Assert.That(requestCount).IsEqualTo(1);
            await Assert.That(graph.Tasks).IsEmpty();
            await Assert.That(graph.LoadErrors.Count).IsEqualTo(1);
            await Assert.That(graph.LoadErrors[0].File)
                .IsEqualTo("<server:GetAllTasks:0>");
            await Assert.That(validation.IsWriteSafe).IsFalse();
        }
    }

    [Test]
    public async Task ReadGraph_ReportsDuplicateServerIdsAsWriteUnsafe()
    {
        var storage = CreateStorage(request => Task.FromResult(new TaskItemPage
        {
            Tasks =
            [
                new TaskItemMold { Id = "duplicate", Title = "First" },
                new TaskItemMold { Id = "duplicate", Title = "Second" }
            ]
        }));

        var graph = await storage.ReadGraphAsync();
        var validation = TaskGraphValidationReport.From(graph);

        using (Assert.Multiple())
        {
            await Assert.That(graph.DuplicateIdIssues.Count).IsEqualTo(1);
            await Assert.That(graph.DuplicateIdIssues[0].TaskId).IsEqualTo("duplicate");
            await Assert.That(validation.IsWriteSafe).IsFalse();
        }
    }

    private static ServerStorage CreateStorage(
        Func<GetAllTasks, Task<TaskItemPage>> fetchAllTasks,
        Func<TaskItem, Task<TaskItem>>? saveTask = null,
        Func<string, Task<TaskItem?>>? loadTask = null) =>
        new(
            "https://status-contract.invalid",
            new ConfigurationBuilder().Build(),
            fetchAllTasks,
            saveTask,
            loadTask);

    private static TaskItem CreateTask(string id, DomainTaskStatus status) => new()
    {
        Id = id,
        UserId = "server-user",
        Title = id,
        Description = string.Empty,
        Status = status,
        IsCanBeCompleted = true,
        CreatedDateTime = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero),
        StatusHistory =
        [
            new TaskStatusHistoryEntry
            {
                Status = status,
                ChangedAt = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero),
                Author = "seed"
            }
        ]
    };

    private static TaskItemPage CreatePage(TaskItem task) => new()
    {
        Tasks =
        [
            new TaskItemMold
            {
                Id = task.Id,
                UserId = task.UserId,
                Title = task.Title,
                Description = task.Description,
                Status = task.Status,
                StatusHistory = task.StatusHistory
                    .Select(entry => new TaskStatusHistoryEntry
                    {
                        Status = entry.Status,
                        ChangedAt = entry.ChangedAt,
                        Author = entry.Author
                    })
                    .ToList(),
                CompletionCriteria = task.CompletionCriteria
                    .Select(criterion => new TaskCompletionCriterion
                    {
                        Id = criterion.Id,
                        Text = criterion.Text,
                        IsSatisfied = criterion.IsSatisfied
                    })
                    .ToList(),
                IsCanBeCompleted = task.IsCanBeCompleted,
                CreatedDateTime = task.CreatedDateTime,
                UpdatedDateTime = task.UpdatedDateTime,
                PlannedBeginDateTime = task.PlannedBeginDateTime,
                PlannedEndDateTime = task.PlannedEndDateTime,
                PlannedDuration = task.PlannedDuration,
                ContainsTasks = task.ContainsTasks.ToList(),
                ParentTasks = task.ParentTasks.ToList(),
                BlocksTasks = task.BlocksTasks.ToList(),
                BlockedByTasks = task.BlockedByTasks.ToList(),
                Importance = task.Importance,
                Wanted = task.Wanted,
                Version = task.Version
            }
        ]
    };

    private static TaskItem CloneTask(TaskItem task) => task with
    {
        StatusHistory = task.StatusHistory
            .Select(entry => new TaskStatusHistoryEntry
            {
                Status = entry.Status,
                ChangedAt = entry.ChangedAt,
                Author = entry.Author
            })
            .ToList(),
        CompletionCriteria = task.CompletionCriteria
            .Select(criterion => new TaskCompletionCriterion
            {
                Id = criterion.Id,
                Text = criterion.Text,
                IsSatisfied = criterion.IsSatisfied
            })
            .ToList(),
        ContainsTasks = task.ContainsTasks.ToList(),
        ParentTasks = task.ParentTasks.ToList(),
        BlocksTasks = task.BlocksTasks.ToList(),
        BlockedByTasks = task.BlockedByTasks.ToList()
    };
}
