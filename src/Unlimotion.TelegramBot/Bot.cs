using System.Reactive.Linq;
using DynamicData;
using Microsoft.Extensions.Configuration;
using Serilog;
using ServiceStack;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Unlimotion;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;
using TelegramUpdateType = Telegram.Bot.Types.Enums.UpdateType;
using Timer = System.Timers.Timer;

namespace Unlimotion.TelegramBot
{
    public class Bot
    {
        private const string Open = TelegramCallbackHandler.OpenPrefix;
        private const string CreateSub = TelegramCallbackHandler.CreateSubPrefix;
        private const string CreateSib = TelegramCallbackHandler.CreateSibPrefix;
        private const string SetStatus = TelegramCallbackHandler.SetStatusPrefix;
        private const string Delete = TelegramCallbackHandler.DeletePrefix;
        private const string Parents = TelegramCallbackHandler.ParentsPrefix;
        private const string Blocking = TelegramCallbackHandler.BlockingPrefix;
        private const string Containing = TelegramCallbackHandler.ContainingPrefix;
        private const string Blocked = TelegramCallbackHandler.BlockedPrefix;
        private static TelegramBotClient _client;
        private static TaskService _taskService;
        private static GitService _gitService;
        private static Dictionary<long, string> _userStates = new Dictionary<long, string>();
        private static IConfigurationRoot config;
        private static HashSet<long> AllowedUsers = new HashSet<long>();
        private static TelegramCommandHandler? _commandHandler;
        private static TelegramCallbackHandler? _callbackHandler;

        // Static dependency - set during initialization
        public static ITaskStorage? TaskStorageInstance { get; set; }

        public static async Task StartAsync(IConfigurationRoot configurationRoot)
        {
            config = configurationRoot;
            string token = config["BotToken"];
            AllowedUsers = config.GetSection("AllowedUsers").Get<HashSet<long>>() ?? new HashSet<long>();
            string repoPath = config.Get<GitSettings>("Git").RepositoryPath;

            _client = new TelegramBotClient(token);
            
            // Create task storage for the bot
            var fileStorage = new FileStorage(repoPath);
            var taskTreeManager = new TaskTreeManager(fileStorage);
            var unifiedStorage = new UnifiedTaskStorage(taskTreeManager);
            _taskService = new TaskService(unifiedStorage);
            _gitService = new GitService(config);
            _commandHandler = new TelegramCommandHandler(
                AllowedUsers,
                new TelegramTaskQueryAdapter(_taskService),
                new TelegramCommandResponder());
            _callbackHandler = new TelegramCallbackHandler(
                AllowedUsers,
                new TelegramCallbackTaskOperationsAdapter(_taskService),
                new TelegramCallbackResponder(),
                new DictionaryTelegramUserStateStore(_userStates));

            _gitService.CloneOrUpdateRepo();

            _client.StartReceiving(
                HandleUpdateAsync,
                HandlePollingErrorAsync,
                new ReceiverOptions
                {
                    AllowedUpdates = new[] { TelegramUpdateType.Message, TelegramUpdateType.CallbackQuery }
                });

            Log.Information("Бот запущен.");

            StartTimers(config);

            // Предотвращение завершения приложения
            await Task.Delay(-1);
        }

        private static void StartTimers(IConfigurationRoot configurationRoot)
        {
            var settings = configurationRoot.Get<GitSettings>("Git");
            var timerHandler = new TelegramGitTimerHandler(new GitServiceTelegramGitSyncOperations(_gitService));

            // Таймер для pull каждые 5 минут
            var pullTimer = new Timer(TimeSpan.FromSeconds(settings.PullIntervalSeconds).TotalMilliseconds);
            pullTimer.Elapsed += (sender, e) => timerHandler.PullLatestChanges();
            pullTimer.Start();

            // Таймер для commit/push каждую минуту
            var pushTimer = new Timer(TimeSpan.FromSeconds(settings.PushIntervalSeconds).TotalMilliseconds);
            pushTimer.Elapsed += (sender, e) => timerHandler.CommitAndPushChanges();
            pushTimer.Start();
        }

        private static Task HandleUpdateAsync(
            ITelegramBotClient botClient,
            Update update,
            CancellationToken cancellationToken)
        {
            return update.Type switch
            {
                TelegramUpdateType.Message when update.Message is not null => OnMessageReceived(update.Message),
                TelegramUpdateType.CallbackQuery when update.CallbackQuery is not null => OnCallbackQueryReceived(update.CallbackQuery),
                _ => Task.CompletedTask
            };
        }

