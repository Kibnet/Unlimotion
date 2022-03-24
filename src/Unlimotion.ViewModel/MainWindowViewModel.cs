using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Windows.Input;
using DynamicData;
using DynamicData.Binding;
using PropertyChanged;
using ReactiveUI;
using Splat;

namespace Unlimotion.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    public class MainWindowViewModel : DisposableList
    {
        public MainWindowViewModel()
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();

            //Bind Roots
            taskRepository.GetRoots()
                .AutoRefreshOnObservable(m => m.Contains.ToObservableChangeSet())
                .Transform(item =>
                {
                    var wrapper = new TaskWrapperViewModel(null, item, 
                        m => m.ContainsTasks.ToObservableChangeSet(),
                        m => m.TaskItem.RemoveFunc.Invoke(m.Parent?.TaskItem));
                    return wrapper;
                }).Bind(out _currentItems)
                .Subscribe()
                .AddToDispose(this);

            //Bind Unlocked
            taskRepository.Tasks
                .Connect()
                .AutoRefreshOnObservable(m => m.WhenAny(m => m.IsCanBeComplited, m => m.IsCompleted,(c, d) => c.Value&& (d.Value==false)))
                .Filter(m => m.IsCanBeComplited && (m.IsCompleted == false))
                .Transform(item =>
                {
                    var wrapper = new TaskWrapperViewModel(null, item,
                        m => m.ContainsTasks.ToObservableChangeSet(),
                        m => m.TaskItem.RemoveFunc.Invoke(m.Parent?.TaskItem));
                    return wrapper;
                }).Bind(out _unlockedItems)
                .Subscribe()
                .AddToDispose(this);

            //Bind Current Item Contains
            this.WhenAnyValue(m => m.CurrentItem)
                .Subscribe(item =>
                {
                    if (item != null)
                    {
                        CurrentItemContains = new TaskWrapperViewModel(null, item.TaskItem,
                            m => m.ContainsTasks.ToObservableChangeSet(),
                            m =>
                            {
                                m.Parent.TaskItem.Contains.Remove(m.TaskItem.Id);
                                m.Parent.TaskItem.SaveItemCommand.Execute(null);
                            });}
                    else
                    {
                        CurrentItemContains = null;
                    }
                })
                .AddToDispose(this);

            //Bind Current Item Parents
            this.WhenAnyValue(m => m.CurrentItem)
                .Subscribe(item =>
                {
                    if (item != null)
                    {
                        CurrentItemParents = new TaskWrapperViewModel(null, item.TaskItem,
                            m => m.ParentsTasks.ToObservableChangeSet(),
                            m =>
                            {
                                m.TaskItem.Contains.Remove(m.Parent.TaskItem.Id);
                                m.TaskItem.SaveItemCommand.Execute(null);
                            });
                    }
                    else
                    {
                        CurrentItemParents = null;
                    }
                })
                .AddToDispose(this);

            //Bind Current Item Blocks
            this.WhenAnyValue(m => m.CurrentItem)
                .Subscribe(item =>
                {
                    if (item != null)
                    {
                        CurrentItemBlocks = new TaskWrapperViewModel(null, item.TaskItem,
                            m => m.BlocksTasks.ToObservableChangeSet(),
                            m =>
                            {
                                m.TaskItem.UnblockCommand.Execute(m.Parent.TaskItem);
                                m.TaskItem.SaveItemCommand.Execute(null);
                            });
                    }
                    else
                    {
                        CurrentItemBlocks = null;
                    }
                })
                .AddToDispose(this);

            //Bind Current Item BlockedBy
            this.WhenAnyValue(m => m.CurrentItem)
                .Subscribe(item =>
                {
                    if (item != null)
                    {
                        CurrentItemBlockedBy = new TaskWrapperViewModel(null, item.TaskItem,
                            m => m.BlockedByTasks.ToObservableChangeSet(),
                            m =>
                            {
                                m.TaskItem.UnblockMeCommand.Execute(m.Parent.TaskItem);
                                m.TaskItem.SaveItemCommand.Execute(null);
                            });
                    }
                    else
                    {
                        CurrentItemBlockedBy = null;
                    }
                })
                .AddToDispose(this);

            CreateSibling = ReactiveCommand.Create(() =>
                {
                    if (CurrentItem != null && string.IsNullOrWhiteSpace(CurrentItem?.TaskItem.Title))
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

        private ReadOnlyObservableCollection<TaskWrapperViewModel> _currentItems;

        private ReadOnlyObservableCollection<TaskWrapperViewModel> _unlockedItems;
        public ReadOnlyObservableCollection<TaskWrapperViewModel> CurrentItems => _currentItems;
        public ReadOnlyObservableCollection<TaskWrapperViewModel> UnlockedItems => _unlockedItems;

        public TaskWrapperViewModel CurrentItem { get; set; }

        public TaskWrapperViewModel CurrentItemContains { get; private set; }
        public TaskWrapperViewModel CurrentItemParents { get; private set; }
        public TaskWrapperViewModel CurrentItemBlocks { get; private set; }
        public TaskWrapperViewModel CurrentItemBlockedBy { get; private set; }

        public ICommand CreateSibling { get; set; }

        public ICommand CreateInner { get; set; }
    }
}
