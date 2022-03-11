using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using PropertyChanged;
using ReactiveUI;
using Splat;

namespace Unlimotion.ViewModel
{
    public interface ITaskStorage
    {
        IEnumerable<TaskItem> GetAll();

        bool Save(TaskItem item);
        bool Remove(string itemId);
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
            
            CurrentItems = new ObservableCollection<TaskWrapperViewModel>();
            foreach (var root in taskRepository.GetRoots())
            {
                var vm = TaskItemViewModel.GetViewModel(root);
                var wrapper = new TaskWrapperViewModel(null, vm);
                CurrentItems.Add(wrapper);
            }

            RemoveCommand = ReactiveCommand.Create(() =>
            {
                var current = CurrentItem;
                //Удаление ссылки из родителя
                current?.Parent?.TaskItem.ContainsTasks.Remove(current.TaskItem);
                //Если родителей не осталось, удаляется сама задача
                if (current.TaskItem.ParentsTasks.Count == 0)
                {
                    taskRepository.Remove(current.TaskItem.Id);
                    CurrentItems.Remove(current);
                }
            },
            this.WhenAny(m => m.CurrentItem, m => m.Value != null));
        }

        public string BreadScrumbs
        {
            get
            {
                var nodes = new List<string>();
                var current = CurrentItem;
                while (current != null)
                {
                    nodes.Insert(0, current.TaskItem.Title);
                    //TODO Сделать вывод всех альтернативных веток родителей
                    current = current.Parent;
                }
                return String.Join(" / ", nodes);
            }
        }

        public ObservableCollection<TaskWrapperViewModel> CurrentItems { get; set; }

        public TaskWrapperViewModel CurrentItem { get; set; }

        public ICommand RemoveCommand { get; set; }
    }
}
