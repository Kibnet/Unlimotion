using Serilog;
using Unlimotion.ViewModel;

namespace Unlimotion.TelegramBot;

internal sealed class TelegramCommandHandler
{
    private readonly ISet<long> _allowedUsers;
    private readonly ITelegramCommandTaskQuery _tasks;
    private readonly ITelegramCommandResponder _responder;

    public TelegramCommandHandler(
        ISet<long> allowedUsers,
        ITelegramCommandTaskQuery tasks,
        ITelegramCommandResponder responder)
    {
        _allowedUsers = allowedUsers;
        _tasks = tasks;
        _responder = responder;
    }

    public bool HasAccess(long userId, string? userName)
    {
        if (_allowedUsers.Contains(userId))
        {
            return true;
        }

        Log.Warning("Пользователю {User} [id:{UserId}] запрещено обращаться к боту", userName, userId);
        return false;
    }

    public async Task HandleMessageAsync(TelegramCommandMessage message)
    {
        if (!HasAccess(message.UserId, message.UserName))
        {
            return;
        }

        if (message.Text.StartsWith("/start", StringComparison.Ordinal))
        {
            await _responder.SendMessage(message.ChatId, "Добро пожаловать! Введите /help для просмотра доступных команд.");
        }
        else if (message.Text.StartsWith("/help", StringComparison.Ordinal))
        {
            const string helpText = "/search [запрос] - поиск задач\n" +
                                    "/task [ID] - просмотр задачи\n" +
                                    "/root - корневые задачи";
            await _responder.SendMessage(message.ChatId, helpText);
        }
        else if (message.Text.StartsWith("/search", StringComparison.Ordinal))
        {
            var query = GetCommandArgument(message.Text);
            var results = _tasks.SearchTasks(query).ToList();
            if (results.Count > 0)
            {
                await _responder.ShowTaskList(results, "Результаты поиска:\n", message.ChatId);
            }
            else
            {
                await _responder.SendMessage(message.ChatId, "Задачи не найдены.");
            }
        }
        else if (message.Text.StartsWith("/task", StringComparison.Ordinal))
        {
            var id = GetCommandArgument(message.Text);
            await _responder.ShowTask(message.ChatId, id);
        }
        else if (message.Text.StartsWith("/root", StringComparison.Ordinal))
        {
            var results = _tasks.RootTasks().ToList();
            if (results.Count > 0)
            {
                await _responder.ShowTaskList(results, "Корневые задачи:\n", message.ChatId);
            }
            else
            {
                await _responder.SendMessage(message.ChatId, "Задачи не найдены.");
            }
        }
        else
        {
            await _responder.SendMessage(message.ChatId, "Неизвестная команда. Введите /help для просмотра доступных команд.");
        }
    }

    private static string GetCommandArgument(string text)
    {
        var separator = text.IndexOf(' ', StringComparison.Ordinal);
        return separator < 0 ? string.Empty : text[(separator + 1)..].Trim();
    }
}

internal readonly record struct TelegramCommandMessage(
    long UserId,
    string? UserName,
    long ChatId,
    string Text);

internal interface ITelegramCommandTaskQuery
{
    IEnumerable<TaskItemViewModel> SearchTasks(string query);
    TaskItemViewModel? GetTask(string id);
    IEnumerable<TaskItemViewModel> RootTasks();
}

internal interface ITelegramCommandResponder
{
    Task SendMessage(long chatId, string text);
    Task ShowTask(long chatId, string id);
    Task ShowTaskList(IEnumerable<TaskItemViewModel> results, string messageText, long chatId);
}
