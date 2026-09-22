using System;
using System.Collections.Generic;
using System.ComponentModel;
using Unlimotion.Domain;

namespace Unlimotion.Server.ServiceModel.Molds.Tasks
{
    [Description("Задача")]
    public class TaskItemMold
    {
        public TaskItemMold()
        {
            Id = string.Empty;
            UserId = string.Empty;
            Title = string.Empty;
            Description = string.Empty;
            ContainsTasks = new List<string>();
            ParentTasks = new List<string>();
            BlocksTasks = new List<string>();
            BlockedByTasks = new List<string>();
            AreaIds = new List<string>();
            Repeater = new RepeaterPatternMold();
        }

        [Description("Идентификатор")]
        public string Id { get; set; }
        [Description("Идентификатор пользователя")]
        public string UserId { get; set; }
        [Description("Название")]
        public string Title { get; set; }
        [Description("Описание")]
        public string Description { get; set; }
        [Description("Статус задачи")]
        public TaskStatus Status { get; set; } = TaskStatus.NotReady;
        [Description("История изменения статусов")]
        public List<TaskStatusHistoryEntry> StatusHistory { get; set; } = new();
        [Description("Критерии проверки выполнения")]
        public List<TaskCompletionCriterion> CompletionCriteria { get; set; } = new();
        [Description("Доступность выполнения")]
        public bool IsCanBeCompleted { get; set; }
        [Description("Дата создания")]
        public DateTimeOffset CreatedDateTime { get; set; }
        [Description("Дата обновления")]
        public DateTimeOffset? UpdatedDateTime { get; set; }
        [Description("Дата разблокировки")]
        public DateTimeOffset? UnlockedDateTime { get; set; }

        [Description("Планируемая дата начала выполнения")]
        public DateTimeOffset? PlannedBeginDateTime { get; set; }
        [Description("Планируемая дата окончания выполнения")]
        public DateTimeOffset? PlannedEndDateTime { get; set; }
        [Description("Планируемая длительность выполения")]
        public TimeSpan? PlannedDuration { get; set; }
        [Description("Дочерние задачи")]
        public List<string> ContainsTasks { get; set; }
        [Description("Родительские задачи")]
        public List<string> ParentTasks { get; set; }
        [Description("Блокируемые задачи")]
        public List<string> BlocksTasks { get; set; }
        [Description("Блокирующие задачи")]
        public List<string> BlockedByTasks { get; set; }
        [Description("Повторение")]
        public RepeaterPatternMold Repeater { get; set; }
        [Description("Важность")]
        public int Importance { get; set; }
        [Description("Желаемость")]
        public bool Wanted { get; set; }
        [Description("Является целью")]
        public bool IsGoal { get; set; }
        [Description("Области")]
        public List<string> AreaIds { get; set; }
        [Description("Создан в предыдущей версии приложения")]
        public int Version { get; set; } = 0;
        [Description("Порядок изменения тасков")]
        public DateTime SortOrder { get; set; }
    }
}
