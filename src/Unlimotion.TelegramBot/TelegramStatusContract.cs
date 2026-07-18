using DynamicData;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.TelegramBot;

internal readonly record struct TelegramStatusCallbackRequest(
    DomainTaskStatus RequestedStatus,
    string TaskId);

internal sealed record TelegramStatusButtonDescriptor(
    DomainTaskStatus Status,
    string Text,
    string CallbackData);

internal sealed record TelegramStatusCallbackOutcome(
    bool IsValid,
    string? TaskId,
    DomainTaskStatus? RequestedStatus,
    TaskOperationResult? OperationResult,
    TaskItemViewModel? RefreshedTask,
    DomainTaskStatus? DisplayStatus,
    string AnswerText);

internal static class TelegramStatusContract
{
    internal const string CallbackPrefix = "status_";

    internal static IReadOnlyList<DomainTaskStatus> GetAllowedTargets(TaskItemViewModel task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var facts = new TaskStatusTransitionFacts(
            task.Status,
            task.IsCanBeCompleted,
            task.PlannedBeginDateTime.HasValue && task.PlannedBeginDateTime.Value > DateTime.Now,
            task.CompletionCriteria.All(static criterion => criterion.IsSatisfied));

        return Enum.GetValues<DomainTaskStatus>()
            .Where(status => status != task.Status)
            .Where(status => TaskStatusTransitionPolicy.Evaluate(status, facts).IsAllowed)
            .ToArray();
    }

    internal static string CreateCallbackData(DomainTaskStatus status, string taskId) =>
        $"{CallbackPrefix}{status}_{taskId}";

    internal static IReadOnlyList<TelegramStatusButtonDescriptor> BuildAllowedButtons(
        TaskItemViewModel task) =>
        GetAllowedTargets(task)
            .Select(status => new TelegramStatusButtonDescriptor(
                status,
                FormatStatus(status),
                CreateCallbackData(status, task.Id)))
            .ToArray();

