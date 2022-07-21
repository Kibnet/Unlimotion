using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Unlimotion.Server.ServiceModel.Molds.Tasks
{
    [Description("Задача")]
    public class TaskItemMold
    {
        [Description("Идентификатор")]
        public string Id { get; set; }
        [Description("Идентификатор пользователя")]
        public string UserId { get; set; }
        [Description("Название")]
        public string Title { get; set; }
        [Description("Описание")]
        public string Description { get; set; }
        [Description("Статус завершения")]
        public bool? IsCompleted { get; set; } = false;
        [Description("Дата создания")]
        public DateTimeOffset CreatedDateTime { get; set; }
        [Description("Дата разблокировки")]
        public DateTimeOffset? UnlockedDateTime { get; set; }
        [Description("Дата завершения")]
        public DateTimeOffset? CompletedDateTime { get; set; }
        [Description("Дата архивации")]
        public DateTimeOffset? ArchiveDateTime { get; set; }
        [Description("Планируемая дата начала выполнения")]
        public DateTimeOffset? PlannedBeginDateTime { get; set; }
        [Description("Планируемая дата окончания выполнения")]
        public DateTimeOffset? PlannedEndDateTime { get; set; }
        [Description("Планируемая длительность выполения")]
        public TimeSpan? PlannedDuration { get; set; }
        [Description("Дочерние задачи")]
        public List<string> ContainsTasks { get; set; }
        [Description("Блокируемые задачи")]
        public List<string> BlocksTasks { get; set; }
        [Description("Повторение")]
        public RepeaterPatternMold Repeater { get; set; }
        [Description("Важность")]
        public int Importance { get; set; }
        [Description("Желаемость")]
        public bool Wanted { get; set; }
    }
}
