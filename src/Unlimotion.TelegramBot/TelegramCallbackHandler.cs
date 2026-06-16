using Serilog;
using Unlimotion.ViewModel;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.TelegramBot;

internal sealed class TelegramCallbackHandler
{
    internal const string OpenPrefix = "open_";
    internal const string CreateSubPrefix = "createSub_";
    internal const string CreateSibPrefix = "createSib_";
    internal const string SetStatusPrefix = "status_";
    internal const string DeletePrefix = "delete_";
    internal const string ParentsPrefix = "parents_";
    internal const string BlockingPrefix = "blocking_";
    internal const string ContainingPrefix = "containing_";
    internal const string BlockedPrefix = "blocked_";

    private readonly ISet<long> _allowedUsers;
    private readonly ITelegramCallbackTaskOperations _tasks;
    private readonly ITelegramCallbackResponder _responder;
    private readonly ITelegramUserStateStore _userStates;

    public TelegramCallbackHandler(
        ISet<long> allowedUsers,
        ITelegramCallbackTaskOperations tasks,
        ITelegramCallbackResponder responder,
        ITelegramUserStateStore userStates)
    {
        _allowedUsers = allowedUsers;
        _tasks = tasks;
        _responder = responder;
        _userStates = userStates;
    }

    public async Task HandleCallbackAsync(TelegramCallbackRequest request)
    {
        if (!HasAccess(request.UserId, request.UserName))
        {
            return;
        }

        if (request.Data.StartsWith(SetStatusPrefix, StringComparison.Ordinal))
        {
            await HandleSetStatusAsync(request);
        }
        else if (request.Data.StartsWith(DeletePrefix, StringComparison.Ordinal))
        {
            await HandleDeleteAsync(request);
        }
        else if (request.Data.StartsWith(CreateSubPrefix, StringComparison.Ordinal))
        {
            await SetCreateStateAsync(request, CreateSubPrefix, "Введите название подзадачи");
        }
        else if (request.Data.StartsWith(CreateSibPrefix, StringComparison.Ordinal))
        {
            await SetCreateStateAsync(request, CreateSibPrefix, "Введите название соседней задачи");
        }
        else if (request.Data.StartsWith("parent_", StringComparison.Ordinal))
        {
            await HandleLegacyParentAsync(request);
        }
        else if (request.Data.StartsWith(ParentsPrefix, StringComparison.Ordinal))
        {
            await HandleRelationListAsync(
                request,
                ParentsPrefix,
                task => task.ParentsTasks,
                "Родительские задачи:\n",
                "Нет родительских задач");
        }
        else if (request.Data.StartsWith(BlockingPrefix, StringComparison.Ordinal))
        {
            await HandleRelationListAsync(
                request,
                BlockingPrefix,
                task => task.BlockedByTasks,
                "Блокирующие задачи:\n",
                "Нет блокирующих задач");
        }
        else if (request.Data.StartsWith(ContainingPrefix, StringComparison.Ordinal))
        {
            await HandleRelationListAsync(
                request,
                ContainingPrefix,
                task => task.ContainsTasks,
                "Дочерние задачи:\n",
                "Нет дочерних задач");
        }
        else if (request.Data.StartsWith(BlockedPrefix, StringComparison.Ordinal))
        {
            await HandleRelationListAsync(
                request,
                BlockedPrefix,
                task => task.BlocksTasks,
                "Блокируемые задачи:\n",
                "Нет блокируемых задач");
        }
        else if (request.Data.StartsWith(OpenPrefix, StringComparison.Ordinal))
        {
            var taskId = PayloadAfter(request.Data, OpenPrefix);
            await _responder.ShowTask(request.ChatId, taskId);
            await _responder.AnswerCallback(request.CallbackId);
        }
    }

    private bool HasAccess(long userId, string? userName)
    {
        if (_allowedUsers.Contains(userId))
        {
            return true;
        }

        Log.Warning("Пользователю {User} [id:{UserId}] запрещено обращаться к боту", userName, userId);
        return false;
    }