    internal static bool TryParseCallback(
        string? callbackData,
        out TelegramStatusCallbackRequest request)
    {
        request = default;
        if (string.IsNullOrEmpty(callbackData) ||
            !callbackData.StartsWith(CallbackPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var payload = callbackData[CallbackPrefix.Length..];
        var separatorIndex = payload.IndexOf('_');
        if (separatorIndex <= 0 || separatorIndex == payload.Length - 1)
        {
            return false;
        }

        var statusToken = payload[..separatorIndex];
        if (!Enum.TryParse<DomainTaskStatus>(statusToken, ignoreCase: false, out var status) ||
            !Enum.IsDefined(status) ||
            !string.Equals(statusToken, status.ToString(), StringComparison.Ordinal))
        {
            return false;
        }

        var taskId = payload[(separatorIndex + 1)..];
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return false;
        }

        request = new TelegramStatusCallbackRequest(status, taskId);
        return true;
    }

    internal static async Task<TelegramStatusCallbackOutcome> ExecuteCallbackAsync(
        ITaskStorage storage,
        string? callbackData,
        string? author = null)
    {
        ArgumentNullException.ThrowIfNull(storage);

        if (!TryParseCallback(callbackData, out var request))
        {
            return new TelegramStatusCallbackOutcome(
                false,
                null,
                null,
                null,
                null,
                null,
                "Неизвестный статус задачи.");
        }

        var result = await storage
            .TrySetStatusAsync(request.TaskId, request.RequestedStatus, author)
            .ConfigureAwait(false);

        TaskItemViewModel? refreshedTask;
        DomainTaskStatus? displayStatus;
        if (result.DeniedReason?.Kind == TaskOperationDeniedKind.OutcomeUnknown)
        {
            var reloadedTask = await TryReloadAuthoritativeTaskAsync(storage, request.TaskId)
                .ConfigureAwait(false);
            displayStatus = reloadedTask is not null &&
                            string.Equals(reloadedTask.Id, request.TaskId, StringComparison.Ordinal)
                ? reloadedTask.Status
                : null;

            // OutcomeUnknown cannot prove that the cached ViewModel was refreshed. The callback
            // reports the explicit read-back status, but never renders a potentially stale card.
            refreshedTask = null;
        }
        else
        {
            refreshedTask = FindCachedTask(storage, request.TaskId);
            var authoritativeStatus = result.AuthoritativeTask is { } authoritativeTask &&
                                      string.Equals(authoritativeTask.Id, request.TaskId, StringComparison.Ordinal)
                ? authoritativeTask.Status
                : (DomainTaskStatus?)null;
            displayStatus = authoritativeStatus ?? refreshedTask?.Status;
        }

        return new TelegramStatusCallbackOutcome(
            true,
            request.TaskId,
            request.RequestedStatus,
            result,
            refreshedTask,
            displayStatus,
            CreateAnswerText(result, displayStatus));
    }

    internal static string FormatStatus(DomainTaskStatus status) =>
        $"{GetStatusEmoji(status)} {GetStatusText(status)}";

    internal static string GetStatusText(DomainTaskStatus status) => status switch
    {
        DomainTaskStatus.NotReady => "Не готово",
        DomainTaskStatus.Prepared => "Подготовлено",
        DomainTaskStatus.InProgress => "Выполняется",
        DomainTaskStatus.Completed => "Выполнено",
        DomainTaskStatus.Archived => "Архивировано",
        _ => status.ToString()
    };

    internal static string GetStatusEmoji(DomainTaskStatus status) => status switch
    {
        DomainTaskStatus.NotReady => "⬜",
        DomainTaskStatus.Prepared => "❗",
        DomainTaskStatus.InProgress => "▶️",
        DomainTaskStatus.Completed => "✅",
        DomainTaskStatus.Archived => "🗄️",
        _ => "⬜"
    };

    private static TaskItemViewModel? FindCachedTask(ITaskStorage storage, string taskId)
    {
        var lookup = storage.Tasks.Lookup(taskId);
        return lookup.HasValue ? lookup.Value : null;
    }

    private static async Task<TaskItem?> TryReloadAuthoritativeTaskAsync(
        ITaskStorage storage,
        string taskId)
    {
        try
        {
            return await storage.TaskTreeManager.Storage.Load(taskId).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static string CreateAnswerText(
        TaskOperationResult result,
        DomainTaskStatus? displayStatus)
    {
        var statusText = displayStatus.HasValue
            ? GetStatusText(displayStatus.Value)
            : null;

        if (result.Success)
        {
            return statusText is null
                ? "Статус задачи обновлён, но не удалось прочитать сохранённое значение."
                : $"Статус задачи: {statusText}";
        }

        if (result.DeniedReason?.Kind == TaskOperationDeniedKind.OutcomeUnknown)
        {
            return statusText is null
                ? "Итог операции и текущий статус задачи неизвестны. Обновите задачу перед повторной попыткой."
                : $"Итог операции неизвестен. Текущий отображаемый статус: {statusText}. " +
                  "Обновите задачу перед повторной попыткой.";
        }

        var reason = GetDeniedReasonText(result);
        return statusText is null
            ? reason
            : $"{reason} Текущий статус: {statusText}";
    }

    private static string GetDeniedReasonText(TaskOperationResult result)
    {
        var reason = result.DeniedReason;
        if (reason?.StatusTransitionReason is { } transitionReason)
        {
            return transitionReason switch
            {
                TaskStatusTransitionDenialReason.TerminalCannotStart =>
                    "Завершённую или архивную задачу нельзя вернуть в работу.",
                TaskStatusTransitionDenialReason.GraphUnavailableForStart =>
                    GetGraphDeniedReasonText(
                        result.Before?.Reasons,
                        "Задача заблокирована связями и пока не может быть начата."),
                TaskStatusTransitionDenialReason.FutureDatePreventsStart =>
                    "Задачу нельзя начать до запланированной даты.",
                TaskStatusTransitionDenialReason.TerminalCannotComplete =>
                    "Завершённую или архивную задачу нельзя повторно завершить.",
                TaskStatusTransitionDenialReason.GraphUnavailableForCompletion =>
                    GetGraphDeniedReasonText(
                        result.Before?.Reasons,
                        "Задача заблокирована связями и пока не может быть завершена."),
                TaskStatusTransitionDenialReason.CompletionCriteriaIncomplete =>
                    "Сначала выполните все критерии завершения.",
                TaskStatusTransitionDenialReason.CompletedCannotArchive =>
                    "Выполненную задачу нельзя архивировать.",
                TaskStatusTransitionDenialReason.InvalidTargetStatus =>
                    "Неизвестный статус задачи.",
                _ => "Переход статуса недоступен для этой задачи."
            };
        }

        return reason?.Kind switch
        {
            TaskOperationDeniedKind.TaskNotFound => "Задача не найдена.",
            TaskOperationDeniedKind.ValidationFailed =>
                "Граф задач содержит ошибки; изменение статуса недоступно.",
            TaskOperationDeniedKind.StorageFailed => "Не удалось сохранить статус задачи.",
            _ => "Переход статуса недоступен для этой задачи."
        };
    }

    private static string GetGraphDeniedReasonText(
        IReadOnlyList<TaskAvailabilityReason>? reasons,
        string fallback)
    {
        if (reasons is null)
        {
            return fallback;
        }

        var hasContainedTask = reasons.Any(static item =>
            item.Kind == TaskAvailabilityReasonKind.IncompleteContainedTask);
        var hasDirectBlocker = reasons.Any(static item =>
            item.Kind == TaskAvailabilityReasonKind.IncompleteDirectBlocker);
        var hasInheritedBlocker = reasons.Any(static item =>
            item.Kind == TaskAvailabilityReasonKind.IncompleteInheritedBlocker);

        return (hasContainedTask, hasDirectBlocker, hasInheritedBlocker) switch
        {
            (true, _, _) => "Сначала выполните все вложенные задачи.",
            (false, true, _) => "Сначала выполните прямые блокирующие задачи.",
            (false, false, true) =>
                "Сначала выполните блокирующие задачи, унаследованные от родительских задач.",
            _ => fallback
        };
    }
}
