using System;
using System.Collections.Generic;
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
    public async Task GetRestoreStatusAfterArchive_NormalizesLastValidStatusByTimestamp()
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

        await Assert.That(task.GetRestoreStatusAfterArchive(new DateTimeOffset(2026, 2, 1, 14, 0, 0, TimeSpan.Zero)))
            .IsEqualTo(DomainTaskStatus.Prepared);
    }

    [Test]
    [Arguments(DomainTaskStatus.NotReady, DomainTaskStatus.NotReady)]
    [Arguments(DomainTaskStatus.Prepared, DomainTaskStatus.Prepared)]
    [Arguments(DomainTaskStatus.InProgress, DomainTaskStatus.Prepared)]
    [Arguments(DomainTaskStatus.Completed, DomainTaskStatus.NotReady)]
    public async Task GetRestoreStatusAfterArchive_NormalizesDefinedHistoryMatrix(
        DomainTaskStatus previousStatus,
        DomainTaskStatus expectedStatus)
    {
        var now = new DateTimeOffset(2026, 7, 18, 10, 0, 0, TimeSpan.Zero);
        var task = new TaskItem
        {
            Status = DomainTaskStatus.Archived,
            StatusHistory =
            [
                new() { Status = previousStatus, ChangedAt = now.AddHours(-1) },
                new() { Status = DomainTaskStatus.Archived, ChangedAt = now }
            ]
        };

        await Assert.That(task.GetRestoreStatusAfterArchive(now)).IsEqualTo(expectedStatus);
    }

    [Test]
    public async Task GetRestoreStatusAfterArchive_IgnoresNullUndefinedArchivedAndFarFutureEntries()
    {
        var now = new DateTimeOffset(2026, 7, 18, 10, 0, 0, TimeSpan.Zero);
        var valid = new TaskStatusHistoryEntry
        {
            Status = DomainTaskStatus.Prepared,
            ChangedAt = now.AddHours(-1),
            Author = "owner"
        };
        var history = new List<TaskStatusHistoryEntry>
        {
            valid,
            null!,
            new() { Status = (DomainTaskStatus)int.MaxValue, ChangedAt = now.AddMinutes(1) },
            new() { Status = DomainTaskStatus.Archived, ChangedAt = now.AddMinutes(2) },
            new() { Status = DomainTaskStatus.InProgress, ChangedAt = now.AddMinutes(5).AddTicks(1) }
        };
        var task = new TaskItem { Status = DomainTaskStatus.Archived, StatusHistory = history };

        var restored = task.GetRestoreStatusAfterArchive(now);

        using (Assert.Multiple())
        {
            await Assert.That(restored).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(task.StatusHistory.Count).IsEqualTo(5);
            await Assert.That(task.StatusHistory[0]).IsSameReferenceAs(valid);
            await Assert.That(task.StatusHistory[1]).IsNull();
            await Assert.That(task.StatusHistory[2].Status).IsEqualTo((DomainTaskStatus)int.MaxValue);
            await Assert.That(task.StatusHistory[3].Status).IsEqualTo(DomainTaskStatus.Archived);
            await Assert.That(task.StatusHistory[4].Status).IsEqualTo(DomainTaskStatus.InProgress);
        }
    }

    [Test]
    public async Task GetRestoreStatusAfterArchive_AcceptsFiveMinuteClockSkewBoundary()
    {
        var now = new DateTimeOffset(2026, 7, 18, 10, 0, 0, TimeSpan.Zero);
        var task = new TaskItem
        {
            Status = DomainTaskStatus.Archived,
            StatusHistory =
            [
                new() { Status = DomainTaskStatus.NotReady, ChangedAt = now },
                new() { Status = DomainTaskStatus.InProgress, ChangedAt = now.AddMinutes(5) }
            ]
        };

        await Assert.That(task.GetRestoreStatusAfterArchive(now)).IsEqualTo(DomainTaskStatus.Prepared);
    }

    [Test]
    public async Task RestoreHistoryEntryValidity_UsesDefinedNonArchivedStatusAndClockTolerance()
    {
        var now = new DateTimeOffset(2026, 7, 18, 10, 0, 0, TimeSpan.Zero);

        using (Assert.Multiple())
        {
            await Assert.That(TaskStatusTransitionPolicy.IsValidRestoreStatusHistoryEntry(null, now)).IsFalse();
            await Assert.That(TaskStatusTransitionPolicy.IsValidRestoreStatusHistoryEntry(
                    new TaskStatusHistoryEntry { Status = (DomainTaskStatus)int.MaxValue, ChangedAt = now },
                    now))
                .IsFalse();
            await Assert.That(TaskStatusTransitionPolicy.IsValidRestoreStatusHistoryEntry(
                    new TaskStatusHistoryEntry { Status = DomainTaskStatus.Archived, ChangedAt = now },
                    now))
                .IsFalse();
            await Assert.That(TaskStatusTransitionPolicy.IsValidRestoreStatusHistoryEntry(
                    new TaskStatusHistoryEntry { Status = DomainTaskStatus.Prepared, ChangedAt = now.AddMinutes(5) },
                    now))
                .IsTrue();
            await Assert.That(TaskStatusTransitionPolicy.IsValidRestoreStatusHistoryEntry(
                    new TaskStatusHistoryEntry { Status = DomainTaskStatus.Prepared, ChangedAt = now.AddMinutes(5).AddTicks(1) },
                    now))
                .IsFalse();
        }
    }

    [Test]
    public async Task GetRestoreStatusAfterArchive_EqualTimestamp_UsesHigherOriginalIndex()
    {
        var now = new DateTimeOffset(2026, 7, 18, 10, 0, 0, TimeSpan.Zero);
        var task = new TaskItem
        {
            Status = DomainTaskStatus.Archived,
            StatusHistory =
            [
                new() { Status = DomainTaskStatus.Prepared, ChangedAt = now },
                null!,
                new() { Status = DomainTaskStatus.Completed, ChangedAt = now }
            ]
        };

        await Assert.That(task.GetRestoreStatusAfterArchive(now)).IsEqualTo(DomainTaskStatus.NotReady);
    }

    [Test]
    public async Task GetRestoreStatusAfterArchive_MissingOrOnlyCorruptHistory_ReturnsNotReady()
    {
        var now = new DateTimeOffset(2026, 7, 18, 10, 0, 0, TimeSpan.Zero);
        var missing = new TaskItem { Status = DomainTaskStatus.Archived, StatusHistory = null! };
        var corrupt = new TaskItem
        {
            Status = DomainTaskStatus.Archived,
            StatusHistory =
            [
                null!,
                new() { Status = (DomainTaskStatus)int.MaxValue, ChangedAt = now },
                new() { Status = DomainTaskStatus.Archived, ChangedAt = now }
            ]
        };

        using (Assert.Multiple())
        {
            await Assert.That(missing.GetRestoreStatusAfterArchive(now)).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(corrupt.GetRestoreStatusAfterArchive(now)).IsEqualTo(DomainTaskStatus.NotReady);
        }
    }

    [Test]
    public async Task EnsureStatusHistory_UsesLastPhysicalNonNullEntryForIdempotency()
    {
        var now = new DateTimeOffset(2026, 7, 18, 10, 0, 0, TimeSpan.Zero);
        var task = new TaskItem
        {
            Status = DomainTaskStatus.Prepared,
            UpdatedDateTime = now,
            StatusHistory =
            [
                new() { Status = DomainTaskStatus.NotReady, ChangedAt = now.AddDays(1) },
                null!,
                new() { Status = DomainTaskStatus.Prepared, ChangedAt = now.AddHours(-1) },
                null!
            ]
        };

        task.EnsureStatusHistory();
        task.SetStatus(DomainTaskStatus.Prepared, now.AddHours(1), "owner");

        using (Assert.Multiple())
        {
            await Assert.That(task.StatusHistory.Count).IsEqualTo(4);
            await Assert.That(task.StatusHistory[1]).IsNull();
            await Assert.That(task.StatusHistory[3]).IsNull();
        }
    }

    [Test]
    public async Task HistoryDateHelpers_IgnoreNullSlots()
    {
        var completedAt = new DateTimeOffset(2026, 7, 18, 10, 0, 0, TimeSpan.Zero);
        var task = new TaskItem
        {
            Status = DomainTaskStatus.Completed,
            StatusHistory =
            [
                null!,
                new() { Status = DomainTaskStatus.Prepared, ChangedAt = completedAt.AddHours(-1) },
                null!,
                new() { Status = DomainTaskStatus.Completed, ChangedAt = completedAt }
            ]
        };

        using (Assert.Multiple())
        {
            await Assert.That(task.CompletedDateTime).IsEqualTo(completedAt);
            await Assert.That(task.StatusHistory.LastNonArchivedStatus()).IsEqualTo(DomainTaskStatus.Completed);
        }
    }

    [Test]
    public async Task StatusHistoryTimestampSetter_IgnoresNullSlotsAndPreservesTheirOrder()
    {
        var original = new DateTimeOffset(2026, 7, 18, 10, 0, 0, TimeSpan.Zero);
        var updated = original.AddHours(1);
        var archivedEntry = new TaskStatusHistoryEntry
        {
            Status = DomainTaskStatus.Archived,
            ChangedAt = original,
            Author = " owner "
        };
        var task = new TaskItem
        {
            Status = DomainTaskStatus.Archived,
            StatusHistory = [null!, archivedEntry, null!]
        };

        task.ArchiveDateTime = updated;

        using (Assert.Multiple())
        {
            await Assert.That(task.StatusHistory.Count).IsEqualTo(3);
            await Assert.That(task.StatusHistory[0]).IsNull();
            await Assert.That(task.StatusHistory[1]).IsSameReferenceAs(archivedEntry);
            await Assert.That(task.StatusHistory[1].ChangedAt).IsEqualTo(updated);
            await Assert.That(task.StatusHistory[1].Author).IsEqualTo("owner");
            await Assert.That(task.StatusHistory[2]).IsNull();
        }
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
