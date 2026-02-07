using System.Reactive.Linq;
using DynamicData;
using Microsoft.Extensions.Configuration;
using Serilog;
using ServiceStack;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Unlimotion;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using Timer = System.Timers.Timer;

namespace Unlimotion.TelegramBot
{
    public class Bot
    {
        private const string Open = "open_";
        private const string CreateSub = "createSub_";
        private const string CreateSib = "createSib_";
        private const string Toggle = "toggle_";
        private const string Delete = "delete_";
        private const string Parents = "parents_";
        private const string Blocking = "blocking_";
        private const string Containing = "containing_";
        private const string Blocked = "blocked_";
        private static TelegramBotClient _client;
        private static TaskService _taskService;
        private static GitService _gitService;
        private static Dictionary<long, string> _userStates = new Dictionary<long, string>();
        private static IConfigurationRoot config;
        private static HashSet<long> AllowedUsers = new HashSet<long>();

        // Static dependency - set during initialization
        public static ITaskStorage? TaskStorageInstance { get; set; }

        public static async Task StartAsync(IConfigurationRoot configurationRoot)
        {
            config = configurationRoot;
            string token = config["BotToken"];
            AllowedUsers = config.GetSection("AllowedUsers").Get<HashSet<long>>();
            string repoPath = config.Get<GitSettings>("Git").RepositoryPath;

            _client = new TelegramBotClient(token);
            
            // Create task storage for the bot
            var fileStorage = new FileStorage(repoPath);
            var taskTreeManager = new TaskTreeManager(fileStorage);
            var unifiedStorage = new UnifiedTaskStorage(taskTreeManager);
            _taskService = new TaskService(unifiedStorage);
            _gitService = new GitService(config);

            _gitService.CloneOrUpdateRepo();

            _client.OnMessage += OnMessageReceived;
            _client.OnCallbackQuery += OnCallbackQueryReceived;

            _client.StartReceiving();

            Log.Information("Бот запущен.");

            StartTimers(config);

            // Предотвращение завершения приложения
            await Task.Delay(-1);
        }

        private static void StartTimers(IConfigurationRoot configurationRoot)
        {
            var settings = configurationRoot.Get<GitSettings>("Git");
            // Таймер для pull каждые 5 минут
            var pullTimer = new Timer(TimeSpan.FromSeconds(settings.PullIntervalSeconds).TotalMilliseconds);
            pullTimer.Elapsed += (sender, e) =>
            {
                Log.Information("Выполняется автоматический pull изменений из репозитория.");
                _gitService.PullLatestChanges();
            };
            pullTimer.Start();

            // Таймер для commit/push каждую минуту
            var pushTimer = new Timer(TimeSpan.FromSeconds(settings.PushIntervalSeconds).TotalMilliseconds);
            pushTimer.Elapsed += (sender, e) =>
            {
                Log.Information("Выполняется автоматический commit/push изменений в репозиторий.");
                _gitService.CommitAndPushChanges("Автоматический коммит изменений.");
            };
            pushTimer.Start();
        }

        private static async void OnMessageReceived(object sender, MessageEventArgs e)
        {
            var message = e.Message;
            if (message.Type != MessageType.Text) return;

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

                if (message.Text.StartsWith("/start"))
                {
                    await _client.SendTextMessageAsync(message.Chat.Id, "Добро пожаловать! Введите /help для просмотра доступных команд.");
                }
                else if (message.Text.StartsWith("/help"))
                {
                    string helpText = "/search [запрос] - поиск задач\n" +
                                      "/task [ID] - просмотр задачи\n" +
                                      "/root - корневые задачи";
                    await _client.SendTextMessageAsync(message.Chat.Id, helpText);
                }
                else if (message.Text.StartsWith("/search"))
                {
                    string query = message.Text.SplitOnFirst(' ')[1].Trim();
                    var results = _taskService.SearchTasks(query);
                    if (results.Count > 0)
                    {
                        await ShowTaskList(results, "Результаты поиска:\n", message.Chat.Id);
                    }
                    else
                    {
                        await _client.SendTextMessageAsync(message.Chat.Id, "Задачи не найдены.");
                    }
                }
                else if (message.Text.StartsWith("/task"))
                {
                    string id = message.Text.SplitOnFirst(' ')[1].Trim();
                    await ShowTask(message.Chat.Id, id);
                }
                else if (message.Text.StartsWith("/root"))
                {
                    var results = _taskService.RootTasks().ToList();
                    if (results.Any())
                    {
                        await ShowTaskList(results, "Корневые задачи:\n", message.Chat.Id);
                    }
                    else
                    {
                        await _client.SendTextMessageAsync(message.Chat.Id, "Задачи не найдены.");
                    }
                }
                else
                {
                    await _client.SendTextMessageAsync(message.Chat.Id, "Неизвестная команда. Введите /help для просмотра доступных команд.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при обработке сообщения");
                await _client.SendTextMessageAsync(message.Chat.Id, "Произошла ошибка при обработке вашего запроса.");
            }
        }

