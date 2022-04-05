using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Windows.Input;
using DynamicData;
using DynamicData.Binding;
using Microsoft.Extensions.Configuration;
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
            ManagerWrapper = Locator.Current.GetService<INotificationManagerWrapper>();

            _configuration = Splat.Locator.Current.GetService<IConfiguration>();
            ShowCompleted = _configuration.GetSection("AllTasks:ShowCompleted").Get<bool>();
            ShowArchived = _configuration.GetSection("AllTasks:ShowArchived").Get<bool>();
            ShowPlanned = _configuration.GetSection("AllTasks:ShowPlanned").Get<bool>();
            var sortName = _configuration.GetSection("AllTasks:CurrentSortDefinition").Get<string>();
            CurrentSortDefinition = SortDefinitions.FirstOrDefault(s => s.Name == sortName) ?? SortDefinitions.First();

            this.WhenAnyValue(m => m.ShowCompleted)
                .Subscribe(b => _configuration.GetSection("AllTasks:ShowCompleted").Set(b));
            this.WhenAnyValue(m => m.ShowArchived)
                .Subscribe(b => _configuration.GetSection("AllTasks:ShowArchived").Set(b));
            this.WhenAnyValue(m => m.ShowPlanned)
                .Subscribe(b => _configuration.GetSection("AllTasks:ShowPlanned").Set(b));
            this.WhenAnyValue(m => m.CurrentSortDefinition)
                .Subscribe(b => _configuration.GetSection("AllTasks:CurrentSortDefinition").Set(b.Name));

            //Set sort definition
            var sortObservable = this.WhenAnyValue(m => m.CurrentSortDefinition).Select(d => d.Comparer);

            //Set All Tasks Filter
            var taskFilter = this.WhenAnyValue(m => m.ShowCompleted, m => m.ShowArchived)
                .Select(filters =>
                {
                    bool Predicate(TaskItemViewModel task) =>
                        task.IsCompleted == false ||
                        ((task.IsCompleted == true) && filters.Item1) ||
                        ((task.IsCompleted == null) && filters.Item2);
                    return (Func<TaskItemViewModel, bool>)Predicate;
                });

            //Bind Roots
            taskRepository.GetRoots()
                .AutoRefreshOnObservable(m => m.Contains.ToObservableChangeSet())
                .AutoRefreshOnObservable(m => m.WhenAny(m => m.IsCanBeComplited, m => m.IsCompleted, m => m.UnlockedDateTime, (c, d, u) => c.Value && (d.Value == false)))
                .Filter(taskFilter)
                .Transform(item =>
                {
                    var actions = new TaskWrapperActions()
                    {
                        ChildSelector = m => m.ContainsTasks.ToObservableChangeSet(),
                        RemoveAction = RemoveTask,
                        GetBreadScrumbs = BredScrumbsAlgorithms.WrapperParent,
                        SortComparer = sortObservable,
                        Filter = taskFilter,
                    };
                    var wrapper = new TaskWrapperViewModel(null, item, actions);
                    return wrapper;
                })
                .Sort(sortObservable)
                .TreatMovesAsRemoveAdd()
                .Bind(out _currentItems)
                .Subscribe()
                .AddToDispose(this);

            //Set Unlocked Filter
            var unlockedFilter = this.WhenAnyValue(m => m.ShowPlanned)
                .Select(filter =>
                {
                    bool Predicate(TaskItemViewModel task) =>
                        task.IsCanBeComplited && (task.IsCompleted == false) &&
                        (filter == null || 
                         (filter == true && 
                          (task.PlannedBeginDateTime != null && task.PlannedBeginDateTime < DateTimeOffset.Now)
                          ) ||
                         (filter == false &&
                          (task.PlannedBeginDateTime == null)
                         )
                         )
                        ;
                    return (Func<TaskItemViewModel, bool>)Predicate;
                });

            //Bind Unlocked
            taskRepository.Tasks
                .Connect()
                .AutoRefreshOnObservable(m => m.WhenAnyValue(m => m.IsCanBeComplited, m => m.IsCompleted, m => m.UnlockedDateTime, m => m.PlannedBeginDateTime))
                .Filter(unlockedFilter)
                .Transform(item =>
                {
                    var actions = new TaskWrapperActions()
                    {
                        ChildSelector = m => m.ContainsTasks.ToObservableChangeSet(),
                        RemoveAction = RemoveTask,
                        GetBreadScrumbs = BredScrumbsAlgorithms.FirstTaskParent,
                    };
                    var wrapper = new TaskWrapperViewModel(null, item, actions);
                    return wrapper;
                })
                .SortBy(m => m.TaskItem.UnlockedDateTime)
                .Bind(out _unlockedItems)
                .Subscribe()
                .AddToDispose(this);

            //Bind Completed
            taskRepository.Tasks
                .Connect()
                .AutoRefreshOnObservable(m => m.WhenAny(m => m.IsCompleted, (c) => c.Value == true))
                .Filter(m => m.IsCompleted == true)
                .Transform(item =>
                {
                    var actions = new TaskWrapperActions()
                    {
                        ChildSelector = m => m.ContainsTasks.ToObservableChangeSet(),
                        RemoveAction = RemoveTask,
                        GetBreadScrumbs = BredScrumbsAlgorithms.FirstTaskParent,
                    };
                    var wrapper = new TaskWrapperViewModel(null, item, actions);
                    return wrapper;
                })
                .SortBy(m => m.TaskItem.CompletedDateTime, SortDirection.Descending)
                .Bind(out _completedItems)
                .Subscribe()
                .AddToDispose(this);

            //Bind Archived
            taskRepository.Tasks
                .Connect()
                .AutoRefreshOnObservable(m => m.WhenAny(m => m.IsCompleted, (c) => c.Value == null))
                .Filter(m => m.IsCompleted == null)
                .Transform(item =>
                {
                    var actions = new TaskWrapperActions()
                    {
                        ChildSelector = m => m.ContainsTasks.ToObservableChangeSet(),
                        RemoveAction = RemoveTask,
                        GetBreadScrumbs = BredScrumbsAlgorithms.FirstTaskParent,
                    };
                    var wrapper = new TaskWrapperViewModel(null, item, actions);
                    return wrapper;
                })
                .SortBy(m => m.TaskItem.ArchiveDateTime, SortDirection.Descending)
                .Bind(out _archivedItems)
                .Subscribe()
                .AddToDispose(this);

            //Bind Current Item Contains
            this.WhenAnyValue(m => m.CurrentItem)
                .Subscribe(item =>
                {
                    if (item != null)
                    {
                        var actions = new TaskWrapperActions()
                        {
                            ChildSelector = m => m.ContainsTasks.ToObservableChangeSet(),
                            RemoveAction = m =>
                            {
                                m.Parent.TaskItem.Contains.Remove(m.TaskItem.Id);
                                m.Parent.TaskItem.SaveItemCommand.Execute(null);
                            },
                            SortComparer = sortObservable
                        };
                        var wrapper = new TaskWrapperViewModel(null, item.TaskItem, actions);
                        CurrentItemContains = wrapper;
                    }
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
                        var actions = new TaskWrapperActions()
                        {
                            ChildSelector = m => m.ParentsTasks.ToObservableChangeSet(),
                            RemoveAction = m =>
                            {
                                m.TaskItem.Contains.Remove(m.Parent.TaskItem.Id);
                                m.TaskItem.SaveItemCommand.Execute(null);
                            },
                            SortComparer = sortObservable
                        };
                        var wrapper = new TaskWrapperViewModel(null, item.TaskItem, actions);
                        CurrentItemParents = wrapper;
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
                        var actions = new TaskWrapperActions()
                        {
                            ChildSelector = m => m.BlocksTasks.ToObservableChangeSet(),
                            RemoveAction = m =>
                            {
                                m.TaskItem.UnblockCommand.Execute(m.Parent.TaskItem);
                                m.TaskItem.SaveItemCommand.Execute(null);
                            },
                            SortComparer = sortObservable
                        };
                        var wrapper = new TaskWrapperViewModel(null, item.TaskItem, actions);
                        CurrentItemBlocks = wrapper;
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
                        var actions = new TaskWrapperActions()
                        {
                            ChildSelector = m => m.BlockedByTasks.ToObservableChangeSet(),
                            RemoveAction = m =>
                            {
                                m.TaskItem.UnblockMeCommand.Execute(m.Parent.TaskItem);
                                m.TaskItem.SaveItemCommand.Execute(null);
                            },
                            SortComparer = sortObservable
                        };
                        var wrapper = new TaskWrapperViewModel(null, item.TaskItem, actions);
                        CurrentItemBlockedBy = wrapper;
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
                        var taskWrapper = CurrentItems.FirstOrDefault(m => m.TaskItem == task);
                        CurrentItem = taskWrapper;
                    }
                    else
                    {
                        var taskWrapper = CurrentItem.Parent.SubTasks.First(m => m.TaskItem == task);
                        CurrentItem = taskWrapper;
                    }
                },
                this.WhenAny(m => m.CurrentItem, m => true));// || !string.IsNullOrWhiteSpace(m.TaskItem.Title)));

            CreateBlockedSibling = ReactiveCommand.Create(() =>
            {
                var parent = CurrentItem;
                if (CurrentItem != null)
                {
                    CreateSibling.Execute(null);
                    parent.TaskItem.Blocks.Add(CurrentItem.TaskItem.Id);
                }
            });

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

            this.WhenAnyValue(m => m.CurrentItem).Subscribe(m =>
            {
                if (_currentItemUpdating > 0) return;
                Interlocked.Increment(ref _currentItemUpdating);
                if (CurrentUnlockedItem?.TaskItem != m?.TaskItem)
                {
                    if (m == null)
                        CurrentUnlockedItem = null;
                    else
                        CurrentUnlockedItem = UnlockedItems.FirstOrDefault(u => u.TaskItem == m.TaskItem);
                }
                if (CurrentCompletedItem?.TaskItem != m?.TaskItem)
                {
                    if (m == null)
                        CurrentCompletedItem = null;
                    else
                        CurrentCompletedItem = CompletedItems.FirstOrDefault(u => u.TaskItem == m.TaskItem);
                }
                if (CurrentArchivedItem?.TaskItem != m?.TaskItem)
                {
                    if (m == null)
                        CurrentArchivedItem = null;
                    else
                        CurrentArchivedItem = ArchivedItems.FirstOrDefault(u => u.TaskItem == m.TaskItem);
                }
                Interlocked.Decrement(ref _currentItemUpdating);
            });

            this.WhenAnyValue(m => m.CurrentUnlockedItem).Subscribe(m =>
            {
                if (_currentItemUpdating > 0) return;
                Interlocked.Increment(ref _currentItemUpdating);
                if (CurrentItem?.TaskItem != m?.TaskItem)
                {
                    if (m == null)
                        CurrentItem = null;
                    else
                    {
                        CurrentItem = FindTaskWrapperViewModel(m);
                    }
                }
                Interlocked.Decrement(ref _currentItemUpdating);
            });

            this.WhenAnyValue(m => m.CurrentCompletedItem).Subscribe(m =>
            {
                if (_currentItemUpdating > 0) return;
                Interlocked.Increment(ref _currentItemUpdating);
                if (CurrentItem?.TaskItem != m?.TaskItem)
                {
                    if (m == null)
                        CurrentItem = null;
                    else
                        CurrentItem = FindTaskWrapperViewModel(m);
                }
                Interlocked.Decrement(ref _currentItemUpdating);
            });

            this.WhenAnyValue(m => m.CurrentArchivedItem).Subscribe(m =>
            {
                if (_currentItemUpdating > 0) return;
                Interlocked.Increment(ref _currentItemUpdating);
                if (CurrentItem?.TaskItem != m?.TaskItem)
                {
                    if (m == null)
                        CurrentItem = null;
                    else
                        CurrentItem = FindTaskWrapperViewModel(m);
                }
                Interlocked.Decrement(ref _currentItemUpdating);
            });
        }

        private void RemoveTask(TaskWrapperViewModel task)
        {
            if (task.TaskItem.RemoveRequiresConfirmation(task.Parent?.TaskItem.Id))
            {
                this.ManagerWrapper.Ask("Remove task",
                    $"Are you sure you want to remove the task \"{task.TaskItem.Title}\" from disk?",
                    () => task.TaskItem.RemoveFunc.Invoke(task.Parent?.TaskItem));
            }
            else
            {
                task.TaskItem.RemoveFunc.Invoke(task.Parent?.TaskItem);
            }
        }

        private TaskWrapperViewModel FindTaskWrapperViewModel(TaskWrapperViewModel taskItemViewModel)
        {
            var selected = CurrentItems;
            foreach (var parent in taskItemViewModel.TaskItem.GetFirstParentsPath())
            {
                selected = selected.FirstOrDefault(p => p.TaskItem == parent).SubTasks;
            }

            var finded = selected.FirstOrDefault(p => p.TaskItem == taskItemViewModel.TaskItem);
            return finded;
        }

        public INotificationManagerWrapper ManagerWrapper { get; }

        public string BreadScrumbs => CurrentItem?.BreadScrumbs;

        private readonly ReadOnlyObservableCollection<TaskWrapperViewModel> _currentItems;
        public ReadOnlyObservableCollection<TaskWrapperViewModel> CurrentItems => _currentItems;

        private readonly ReadOnlyObservableCollection<TaskWrapperViewModel> _unlockedItems;
        public ReadOnlyObservableCollection<TaskWrapperViewModel> UnlockedItems => _unlockedItems;

        private readonly ReadOnlyObservableCollection<TaskWrapperViewModel> _completedItems;
        public ReadOnlyObservableCollection<TaskWrapperViewModel> CompletedItems => _completedItems;

        private readonly ReadOnlyObservableCollection<TaskWrapperViewModel> _archivedItems;
        public ReadOnlyObservableCollection<TaskWrapperViewModel> ArchivedItems => _archivedItems;

        public TaskWrapperViewModel CurrentItem { get; set; }
        public TaskWrapperViewModel CurrentUnlockedItem { get; set; }
        public TaskWrapperViewModel CurrentCompletedItem { get; set; }
        public TaskWrapperViewModel CurrentArchivedItem { get; set; }

        public TaskWrapperViewModel CurrentItemContains { get; private set; }
        public TaskWrapperViewModel CurrentItemParents { get; private set; }
        public TaskWrapperViewModel CurrentItemBlocks { get; private set; }
        public TaskWrapperViewModel CurrentItemBlockedBy { get; private set; }

        public ICommand CreateSibling { get; set; }

        public ICommand CreateBlockedSibling { get; set; }

        public ICommand CreateInner { get; set; }

        private int _currentItemUpdating;

        private IConfiguration _configuration;

        public ObservableCollection<SortDefinition> SortDefinitions { get; } = new(SortDefinition.GetDefinitions());
        public SortDefinition CurrentSortDefinition { get; set; }

        public bool ShowCompleted { get; set; }

        public bool ShowArchived { get; set; }

        public bool? ShowPlanned { get; set; }
    }
}
