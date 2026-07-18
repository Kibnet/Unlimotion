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

internal static class TelegramCallbackCoverageContract
{
    private const long AllowedUserId = 42;
    private const long UnauthorizedUserId = 404;
    private const long ChatId = 1001;
    private const int MessageId = 77;
    private const string CallbackId = "callback-1";

    public static async Task AssertUnauthorizedUserDoesNotSendTaskDataOrTouchTasksAsync()
    {
        var tasks = new RecordingCallbackTaskOperations(CreateTask("task-1", "Secret task"));
        var responder = new RecordingCallbackResponder();
        var states = new RecordingUserStateStore();
        var handler = CreateHandler(tasks, responder, states);

        await handler.HandleCallbackAsync(Callback(
            UnauthorizedUserId,
            $"{TelegramCallbackHandler.OpenPrefix}task-1"));

        await Assert.That(tasks.TotalCalls).IsEqualTo(0);
        await Assert.That(responder.TotalCalls).IsEqualTo(0);
        await Assert.That(states.SetCalls).IsEqualTo(0);
    }

    public static async Task AssertOpenShowsSelectedTaskForAllowedUserAsync()
    {
        var responder = new RecordingCallbackResponder();
        var handler = CreateHandler(new RecordingCallbackTaskOperations(), responder, new RecordingUserStateStore());

        await handler.HandleCallbackAsync(AllowedCallback($"{TelegramCallbackHandler.OpenPrefix}task-1"));

        await Assert.That(responder.TaskRequests.Count).IsEqualTo(1);
        await Assert.That(responder.TaskRequests[0]).IsEqualTo((ChatId, "task-1"));
        await Assert.That(responder.Answers.Count).IsEqualTo(1);
        await Assert.That(responder.Answers[0]).IsEqualTo((CallbackId, (string?)null));
    }

    public static async Task AssertStatusUpdatesAndSavesSelectedTaskAsync()
    {
        var task = CreateTask("task-1", "Status task", DomainTaskStatus.NotReady);
        var tasks = new RecordingCallbackTaskOperations(task);
        var responder = new RecordingCallbackResponder();
        var handler = CreateHandler(tasks, responder, new RecordingUserStateStore());

        await handler.HandleCallbackAsync(AllowedCallback(
            $"{TelegramCallbackHandler.SetStatusPrefix}{DomainTaskStatus.InProgress}_task-1"));

        await Assert.That(task.Status).IsEqualTo(DomainTaskStatus.InProgress);
        await Assert.That(tasks.SaveTaskCalls).IsEqualTo(1);
        await Assert.That(tasks.SavedTaskIds.Single()).IsEqualTo("task-1");
        await Assert.That(responder.Answers.Count).IsEqualTo(1);
        await Assert.That(responder.Answers[0].Text).IsEqualTo("Статус задачи: Выполняется");
        await Assert.That(responder.TasksShown.Count).IsEqualTo(1);
        await Assert.That(responder.TasksShown[0].Task.Id).IsEqualTo("task-1");
    }

    public static async Task AssertInvalidStatusAnswersWithoutSavingTaskAsync()
    {
        var task = CreateTask("task-1", "Status task", DomainTaskStatus.NotReady);
        var tasks = new RecordingCallbackTaskOperations(task);
        var responder = new RecordingCallbackResponder();
        var handler = CreateHandler(tasks, responder, new RecordingUserStateStore());

        await handler.HandleCallbackAsync(AllowedCallback($"{TelegramCallbackHandler.SetStatusPrefix}Unknown_task-1"));

        await Assert.That(task.Status).IsEqualTo(DomainTaskStatus.NotReady);
        await Assert.That(tasks.SaveTaskCalls).IsEqualTo(0);
        await Assert.That(responder.Answers.Count).IsEqualTo(1);
        await Assert.That(responder.Answers[0].Text).IsEqualTo("Неизвестный статус задачи");
    }