        private static Task HandlePollingErrorAsync(
            ITelegramBotClient botClient,
            Exception exception,
            HandleErrorSource source,
            CancellationToken cancellationToken)
        {
            Log.Error(exception, "Ошибка Telegram polling ({Source})", source);
            return Task.CompletedTask;
        }

        private static async Task OnMessageReceived(Message message)
        {
            if (message.Type != MessageType.Text || message.From is null || message.Text is null) return;

            Log.Information("Получено сообщение от {User}: {Text}", message.From.Username, message.Text);

            try
            {
                long userId = message.From.Id;

                if (await CheckAccess(userId, message.From.Username)) return;

                if (_userStates.ContainsKey(userId))
                {
                    if (await HandleUserState(message))
                        return;
                }

                await CurrentCommandHandler().HandleMessageAsync(
                    new TelegramCommandMessage(userId, message.From.Username, message.Chat.Id, message.Text));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при обработке сообщения");
                await _client.SendMessage(message.Chat.Id, "Произошла ошибка при обработке вашего запроса.");
            }
        }

        private static async Task ShowTaskList(IEnumerable<TaskItemViewModel> results, string messageText, long chatId)
        {
            var flatButtons = new List<InlineKeyboardButton>();
            var index = 1;
            foreach (var task in results)
            {
                messageText += $"{index}.{(task.IsCanBeCompleted?"":"🔒")}{GetStatusEmoji(task.Status)} {task.Title}\n";
                flatButtons.Add(InlineKeyboardButton.WithCallbackData($"{index}", $"{Open}{task.Id}"));
                index++;
            }

            var buttons = new List<List<InlineKeyboardButton>>();
            var buttonCountPerRow = 5;
            var inlineKeyboardMarkup = new List<InlineKeyboardButton>();
            foreach (var button in flatButtons)
            {
                if (inlineKeyboardMarkup.Count<=buttonCountPerRow)
                {
                    inlineKeyboardMarkup.Add(button);
                    if (inlineKeyboardMarkup.Count == 1)
                    {
                        buttons.Add(inlineKeyboardMarkup);
                    }
                }
                else
                {
                    inlineKeyboardMarkup = new List<InlineKeyboardButton>();
                }
            }

            var keyboard = new InlineKeyboardMarkup(buttons);
            await _client.SendMessage(chatId, messageText,
                replyMarkup: keyboard);
        }

        private static async Task<bool> HandleUserState(Message message)
        {
            if (message.From is null || message.Text is null)
            {
                return false;
            }

            long userId = message.From.Id;
            string state = _userStates[userId];
            if (state.StartsWith(CreateSub))
            {
                string parentId = state.SplitOnFirst('_')[1];
                string title = message.Text;

                var parentTask = _taskService.GetTask(parentId);
                if (parentTask == null)
                {
                    return false;
                }
                
                var newTask = new TaskItem
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = title,
                    Description = "",
                };
                var taskStorage = TaskStorageInstance;
                if (taskStorage == null)
                {
                    await _client.SendMessage(message.Chat.Id, "Хранилище задач не инициализировано.");
                    return true;
                }
                var newTaskViewModel = new TaskItemViewModel(newTask, taskStorage);
                await newTaskViewModel.SaveItemCommand.Execute();

                parentTask.Contains.Add(newTask.Id);

                //_gitService.CommitAndPushChanges($"Создана подзадача {title}");

                await _client.SendMessage(message.Chat.Id, $"Подзадача '{title}' создана.");
                _userStates.Remove(userId);
                await ShowTask(message.Chat.Id, newTaskViewModel);
                return true;
            }

            if (state.StartsWith($"{CreateSib}"))
            {
                string siblingId = state.SplitOnFirst('_')[1];
                string title = message.Text;
                var siblingTask = _taskService.GetTask(siblingId);

                var newTask = new TaskItem
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = title,
                    Description = "",
                };
                var taskStorage = TaskStorageInstance;
                if (taskStorage == null)
                {
                    await _client.SendMessage(message.Chat.Id, "Хранилище задач не инициализировано.");
                    return true;
                }
                var newTaskViewModel = new TaskItemViewModel(newTask, taskStorage);
                await newTaskViewModel.SaveItemCommand.Execute();
                if (siblingTask is { ParentsTasks.Count: > 0 })
                {
                    siblingTask.ParentsTasks.First().Contains.Add(newTaskViewModel.Id);
                }

                taskStorage.Tasks.AddOrUpdate(newTaskViewModel);

                //_gitService.CommitAndPushChanges($"Создана соседняя задача {title}");

                await _client.SendMessage(message.Chat.Id, $"Соседняя задача '{title}' создана.");
                _userStates.Remove(userId);
                await ShowTask(message.Chat.Id, newTaskViewModel);
                return true;
            }

