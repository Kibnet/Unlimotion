using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DynamicData;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.TelegramBot;
using Unlimotion.ViewModel;

namespace Unlimotion.Test;

public class TelegramBotCommandAuthorizationTests
{
    private const long AllowedUserId = 42;
    private const long UnauthorizedUserId = 404;
    private const long ChatId = 1001;

    [Test]
    public async Task TelegramBotCommand_UnauthorizedUser_DoesNotSendMessagesOrQueryTasks()
    {
        var query = new RecordingTaskQuery();
        var responder = new RecordingResponder();
        var handler = CreateHandler(query, responder);

        await handler.HandleMessageAsync(new TelegramCommandMessage(
            UnauthorizedUserId,
            "intruder",
            ChatId,
            "/search roadmap"));

        await Assert.That(query.TotalCalls).IsEqualTo(0);
        await Assert.That(responder.TotalCalls).IsEqualTo(0);
    }

    [Test]
    public async Task TelegramBotCommand_StartAndHelp_ReturnRussianCommandTextForAllowedUser()
    {
        var responder = new RecordingResponder();
        var handler = CreateHandler(new RecordingTaskQuery(), responder);

        await handler.HandleMessageAsync(AllowedMessage("/start"));
        await handler.HandleMessageAsync(AllowedMessage("/help"));

        await Assert.That(responder.Messages.Count).IsEqualTo(2);
        await Assert.That(responder.Messages[0].Text)
            .IsEqualTo("Добро пожаловать! Введите /help для просмотра доступных команд.");
        await Assert.That(responder.Messages[1].Text)
            .IsEqualTo("/search [запрос] - поиск задач\n/task [ID] - просмотр задачи\n/root - корневые задачи");
    }

    [Test]
    public async Task TelegramBotCommand_SearchWithResults_ShowsTaskListForAllowedUser()
    {
        var query = new RecordingTaskQuery
        {
            SearchResults = [CreateTask("task-1", "Roadmap")]
        };
        var responder = new RecordingResponder();
        var handler = CreateHandler(query, responder);

        await handler.HandleMessageAsync(AllowedMessage("/search road"));

        await Assert.That(query.SearchCalls).IsEqualTo(1);
        await Assert.That(query.LastSearchQuery).IsEqualTo("road");
        await Assert.That(responder.TaskLists.Count).IsEqualTo(1);
        await Assert.That(responder.TaskLists[0].MessageText).IsEqualTo("Результаты поиска:\n");
        await Assert.That(responder.TaskLists[0].Results.Single().Title).IsEqualTo("Roadmap");
        await Assert.That(responder.Messages).IsEmpty();
    }

    [Test]
    public async Task TelegramBotCommand_SearchWithoutResults_SendsNotFoundMessageForAllowedUser()
    {
        var responder = new RecordingResponder();
        var handler = CreateHandler(new RecordingTaskQuery(), responder);

        await handler.HandleMessageAsync(AllowedMessage("/search missing"));

        await Assert.That(responder.Messages.Count).IsEqualTo(1);
        await Assert.That(responder.Messages[0].Text).IsEqualTo("Задачи не найдены.");
        await Assert.That(responder.TaskLists).IsEmpty();
    }

    [Test]
    public async Task TelegramBotCommand_TaskCommand_RoutesTaskIdToResponderForAllowedUser()
    {
        var responder = new RecordingResponder();
        var handler = CreateHandler(new RecordingTaskQuery(), responder);

        await handler.HandleMessageAsync(AllowedMessage("/task task-42"));

        await Assert.That(responder.TaskRequests.Count).IsEqualTo(1);
        await Assert.That(responder.TaskRequests[0]).IsEqualTo((ChatId, "task-42"));
    }

    [Test]
    public async Task TelegramBotCommand_RootWithResults_ShowsRootTaskListForAllowedUser()
    {
        var query = new RecordingTaskQuery
        {
            RootResults = [CreateTask("root-1", "Root task")]
        };
        var responder = new RecordingResponder();
        var handler = CreateHandler(query, responder);

        await handler.HandleMessageAsync(AllowedMessage("/root"));

        await Assert.That(query.RootCalls).IsEqualTo(1);
        await Assert.That(responder.TaskLists.Count).IsEqualTo(1);
        await Assert.That(responder.TaskLists[0].MessageText).IsEqualTo("Корневые задачи:\n");
        await Assert.That(responder.TaskLists[0].Results.Single().Title).IsEqualTo("Root task");
    }

    [Test]
    public async Task TelegramBotCommand_UnknownCommand_ReturnsHelpHintForAllowedUser()
    {
        var responder = new RecordingResponder();
        var handler = CreateHandler(new RecordingTaskQuery(), responder);

        await handler.HandleMessageAsync(AllowedMessage("/unknown"));

        await Assert.That(responder.Messages.Count).IsEqualTo(1);
        await Assert.That(responder.Messages[0].Text)
            .IsEqualTo("Неизвестная команда. Введите /help для просмотра доступных команд.");
    }

    private static TelegramCommandHandler CreateHandler(
        RecordingTaskQuery query,
        RecordingResponder responder)
    {
        return new TelegramCommandHandler(
            new HashSet<long> { AllowedUserId },
            query,
            responder);
    }

    private static TelegramCommandMessage AllowedMessage(string text)
    {
        return new TelegramCommandMessage(AllowedUserId, "allowed", ChatId, text);
    }

    private static TaskItemViewModel CreateTask(string id, string title)
    {
        return new TaskItemViewModel(
            new TaskItem
            {
                Id = id,
                Title = title,
                IsCanBeCompleted = true,
                CreatedDateTime = DateTimeOffset.UtcNow
            },
            new StubTaskStorage(),
            () => false);
    }

    private sealed class RecordingTaskQuery : ITelegramCommandTaskQuery
    {
        public List<TaskItemViewModel> SearchResults { get; init; } = [];
        public List<TaskItemViewModel> RootResults { get; init; } = [];
        public int SearchCalls { get; private set; }
        public int RootCalls { get; private set; }
        public int GetTaskCalls { get; private set; }
        public int TotalCalls => SearchCalls + RootCalls + GetTaskCalls;
        public string? LastSearchQuery { get; private set; }

        public IEnumerable<TaskItemViewModel> SearchTasks(string query)
        {
            SearchCalls++;
            LastSearchQuery = query;
            return SearchResults;
        }

        public TaskItemViewModel? GetTask(string id)
        {
            GetTaskCalls++;
            return null;
        }

        public IEnumerable<TaskItemViewModel> RootTasks()
        {
            RootCalls++;
            return RootResults;
        }
    }

    private sealed class RecordingResponder : ITelegramCommandResponder
    {
        public List<(long ChatId, string Text)> Messages { get; } = [];
        public List<(long ChatId, string MessageText, List<TaskItemViewModel> Results)> TaskLists { get; } = [];
        public List<(long ChatId, string Id)> TaskRequests { get; } = [];
        public int TotalCalls => Messages.Count + TaskLists.Count + TaskRequests.Count;

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

        public Task ShowTaskList(IEnumerable<TaskItemViewModel> results, string messageText, long chatId)
        {
            TaskLists.Add((chatId, messageText, results.ToList()));
            return Task.CompletedTask;
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
