using System;
using System.Collections.Generic;
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
            Settings = new SettingsViewModel(_configuration);
            ShowCompleted = _configuration.GetSection("AllTasks:ShowCompleted").Get<bool?>() == true;
            ShowArchived = _configuration.GetSection("AllTasks:ShowArchived").Get<bool?>() == true;
            ShowPlanned = _configuration.GetSection("AllTasks:ShowPlanned").Get<bool?>() == true;
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
                .AutoRefreshOnObservable(m => m.WhenAny(m => m.IsCanBeCompleted, m => m.IsCompleted, m => m.UnlockedDateTime, (c, d, u) => c.Value && (d.Value == false)))
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


            //Bind Emodji
            taskRepository.Tasks
                .Connect()
                .AutoRefreshOnObservable(m => m.WhenAny(m => m.Emoji, (c) => c.Value == null))
                .DistinctValues(m => m.Emoji)
                .Transform(m =>
                {
                    if (m == "")
                    {
                        return AllEmojiFilter;
                    }
                    return new EmojiFilter() { Emoji = m, ShowTasks = true };
                })
                .Bind(out _emojiFilters)
                .Subscribe()
                .AddToDispose(this);

            //Set Unlocked Filter
            var unlockedFilter = this.WhenAnyValue(m => m.ShowPlanned)
                .Select(filter =>
                {
                    bool Predicate(TaskItemViewModel task) =>
                        task.IsCanBeCompleted && (task.IsCompleted == false) &&
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
            
            var emojiFilter = _emojiFilters.ToObservableChangeSet()
                .AutoRefreshOnObservable(filter => filter.WhenAnyValue(e => e.ShowTasks))
                .ToCollection()
                .Select(filter =>
                {
                    bool Predicate(TaskItemViewModel task)
                    {
                        if (filter.All(e => e.ShowTasks == false))
                        {
                            return true;
                        }
                        foreach (var item in filter.Where(e => e.ShowTasks))
                        {
                            if (task.GetAllEmoji.Contains(item.Emoji))
                                    return true;
                        }

                        return false;
                    }

                    ;
                    return (Func<TaskItemViewModel, bool>)Predicate;
                });

            //Bind Unlocked
            taskRepository.Tasks
                .Connect()
                .AutoRefreshOnObservable(m => m.WhenAnyValue(m => m.IsCanBeCompleted, m => m.IsCompleted, m => m.UnlockedDateTime, m => m.PlannedBeginDateTime))
                .Filter(unlockedFilter)
                .Filter(emojiFilter)
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
                .Filter(emojiFilter)
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
                .Filter(emojiFilter)
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
            this.WhenAnyValue(m => m.CurrentTaskItem)
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
                            },
                            SortComparer = sortObservable
                        };
                        var wrapper = new TaskWrapperViewModel(null, item, actions);
                        CurrentItemContains = wrapper;
                    }
                    else
                    {
                        CurrentItemContains = null;
                    }
                })
                .AddToDispose(this);

            //Bind Current Item Parents
            this.WhenAnyValue(m => m.CurrentTaskItem)
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
                            },
                            SortComparer = sortObservable
                        };
                        var wrapper = new TaskWrapperViewModel(null, item, actions);
                        CurrentItemParents = wrapper;
                    }
                    else
                    {
                        CurrentItemParents = null;
                    }
                })
                .AddToDispose(this);

            //Bind Current Item Blocks
            this.WhenAnyValue(m => m.CurrentTaskItem)
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
                            },
                            SortComparer = sortObservable
                        };
                        var wrapper = new TaskWrapperViewModel(null, item, actions);
                        CurrentItemBlocks = wrapper;
                    }
                    else
                    {
                        CurrentItemBlocks = null;
                    }
                })
                .AddToDispose(this);

            //Bind Current Item BlockedBy
            this.WhenAnyValue(m => m.CurrentTaskItem)
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
                            },
                            SortComparer = sortObservable
                        };
                        var wrapper = new TaskWrapperViewModel(null, item, actions);
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
                if (CurrentTaskItem == null || string.IsNullOrWhiteSpace(CurrentTaskItem.Title))
                    return;
                var task = new TaskItemViewModel(new TaskItem(), taskRepository);
                task.SaveItemCommand.Execute(null);
                if (CurrentItem?.Parent != null)
                {
                    CurrentItem.Parent.TaskItem.Contains.Add(task.Id);
                }
                else if (CurrentTaskItem.ParentsTasks.Count>0)
                {
                    CurrentTaskItem.ParentsTasks.First().Contains.Add(task.Id);
                }
                taskRepository.Tasks.AddOrUpdate(task);

                CurrentTaskItem = task;
                SelectCurrentTask();
            });

            CreateBlockedSibling = ReactiveCommand.Create(() =>
            {
                var parent = CurrentTaskItem;
                if (CurrentTaskItem != null)
                {
                    CreateSibling.Execute(null);
                    parent.Blocks.Add(CurrentTaskItem.Id);
                }
            });

            CreateInner = ReactiveCommand.Create(() =>
            {
                if (CurrentTaskItem == null)
                    return;
                if (string.IsNullOrWhiteSpace(CurrentTaskItem.Title))
                    return;
                var task = new TaskItemViewModel(new TaskItem(), taskRepository);
                task.SaveItemCommand.Execute(null);
                CurrentTaskItem.Contains.Add(task.Id);
                taskRepository.Tasks.AddOrUpdate(task);

                CurrentTaskItem = task;
                SelectCurrentTask();
            });

            //Select CurrentTaskItem from all tabs
            this.WhenAnyValue(m => m.CurrentItem)
                .Subscribe(m =>
                {
                    if (m != null || CurrentTaskItem == null)
                        CurrentTaskItem = m?.TaskItem;
                })
                .AddToDispose(this);

            this.WhenAnyValue(m => m.CurrentUnlockedItem)
                .Subscribe(m =>
                {
                    if (m != null || CurrentTaskItem == null)
                        CurrentTaskItem = m?.TaskItem;
                })
                .AddToDispose(this);

            this.WhenAnyValue(m => m.CurrentCompletedItem)
                .Subscribe(m =>
                {
                    if (m != null || CurrentTaskItem == null)
                        CurrentTaskItem = m?.TaskItem;
                })
                .AddToDispose(this);

            this.WhenAnyValue(m => m.CurrentArchivedItem)
                .Subscribe(m =>
                {
                    if (m != null || CurrentTaskItem == null)
                        CurrentTaskItem = m?.TaskItem;
                })
                .AddToDispose(this);

            this.WhenAnyValue(m => m.AllTasksMode, m => m.UnlockedMode, m => m.CompletedMode, m => m.ArchivedMode)
                .Subscribe((a) => { SelectCurrentTask(); })
                .AddToDispose(this);

            AllEmojiFilter.WhenAnyValue(f => f.ShowTasks)
                .Subscribe(b =>
                {
                    foreach (var filter in EmojiFilters)
                    {
                        filter.ShowTasks = b;
                    }
                })
                .AddToDispose(this);
        }

        private void SelectCurrentTask()
        {
            if (AllTasksMode ^ UnlockedMode ^ CompletedMode ^ ArchivedMode)
            {
                if (AllTasksMode)
                {
                    CurrentItem = FindTaskWrapperViewModel(CurrentTaskItem, CurrentItems);
                }
                else if (UnlockedMode)
                {
                    CurrentUnlockedItem = FindTaskWrapperViewModel(CurrentTaskItem, UnlockedItems);
                }
                else if (CompletedMode)
                {
                    CurrentCompletedItem = FindTaskWrapperViewModel(CurrentTaskItem, CompletedItems);
                }
                else if (ArchivedMode)
                {
                    CurrentArchivedItem = FindTaskWrapperViewModel(CurrentTaskItem, ArchivedItems);
                }
            }
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

        public TaskWrapperViewModel FindTaskWrapperViewModel(TaskItemViewModel taskItemViewModel, ReadOnlyObservableCollection<TaskWrapperViewModel> source)
        {
            if (taskItemViewModel == null)
            {
                return null;
            }

            //Прямой поиск по коллекции
            var finded = source.FirstOrDefault(t => t.TaskItem == taskItemViewModel);
            if (finded != null)
            {
                return finded;
            }

            //Поиск по родителям
            var selected = source;
            foreach (var parent in taskItemViewModel.GetFirstParentsPath())
            {
                selected = selected?.FirstOrDefault(p => p.TaskItem == parent)?.SubTasks;
            }

            finded = selected?.FirstOrDefault(p => p.TaskItem == taskItemViewModel);
            return finded;
        }
        public bool AllTasksMode { get; set; }
        public bool UnlockedMode { get; set; }
        public bool CompletedMode { get; set; }
        public bool ArchivedMode { get; set; }
        public bool SettingsMode { get; set; }

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

        public TaskItemViewModel CurrentTaskItem { get; set; }
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

        private IConfiguration _configuration;

        public ObservableCollection<SortDefinition> SortDefinitions { get; } = new(SortDefinition.GetDefinitions());
        public SortDefinition CurrentSortDefinition { get; set; }

        public bool ShowCompleted { get; set; }

        public bool ShowArchived { get; set; }

        public bool? ShowPlanned { get; set; }

        public SettingsViewModel Settings { get; set; }

        private ReadOnlyObservableCollection<EmojiFilter> _emojiFilters;
        public ReadOnlyObservableCollection<EmojiFilter> EmojiFilters => _emojiFilters;

        public EmojiFilter AllEmojiFilter { get; } = new EmojiFilter() { Emoji = "All", ShowTasks = true };
    }

    [AddINotifyPropertyChangedInterface]
    public class EmojiFilter
    {
        public string Emoji { get; set; }
        public bool ShowTasks { get; set; }
    }
}
