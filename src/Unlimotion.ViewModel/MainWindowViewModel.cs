using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using DynamicData;
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
    public class MainWindowViewModel:DisposableList
    {
        private ITaskStorage TaskStorage;
        private ReadOnlyObservableCollection<TaskWrapperViewModel> _currentItems;

        public MainWindowViewModel()
        {
            TaskStorage = Locator.Current.GetService<ITaskStorage>();
            var taskRepository = new TaskRepository(TaskStorage);
            Locator.CurrentMutable.RegisterConstant(taskRepository);
            taskRepository.Init();

            taskRepository.GetRoots().Transform(item =>
                {
                    var wrapper = new TaskWrapperViewModel(null, item);
                    return wrapper;
                }).Bind(out _currentItems)
                .Subscribe()
                .AddToDispose(this);
            
            RemoveCommand = ReactiveCommand.Create(() =>
            {
                var current = CurrentItem;
                //Удаление ссылки из родителя
                current?.Parent?.TaskItem.Contains.Remove(current.TaskItem.Id);
                //Если родителей не осталось, удаляется сама задача
                if (current.TaskItem.ParentsTasks.Count == 0)
                {
                    taskRepository.Remove(current.TaskItem.Id);
                }
                CurrentItem = null;
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

        public ReadOnlyObservableCollection<TaskWrapperViewModel> CurrentItems => _currentItems;

        public TaskWrapperViewModel CurrentItem { get; set; }

        public ICommand RemoveCommand { get; set; }
    }
}
