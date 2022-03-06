using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PropertyChanged;
using Splat;

namespace Unlimotion.ViewModel
{
    public interface ITaskStorage
    {
        IEnumerable<TaskItem> GetAllTasks();

        bool SaveTask(TaskItem item);
    }

    [AddINotifyPropertyChangedInterface]
    public class MainWindowViewModel
    {
        private ITaskStorage TaskStorage;

        public MainWindowViewModel()
        {
            TaskStorage = Locator.Current.GetService<ITaskStorage>();
            var taskRepository = new TaskRepository(TaskStorage);
            Locator.CurrentMutable.RegisterConstant(taskRepository);
            taskRepository.Init();
            
            CurrentItems = new ObservableCollection<TaskItemViewModel>();
            foreach (var root in taskRepository.GetRoots())
            {
                CurrentItems.Add(TaskItemViewModel.GetViewModel(root));
            }
        }

        public string BreadScrumbs
        {
            get
            {
                var nodes = new List<string>();
                var current = CurrentItem;
                while (current != null)
                {
                    nodes.Insert(0, current.Title);
                    //TODO Сделать вывод всех альтернативных веток родителей
                    current = current.ParentsTasks.FirstOrDefault();
                }
                return String.Join(" / ", nodes);
            }
        }

        public ObservableCollection<TaskItemViewModel> CurrentItems { get; set; }

        public TaskItemViewModel CurrentItem { get; set; }
    }
}