    public static async Task AssertDeleteDeletesTaskAndTelegramMessageAsync()
    {
        var tasks = new RecordingCallbackTaskOperations(CreateTask("task-1", "Deleted task"));
        var responder = new RecordingCallbackResponder();
        var handler = CreateHandler(tasks, responder, new RecordingUserStateStore());

        await handler.HandleCallbackAsync(AllowedCallback($"{TelegramCallbackHandler.DeletePrefix}task-1"));

        await Assert.That(tasks.DeletedTaskIds.Count).IsEqualTo(1);
        await Assert.That(tasks.DeletedTaskIds[0]).IsEqualTo("task-1");
        await Assert.That(responder.Answers.Count).IsEqualTo(1);
        await Assert.That(responder.Answers[0].Text).IsEqualTo("Задача удалена");
        await Assert.That(responder.DeletedMessages.Count).IsEqualTo(1);
        await Assert.That(responder.DeletedMessages[0]).IsEqualTo((ChatId, MessageId));
    }

    public static async Task AssertCreateSubAndSiblingRecordUserStateAndAskForTitleAsync()
    {
        var responder = new RecordingCallbackResponder();
        var states = new RecordingUserStateStore();
        var handler = CreateHandler(new RecordingCallbackTaskOperations(), responder, states);

        await handler.HandleCallbackAsync(AllowedCallback($"{TelegramCallbackHandler.CreateSubPrefix}parent-1"));
        await handler.HandleCallbackAsync(AllowedCallback($"{TelegramCallbackHandler.CreateSibPrefix}sibling-1"));

        await Assert.That(states.States[AllowedUserId]).IsEqualTo($"{TelegramCallbackHandler.CreateSibPrefix}sibling-1");
        await Assert.That(states.SetValues).IsEquivalentTo([
            (AllowedUserId, $"{TelegramCallbackHandler.CreateSubPrefix}parent-1"),
            (AllowedUserId, $"{TelegramCallbackHandler.CreateSibPrefix}sibling-1")
        ]);
        await Assert.That(responder.Answers.Select(answer => answer.Text ?? string.Empty))
            .IsEquivalentTo([
                "Введите название подзадачи",
                "Введите название соседней задачи"
            ]);
    }

    public static async Task AssertRelationsShowExistingListsAndEmptyStatesAsync()
    {
        var task = CreateTask("task-1", "Task");
        var parent = CreateTask("parent-1", "Parent");
        var blocker = CreateTask("blocker-1", "Blocker");
        var child = CreateTask("child-1", "Child");
        var blocked = CreateTask("blocked-1", "Blocked");
        task.ApplyRelations([child], [parent], [blocked], [blocker]);

        var emptyTask = CreateTask("empty-1", "Empty");
        var tasks = new RecordingCallbackTaskOperations(task, parent, blocker, child, blocked, emptyTask);
        var responder = new RecordingCallbackResponder();
        var handler = CreateHandler(tasks, responder, new RecordingUserStateStore());

        await handler.HandleCallbackAsync(AllowedCallback($"{TelegramCallbackHandler.ParentsPrefix}task-1"));
        await handler.HandleCallbackAsync(AllowedCallback($"{TelegramCallbackHandler.BlockingPrefix}task-1"));
        await handler.HandleCallbackAsync(AllowedCallback($"{TelegramCallbackHandler.ContainingPrefix}task-1"));
        await handler.HandleCallbackAsync(AllowedCallback($"{TelegramCallbackHandler.BlockedPrefix}task-1"));
        await handler.HandleCallbackAsync(AllowedCallback($"{TelegramCallbackHandler.ParentsPrefix}empty-1"));

        await Assert.That(responder.TaskLists.Count).IsEqualTo(4);
        await Assert.That(responder.TaskLists[0].MessageText).IsEqualTo("Родительские задачи:\n");
        await Assert.That(responder.TaskLists[0].Results.Single().Id).IsEqualTo("parent-1");
        await Assert.That(responder.TaskLists[1].MessageText).IsEqualTo("Блокирующие задачи:\n");
        await Assert.That(responder.TaskLists[1].Results.Single().Id).IsEqualTo("blocker-1");
        await Assert.That(responder.TaskLists[2].MessageText).IsEqualTo("Дочерние задачи:\n");
        await Assert.That(responder.TaskLists[2].Results.Single().Id).IsEqualTo("child-1");
        await Assert.That(responder.TaskLists[3].MessageText).IsEqualTo("Блокируемые задачи:\n");
        await Assert.That(responder.TaskLists[3].Results.Single().Id).IsEqualTo("blocked-1");
        await Assert.That(responder.Answers.Last().Text).IsEqualTo("Нет родительских задач");
    }

