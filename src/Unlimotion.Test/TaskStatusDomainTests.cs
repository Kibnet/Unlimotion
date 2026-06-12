using System;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public class TaskStatusDomainTests
{
    [Test]
    public async Task EnsureStatusHistory_MissingHistory_AddsCurrentStatusAtCreatedTimeAndNormalizesAuthor()
    {
        var createdAt = new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero);
        var task = new TaskItem
        {
            Status = DomainTaskStatus.Prepared,
            CreatedDateTime = createdAt
        };

        task.EnsureStatusHistory("  owner  ");

        await Assert.That(task.StatusHistory).HasSingleItem();
        await Assert.That(task.StatusHistory[0].Status).IsEqualTo(DomainTaskStatus.Prepared);
        await Assert.That(task.StatusHistory[0].ChangedAt).IsEqualTo(createdAt);
        await Assert.That(task.StatusHistory[0].Author).IsEqualTo("owner");
        await Assert.That(task.CompletionCriteria).IsNotNull();
    }

    [Test]
    public async Task SetStatus_SameAsLatestStatus_DoesNotAddDuplicateHistoryEntry()
    {
        var createdAt = new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero);
        var preparedAt = createdAt.AddHours(1);
        var task = new TaskItem
        {
            Status = DomainTaskStatus.NotReady,
            CreatedDateTime = createdAt
        };

        task.EnsureStatusHistory("owner");
        task.SetStatus(DomainTaskStatus.Prepared, preparedAt, "  delegate  ");
        task.SetStatus(DomainTaskStatus.Prepared, preparedAt.AddHours(1), "ignored");

        await Assert.That(task.Status).IsEqualTo(DomainTaskStatus.Prepared);
        await Assert.That(task.StatusHistory.Count).IsEqualTo(2);
        await Assert.That(task.StatusHistory.Select(entry => entry.Status))
            .IsEquivalentTo([DomainTaskStatus.NotReady, DomainTaskStatus.Prepared]);
        await Assert.That(task.StatusHistory.Last().ChangedAt).IsEqualTo(preparedAt);
        await Assert.That(task.StatusHistory.Last().Author).IsEqualTo("delegate");
        await Assert.That(task.PreparedDateTime).IsEqualTo(preparedAt);
        await Assert.That(task.StartedDateTime).IsNull();
    }

    [Test]
    public async Task GetRestoreStatusAfterArchive_ReturnsLastNonArchivedStatusByTimestamp()
    {
        var task = new TaskItem
        {
            Status = DomainTaskStatus.Archived,
            StatusHistory =
            [
                new() { Status = DomainTaskStatus.Prepared, ChangedAt = new DateTimeOffset(2026, 2, 1, 11, 0, 0, TimeSpan.Zero) },
                new() { Status = DomainTaskStatus.NotReady, ChangedAt = new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero) },
                new() { Status = DomainTaskStatus.Archived, ChangedAt = new DateTimeOffset(2026, 2, 1, 13, 0, 0, TimeSpan.Zero) },
                new() { Status = DomainTaskStatus.InProgress, ChangedAt = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero) }
            ]
        };

        await Assert.That(task.GetRestoreStatusAfterArchive()).IsEqualTo(DomainTaskStatus.InProgress);
    }

    [Test]
    public async Task LegacyComputedProperties_MapToStatusAndHistoryDates()
    {
        var completedAt = new DateTimeOffset(2026, 2, 2, 10, 0, 0, TimeSpan.Zero);
        var archivedAt = new DateTimeOffset(2026, 2, 3, 10, 0, 0, TimeSpan.Zero);
        var task = new TaskItem();

        task.IsCompleted = true;
        task.CompletedDateTime = completedAt;

        await Assert.That(task.Status).IsEqualTo(DomainTaskStatus.Completed);
        await Assert.That(task.IsCompleted).IsTrue();
        await Assert.That(task.CompletedDateTime).IsEqualTo(completedAt);

        task.IsCompleted = null;
        task.ArchiveDateTime = archivedAt;

        await Assert.That(task.Status).IsEqualTo(DomainTaskStatus.Archived);
        await Assert.That(task.IsCompleted).IsNull();
        await Assert.That(task.CompletedDateTime).IsNull();
        await Assert.That(task.ArchiveDateTime).IsEqualTo(archivedAt);
    }
}
