using System;
using System.Collections.Generic;

namespace Unlimotion.ViewModel;

public class ComputedTaskInfo
{
    public string TaskId { get; set; } 
    public List<string> FromIds { get; set; } 
    public DateTimeOffset UpdateDateTime { get; set; }
    public TaskInfoType Type { get; set; }

    public ComputedTaskInfo()
    {
        UpdateDateTime = DateTimeOffset.Now;
        FromIds = new List<string>();
    }
}

public enum TaskInfoType
{
    IsBlocked = 1,
}