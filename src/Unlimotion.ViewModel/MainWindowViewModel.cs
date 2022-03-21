using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using DynamicData;
using DynamicData.Binding;
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
    public class MainWindowViewModel : DisposableList
    {
        private ReadOnlyObservableCollection<TaskWrapperViewModel> _currentItems;

        public MainWindowViewModel()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();

            taskRepository.GetRoots()
                .AutoRefreshOnObservable(m => m.Contains.ToObservableChangeSet())
                .Transform(item =>
                {
                    var wrapper = new TaskWrapperViewModel(null, item);
                    return wrapper;
                }).Bind(out _currentItems)
                .Subscribe()
                .AddToDispose(this);

            RemoveCommand = ReactiveCommand.Create(() =>
                {
                    CurrentItem?.Remove();
                    CurrentItem = null;
                },
                this.WhenAny(m => m.CurrentItem, m => m.Value != null));

            CreateSibling = ReactiveCommand.Create(() =>
                {
                    if (CurrentItem!=null && string.IsNullOrWhiteSpace(CurrentItem?.TaskItem.Title))
                        return;
                    var task = new TaskItemViewModel(new TaskItem(), taskRepository);
                    task.SaveItemCommand.Execute(null);
                    if (CurrentItem?.Parent != null)
                    {
                        CurrentItem.Parent.TaskItem.Contains.Add(task.Id);
                        CurrentItem.Parent.TaskItem.SaveItemCommand.Execute(null);
                    }
                    taskRepository.Tasks.AddOrUpdate(task);

                    if (CurrentItem?.Parent == null)
                    {
                        var taskWrapper = CurrentItems.First(m => m.TaskItem == task);
                        CurrentItem = taskWrapper;
                    }
                    else
                    {
                        var taskWrapper = CurrentItem.Parent.SubTasks.First(m => m.TaskItem == task);
                        CurrentItem = taskWrapper;
                    }
                },
                this.WhenAny(m => m.CurrentItem, m => true));// || !string.IsNullOrWhiteSpace(m.TaskItem.Title)));

            CreateInner = ReactiveCommand.Create(() =>
                {
                    if (CurrentItem == null)
                        return;
                    if (string.IsNullOrWhiteSpace(CurrentItem?.TaskItem.Title))
                        return;
                    var task = new TaskItemViewModel(new TaskItem(), taskRepository);
                    task.SaveItemCommand.Execute(null);
                    CurrentItem.TaskItem.Contains.Add(task.Id);
                    CurrentItem.TaskItem.SaveItemCommand.Execute(null);
                    taskRepository.Tasks.AddOrUpdate(task);

                    var taskWrapper = CurrentItem.SubTasks.First(m => m.TaskItem == task);
                    CurrentItem = taskWrapper;
                },
                this.WhenAny(m => m.CurrentItem, m => true));
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
                    current = current.Parent;
                }
                return String.Join(" / ", nodes);
            }
        }

        public ReadOnlyObservableCollection<TaskWrapperViewModel> CurrentItems => _currentItems;

        public TaskWrapperViewModel CurrentItem { get; set; }

        public ICommand RemoveCommand { get; set; }

        public ICommand CreateSibling { get; set; }

        public ICommand CreateInner { get; set; }
    }
}