    public static async Task<TelegramCallbackScenarioResult> ExecuteSupportedCallbackScenarioAsync()
    {
        await AssertUnauthorizedUserDoesNotSendTaskDataOrTouchTasksAsync();
        await AssertOpenShowsSelectedTaskForAllowedUserAsync();
        await AssertStatusUpdatesAndSavesSelectedTaskAsync();
        await AssertDeleteDeletesTaskAndTelegramMessageAsync();
        await AssertCreateSubAndSiblingRecordUserStateAndAskForTitleAsync();
        await AssertRelationsShowExistingListsAndEmptyStatesAsync();

        return new TelegramCallbackScenarioResult(
            UnauthorizedUserBlocked: true,
            OpenSupported: true,
            StatusChangeSupported: true,
            DeleteSupported: true,
            CreatePromptSupported: true,
            RelationListsSupported: true);
    }

    public static async Task AssertSupportedCallbackScenarioResultAsync(TelegramCallbackScenarioResult result)
    {
        await Assert.That(result.UnauthorizedUserBlocked).IsTrue();
        await Assert.That(result.OpenSupported).IsTrue();
        await Assert.That(result.StatusChangeSupported).IsTrue();
        await Assert.That(result.DeleteSupported).IsTrue();
        await Assert.That(result.CreatePromptSupported).IsTrue();
        await Assert.That(result.RelationListsSupported).IsTrue();
    }

    private static TelegramCallbackHandler CreateHandler(
        RecordingCallbackTaskOperations tasks,
        RecordingCallbackResponder responder,
        RecordingUserStateStore states)
    {
        return new TelegramCallbackHandler(
            new HashSet<long> { AllowedUserId },
            tasks,
            responder,
            states);
    }

    private static TelegramCallbackRequest AllowedCallback(string data)
    {
        return Callback(AllowedUserId, data);
    }

    private static TelegramCallbackRequest Callback(long userId, string data)
    {
        return new TelegramCallbackRequest(CallbackId, data, userId, "user", ChatId, MessageId);
    }

    private static TaskItemViewModel CreateTask(
        string id,
        string title,
        DomainTaskStatus status = DomainTaskStatus.NotReady)
    {
        return new TaskItemViewModel(
            new TaskItem
            {
                Id = id,
                Title = title,
                Description = "",
                Status = status,
                IsCanBeCompleted = true,
                CreatedDateTime = DateTimeOffset.UtcNow
            },
            new StubTaskStorage(),
            () => false);
    }

    private sealed class RecordingCallbackTaskOperations : ITelegramCallbackTaskOperations
    {
        private readonly Dictionary<string, TaskItemViewModel> _tasks;

        public RecordingCallbackTaskOperations(params TaskItemViewModel[] tasks)
        {
            _tasks = tasks.ToDictionary(task => task.Id);
        }

        public int GetTaskCalls { get; private set; }

        public int SaveTaskCalls { get; private set; }

        public int DeleteTaskCalls { get; private set; }

        public int TotalCalls => GetTaskCalls + SaveTaskCalls + DeleteTaskCalls;

        public List<string> SavedTaskIds { get; } = [];

        public List<string> DeletedTaskIds { get; } = [];

        public TaskItemViewModel? GetTask(string id)
        {
            GetTaskCalls++;
            return _tasks.GetValueOrDefault(id);
        }

        public Task SaveTask(TaskItemViewModel task)
        {
            SaveTaskCalls++;
            SavedTaskIds.Add(task.Id);
            _tasks[task.Id] = task;
            return Task.CompletedTask;
        }

        public void DeleteTask(string id)
        {
            DeleteTaskCalls++;
            DeletedTaskIds.Add(id);
            _tasks.Remove(id);
        }
    }

    private sealed class RecordingCallbackResponder : ITelegramCallbackResponder
    {
        public List<(string CallbackId, string? Text)> Answers { get; } = [];

        public List<(long ChatId, int MessageId)> DeletedMessages { get; } = [];