        private static async Task ShowTaskList(IEnumerable<TaskItemViewModel> results, string messageText, long chatId)
        {
            var flatButtons = new List<InlineKeyboardButton>();
            var index = 1;
            foreach (var task in results)
            {
                messageText += $"{index}.{(task.IsCanBeCompleted?"":"🔒")}{GetStatusEmodji(task.IsCompleted)} {task.Title}\n";
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
            await _client.SendTextMessageAsync(chatId, messageText,
                replyMarkup: keyboard);
        }

        private static async Task<bool> HandleUserState(Message message)
        {
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
                    await _client.SendTextMessageAsync(message.Chat.Id, "Хранилище задач не инициализировано.");
                    return true;
                }
                var newTaskViewModel = new TaskItemViewModel(newTask, taskStorage);
                await newTaskViewModel.SaveItemCommand.Execute();

                parentTask.Contains.Add(newTask.Id);

                //_gitService.CommitAndPushChanges($"Создана подзадача {title}");

                await _client.SendTextMessageAsync(message.Chat.Id, $"Подзадача '{title}' создана.");
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
                    await _client.SendTextMessageAsync(message.Chat.Id, "Хранилище задач не инициализировано.");
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

                await _client.SendTextMessageAsync(message.Chat.Id, $"Соседняя задача '{title}' создана.");
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
                await _client.SendTextMessageAsync(chatId, "Задача не найдена");
            }
        }

        private static async Task ShowTask(long chatId, TaskItemViewModel task)
        {
            long userId = chatId;
            _userStates[userId] = task.Id;
            string response = $"{(task.IsCanBeCompleted?"":"🔒")}{GetStatusEmodji(task.IsCompleted)} {(task.Wanted?"*":"")}{task.Title}{(task.Wanted?"*":"")}\n" +
                              $"{GetStatusEmodji(task.Wanted)} Wanted | Importance {task.Importance}\nId {task.Id}\n" +
                              $"{task.Description}\n" +
                              $"Created {task.CreatedDateTime:yyyy.MM.dd HH:mm} Unlocked {task.UnlockedDateTime:yyyy.MM.dd HH:mm} Completed {task.CompletedDateTime:yyyy.MM.dd HH:mm} Archive {task.ArchiveDateTime:yyyy.MM.dd HH:mm}\n" +
                              $"Begin {task.PlannedBeginDateTime:yyyy.MM.dd} Duration {TimeSpanStringConverter.SpanToString(task.PlannedDuration)} End {task.PlannedEndDateTime:yyyy.MM.dd}\n"
                              ;

            var inlineKeyboardButtons = new List<InlineKeyboardButton[]>
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(task.IsCompleted == true ? "Отменить выполнение" : "Выполнить задачу",
                        $"{Toggle}{task.Id}"),
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
            await _client.SendTextMessageAsync(chatId, response, ParseMode.Markdown, replyMarkup: keyboard);
        }

