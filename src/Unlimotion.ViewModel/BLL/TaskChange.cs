using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Unlimotion.ViewModel.BLL
{
    public class TaskChange
    {
        public string Id { get; set; }
        public TaskItemViewModel Parent { get; set; }
        public Action Action { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public bool? IsCompleted { get; set; } = false;
        public DateTimeOffset? PlannedBeginDateTime { get; set; }
        public DateTimeOffset? PlannedEndDateTime { get; set; }
        public TimeSpan? PlannedDuration { get; set; }
        public int? Importance { get; set; }
        public bool? Wanted { get; set; }
    }
}
