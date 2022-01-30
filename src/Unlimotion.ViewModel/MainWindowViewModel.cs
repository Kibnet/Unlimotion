using System;
using System.Collections.Generic;
using System.ComponentModel;
using PropertyChanged;
using ReactiveUI;

namespace Unlimotion.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    public class MainWindowViewModel
    {
        public string Greeting => "Welcome to Avalonia!";

        
        public string BreadScrumbs
        {
            get { return "BreadScrumbs"; }
        }

        public IEnumerable<string> CurrentItems
        {
            get { throw new NotImplementedException(); }
        }

        public TaskItem CurrentItem
        {
            get { throw new NotImplementedException(); }
        }
    }
}
