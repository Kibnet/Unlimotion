using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using PropertyChanged;
using ReactiveUI;
using Splat;

namespace Unlimotion.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    public class MainWindowViewModel
    {
        public MainWindowViewModel()
        {
            var taskRepository = new TaskRepository();
            Locator.CurrentMutable.RegisterConstant(taskRepository);
            taskRepository.Init(new[]
            {
                new TaskItem { Title = "Task 1", Id = "1", ContainsTasks = new List<string>{ "1.1","1.2","3" }},
                new TaskItem { Title = "Task 1.1", Id = "1.1", BlocksTasks = new List<string>{"1.2" }},
                new TaskItem { Title = "Task 1.2", Id = "1.2"},
                new TaskItem { Title = "Task 2", Id = "2", ContainsTasks = new List<string>{ "2.1", "3" } },
                new TaskItem { Title = "Task 2.1", Id = "2.1" },
                new TaskItem { Title = "Task 3", Id = "3" }
            });


            CurrentItems = new ObservableCollection<TaskItemViewModel>();
            foreach (var root in taskRepository.GetRoots())
            {
                CurrentItems.Add(TaskItemViewModel.GetViewModel(root));
            }

        }

        public string BreadScrumbs
        {
            get { return "BreadScrumbs"; }
        }

        public ObservableCollection<TaskItemViewModel> CurrentItems { get; set; }

        public TaskItemViewModel CurrentItem { get; set; }
    }
}
