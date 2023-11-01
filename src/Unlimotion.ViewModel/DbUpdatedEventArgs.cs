using System;
using System.Collections.Generic;
using Unlimotion.ViewModel.Models;

namespace Unlimotion.ViewModel
{
    public class DbUpdatedEventArgs : EventArgs
    {
        public List<TaskUpdateEvent> UpdatedTasks { get; set; } = new List<TaskUpdateEvent>();
    }
}