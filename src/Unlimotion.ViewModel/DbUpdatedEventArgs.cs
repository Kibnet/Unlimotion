using System;
using System.Collections.Generic;
using Unlimotion.ViewModel.Models;

namespace Unlimotion.ViewModel
{
    public class DbUpdatedEventArgs : EventArgs {
        public List<UpdatedTask> UpdatedTasks { get; set; } = new List<UpdatedTask>();
    }
}