            return false;
        }

        private static async Task ShowTask(long chatId, string id)
        {
            var task = _taskService.GetTask(id);
            if (task != null)
            {
                await ShowTask(chatId, task);
            }
            else
            {
                await _client.SendMessage(chatId, "Задача не найдена");
            }
        }

        private static async Task ShowTask(long chatId, TaskItemViewModel task)
        {
            long userId = chatId;
            _userStates[userId] = task.Id;
            string response = $"{(task.IsCanBeCompleted?"":"🔒")}{GetStatusEmoji(task.Status)} {(task.Wanted?"*":"")}{task.Title}{(task.Wanted?"*":"")}\n" +
                              $"{GetStatusEmojiAndText(task.Status)}\n" +
                              $"{GetStatusEmodji(task.Wanted)} Wanted | Importance {task.Importance}\nId {task.Id}\n" +
                              $"{task.Description}\n" +
                              $"Created {task.CreatedDateTime:yyyy.MM.dd HH:mm} Updated {task.UpdatedDateTime:yyyy.MM.dd HH:mm} Unlocked {task.UnlockedDateTime:yyyy.MM.dd HH:mm} Completed {task.CompletedDateTime:yyyy.MM.dd HH:mm} Archive {task.ArchiveDateTime:yyyy.MM.dd HH:mm}\n" +
                              $"Begin {task.PlannedBeginDateTime:yyyy.MM.dd} Duration {TimeSpanStringConverter.SpanToString(task.PlannedDuration)} End {task.PlannedEndDateTime:yyyy.MM.dd}\n"
                              ;

            var inlineKeyboardButtons = new List<InlineKeyboardButton[]>
            {
                new[]
                {
                    CreateStatusButton(task, DomainTaskStatus.NotReady),
                    CreateStatusButton(task, DomainTaskStatus.Prepared),
                },
                new[]
                {
                    CreateStatusButton(task, DomainTaskStatus.InProgress),
                    CreateStatusButton(task, DomainTaskStatus.Completed),
                    CreateStatusButton(task, DomainTaskStatus.Archived),
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("Удалить задачу", $"{Delete}{task.Id}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("Создать подзадачу", $"{CreateSub}{task.Id}"),
                    InlineKeyboardButton.WithCallbackData("Создать соседнюю задачу", $"{CreateSib}{task.Id}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData($"Parents {task.Parents.Count}", $"{Parents}{task.Id}"),
                    InlineKeyboardButton.WithCallbackData($"Blocking {task.BlockedBy.Count}", $"{Blocking}{task.Id}"),
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData($"Containing {task.Contains.Count}", $"{Containing}{task.Id}"),
                    InlineKeyboardButton.WithCallbackData($"Blocked {task.Blocks.Count}", $"{Blocked}{task.Id}"),
                }
            };
            //var getParents = task.ParentsTasks.Select(parent => new[]
            //{
            //    InlineKeyboardButton.WithCallbackData($"Перейти к родителю {parent.Title}", $"parent_{parent.Id ?? "null"}")
            //});
            //inlineKeyboardButtons.AddRange(getParents);
            var keyboard = new InlineKeyboardMarkup(inlineKeyboardButtons);
            await _client.SendMessage(chatId, response, ParseMode.Markdown, replyMarkup: keyboard);
        }

        private static InlineKeyboardButton CreateStatusButton(TaskItemViewModel task, DomainTaskStatus status)
        {
            var currentMarker = task.Status == status ? "• " : string.Empty;
            return InlineKeyboardButton.WithCallbackData(
                $"{currentMarker}{GetStatusEmoji(status)} {GetStatusText(status)}",
                $"{SetStatus}{status}_{task.Id}");
        }

        private static string GetStatusEmojiAndText(DomainTaskStatus status)
        {
            return $"{GetStatusEmoji(status)} {GetStatusText(status)}";
        }

        private static string GetStatusText(DomainTaskStatus status)
        {
            string text = status switch
            {
                DomainTaskStatus.NotReady => "Не готово",
                DomainTaskStatus.Prepared => "Подготовлено",
                DomainTaskStatus.InProgress => "Выполняется",
                DomainTaskStatus.Completed => "Выполнено",
                DomainTaskStatus.Archived => "Архивировано",
                _ => status.ToString()
            };
            return text;
        }

        private static string GetStatusEmoji(DomainTaskStatus status)
        {
            string emoji = status switch
            {
                DomainTaskStatus.NotReady => "⬜",
                DomainTaskStatus.Prepared => "❗",
                DomainTaskStatus.InProgress => "▶️",
                DomainTaskStatus.Completed => "✅",
                DomainTaskStatus.Archived => "🗄️",
                _ => "⬜"
            };
            return emoji;
        }

        private static string GetStatusEmodji(bool? value)
        {
            string status = value switch
            {
                true => "✅",
                false => "\ud83d\udfe9",
                null => "🗄️"
            };
            return status;
        }

        private static async Task OnCallbackQueryReceived(CallbackQuery callbackQuery)
        {
            var callbackData = callbackQuery.Data;
            if (callbackData is null || callbackQuery.Message is null)
            {
                await _client.AnswerCallbackQuery(callbackQuery.Id);
                return;
            }

            var chatId = callbackQuery.Message.Chat.Id;
            var messageId = callbackQuery.Message.MessageId;
            var userId = callbackQuery.From.Id;
            Log.Information("Обработка колбэка от {User}: {Data}", callbackQuery.From.Username, callbackData);

            try
            {
                await CurrentCallbackHandler().HandleCallbackAsync(
                    new TelegramCallbackRequest(
                        callbackQuery.Id,
                        callbackData,
                        userId,
                        callbackQuery.From.Username,
                        chatId,
                        messageId));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при обработке колбэка");
                await _client.SendMessage(chatId, "Произошла ошибка при обработке вашего запроса.");
            }
        }

        private static async ValueTask<bool> CheckAccess(long userId, string? userName)
        {
            return !CurrentCommandHandler().HasAccess(userId, userName);
        }

        private static TelegramCommandHandler CurrentCommandHandler()
        {
            return _commandHandler ??= new TelegramCommandHandler(
                AllowedUsers,
                new TelegramTaskQueryAdapter(_taskService),
                new TelegramCommandResponder());
        }

        private static TelegramCallbackHandler CurrentCallbackHandler()
        {
            return _callbackHandler ??= new TelegramCallbackHandler(
                AllowedUsers,
                new TelegramCallbackTaskOperationsAdapter(_taskService),
                new TelegramCallbackResponder(),
                new DictionaryTelegramUserStateStore(_userStates));
        }

        private sealed class TelegramTaskQueryAdapter(TaskService taskService) : ITelegramCommandTaskQuery
        {
            public IEnumerable<TaskItemViewModel> SearchTasks(string query)
            {
                return taskService.SearchTasks(query);
            }

            public TaskItemViewModel? GetTask(string id)
            {
                return taskService.GetTask(id);
            }

            public IEnumerable<TaskItemViewModel> RootTasks()
            {
                return taskService.RootTasks();
            }
        }

        private sealed class TelegramCommandResponder : ITelegramCommandResponder
        {
            public Task SendMessage(long chatId, string text)
            {
                return _client.SendMessage(chatId, text);
            }

            public Task ShowTask(long chatId, string id)
            {
                return Bot.ShowTask(chatId, id);
            }

            public Task ShowTaskList(IEnumerable<TaskItemViewModel> results, string messageText, long chatId)
            {
                return Bot.ShowTaskList(results, messageText, chatId);
            }
        }

        private sealed class TelegramCallbackTaskOperationsAdapter(TaskService taskService)
            : ITelegramCallbackTaskOperations
        {
            public TaskItemViewModel? GetTask(string id)
            {
                return taskService.GetTask(id);
            }

            public async Task SaveTask(TaskItemViewModel task)
            {
                await task.SaveItemCommand.Execute();
            }

            public void DeleteTask(string id)
            {
                taskService.DeleteTask(id);
            }
        }

        private sealed class TelegramCallbackResponder : ITelegramCallbackResponder
        {
            public Task AnswerCallback(string callbackId, string? text = null)
            {
                return text is null
                    ? _client.AnswerCallbackQuery(callbackId)
                    : _client.AnswerCallbackQuery(callbackId, text);
            }

            public Task DeleteMessage(long chatId, int messageId)
            {
                return _client.DeleteMessage(chatId, messageId);
            }

            public Task SendMessage(long chatId, string text)
            {
                return _client.SendMessage(chatId, text);
            }

            public Task ShowTask(long chatId, string id)
            {
                return Bot.ShowTask(chatId, id);
            }

            public Task ShowTask(long chatId, TaskItemViewModel task)
            {
                return Bot.ShowTask(chatId, task);
            }

            public Task ShowTaskList(IEnumerable<TaskItemViewModel> results, string messageText, long chatId)
            {
                return Bot.ShowTaskList(results, messageText, chatId);
            }
        }

        private sealed class DictionaryTelegramUserStateStore(Dictionary<long, string> userStates)
            : ITelegramUserStateStore
        {
            public void Set(long userId, string state)
            {
                userStates[userId] = state;
            }
        }
    }
}
