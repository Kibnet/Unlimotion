using System;
using System.Collections.Generic;

namespace Unlimotion.ViewModel;

public class ComputedTaskInfo
{
    public string TaskId { get; set; } 
    public List<string> FromIds { get; set; } 

    public ComputedTaskInfo()
    {
        FromIds = new List<string>();
    }
}