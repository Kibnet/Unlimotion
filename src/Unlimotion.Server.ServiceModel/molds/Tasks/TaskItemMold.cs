using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Unlimotion.Server.ServiceModel.Molds.Tasks
{
    [Description("������")]
    public class TaskItemMold
    {
        [Description("�������������")]
        public string Id { get; set; }
        [Description("������������� ������������")]
        public string UserId { get; set; }
        [Description("��������")]
        public string Title { get; set; }
        [Description("��������")]
        public string Description { get; set; }
        [Description("������ ����������")]
        public bool? IsCompleted { get; set; } = false;
        [Description("���� ��������")]
        public DateTimeOffset CreatedDateTime { get; set; }
        [Description("���� �������������")]
        public DateTimeOffset? UnlockedDateTime { get; set; }
        [Description("���� ����������")]
        public DateTimeOffset? CompletedDateTime { get; set; }
        [Description("���� ���������")]
        public DateTimeOffset? ArchiveDateTime { get; set; }
        [Description("����������� ���� ������ ����������")]
        public DateTimeOffset? PlannedBeginDateTime { get; set; }
        [Description("����������� ���� ��������� ����������")]
        public DateTimeOffset? PlannedEndDateTime { get; set; }
        [Description("����������� ������������ ���������")]
        public TimeSpan? PlannedDuration { get; set; }
        [Description("�������� ������")]
        public List<string> ContainsTasks { get; set; }
        [Description("����������� ������")]
        public List<string> BlocksTasks { get; set; }
        [Description("����������")]
        public RepeaterPatternMold Repeater { get; set; }
        [Description("��������")]
        public int Importance { get; set; }
        [Description("����������")]
        public bool Wanted { get; set; }
    }
}