    private async Task HandleSetStatusAsync(TelegramCallbackRequest request)
    {
        var payload = PayloadAfter(request.Data, SetStatusPrefix);
        if (!TrySplitOnFirst(payload, '_', out var statusText, out var taskId)
            || !Enum.TryParse<DomainTaskStatus>(statusText, out var status))
        {
            await _responder.AnswerCallback(request.CallbackId, "Неизвестный статус задачи");
            return;
        }

        var task = _tasks.GetTask(taskId);
        if (task == null)
        {
            return;
        }

        task.Status = status;
        await _tasks.SaveTask(task);

        var refreshedTask = _tasks.GetTask(taskId) ?? task;
        await _responder.AnswerCallback(request.CallbackId, $"Статус задачи: {GetStatusText(refreshedTask.Status)}");
        await _responder.ShowTask(request.ChatId, refreshedTask);
    }

    private async Task HandleDeleteAsync(TelegramCallbackRequest request)
    {
        var taskId = PayloadAfter(request.Data, DeletePrefix);
        _tasks.DeleteTask(taskId);
        await _responder.AnswerCallback(request.CallbackId, "Задача удалена");
        await _responder.DeleteMessage(request.ChatId, request.MessageId);
    }

    private async Task SetCreateStateAsync(
        TelegramCallbackRequest request,
        string prefix,
        string prompt)
    {
        var taskId = PayloadAfter(request.Data, prefix);
        _userStates.Set(request.UserId, $"{prefix}{taskId}");
        await _responder.AnswerCallback(request.CallbackId, prompt);
    }

    private async Task HandleLegacyParentAsync(TelegramCallbackRequest request)
    {
        var parentId = PayloadAfter(request.Data, "parent_");
        if (parentId == "null")
        {
            await _responder.AnswerCallback(request.CallbackId, "У данной задачи нет родителя");
            return;
        }

        await _responder.ShowTask(request.ChatId, parentId);
        await _responder.AnswerCallback(request.CallbackId);
    }

    private async Task HandleRelationListAsync(
        TelegramCallbackRequest request,
        string prefix,
        Func<TaskItemViewModel, IReadOnlyCollection<TaskItemViewModel>> relationSelector,
        string listTitle,
        string emptyText)
    {
        var taskId = PayloadAfter(request.Data, prefix);
        var task = _tasks.GetTask(taskId);
        var relatedTasks = task == null ? [] : relationSelector(task).ToList();

        if (relatedTasks.Count > 0)
        {
            await _responder.ShowTaskList(relatedTasks, listTitle, request.ChatId);
            await _responder.AnswerCallback(request.CallbackId);
            return;
        }

        await _responder.AnswerCallback(request.CallbackId, emptyText);
    }

    internal static string GetStatusText(DomainTaskStatus status)
    {
        return status switch
        {
            DomainTaskStatus.NotReady => "Не готово",
            DomainTaskStatus.Prepared => "Подготовлено",
            DomainTaskStatus.InProgress => "Выполняется",
            DomainTaskStatus.Completed => "Выполнено",
            DomainTaskStatus.Archived => "Архивировано",
            _ => status.ToString()
        };
    }

    private static string PayloadAfter(string data, string prefix)
    {
        return data[prefix.Length..];
    }

    private static bool TrySplitOnFirst(
        string value,
        char separator,
        out string first,
        out string second)
    {
        var index = value.IndexOf(separator, StringComparison.Ordinal);
        if (index < 0)
        {
            first = value;
            second = string.Empty;
            return false;
        }

        first = value[..index];
        second = value[(index + 1)..];
        return true;
    }
}

internal readonly record struct TelegramCallbackRequest(
    string CallbackId,
    string Data,
    long UserId,
    string? UserName,
    long ChatId,
    int MessageId);

internal interface ITelegramCallbackTaskOperations
{
    TaskItemViewModel? GetTask(string id);
    Task SaveTask(TaskItemViewModel task);
    void DeleteTask(string id);
}

internal interface ITelegramCallbackResponder
{
    Task AnswerCallback(string callbackId, string? text = null);
    Task DeleteMessage(long chatId, int messageId);
    Task SendMessage(long chatId, string text);
    Task ShowTask(long chatId, string id);
    Task ShowTask(long chatId, TaskItemViewModel task);
    Task ShowTaskList(IEnumerable<TaskItemViewModel> results, string messageText, long chatId);
}

internal interface ITelegramUserStateStore
{
    void Set(long userId, string state);
}
