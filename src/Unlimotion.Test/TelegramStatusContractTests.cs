using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DynamicData;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.TelegramBot;
using Unlimotion.ViewModel;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public sealed class TelegramStatusContractTests
{
    [Test]
    public async Task InvalidCallback_StrictlyRejectsUndefinedNumericAndMalformedStatus()
    {
        using var storage = new ScriptedTaskStorage();
        storage.Seed(CreateTask("task", DomainTaskStatus.Prepared));
        string?[] invalidCallbacks =
        [
            null,
            "status_",
            "status_999_task",
            "status_1_task",
            "status_prepared_task",
            "status_Prepared_"
        ];

        foreach (var callbackData in invalidCallbacks)
        {
            var outcome = await TelegramStatusContract.ExecuteCallbackAsync(
                storage,
                callbackData,
                "tester");

            using (Assert.Multiple())
            {
                await Assert.That(outcome.IsValid).IsFalse();
                await Assert.That(outcome.OperationResult).IsNull();
                await Assert.That(outcome.AnswerText).Contains("Неизвестный статус");
            }
        }

        await Assert.That(storage.StatusCalls).IsEmpty();
    }

    [Test]
    public async Task DeniedCallback_AwaitsStoragePolicyResultAndKeepsPersistedStatus()
    {
        using var storage = new ScriptedTaskStorage();
        var cachedSnapshot = CreateTask("terminal_task", DomainTaskStatus.Prepared);
        var persisted = CreateTask(cachedSnapshot.Id, DomainTaskStatus.Completed);
        var cached = storage.Seed(cachedSnapshot);
        var initialHistoryCount = cached.StatusHistory.Count;
        storage.StatusHandler = (taskId, requestedStatus, author) =>
        {
            storage.RefreshCachedTask(persisted);
            return Task.FromResult(TaskOperationResult.DeniedWithAuthoritativeTask(
                TaskOperationDeniedReason.CreateWithStatusTransition(
                    TaskOperationDeniedKind.StatusTransitionDenied,
                    "terminal cannot start",
                    statusTransitionReason: TaskStatusTransitionDenialReason.TerminalCannotStart,
                    taskId: taskId,
                    requestedStatus: requestedStatus),
                authoritativeTask: CloneTask(persisted)));
        };

        var outcome = await TelegramStatusContract.ExecuteCallbackAsync(
            storage,
            TelegramStatusContract.CreateCallbackData(DomainTaskStatus.InProgress, persisted.Id),
            "tester");

        using (Assert.Multiple())
        {
            await Assert.That(storage.StatusCalls.Count).IsEqualTo(1);
            await Assert.That(storage.StatusCalls[0].ObservedStatus)
                .IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(outcome.OperationResult?.Success).IsFalse();
            await Assert.That(outcome.DisplayStatus).IsEqualTo(DomainTaskStatus.Completed);
            await Assert.That(cached.Status).IsEqualTo(DomainTaskStatus.Completed);
            await Assert.That(cached.StatusHistory.Count).IsEqualTo(initialHistoryCount);
            await Assert.That(outcome.AnswerText).Contains("нельзя вернуть в работу");
            await Assert.That(outcome.AnswerText).Contains("Выполнено");
        }
    }

    [Test]
    [Arguments(
        TaskAvailabilityReasonKind.IncompleteContainedTask,
        "Сначала выполните все вложенные задачи.")]
    [Arguments(
        TaskAvailabilityReasonKind.IncompleteDirectBlocker,
        "Сначала выполните прямые блокирующие задачи.")]
    [Arguments(
        TaskAvailabilityReasonKind.IncompleteInheritedBlocker,
        "Сначала выполните блокирующие задачи, унаследованные от родительских задач.")]
    public async Task DeniedGraphCallback_UsesAuthoritativeStructuredReason(
        TaskAvailabilityReasonKind reasonKind,
        string expectedReason)
    {
        using var storage = new ScriptedTaskStorage();
        var persisted = CreateTask($"graph-{reasonKind}", DomainTaskStatus.Prepared);
        var cached = storage.Seed(persisted);
        var initialHistoryCount = cached.StatusHistory.Count;
        storage.StatusHandler = (taskId, requestedStatus, _) => Task.FromResult(
            TaskOperationResult.DeniedWithAuthoritativeTask(
                TaskOperationDeniedReason.CreateWithStatusTransition(
                    TaskOperationDeniedKind.StatusTransitionDenied,
                    "misleading coarse message",
                    statusTransitionReason: TaskStatusTransitionDenialReason.GraphUnavailableForStart,
                    taskId: taskId,
                    requestedStatus: requestedStatus),
                authoritativeTask: CloneTask(persisted),
                before: new TaskAvailabilityAnalysis
                {
                    TaskId = taskId,
                    Status = DomainTaskStatus.Prepared,
                    IsCanBeCompleted = false,
                    Reasons =
                    [
                        new TaskAvailabilityReason
                        {
                            Kind = reasonKind,
                            SubjectId = $"subject-{reasonKind}",
                            Details = "authoritative"
                        }
                    ]
                }));

        var outcome = await TelegramStatusContract.ExecuteCallbackAsync(
            storage,
            TelegramStatusContract.CreateCallbackData(DomainTaskStatus.InProgress, persisted.Id),
            "tester");

        using (Assert.Multiple())
        {
            await Assert.That(outcome.OperationResult?.Success).IsFalse();
            await Assert.That(outcome.AnswerText).Contains(expectedReason);
            await Assert.That(outcome.AnswerText).DoesNotContain("заблокирована связями");
            await Assert.That(outcome.AnswerText).Contains("Подготовлено");
            await Assert.That(cached.Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(cached.StatusHistory.Count).IsEqualTo(initialHistoryCount);
            await Assert.That(storage.StatusCalls.Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task DeniedGraphCallback_PrioritizesContainedAndDirectAcrossMixedReasons()
    {
        var scenarios = new[]
        {
            new
            {
                Name = "direct-over-inherited",
                Reasons = new[]
                {
                    TaskAvailabilityReasonKind.IncompleteInheritedBlocker,
                    TaskAvailabilityReasonKind.IncompleteDirectBlocker
                },
                ExpectedReason = "Сначала выполните прямые блокирующие задачи.",
                LowerPriorityReason = "Сначала выполните блокирующие задачи, унаследованные от родительских задач.",
                DenialReason = TaskStatusTransitionDenialReason.GraphUnavailableForStart,
                RequestedStatus = DomainTaskStatus.InProgress
            },
            new
            {
                Name = "contained-over-all-completion",
                Reasons = new[]
                {
                    TaskAvailabilityReasonKind.IncompleteInheritedBlocker,
                    TaskAvailabilityReasonKind.IncompleteDirectBlocker,
                    TaskAvailabilityReasonKind.IncompleteContainedTask
                },
                ExpectedReason = "Сначала выполните все вложенные задачи.",
                LowerPriorityReason = "Сначала выполните прямые блокирующие задачи.",
                DenialReason = TaskStatusTransitionDenialReason.GraphUnavailableForCompletion,
                RequestedStatus = DomainTaskStatus.Completed
            }
        };

        foreach (var scenario in scenarios)
        {
            using var storage = new ScriptedTaskStorage();
            var persisted = CreateTask($"graph-priority-{scenario.Name}", DomainTaskStatus.Prepared);
            var cached = storage.Seed(persisted);
            var initialHistoryCount = cached.StatusHistory.Count;
            storage.StatusHandler = (taskId, requestedStatus, _) => Task.FromResult(
                TaskOperationResult.DeniedWithAuthoritativeTask(
                    TaskOperationDeniedReason.CreateWithStatusTransition(
                        TaskOperationDeniedKind.StatusTransitionDenied,
                        "misleading coarse message",
                        statusTransitionReason: scenario.DenialReason,
                        taskId: taskId,
                        requestedStatus: requestedStatus),
                    authoritativeTask: CloneTask(persisted),
                    before: new TaskAvailabilityAnalysis
                    {
                        TaskId = taskId,
                        Status = DomainTaskStatus.Prepared,
                        IsCanBeCompleted = false,
                        Reasons = scenario.Reasons
                            .Select(reasonKind => new TaskAvailabilityReason
                            {
                                Kind = reasonKind,
                                SubjectId = $"subject-{reasonKind}",
                                Details = "authoritative"
                            })
                            .ToArray()
                    }));

            var outcome = await TelegramStatusContract.ExecuteCallbackAsync(
                storage,
                TelegramStatusContract.CreateCallbackData(scenario.RequestedStatus, persisted.Id),
                "tester");

            using (Assert.Multiple())
            {
                await Assert.That(outcome.OperationResult?.Success).IsFalse();
                await Assert.That(outcome.AnswerText).Contains(scenario.ExpectedReason);
                await Assert.That(outcome.AnswerText).DoesNotContain(scenario.LowerPriorityReason);
                await Assert.That(outcome.AnswerText).Contains("Подготовлено");
                await Assert.That(cached.Status).IsEqualTo(DomainTaskStatus.Prepared);
                await Assert.That(cached.StatusHistory.Count).IsEqualTo(initialHistoryCount);
                await Assert.That(storage.StatusCalls.Count).IsEqualTo(1);
            }
        }
    }

    [Test]
    public async Task DuplicateCurrentStatusCallback_IsNoOpWithoutHistoryWrite()
    {
        using var storage = new ScriptedTaskStorage();
        var persisted = CreateTask("same_status_task", DomainTaskStatus.Prepared);
        var cached = storage.Seed(persisted);
        var initialHistoryCount = cached.StatusHistory.Count;
        storage.StatusHandler = (taskId, requestedStatus, author) => Task.FromResult(
            TaskOperationResult.Succeeded(
                [],
                null,
                null,
                null,
                CloneTask(persisted)));

        var outcome = await TelegramStatusContract.ExecuteCallbackAsync(
            storage,
            TelegramStatusContract.CreateCallbackData(DomainTaskStatus.Prepared, persisted.Id),
            "tester");

        using (Assert.Multiple())
        {
            await Assert.That(storage.StatusCalls.Count).IsEqualTo(1);
            await Assert.That(outcome.OperationResult?.Success).IsTrue();
            await Assert.That(outcome.OperationResult?.ChangedTasks).IsEmpty();
            await Assert.That(outcome.DisplayStatus).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(cached.Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(cached.StatusHistory.Count).IsEqualTo(initialHistoryCount);
            await Assert.That(outcome.AnswerText).Contains("Подготовлено");
        }
    }

    [Test]
    public async Task AllowedCallback_DoesNotMutateBeforeAwaitedStorageCommandAndShowsAuthoritativeStatus()
    {
        using var storage = new ScriptedTaskStorage();
        var persisted = CreateTask("allowed_task", DomainTaskStatus.Prepared);
        var cached = storage.Seed(persisted);
        var commandStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCommand = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.StatusHandler = async (taskId, requestedStatus, author) =>
        {
            commandStarted.TrySetResult();
            await releaseCommand.Task;
            var updated = CloneTask(persisted);
            updated.SetStatus(requestedStatus, DateTimeOffset.UtcNow, TaskItem.NormalizeAuthor(author));
            storage.RefreshCachedTask(updated);
            return TaskOperationResult.Succeeded(
                [CloneTask(updated)],
                null,
                null,
                null,
                CloneTask(updated));
        };

        var callbackTask = TelegramStatusContract.ExecuteCallbackAsync(
            storage,
            TelegramStatusContract.CreateCallbackData(DomainTaskStatus.InProgress, persisted.Id),
            "telegram-user");
        await commandStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.Multiple())
        {
            await Assert.That(callbackTask.IsCompleted).IsFalse();
            await Assert.That(cached.Status).IsEqualTo(DomainTaskStatus.Prepared);
        }

        releaseCommand.TrySetResult();
        var outcome = await callbackTask.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.Multiple())
        {
            await Assert.That(storage.StatusCalls.Count).IsEqualTo(1);
            await Assert.That(storage.StatusCalls[0].ObservedStatus)
                .IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(storage.StatusCalls[0].Author).IsEqualTo("telegram-user");
            await Assert.That(outcome.OperationResult?.Success).IsTrue();
            await Assert.That(outcome.DisplayStatus).IsEqualTo(DomainTaskStatus.InProgress);
            await Assert.That(cached.Status).IsEqualTo(DomainTaskStatus.InProgress);
            await Assert.That(outcome.AnswerText).Contains("Выполняется");
        }
    }

    [Test]
    public async Task OutcomeUnknown_ShowsRefreshedCacheStatusInsteadOfRequestedStatus()
    {
        using var storage = new ScriptedTaskStorage();
        var persisted = CreateTask("unknown_task", DomainTaskStatus.Prepared);
        var cached = storage.Seed(persisted);
        storage.StatusHandler = async (taskId, requestedStatus, author) =>
        {
            var refreshed = CloneTask(persisted);
            refreshed.SetStatus(
                DomainTaskStatus.NotReady,
                DateTimeOffset.UtcNow,
                TaskItem.NormalizeAuthor(author));
            storage.RefreshCachedTask(refreshed);
            await storage.BackingStorage.Save(CloneTask(refreshed));
            return TaskOperationResult.Denied(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.OutcomeUnknown,
                    "post-write verification failed",
                    taskId,
                    requestedStatus));
        };

        var outcome = await TelegramStatusContract.ExecuteCallbackAsync(
            storage,
            TelegramStatusContract.CreateCallbackData(DomainTaskStatus.InProgress, persisted.Id),
            "tester");

        using (Assert.Multiple())
        {
            await Assert.That(outcome.OperationResult?.DeniedReason?.Kind)
                .IsEqualTo(TaskOperationDeniedKind.OutcomeUnknown);
            await Assert.That(outcome.OperationResult?.AuthoritativeTask).IsNull();
            await Assert.That(outcome.DisplayStatus).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(outcome.RefreshedTask).IsNull();
            await Assert.That(cached.Status).IsEqualTo(DomainTaskStatus.NotReady);
            await Assert.That(outcome.AnswerText).Contains("Итог операции неизвестен");
            await Assert.That(outcome.AnswerText).Contains("Не готово");
            await Assert.That(outcome.AnswerText).DoesNotContain("Выполняется");
        }
    }

    [Test]
    public async Task OutcomeUnknown_WhenAuthoritativeReloadFails_DoesNotExposeStaleCache()
    {
        using var storage = new ScriptedTaskStorage();
        var cached = storage.Seed(CreateTask("failed_reload_task", DomainTaskStatus.Prepared));
        storage.StatusHandler = (taskId, requestedStatus, author) => Task.FromResult(
            TaskOperationResult.Denied(
                TaskOperationDeniedReason.Create(
                    TaskOperationDeniedKind.OutcomeUnknown,
                    "post-write verification and reload failed",
                    taskId,
                    requestedStatus)));

        var outcome = await TelegramStatusContract.ExecuteCallbackAsync(
            storage,
            TelegramStatusContract.CreateCallbackData(DomainTaskStatus.InProgress, cached.Id),
            "tester");

        using (Assert.Multiple())
        {
            await Assert.That(outcome.OperationResult?.DeniedReason?.Kind)
                .IsEqualTo(TaskOperationDeniedKind.OutcomeUnknown);
            await Assert.That(outcome.DisplayStatus).IsNull();
            await Assert.That(outcome.RefreshedTask).IsNull();
            await Assert.That(cached.Status).IsEqualTo(DomainTaskStatus.Prepared);
            await Assert.That(outcome.AnswerText).Contains("текущий статус задачи неизвестны");
            await Assert.That(outcome.AnswerText).DoesNotContain("Подготовлено");
        }
    }

    [Test]
    public async Task Keyboard_TargetsMatchSharedPolicyAndCurrentStatusRemainsTextOnly()
    {
        using var storage = new ScriptedTaskStorage();
        var scenarios = new[]
        {
            new KeyboardScenario(DomainTaskStatus.NotReady, true, false, true),
            new KeyboardScenario(DomainTaskStatus.Prepared, true, true, true),
            new KeyboardScenario(DomainTaskStatus.InProgress, false, false, false),
            new KeyboardScenario(DomainTaskStatus.Completed, true, false, true),
            new KeyboardScenario(DomainTaskStatus.Archived, true, false, true)
        };

        foreach (var scenario in scenarios)
        {
            var task = CreateTask(
                $"keyboard_{scenario.CurrentStatus}_with_underscores",
                scenario.CurrentStatus,
                scenario.IsGraphAvailable,
                scenario.PlannedBeginIsFuture,
                scenario.CompletionCriteriaSatisfied);
            using var viewModel = new TaskItemViewModel(task, storage, () => false);
            var facts = new TaskStatusTransitionFacts(
                scenario.CurrentStatus,
                scenario.IsGraphAvailable,
                scenario.PlannedBeginIsFuture,
                scenario.CompletionCriteriaSatisfied);
            var expectedTargets = Enum.GetValues<DomainTaskStatus>()
                .Where(status => status != scenario.CurrentStatus)
                .Where(status => TaskStatusTransitionPolicy.Evaluate(status, facts).IsAllowed)
                .ToArray();
            var buttons = Bot.BuildStatusKeyboard(viewModel)
                .SelectMany(static row => row)
                .ToArray();
            var actualTargets = new List<DomainTaskStatus>();

            foreach (var button in buttons)
            {
                await Assert.That(TelegramStatusContract.TryParseCallback(
                        button.CallbackData,
                        out var request))
                    .IsTrue();
                actualTargets.Add(request.RequestedStatus);
                await Assert.That(request.TaskId).IsEqualTo(task.Id);
                await Assert.That(button.Text)
                    .IsEqualTo(TelegramStatusContract.FormatStatus(request.RequestedStatus));
            }

            using (Assert.Multiple())
            {
                await Assert.That(string.Join(",", actualTargets))
                    .IsEqualTo(string.Join(",", expectedTargets));
                await Assert.That(actualTargets).DoesNotContain(scenario.CurrentStatus);
                await Assert.That(Bot.BuildCurrentStatusText(scenario.CurrentStatus))
                    .IsEqualTo(TelegramStatusContract.FormatStatus(scenario.CurrentStatus));
            }
        }
    }

    private static TaskItem CreateTask(
        string id,
        DomainTaskStatus status,
        bool isGraphAvailable = true,
        bool plannedBeginIsFuture = false,
        bool completionCriteriaSatisfied = true) => new()
        {
            Id = id,
            UserId = "telegram-user",
            Title = id,
            Description = string.Empty,
            Status = status,
            IsCanBeCompleted = isGraphAvailable,
            PlannedBeginDateTime = plannedBeginIsFuture
                ? DateTimeOffset.Now.AddDays(1)
                : DateTimeOffset.Now.AddDays(-1),
            CompletionCriteria =
            [
                new TaskCompletionCriterion
                {
                    Id = "criterion",
                    Text = "criterion",
                    IsSatisfied = completionCriteriaSatisfied
                }
            ],
            StatusHistory =
            [
                new TaskStatusHistoryEntry
                {
                    Status = status,
                    ChangedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                    Author = "seed"
                }
            ]
        };

    private static TaskItem CloneTask(TaskItem task) => task with
    {
        StatusHistory = task.StatusHistory
            .Select(static entry => entry is null
                ? null!
                : new TaskStatusHistoryEntry
                {
                    Status = entry.Status,
                    ChangedAt = entry.ChangedAt,
                    Author = entry.Author,
                    ExtensionData = entry.ExtensionData
                })
            .ToList(),
        CompletionCriteria = task.CompletionCriteria
            .Select(static criterion => new TaskCompletionCriterion
            {
                Id = criterion.Id,
                Text = criterion.Text,
                IsSatisfied = criterion.IsSatisfied,
                ExtensionData = criterion.ExtensionData
            })
            .ToList(),
        ContainsTasks = task.ContainsTasks.ToList(),
        ParentTasks = task.ParentTasks.ToList(),
        BlocksTasks = task.BlocksTasks.ToList(),
        BlockedByTasks = task.BlockedByTasks.ToList()
    };

    private sealed record KeyboardScenario(
        DomainTaskStatus CurrentStatus,
        bool IsGraphAvailable,
        bool PlannedBeginIsFuture,
        bool CompletionCriteriaSatisfied);

    private sealed class ScriptedTaskStorage : ITaskStorage, IDisposable
    {
        private readonly List<TaskItemViewModel> ownedViewModels = [];

        public ScriptedTaskStorage()
        {
            TaskTreeManager = new TaskTreeManager(BackingStorage);
        }

        public SourceCache<TaskItemViewModel, string> Tasks { get; } = new(task => task.Id);

        public ITaskRelationsIndex Relations { get; } = new TaskRelationsIndex();

        public InMemoryStorage BackingStorage { get; } = new();

        public TaskTreeManager TaskTreeManager { get; }

        public List<StatusCall> StatusCalls { get; } = [];

        public Func<string, DomainTaskStatus, string?, Task<TaskOperationResult>>? StatusHandler { get; set; }

        public event EventHandler<EventArgs>? Initiated;

        public TaskItemViewModel Seed(TaskItem task)
        {
            var viewModel = new TaskItemViewModel(CloneTask(task), this, () => false);
            ownedViewModels.Add(viewModel);
            Tasks.AddOrUpdate(viewModel);
            return viewModel;
        }

        public void RefreshCachedTask(TaskItem task)
        {
            var cached = Tasks.Lookup(task.Id);
            if (!cached.HasValue)
            {
                throw new InvalidOperationException($"Task '{task.Id}' is not cached.");
            }

            cached.Value.Update(CloneTask(task));
        }

        public async Task<TaskOperationResult> TrySetStatusAsync(
            string taskId,
            DomainTaskStatus requestedStatus,
            string? author = null)
        {
            var cached = Tasks.Lookup(taskId);
            StatusCalls.Add(new StatusCall(
                taskId,
                requestedStatus,
                author,
                cached.HasValue ? cached.Value.Status : null));

            if (StatusHandler is null)
            {
                throw new InvalidOperationException("StatusHandler must be configured for this test.");
            }

            return await StatusHandler(taskId, requestedStatus, author);
        }

        public Task Init()
        {
            Initiated?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task<TaskItemViewModel> Add(TaskItemViewModel? currentTask = null, bool isBlocked = false) =>
            throw new NotSupportedException();

        public Task<TaskItemViewModel> AddChild(TaskItemViewModel currentTask) =>
            throw new NotSupportedException();

        public Task<bool> Delete(TaskItemViewModel change, bool deleteInStorage = true) =>
            throw new NotSupportedException();

        public Task<bool> Delete(TaskItemViewModel change, TaskItemViewModel parent) =>
            throw new NotSupportedException();

        public Task<TaskItemViewModel> Update(TaskItemViewModel change) =>
            throw new NotSupportedException();

        public Task<TaskItemViewModel> Update(TaskItem change) =>
            throw new NotSupportedException();

        public Task<TaskItemViewModel> Clone(
            TaskItemViewModel change,
            params TaskItemViewModel[]? additionalParents) =>
            throw new NotSupportedException();

        public Task<bool> CopyInto(TaskItemViewModel change, TaskItemViewModel[]? additionalParents) =>
            throw new NotSupportedException();

        public Task<bool> MoveInto(
            TaskItemViewModel change,
            TaskItemViewModel[] additionalParents,
            TaskItemViewModel? currentTask) =>
            throw new NotSupportedException();

        public Task<bool> Unblock(TaskItemViewModel taskToUnblock, TaskItemViewModel blockingTask) =>
            throw new NotSupportedException();

        public Task<bool> Block(TaskItemViewModel change, TaskItemViewModel currentTask) =>
            throw new NotSupportedException();

        public Task RemoveParentChildConnection(TaskItemViewModel parent, TaskItemViewModel child) =>
            throw new NotSupportedException();

        public void Dispose()
        {
            foreach (var viewModel in ownedViewModels)
            {
                viewModel.Dispose();
            }

            Tasks.Dispose();
        }
    }

    private sealed record StatusCall(
        string TaskId,
        DomainTaskStatus RequestedStatus,
        string? Author,
        DomainTaskStatus? ObservedStatus);
}
