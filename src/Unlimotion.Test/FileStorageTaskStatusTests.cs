using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public class FileStorageTaskStatusTests
{
    [Test]
    public async Task ImmediateCriterionSatisfaction_IsFlushedBeforeCompletion()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var storage = new CountingFileStorage(tempDir, new RecordingDatabaseWatcher());
            var source = new TaskItem
            {
                Id = "immediate-criterion-completion",
                Title = "Criterion task",
                Status = DomainTaskStatus.Prepared,
                IsCanBeCompleted = true,
                CompletionCriteria =
                [
                    new TaskCompletionCriterion
                    {
                        Text = "Verify result",
                        IsSatisfied = false
                    }
                ]
            };
            await storage.Save(source);
            using var unified = new UnifiedTaskStorage(new TaskTreeManager(storage));
            await unified.Init();
            var viewModel = unified.Tasks.Lookup(source.Id).Value;
            viewModel.IsInitializedProvider = () => true;

            viewModel.CompletionCriteria.Single().IsSatisfied = true;
            var result = await viewModel.TryTransitionToStatusAsync(
                DomainTaskStatus.Completed,
                "tester");
            var persisted = await storage.Load(source.Id, forced: true);

            using (Assert.Multiple())
            {
                await Assert.That(result.Success).IsTrue();
                await Assert.That(persisted?.Status).IsEqualTo(DomainTaskStatus.Completed);
                await Assert.That(persisted?.CompletionCriteria.Single().IsSatisfied).IsTrue();
            }
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task ImmediateRepeatingCompletion_FlushesEditorFieldsAndCreatesNextOccurrence()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var storage = new CountingFileStorage(tempDir, new RecordingDatabaseWatcher());
            var plannedBegin = DateTime.Now.AddDays(-1);
            var grandchild = new TaskItem
            {
                Id = "immediate-repeating-grandchild",
                Title = "Daily review nested subtask",
                Status = DomainTaskStatus.Completed,
                ParentTasks = ["immediate-repeating-child"],
                PlannedBeginDateTime = plannedBegin.AddHours(3)
            };
            var child = new TaskItem
            {
                Id = "immediate-repeating-child",
                Title = "Daily review subtask",
                Status = DomainTaskStatus.Completed,
                ParentTasks = ["immediate-repeating-completion"],
                ContainsTasks = [grandchild.Id],
                PlannedBeginDateTime = plannedBegin.AddHours(2)
            };
            var source = new TaskItem
            {
                Id = "immediate-repeating-completion",
                Status = DomainTaskStatus.Prepared,
                IsCanBeCompleted = true,
                ContainsTasks = [child.Id]
            };
            await storage.Save(grandchild);
            await storage.Save(child);
            await storage.Save(source);
            using var unified = new UnifiedTaskStorage(new TaskTreeManager(storage));
            await unified.Init();
            var viewModel = unified.Tasks.Lookup(source.Id).Value;
            viewModel.IsInitializedProvider = () => true;

            viewModel.Title = "Daily review";
            viewModel.PlannedBeginDateTime = plannedBegin;
            viewModel.Repeater = new RepeaterPatternViewModel
            {
                Type = RepeaterType.Daily,
                Period = 1
            };

            var result = await viewModel.TryTransitionToStatusAsync(
                DomainTaskStatus.Completed,
                "tester");
            var graph = await storage.ReadGraphAsync();
            var persistedSource = graph.TasksById[source.Id];
            var persistedChild = graph.TasksById[child.Id];
            var persistedGrandchild = graph.TasksById[grandchild.Id];
            var next = graph.Tasks.Single(task => task.Id != source.Id && task.Title == "Daily review");
            var nextChild = graph.Tasks.Single(task => task.Id != child.Id && task.Title == child.Title);
            var nextGrandchild = graph.Tasks.Single(task =>
                task.Id != grandchild.Id && task.Title == grandchild.Title);

            using (Assert.Multiple())
            {
                await Assert.That(result.Success).IsTrue();
                await Assert.That(persistedSource.Title).IsEqualTo("Daily review");
                await Assert.That(persistedSource.PlannedBeginDateTime?.LocalDateTime).IsEqualTo(plannedBegin);
                await Assert.That(persistedSource.Repeater?.Type).IsEqualTo(RepeaterType.Daily);
                await Assert.That(next.Title).IsEqualTo("Daily review");
                await Assert.That(next.Status).IsEqualTo(DomainTaskStatus.Prepared);
                await Assert.That(next.PlannedBeginDateTime?.LocalDateTime).IsEqualTo(plannedBegin.AddDays(1));
                await Assert.That(next.Repeater?.Type).IsEqualTo(RepeaterType.Daily);
                await Assert.That(next.ContainsTasks).IsEquivalentTo([nextChild.Id]);
                await Assert.That(nextChild.ParentTasks).IsEquivalentTo([next.Id]);
                await Assert.That(nextChild.ContainsTasks).IsEquivalentTo([nextGrandchild.Id]);
                await Assert.That(nextGrandchild.ParentTasks).IsEquivalentTo([nextChild.Id]);
                await Assert.That(nextChild.Status).IsEqualTo(DomainTaskStatus.NotReady);
                await Assert.That(nextGrandchild.Status).IsEqualTo(DomainTaskStatus.NotReady);
                await Assert.That(nextChild.PlannedBeginDateTime?.LocalDateTime)
                    .IsEqualTo(persistedChild.PlannedBeginDateTime?.LocalDateTime.AddDays(1));
                await Assert.That(nextGrandchild.PlannedBeginDateTime?.LocalDateTime)
                    .IsEqualTo(persistedGrandchild.PlannedBeginDateTime?.LocalDateTime.AddDays(1));
                await Assert.That(unified.Tasks.Lookup(next.Id).HasValue).IsTrue();
                await Assert.That(unified.Tasks.Lookup(nextChild.Id).HasValue).IsTrue();
                await Assert.That(unified.Tasks.Lookup(nextGrandchild.Id).HasValue).IsTrue();
            }
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task EditDuringFileBackedStatusCommand_SurvivesUnifiedCacheHydration()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var storage = new BlockingCompletedWriteFileStorage(tempDir, new RecordingDatabaseWatcher());
            var source = new TaskItem
            {
                Id = "file-backed-editor-race",
                Title = "Original title",
                Status = DomainTaskStatus.Prepared,
                IsCanBeCompleted = true
            };
            await storage.Save(source);
            using var unified = new UnifiedTaskStorage(new TaskTreeManager(storage));
            await unified.Init();
            var viewModel = unified.Tasks.Lookup(source.Id).Value;
            viewModel.IsInitializedProvider = () => true;
            viewModel.Title = "Before command";
            storage.BlockCompletedWrite = true;

            var transition = viewModel.TryTransitionToStatusAsync(
                DomainTaskStatus.Completed,
                "tester");
            await storage.CompletedWriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.Title = "Edited while command is pending";

            storage.ReleaseCompletedWrite.TrySetResult();
            var result = await transition.WaitAsync(TimeSpan.FromSeconds(5));
            var persisted = await storage.Load(source.Id, forced: true);

            using (Assert.Multiple())
            {
                await Assert.That(result.Success).IsTrue();
                await Assert.That(viewModel.Status).IsEqualTo(DomainTaskStatus.Completed);
                await Assert.That(viewModel.Title).IsEqualTo("Edited while command is pending");
                await Assert.That(persisted?.Status).IsEqualTo(DomainTaskStatus.Completed);
                await Assert.That(persisted?.Title).IsEqualTo("Edited while command is pending");
            }
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task WarmStatusCommand_UsesLoadedGraphWithoutEnumeratingTaskDirectory()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var storage = new CountingFileStorage(tempDir, new RecordingDatabaseWatcher());
            for (var index = 0; index < 32; index++)
            {
                await storage.Save(new TaskItem
                {
                    Id = $"cached-status-{index:D2}",
                    UserId = "owner",
                    Title = $"Cached status {index}",
                    Status = DomainTaskStatus.NotReady,
                    IsCanBeCompleted = true
                });
            }

            using var repository = new UnifiedTaskStorage(new TaskTreeManager(storage));
            await repository.Init();
            var enumerationsAfterWarmUp = storage.DirectoryEnumerationCount;

            var result = await repository.TrySetStatusAsync("cached-status-00", DomainTaskStatus.Prepared, "tester");
            var persisted = await storage.Load("cached-status-00", forced: true);

            using (Assert.Multiple())
            {
                await Assert.That(result.Success).IsTrue();
                await Assert.That(result.AuthoritativeTask?.Status).IsEqualTo(DomainTaskStatus.Prepared);
                await Assert.That(persisted?.Status).IsEqualTo(DomainTaskStatus.Prepared);
                await Assert.That(persisted?.StatusHistory.Last().Author).IsEqualTo("tester");
                await Assert.That(storage.DirectoryEnumerationCount).IsEqualTo(enumerationsAfterWarmUp);
            }
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task WatcherlessStorage_RemainsColdAndReadsExternalEditForNextCommand()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var storage = new CountingFileStorage(tempDir);
            var task = new TaskItem
            {
                Id = "watcherless-cold-task",
                Title = "Before external edit",
                Status = DomainTaskStatus.NotReady
            };
            await storage.Save(task);
            using var unified = new UnifiedTaskStorage(new TaskTreeManager(storage));
            await unified.Init();
            var enumerationsAfterInit = storage.DirectoryEnumerationCount;

            task.Title = "After external edit";
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, task.Id),
                JsonConvert.SerializeObject(task));
            var result = await unified.TrySetStatusAsync(task.Id, DomainTaskStatus.NotReady);

            using (Assert.Multiple())
            {
                await Assert.That(result.AuthoritativeTask?.Title).IsEqualTo("After external edit");
                await Assert.That(storage.DirectoryEnumerationCount).IsGreaterThan(enumerationsAfterInit);
            }
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task InitFinalCheckpoint_RemovesDeletedTaskButPreservesUnrelatedCorruptProjection()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var watcher = new RecordingDatabaseWatcher();
            var storage = new TestFileStorage(tempDir, watcher);
            var deleted = new TaskItem { Id = "init-deleted", Title = "Deleted", Status = DomainTaskStatus.NotReady };
            var corrupt = new TaskItem { Id = "init-corrupt", Title = "Last valid projection", Status = DomainTaskStatus.NotReady };
            await storage.Save(deleted);
            await storage.Save(corrupt);
            watcher.OnEnabled = () =>
            {
                File.Delete(Path.Combine(tempDir, deleted.Id));
                File.WriteAllText(Path.Combine(tempDir, corrupt.Id), "{ corrupt json");
                watcher.EmitRaw(deleted.Id, UpdateType.Removed);
                watcher.EmitRaw(corrupt.Id, UpdateType.Saved);
            };

            using var unified = new UnifiedTaskStorage(new TaskTreeManager(storage));
            await unified.Init();
            var graph = await storage.ReadGraphAsync();

            using (Assert.Multiple())
            {
                await Assert.That(unified.Tasks.Lookup(deleted.Id).HasValue).IsFalse();
                await Assert.That(unified.Tasks.Lookup(corrupt.Id).HasValue).IsTrue();
                await Assert.That(unified.Tasks.Lookup(corrupt.Id).Value.Title).IsEqualTo("Last valid projection");
                await Assert.That(graph.LoadErrors.Select(static error => Path.GetFileName(error.File)))
                    .Contains(corrupt.Id);
            }
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task LiveGraph_LoadReturnsIsolatedTaskSnapshot()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var storage = new FileStorage(tempDir, watcher: false);
            await storage.Save(new TaskItem
            {
                Id = "isolated-live-task",
                Title = "Persisted",
                Status = DomainTaskStatus.NotReady
            });
            await storage.EnableLiveGraphAsync();

            var loaded = await storage.Load("isolated-live-task");
            loaded!.Status = DomainTaskStatus.Completed;
            loaded.StatusHistory.Add(new TaskStatusHistoryEntry
            {
                Status = DomainTaskStatus.Completed,
                ChangedAt = DateTimeOffset.UtcNow,
                Author = "mutator"
            });

            var graph = await storage.ReadGraphAsync();
            var cached = graph.TasksById["isolated-live-task"];
            await Assert.That(cached.Status).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(cached.StatusHistory.Any(entry => entry?.Author == "mutator")).IsFalse();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task StatusCommand_AppliesRawWatcherChangeBeforeDebouncedUiUpdate()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var watcher = new RecordingDatabaseWatcher();
            var storage = new TestFileStorage(tempDir, watcher);
            var task = new TaskItem
            {
                Id = "raw-watcher-task",
                Title = "Before external edit",
                Status = DomainTaskStatus.NotReady
            };
            await storage.Save(task);
            await storage.EnableLiveGraphAsync();

            task.Title = "After external edit";
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, task.Id),
                JsonConvert.SerializeObject(task));
            watcher.EmitRaw(task.Id, UpdateType.Saved);

            var result = await new TaskGraphCommandService(storage)
                .TrySetStatusAsync(task.Id, DomainTaskStatus.NotReady, "tester");

            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.AuthoritativeTask?.Title).IsEqualTo("After external edit");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task WatcherInvalidation_PerformsOneReloadBeforeNextCommand()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var watcher = new RecordingDatabaseWatcher();
            var storage = new CountingFileStorage(tempDir, watcher);
            await storage.Save(new TaskItem
            {
                Id = "reload-once-task",
                Title = "Reload once",
                Status = DomainTaskStatus.NotReady
            });
            await storage.EnableLiveGraphAsync();
            var initialEnumerations = storage.DirectoryEnumerationCount;
            watcher.Invalidate();

            var service = new TaskGraphCommandService(storage);
            var first = await service.TrySetStatusAsync("reload-once-task", DomainTaskStatus.NotReady);
            var afterRecovery = storage.DirectoryEnumerationCount;
            var second = await service.TrySetStatusAsync("reload-once-task", DomainTaskStatus.NotReady);

            using (Assert.Multiple())
            {
                await Assert.That(first.Success).IsTrue();
                await Assert.That(second.Success).IsTrue();
                await Assert.That(afterRecovery).IsEqualTo(initialEnumerations + 1);
                await Assert.That(storage.DirectoryEnumerationCount).IsEqualTo(afterRecovery);
            }
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task TruncatedFile_IsAppliedBeforeWarmStatusCommandAndIsNotOverwritten()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var watcher = new RecordingDatabaseWatcher();
            var storage = new TestFileStorage(tempDir, watcher);
            const string taskId = "truncated-before-command";
            await storage.Save(new TaskItem
            {
                Id = taskId,
                Title = "Must not be restored",
                Status = DomainTaskStatus.NotReady
            });
            await storage.EnableLiveGraphAsync();
            var filePath = Path.Combine(tempDir, taskId);
            await File.WriteAllTextAsync(filePath, string.Empty);
            watcher.EmitRaw(taskId, UpdateType.Saved);

            var result = await new TaskGraphCommandService(storage)
                .TrySetStatusAsync(taskId, DomainTaskStatus.Prepared, "tester");

            using (Assert.Multiple())
            {
                await Assert.That(result.Success).IsFalse();
                await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.TaskNotFound);
                await Assert.That(new FileInfo(filePath).Length).IsEqualTo(0);
                await Assert.That(await storage.Load(taskId)).IsNull();
            }
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task LiveGraphMiss_DoesNotFallBackToStaleLegacyCacheAfterDeleteOrCorruption()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var watcher = new RecordingDatabaseWatcher();
            var storage = new TestFileStorage(tempDir, watcher);
            const string taskId = "authoritative-live-miss";
            var task = new TaskItem { Id = taskId, Title = "Original", Status = DomainTaskStatus.NotReady };
            await storage.Save(task);
            await storage.EnableLiveGraphAsync();
            var filePath = Path.Combine(tempDir, taskId);

            File.Delete(filePath);
            watcher.EmitRaw(taskId, UpdateType.Removed);
            await storage.SynchronizePendingFileChangesAsync();
            await Assert.That(await storage.Load(taskId)).IsNull();

            await storage.Save(task);
            await File.WriteAllTextAsync(filePath, "{ corrupt json");
            watcher.EmitRaw(taskId, UpdateType.Saved);
            var graph = await storage.SynchronizePendingFileChangesAsync();
            var result = await new TaskGraphCommandService(storage)
                .TrySetStatusAsync(taskId, DomainTaskStatus.Prepared);

            using (Assert.Multiple())
            {
                await Assert.That(await storage.Load(taskId)).IsNull();
                await Assert.That(graph.LoadErrors).HasCount().EqualTo(1);
                await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.ValidationFailed);
                await Assert.That(await File.ReadAllTextAsync(filePath)).IsEqualTo("{ corrupt json");
            }
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task DebouncedEventType_IsDerivedFromCurrentPhysicalState()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var storage = new TestFileStorage(tempDir);
            const string taskId = "event-reorder";
            var task = new TaskItem { Id = taskId, Title = "Initial", Status = DomainTaskStatus.NotReady };
            await storage.Save(task);
            await storage.EnableLiveGraphAsync();
            var observed = new List<UpdateType>();
            storage.Updating += (_, args) => observed.Add(args.Type);

            task.Title = "Recreated";
            await File.WriteAllTextAsync(Path.Combine(tempDir, taskId), JsonConvert.SerializeObject(task));
            await storage.TriggerUpdatingAsync(taskId, UpdateType.Removed);
            File.Delete(Path.Combine(tempDir, taskId));
            await storage.TriggerUpdatingAsync(taskId, UpdateType.Saved);

            await Assert.That(observed).IsEquivalentTo([UpdateType.Saved, UpdateType.Removed]);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task LegacyNullNestedValues_AreClonedWithoutAliasingOrStartupFailure()
    {
        var task = new TaskItem
        {
            Id = "legacy-null-nested",
            CompletionCriteria = [null!],
            StatusHistory = [null!],
            Repeater = new RepeaterPattern { Type = RepeaterType.Daily, Pattern = null! }
        };

        var clone = TaskItemSnapshot.Clone(task);

        using (Assert.Multiple())
        {
            await Assert.That(clone.CompletionCriteria).HasCount().EqualTo(1);
            await Assert.That(clone.CompletionCriteria[0]).IsNull();
            await Assert.That(clone.StatusHistory[0]).IsNull();
            await Assert.That(clone.Repeater?.Pattern).IsNull();
        }
    }

    [Test]
    public async Task Dispose_StopsOwnedWatcherLifetime()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var watcher = new RecordingDatabaseWatcher();
            var storage = new TestFileStorage(tempDir, watcher);

            storage.Dispose();

            await Assert.That(watcher.IsDisposed).IsTrue();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task NewRawGenerationDuringRefresh_RemainsPendingForNextCommand()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var watcher = new RecordingDatabaseWatcher();
            var storage = new TestFileStorage(tempDir, watcher);
            const string taskId = "generation-during-refresh";
            var task = new TaskItem { Id = taskId, Title = "Initial", Status = DomainTaskStatus.NotReady };
            await storage.Save(task);
            await storage.EnableLiveGraphAsync();

            var lockEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var heldLock = storage.WithDirectoryLockAsync(async () =>
            {
                lockEntered.SetResult();
                await releaseLock.Task;
            });
            await lockEntered.Task;

            task.Title = "First external edit";
            await File.WriteAllTextAsync(Path.Combine(tempDir, taskId), JsonConvert.SerializeObject(task));
            watcher.EmitRaw(taskId, UpdateType.Saved);
            var refresh = storage.TriggerUpdatingAsync(taskId);
            await Task.Delay(50);
            task.Title = "Second external edit";
            await File.WriteAllTextAsync(Path.Combine(tempDir, taskId), JsonConvert.SerializeObject(task));
            watcher.EmitRaw(taskId, UpdateType.Saved);
            releaseLock.SetResult();
            await Task.WhenAll(heldLock, refresh);

            var result = await new TaskGraphCommandService(storage)
                .TrySetStatusAsync(taskId, DomainTaskStatus.NotReady);

            await Assert.That(result.AuthoritativeTask?.Title).IsEqualTo("Second external edit");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task PartialRepeaterFailure_ReconcilesEveryAttemptedTaskFromLiveGraph()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var storage = new FailingWriteFileStorage(tempDir, new RecordingDatabaseWatcher());
            var source = new TaskItem
            {
                Id = "partial-repeater-source",
                UserId = "owner",
                Title = "Repeating source",
                Status = DomainTaskStatus.Prepared,
                IsCanBeCompleted = true,
                PlannedBeginDateTime = DateTimeOffset.UtcNow.AddDays(-1),
                Repeater = new RepeaterPattern { Type = RepeaterType.Daily, Period = 1, Pattern = [] }
            };
            await storage.Save(source);
            using var unified = new UnifiedTaskStorage(new TaskTreeManager(storage));
            await unified.Init();
            var enumerationsAfterInit = storage.DirectoryEnumerationCount;
            storage.FailWhenWriting(source.Id);

            var result = await unified.TrySetStatusAsync(source.Id, DomainTaskStatus.Completed, "tester");
            var graph = await storage.ReadGraphAsync();
            var clone = graph.Tasks.Single(task => task.Id != source.Id && task.Title == source.Title);

            using (Assert.Multiple())
            {
                await Assert.That(result.DeniedReason?.Kind).IsEqualTo(TaskOperationDeniedKind.OutcomeUnknown);
                await Assert.That(result.StorageRevision).IsGreaterThan(0);
                await Assert.That(result.StorageRevision).IsEqualTo(graph.Revision);
                await Assert.That(storage.DirectoryEnumerationCount).IsEqualTo(enumerationsAfterInit);
                await Assert.That(result.ChangedTasks.Select(static task => task.Id)).Contains(clone.Id);
                await Assert.That(unified.Tasks.Lookup(clone.Id).HasValue).IsTrue();
                await Assert.That(unified.Tasks.Lookup(source.Id).Value.Status).IsEqualTo(DomainTaskStatus.Completed);
            }
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task UnarchiveCommand_PreservesNullAndFutureHistoryAndAppendsOneNormalizedEntry()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var validInProgressAt = now.AddHours(-2);
            var farFutureAt = now.AddDays(30);
            var storage = new FileStorage(tempDir, watcher: false);
            var task = new TaskItem
            {
                Id = "legacy-unarchive-history-task",
                UserId = "owner",
                Title = "Legacy unarchive history",
                Status = DomainTaskStatus.Archived,
                IsCanBeCompleted = true,
                StatusHistory =
                [
                    null!,
                    new TaskStatusHistoryEntry
                    {
                        Status = DomainTaskStatus.InProgress,
                        ChangedAt = validInProgressAt,
                        Author = "legacy"
                    },
                    new TaskStatusHistoryEntry
                    {
                        Status = DomainTaskStatus.Completed,
                        ChangedAt = farFutureAt,
                        Author = "future-corrupt"
                    },
                    new TaskStatusHistoryEntry
                    {
                        Status = DomainTaskStatus.Archived,
                        ChangedAt = now.AddHours(-1),
                        Author = "legacy"
                    }
                ]
            };
            await storage.Save(task);
            using var repository = new UnifiedTaskStorage(new TaskTreeManager(storage));
            await repository.Init();

            var result = await repository.TryUnarchiveAsync(task.Id, "tester");
            var persisted = await storage.Load(task.Id, forced: true);

            using (Assert.Multiple())
            {
                await Assert.That(result.Success).IsTrue();
                await Assert.That(result.AuthoritativeTask?.Status)
                    .IsEqualTo(DomainTaskStatus.Prepared);
                await Assert.That(persisted).IsNotNull();
                await Assert.That(persisted!.Status).IsEqualTo(DomainTaskStatus.Prepared);
                await Assert.That(persisted.StatusHistory.Count)
                    .IsEqualTo(task.StatusHistory.Count + 1);
                await Assert.That(persisted.StatusHistory[0]).IsNull();
                await Assert.That(persisted.StatusHistory[1].Status)
                    .IsEqualTo(DomainTaskStatus.InProgress);
                await Assert.That(persisted.StatusHistory[1].ChangedAt.ToUnixTimeSeconds())
                    .IsEqualTo(validInProgressAt.ToUnixTimeSeconds());
                await Assert.That(persisted.StatusHistory[2].ChangedAt.ToUnixTimeSeconds())
                    .IsEqualTo(farFutureAt.ToUnixTimeSeconds());
                await Assert.That(persisted.StatusHistory[^1].Status)
                    .IsEqualTo(DomainTaskStatus.Prepared);
                await Assert.That(persisted.StatusHistory[^1].Author).IsEqualTo("tester");
            }
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task Save_WritesExplicitStatusHistoryAndCompletionCriteriaWithoutLegacyFields()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var startedAt = new DateTimeOffset(2026, 3, 1, 10, 30, 0, TimeSpan.Zero);
            var storage = new FileStorage(tempDir, watcher: false);
            var task = new TaskItem
            {
                Id = "status-storage-task",
                UserId = "owner",
                Title = "Status storage task",
                Description = "Storage contract",
                Status = DomainTaskStatus.InProgress,
                StatusHistory =
                [
                    new()
                    {
                        Status = DomainTaskStatus.NotReady,
                        ChangedAt = startedAt.AddHours(-1),
                        Author = "owner"
                    },
                    new()
                    {
                        Status = DomainTaskStatus.InProgress,
                        ChangedAt = startedAt,
                        Author = "owner"
                    }
                ],
                CompletionCriteria =
                [
                    new()
                    {
                        Id = "criterion-1",
                        Text = "Проверить результат",
                        IsSatisfied = true
                    }
                ]
            };

            await storage.Save(task);

            var json = await File.ReadAllTextAsync(Path.Combine(tempDir, task.Id));
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var loaded = await storage.Load(task.Id, forced: true);

            await Assert.That(root.GetProperty("Status").GetString()).IsEqualTo(nameof(DomainTaskStatus.InProgress));
            await Assert.That(root.GetProperty("StatusHistory").EnumerateArray().Count()).IsEqualTo(2);
            await Assert.That(root.GetProperty("CompletionCriteria").EnumerateArray().Count()).IsEqualTo(1);
            await Assert.That(root.TryGetProperty("IsCompleted", out _)).IsFalse();
            await Assert.That(root.TryGetProperty("CompletedDateTime", out _)).IsFalse();
            await Assert.That(root.TryGetProperty("ArchiveDateTime", out _)).IsFalse();
            await Assert.That(loaded).IsNotNull();
            await Assert.That(loaded!.Status).IsEqualTo(DomainTaskStatus.InProgress);
            await Assert.That(loaded.StartedDateTime).IsEqualTo(startedAt);
            await Assert.That(loaded.CompletionCriteria.Single().Text).IsEqualTo("Проверить результат");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task OnUpdating_MalformedTaskFileDoesNotThrowOrDeleteExistingProjection()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(tempDir, "malformed-task");
            await File.WriteAllTextAsync(filePath, "{ malformed json");
            var storage = new TestFileStorage(tempDir);
            var raised = false;
            string? observedId = null;

            storage.Updating += (_, args) =>
            {
                raised = true;
                observedId = args.Id;
            };

            await storage.TriggerUpdatingAsync("malformed-task");

            await Assert.That(raised).IsFalse();
            await Assert.That(observedId).IsNull();
            await Assert.That(await storage.Load("malformed-task", forced: true)).IsNull();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task OnUpdating_RefreshesCacheBeforeRaisingUpdating()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var storage = new TestFileStorage(tempDir);
            var task = new TaskItem { Id = "updated-task", Title = "Old title" };
            await storage.Save(task);
            _ = await storage.Load(task.Id, forced: true);
            task.Title = "New title";
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, task.Id),
                JsonConvert.SerializeObject(task));
            string? observedTitle = null;

            storage.Updating += (_, args) =>
                observedTitle = storage.Load(args.Id).GetAwaiter().GetResult()?.Title;

            await storage.TriggerUpdatingAsync(task.Id);

            await Assert.That(observedTitle).IsEqualTo("New title");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task Save_TellsWatcherToIgnoreActualSourceFileName()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(tempDir, "alias.json");
            await File.WriteAllTextAsync(sourcePath, JsonConvert.SerializeObject(new TaskItem
            {
                Id = "alias",
                Title = "Original"
            }));
            var watcher = new RecordingDatabaseWatcher();
            var storage = new TestFileStorage(tempDir, watcher);
            var task = (await storage.ReadDirectoryAsync()).Tasks.Single();

            await storage.Save(task);

            await Assert.That(watcher.IgnoredTaskIds).Contains("alias.json");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task OnUpdating_SourceFileNameRaisesDomainTaskId()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "alias.json"),
                JsonConvert.SerializeObject(new TaskItem { Id = "alias", Title = "Aliased" }));
            var storage = new TestFileStorage(tempDir);
            _ = await storage.ReadDirectoryAsync();
            string? observedId = null;
            storage.Updating += (_, args) => observedId = args.Id;

            await storage.TriggerUpdatingAsync("alias.json");

            await Assert.That(observedId).IsEqualTo("alias");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "file-storage-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp artifacts.
        }
    }

    private sealed class TestFileStorage : FileStorage
    {
        public TestFileStorage(string path) : base(path, watcher: false)
        {
        }

        public TestFileStorage(string path, IDatabaseWatcher watcher) : base(path, watcher)
        {
        }

        public Task TriggerUpdatingAsync(string id, UpdateType type = UpdateType.Saved) => OnUpdatingAsync(new TaskStorageUpdateEventArgs
        {
            Id = id,
            Type = type
        });
    }

    private sealed class CountingFileStorage : FileStorage
    {
        public CountingFileStorage(string path) : base(path, watcher: false)
        {
        }

        public CountingFileStorage(string path, IDatabaseWatcher watcher) : base(path, watcher)
        {
        }

        public int DirectoryEnumerationCount { get; private set; }

        protected override IEnumerable<string> EnumerateTaskFiles()
        {
            DirectoryEnumerationCount++;
            return base.EnumerateTaskFiles();
        }
    }

    private sealed class FailingWriteFileStorage : FileStorage
    {
        private string? _failingTaskId;

        public FailingWriteFileStorage(string path, IDatabaseWatcher watcher) : base(path, watcher)
        {
        }

        public int DirectoryEnumerationCount { get; private set; }

        public void FailWhenWriting(string taskId) => _failingTaskId = taskId;

        protected override IEnumerable<string> EnumerateTaskFiles()
        {
            DirectoryEnumerationCount++;
            return base.EnumerateTaskFiles();
        }

        protected override void OnAfterWritePersisted(string taskId, string filePath)
        {
            base.OnAfterWritePersisted(taskId, filePath);
            if (string.Equals(taskId, _failingTaskId, StringComparison.Ordinal))
            {
                throw new IOException("simulated post-commit cache publication failure");
            }
        }
    }

    private sealed class BlockingCompletedWriteFileStorage : FileStorage
    {
        private int _hasBlockedCompletedWrite;

        public BlockingCompletedWriteFileStorage(string path, IDatabaseWatcher watcher) : base(path, watcher)
        {
        }

        public bool BlockCompletedWrite { get; set; }

        public TaskCompletionSource CompletedWriteEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseCompletedWrite { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override void OnAfterWritePersisted(string taskId, string filePath)
        {
            base.OnAfterWritePersisted(taskId, filePath);
            if (!BlockCompletedWrite || Volatile.Read(ref _hasBlockedCompletedWrite) != 0)
            {
                return;
            }

            var persisted = JsonConvert.DeserializeObject<TaskItem>(File.ReadAllText(filePath));
            if (persisted?.Status != DomainTaskStatus.Completed ||
                Interlocked.Exchange(ref _hasBlockedCompletedWrite, 1) != 0)
            {
                return;
            }

            CompletedWriteEntered.TrySetResult();
            ReleaseCompletedWrite.Task.GetAwaiter().GetResult();
        }
    }

    private sealed class RecordingDatabaseWatcher : IDatabaseWatcher, IRawDatabaseWatcher, IDisposable
    {
        public List<string> IgnoredTaskIds { get; } = [];
        public bool IsDisposed { get; private set; }
        public Action? OnEnabled { get; set; }

        public event EventHandler<DbUpdatedEventArgs>? OnUpdated;
        public event EventHandler<DbUpdatedEventArgs>? OnRawUpdated;
        public event EventHandler? OnInvalidated;

        public void AddIgnoredTask(string taskId) => IgnoredTaskIds.Add(taskId);

        public void SetEnable(bool enable)
        {
            if (enable)
            {
                var callback = OnEnabled;
                OnEnabled = null;
                callback?.Invoke();
            }
        }

        public void ForceUpdateFile(string filename, UpdateType type) => OnUpdated?.Invoke(this, new DbUpdatedEventArgs
        {
            Id = filename,
            Type = type
        });

        public void EmitRaw(string filename, UpdateType type) => OnRawUpdated?.Invoke(this, new DbUpdatedEventArgs
        {
            Id = filename,
            Type = type
        });

        public void Invalidate() => OnInvalidated?.Invoke(this, EventArgs.Empty);

        public void Dispose() => IsDisposed = true;
    }
}
