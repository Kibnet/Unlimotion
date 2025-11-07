using System;
using Unlimotion.TaskTree;

namespace Unlimotion.ViewModel
{
    public class DbUpdatedEventArgs : EventArgs
    {
        public string Id { get; set; }
        public UpdateType Type { get; set; }
    }
}