        private static string GetStatusEmodjiAndText(TaskItemViewModel task)
        {
            string status = task.IsCompleted switch
            {
                true => "✅ Выполнена",
                false => "\ud83d\udfe9 Не выполнена",
                null => "🗄️ Архивирована"
            };
            return status;
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

        private static async void OnCallbackQueryReceived(object sender, CallbackQueryEventArgs e)
        {
            var callbackData = e.CallbackQuery.Data;
            var chatId = e.CallbackQuery.Message.Chat.Id;
            var messageId = e.CallbackQuery.Message.MessageId;
            var userId = e.CallbackQuery.From.Id;
            Log.Information("Обработка колбэка от {User}: {Data}", e.CallbackQuery.From.Username, callbackData);

            if (await CheckAccess(userId, e.CallbackQuery.From.Username)) return;
            try
            {
                if (callbackData.StartsWith(Toggle))
                {
                    string id = callbackData.SplitOnFirst('_')[1];
                    var task = _taskService.GetTask(id);
                    if (task != null)
                    {
                        task.IsCompleted = task.IsCompleted != true;
                        //_taskService.SaveTask(task);
                        //_gitService.CommitAndPushChanges($"Изменен статус задачи {task.Title}");
                        await _client.AnswerCallbackQueryAsync(e.CallbackQuery.Id, $"Статус задачи обновлен: {(task.IsCompleted == true ? "Выполнена" : "Не выполнена")}");
                        await ShowTask(chatId, task);
                    }
                }
                else if (callbackData.StartsWith(Delete))
                {
                    string id = callbackData.SplitOnFirst('_')[1];
                    _taskService.DeleteTask(id);
                    //_gitService.CommitAndPushChanges($"Удалена задача с ID {id}");
                    await _client.AnswerCallbackQueryAsync(e.CallbackQuery.Id, "Задача удалена");
                    await _client.DeleteMessageAsync(chatId, messageId);
                }
                else if (callbackData.StartsWith(CreateSub))
                {
                    string id = callbackData.SplitOnFirst('_')[1];
                    _userStates[userId] = $"{CreateSub}{id}";
                    await _client.AnswerCallbackQueryAsync(e.CallbackQuery.Id, "Введите название подзадачи");
                }
                else if (callbackData.StartsWith(CreateSib))
                {
                    string id = callbackData.SplitOnFirst('_')[1];
                    _userStates[userId] = $"{CreateSib}{id}";
                    await _client.AnswerCallbackQueryAsync(e.CallbackQuery.Id, "Введите название соседней задачи");
                }
                else if (callbackData.StartsWith("parent_"))
                {
                    string parentId = callbackData.SplitOnFirst('_')[1];
                    if (parentId == "null")
                    {
                        await _client.AnswerCallbackQueryAsync(e.CallbackQuery.Id, "У данной задачи нет родителя");
                    }
                    else
                    {
                        await ShowTask(chatId, parentId);
                        await _client.AnswerCallbackQueryAsync(e.CallbackQuery.Id);
                    }
                }
                else if (callbackData.StartsWith(Parents))
                {
                    string id = callbackData.SplitOnFirst('_')[1];
                    var task = _taskService.GetTask(id);
                    if (task != null && task.ParentsTasks.Count > 0)
                    {
                        await ShowTaskList(task.ParentsTasks, "Родительские задачи:\n", chatId);
                        await _client.AnswerCallbackQueryAsync(e.CallbackQuery.Id);
                    }
                    else
                    {
                        await _client.AnswerCallbackQueryAsync(e.CallbackQuery.Id, "Нет родительских задач");
                    }
                }
                else if (callbackData.StartsWith(Blocking))
                {
                    string id = callbackData.SplitOnFirst('_')[1];
                    var task = _taskService.GetTask(id);
                    if (task != null && task.BlockedByTasks.Count > 0)
                    {
                        await ShowTaskList(task.BlockedByTasks, "Блокирующие задачи:\n", chatId);
                        await _client.AnswerCallbackQueryAsync(e.CallbackQuery.Id);
                    }
                    else
                    {
                        await _client.AnswerCallbackQueryAsync(e.CallbackQuery.Id, "Нет блокирующих задач");
                    }
                }
                else if (callbackData.StartsWith(Containing))
                {
                    string id = callbackData.SplitOnFirst('_')[1];
                    var task = _taskService.GetTask(id);
                    if (task != null && task.ContainsTasks.Count > 0)
                    {
                        await ShowTaskList(task.ContainsTasks, "Дочерние задачи:\n", chatId);
                        await _client.AnswerCallbackQueryAsync(e.CallbackQuery.Id);
                    }
                    else
                    {
                        await _client.AnswerCallbackQueryAsync(e.CallbackQuery.Id, "Нет дочерних задач");
                    }
                }
                else if (callbackData.StartsWith(Blocked))
                {
                    string id = callbackData.SplitOnFirst('_')[1];
                    var task = _taskService.GetTask(id);
                    if (task != null && task.BlocksTasks.Count > 0)
                    {
                        await ShowTaskList(task.BlocksTasks, "Блокируемые задачи:\n", chatId);
                        await _client.AnswerCallbackQueryAsync(e.CallbackQuery.Id);
                    }
                    else
                    {
                        await _client.AnswerCallbackQueryAsync(e.CallbackQuery.Id, "Нет блокируемых задач");
                    }
                }
                else if (callbackData.StartsWith(Open))
                {
                    string id = callbackData.SplitOnFirst('_')[1];
                    await ShowTask(chatId, id);
                    await _client.AnswerCallbackQueryAsync(e.CallbackQuery.Id);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при обработке колбэка");
                await _client.SendTextMessageAsync(chatId, "Произошла ошибка при обработке вашего запроса.");
            }
        }

        private static async ValueTask<bool> CheckAccess(long userId, string userName)
        {
            if (!AllowedUsers.Contains(userId))
            {
                Log.Warning("Пользователю {User} [id:{UserId}] запрещено обращаться к боту", userName,
                    userId);
                return true;
            }

            return false;
        }
    }
}
