using System.Collections.Generic;
using System.ComponentModel;

namespace Unlimotion.Server.ServiceModel.Molds.Tasks
{
    [Description("Страница задач")]
    public class TaskItemPage
    {
        [Description("Cписок задач")]
        public List<TaskItemMold> Tasks { get;set; }
    }
}