        public List<(long ChatId, string Text)> Messages { get; } = [];

        public List<(long ChatId, string Id)> TaskRequests { get; } = [];

        public List<(long ChatId, TaskItemViewModel Task)> TasksShown { get; } = [];

        public List<(long ChatId, string MessageText, List<TaskItemViewModel> Results)> TaskLists { get; } = [];

        public int TotalCalls => Answers.Count + DeletedMessages.Count + Messages.Count
            + TaskRequests.Count + TasksShown.Count + TaskLists.Count;

        public Task AnswerCallback(string callbackId, string? text = null)
        {
            Answers.Add((callbackId, text));
            return Task.CompletedTask;
        }

        public Task DeleteMessage(long chatId, int messageId)
        {
            DeletedMessages.Add((chatId, messageId));
            return Task.CompletedTask;
        }

        public Task SendMessage(long chatId, string text)
        {
            Messages.Add((chatId, text));
            return Task.CompletedTask;
        }

        public Task ShowTask(long chatId, string id)
        {
            TaskRequests.Add((chatId, id));
            return Task.CompletedTask;
        }

        public Task ShowTask(long chatId, TaskItemViewModel task)
        {
            TasksShown.Add((chatId, task));
            return Task.CompletedTask;
        }

        public Task ShowTaskList(IEnumerable<TaskItemViewModel> results, string messageText, long chatId)
        {
            TaskLists.Add((chatId, messageText, results.ToList()));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingUserStateStore : ITelegramUserStateStore
    {
        public Dictionary<long, string> States { get; } = [];

        public List<(long UserId, string State)> SetValues { get; } = [];

        public int SetCalls => SetValues.Count;

        public void Set(long userId, string state)
        {
            States[userId] = state;
            SetValues.Add((userId, state));
        }
    }

    private sealed class StubTaskStorage : ITaskStorage
    {
        public SourceCache<TaskItemViewModel, string> Tasks { get; } = new(task => task.Id);

        public ITaskRelationsIndex Relations => throw new NotSupportedException();

        public TaskTreeManager TaskTreeManager => throw new NotSupportedException();

        public event EventHandler<EventArgs>? Initiated
        {
            add { }
            remove { }
        }

        public Task Init() => Task.CompletedTask;

        public Task<TaskItemViewModel> Add(TaskItemViewModel? currentTask = null, bool isBlocked = false) =>
            throw new NotSupportedException();

        public Task<TaskItemViewModel> AddChild(TaskItemViewModel currentTask) =>
            throw new NotSupportedException();

        public Task<bool> Delete(TaskItemViewModel change, bool deleteInStorage = true) =>
            throw new NotSupportedException();

        public Task<bool> Delete(TaskItemViewModel change, TaskItemViewModel parent) =>
            throw new NotSupportedException();

        public Task<TaskItemViewModel> Update(TaskItemViewModel change) => Task.FromResult(change);

        public Task<TaskItemViewModel> Update(TaskItem change) =>
            throw new NotSupportedException();

        public Task<TaskItemViewModel> Clone(TaskItemViewModel change, params TaskItemViewModel[]? additionalParents) =>
            throw new NotSupportedException();

        public Task<bool> CopyInto(TaskItemViewModel change, TaskItemViewModel[]? additionalParents) =>
            throw new NotSupportedException();

        public Task<bool> MoveInto(TaskItemViewModel change, TaskItemViewModel[] additionalParents, TaskItemViewModel? currentTask) =>
            throw new NotSupportedException();

        public Task<bool> Unblock(TaskItemViewModel taskToUnblock, TaskItemViewModel blockingTask) =>
            throw new NotSupportedException();

        public Task<bool> Block(TaskItemViewModel change, TaskItemViewModel currentTask) =>
            throw new NotSupportedException();

        public Task RemoveParentChildConnection(TaskItemViewModel parent, TaskItemViewModel child) =>
            throw new NotSupportedException();
    }
}

internal sealed record TelegramCallbackScenarioResult(
    bool UnauthorizedUserBlocked,
    bool OpenSupported,
    bool StatusChangeSupported,
    bool DeleteSupported,
    bool CreatePromptSupported,
    bool RelationListsSupported);
