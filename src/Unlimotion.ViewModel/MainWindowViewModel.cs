using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
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
        private DisposableList connectionDisposableList = new DisposableListRealization();

        public MainWindowViewModel()
        {
            Locator.CurrentMutable.RegisterConstant(this);
            connectionDisposableList.AddToDispose(this);
            ManagerWrapper = Locator.Current.GetService<INotificationManagerWrapper>();
            _configuration = Locator.Current.GetService<IConfiguration>();
            Settings = new SettingsViewModel(_configuration);
            Graph = new GraphViewModel();
            Locator.CurrentMutable.RegisterConstant(Settings);
            ShowCompleted = _configuration.GetSection("AllTasks:ShowCompleted").Get<bool?>() == true;
            ShowArchived = _configuration.GetSection("AllTasks:ShowArchived").Get<bool?>() == true;
            ShowWanted = _configuration.GetSection("AllTasks:ShowWanted").Get<bool?>() == true;
            var sortName = _configuration.GetSection("AllTasks:CurrentSortDefinition").Get<string>();
            CurrentSortDefinition = SortDefinitions.FirstOrDefault(s => s.Name == sortName) ?? SortDefinitions.First();
            
            this.WhenAnyValue(m => m.ShowCompleted)
                .Subscribe(b => _configuration.GetSection("AllTasks:ShowCompleted").Set(b))
                .AddToDispose(this);
            this.WhenAnyValue(m => m.ShowArchived)
                .Subscribe(b => _configuration.GetSection("AllTasks:ShowArchived").Set(b))
                .AddToDispose(this);
            this.WhenAnyValue(m => m.ShowWanted)
                .Subscribe(b => _configuration.GetSection("AllTasks:ShowWanted").Set(b))
                .AddToDispose(this);
            this.WhenAnyValue(m => m.CurrentSortDefinition)
                .Subscribe(b => _configuration.GetSection("AllTasks:CurrentSortDefinition").Set(b.Name))
                .AddToDispose(this);

            var conn = ReactiveCommand.CreateFromTask(Connect)
                .AddToDisposeAndReturn(this);
            Settings.ConnectCommand = conn;

            conn.Execute().Subscribe(RegisterCommands);
        }

        private void RegisterCommands(Unit unit)
        {
            CreateSibling = ReactiveCommand.CreateFromTask(async () =>
            {
                if (CurrentTaskItem != null && string.IsNullOrWhiteSpace(CurrentTaskItem.Title))
                    return;
                var taskRepository = Locator.Current.GetService<ITaskRepository>();
                var task = new TaskItemViewModel(new TaskItem(), taskRepository);
                await task.SaveItemCommand.Execute();
                if (CurrentTaskItem != null)
                {
                    if (AllTasksMode && CurrentItem?.Parent != null)
                    {
                        CurrentItem.Parent.TaskItem.Contains.Add(task.Id);
                        if (CurrentItem.Parent.TaskItem.BlockedByTasks.Count > 0)
                            CreateComputedBlockedTaskInfo(task.Id, CurrentItem.Parent.TaskItem.Id);
                    }
                    else if (CurrentTaskItem?.ParentsTasks.Count > 0)
                    {
                        var firstParent = CurrentTaskItem.ParentsTasks.First();
                        firstParent.Contains.Add(task.Id);
                        if (CurrentItem.Parent.TaskItem.BlockedByTasks.Count > 0)
                            CreateComputedBlockedTaskInfo(task.Id, firstParent.Id);
                    }
                }

                taskRepository.Tasks.AddOrUpdate(task);

                CurrentTaskItem = task;
                SelectCurrentTask();
            }).AddToDisposeAndReturn(connectionDisposableList);

            CreateBlockedSibling = ReactiveCommand.CreateFromTask(async () =>
            {
                var parent = CurrentTaskItem;
                if (CurrentTaskItem != null)
                {
                    CreateSibling.Execute(null);
                    parent.Blocks.Add(CurrentTaskItem.Id);
                    CreateComputedBlockedTaskInfo(CurrentTaskItem.Id, parent.Id);
                }
            }).AddToDisposeAndReturn(connectionDisposableList);

            CreateInner = ReactiveCommand.CreateFromTask(async () =>
            {
                if (CurrentTaskItem == null)
                    return;
                if (string.IsNullOrWhiteSpace(CurrentTaskItem.Title))
                    return;
                var taskRepository = Locator.Current.GetService<ITaskRepository>();
                var task = new TaskItemViewModel(new TaskItem(), taskRepository);
                await task.SaveItemCommand.Execute();
                CurrentTaskItem.Contains.Add(task.Id);

                if (CurrentTaskItem.BlockedByTasks.Count != 0)
                {
                    CreateComputedBlockedTaskInfo(task.Id, CurrentTaskItem.Id);
                }

                taskRepository.Tasks.AddOrUpdate(task);

                CurrentTaskItem = task;
                SelectCurrentTask();
            }).AddToDisposeAndReturn(connectionDisposableList);

            //Select CurrentTaskItem from all tabs
            this.WhenAnyValue(m => m.CurrentItem)
                .Subscribe(m =>
                {
                    if (m != null || CurrentTaskItem == null)
                        CurrentTaskItem = m?.TaskItem;
                })
                .AddToDispose(connectionDisposableList);

            this.WhenAnyValue(m => m.CurrentUnlockedItem)
                .Subscribe(m =>
                {
                    if (m != null || CurrentTaskItem == null)
                        CurrentTaskItem = m?.TaskItem;
                })
                .AddToDispose(connectionDisposableList);

            this.WhenAnyValue(m => m.CurrentCompletedItem)
                .Subscribe(m =>
                {
                    if (m != null || CurrentTaskItem == null)
                        CurrentTaskItem = m?.TaskItem;
                })
                .AddToDispose(connectionDisposableList);

            this.WhenAnyValue(m => m.CurrentArchivedItem)
                .Subscribe(m =>
                {
                    if (m != null || CurrentTaskItem == null)
                        CurrentTaskItem = m?.TaskItem;
                })
                .AddToDispose(connectionDisposableList);

            this.WhenAnyValue(m => m.CurrentGraphItem)
                .Subscribe(m =>
                {
                    if (m != null || CurrentTaskItem == null)
                        CurrentTaskItem = m?.TaskItem;
                })
                .AddToDispose(connectionDisposableList);

            this.WhenAnyValue(m => m.AllTasksMode, m => m.UnlockedMode, m => m.CompletedMode, m => m.ArchivedMode, m=>m.GraphMode)
                .Subscribe((a) => { SelectCurrentTask(); })
                .AddToDispose(connectionDisposableList);

            AllEmojiFilter.WhenAnyValue(f => f.ShowTasks)
                .Subscribe(b =>
                {
                    foreach (var filter in EmojiFilters)
                    {
                        filter.ShowTasks = b;
                    }
                })
                .AddToDispose(connectionDisposableList);
            ;
        }

        private async Task Connect()
        {
            connectionDisposableList.Dispose();
            connectionDisposableList.Disposables.Clear();

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

            var taskStorage = Locator.Current.GetService<ITaskStorage>();
            await taskStorage.Connect();

            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            taskRepository.Init();

            //Bind Roots
            taskRepository.GetRoots()
                .AutoRefreshOnObservable(m => m.Contains.ToObservableChangeSet())
                .AutoRefreshOnObservable(m => m.WhenAny(m => m.IsCanBeCompleted, m => m.IsCompleted, m => m.UnlockedDateTime, (c, d, u) => c.Value && (d.Value == false)))
                .Filter(taskFilter)
                .Transform(item =>
                {
                    if (item.Parents.Count > 0)
                    {
                        return null;
                    }
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
                .AddToDispose(connectionDisposableList);
            
            CurrentItems = _currentItems;

            //Bind Emoji
            taskRepository.Tasks
                .Connect()
                .AutoRefreshOnObservable(m => m.WhenAny(m => m.Emoji, (c) => c.Value == null))
                .Group(m => m.Emoji)
                .Transform(m =>
                {
                    if (m.Key == "")
                    {
                        return AllEmojiFilter;
                    }

                    var first = m.Cache.Items.First();
                    var filter = new EmojiFilter();
                    filter.ShowTasks = true;
                    filter.Title = first.Title;
                    filter.Emoji = first.Emoji;
                    filter.SortText = (first.Title??"").Replace(first.Emoji, "").Trim();
                    return filter;
                })
                .SortBy(f => f.SortText)
                .Bind(out _emojiFilters)
                .Subscribe()
                .AddToDispose(connectionDisposableList);

            EmojiFilters = _emojiFilters;
            Graph.EmojiFilters = _emojiFilters;

            var wantedFilter = this.WhenAnyValue(m => m.ShowWanted)
                .Select(filter =>
                {
                    bool Predicate(TaskItemViewModel task)
                    {
                        if (!filter.HasValue)
                        {
                            return true;
                        }

                        if (filter.Value)
                        {
                            return task.Wanted;
                        }

                        return !task.Wanted;
                    }

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
                            if (string.IsNullOrEmpty(item?.Emoji)) continue;

                            if (task.GetAllEmoji.Contains(item.Emoji) || (task.Title ?? "").Contains(item.Emoji))
                                return true;
                        }

                        return false;
                    }
                    return (Func<TaskItemViewModel, bool>)Predicate;
                });
            
            var unlockedTimeFilter = UnlockedTimeFilters.ToObservableChangeSet()
                .AutoRefreshOnObservable(filter => filter.WhenAnyValue(e => e.ShowTasks))
                .ToCollection()
                .Select(filter =>
                {
                    bool Predicate(TaskItemViewModel task)
                    {
                        return UnlockedTimeFilter.IsUnlocked(task) && 
                               (filter.All(e => e.ShowTasks == false) || 
                               filter.Where(e => e.ShowTasks).Any(item => item.Predicate(task)));
                    }
                    return (Func<TaskItemViewModel, bool>)Predicate;
                });


            //Bind Unlocked
            taskRepository.Tasks
                .Connect()
                .AutoRefreshOnObservable(m => m.WhenAnyValue(m => m.IsCanBeCompleted, m => m.IsCompleted, m => m.UnlockedDateTime, m => m.PlannedBeginDateTime, m => m.Wanted))
                .Filter(unlockedTimeFilter)
                .Filter(emojiFilter)
                .Filter(wantedFilter)
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
                .AddToDispose(connectionDisposableList);

            UnlockedItems = _unlockedItems;

            Graph.Tasks = CurrentItems;
            taskRepository.Tasks
                .Connect()
                .Filter(taskFilter)
                .Filter(emojiFilter)
                .Filter(wantedFilter)
                .Transform(item =>
                {
                    var actions = new TaskWrapperActions()
                    {
                        ChildSelector = m => m.ContainsTasks.ToObservableChangeSet(),
                        RemoveAction = RemoveTask,
                        GetBreadScrumbs = BredScrumbsAlgorithms.FirstTaskParent,
                        Filter = taskFilter,
                    };
                    var wrapper = new TaskWrapperViewModel(null, item, actions);
                    return wrapper;
                })
                .Bind(out _FilteredItems)
                .Subscribe()
                .AddToDispose(connectionDisposableList);
            Graph.UnlockedTasks = _FilteredItems;

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
                .AddToDispose(connectionDisposableList);

            CompletedItems = _completedItems;

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
                .AddToDispose(connectionDisposableList);

            ArchivedItems = _archivedItems;

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
                .AddToDispose(connectionDisposableList);

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
                .AddToDispose(connectionDisposableList);

            //Bind Current Item Blocks
            this.WhenAnyValue(m => m.CurrentTaskItem)
                .Subscribe(item =>
                {
                    if (item != null)
                    {
                        var actions = new TaskWrapperActions
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
                .AddToDispose(connectionDisposableList);

            //Bind Current Item BlockedBy
            this.WhenAnyValue(m => m.CurrentTaskItem)
                .Subscribe(item =>
                {
                    if (item != null)
                    {
                        var actions = new TaskWrapperActions
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
                .AddToDispose(connectionDisposableList);
        }

        private void SelectCurrentTask()
        {
            if (AllTasksMode ^ UnlockedMode ^ CompletedMode ^ ArchivedMode ^ GraphMode)
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
                else if (GraphMode)
                {
                    CurrentGraphItem = FindTaskWrapperViewModel(CurrentTaskItem, ArchivedItems);
                }
            }
        }

        private void RemoveTask(TaskWrapperViewModel task)
        {
            if (task.TaskItem.RemoveRequiresConfirmation(task.Parent?.TaskItem.Id))
            {
                ManagerWrapper.Ask("Remove task",
                    $"Are you sure you want to remove the task \"{task.TaskItem.Title}\" from disk?",
                    () => task.TaskItem.RemoveFunc.Invoke(task.Parent?.TaskItem));
            }
            else
            {
                task.TaskItem.RemoveFunc.Invoke(task.Parent?.TaskItem);
            }
        }
        
        private void CreateComputedBlockedTaskInfo(string blockedTaskId, string blockingTaskId)
        {
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            taskRepository.ComputedTasksInfo.AddOrUpdate(new ComputedTaskInfo
            {
                TaskId = blockedTaskId,
                FromIds = new List<string> {blockingTaskId}
            });
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
        public bool GraphMode { get; set; }
        public bool SettingsMode { get; set; }

        public INotificationManagerWrapper ManagerWrapper { get; }

        public string BreadScrumbs => AllTasksMode ? CurrentItem?.BreadScrumbs : BredScrumbsAlgorithms.FirstTaskParent(CurrentTaskItem);

        private ReadOnlyObservableCollection<TaskWrapperViewModel> _currentItems;
        public ReadOnlyObservableCollection<TaskWrapperViewModel> CurrentItems { get; set; }

        private ReadOnlyObservableCollection<TaskWrapperViewModel> _unlockedItems;
        public ReadOnlyObservableCollection<TaskWrapperViewModel> UnlockedItems { get; set; }

        private ReadOnlyObservableCollection<TaskWrapperViewModel> _completedItems;
        public ReadOnlyObservableCollection<TaskWrapperViewModel> CompletedItems { get; set; }

        private ReadOnlyObservableCollection<TaskWrapperViewModel> _archivedItems;
        public ReadOnlyObservableCollection<TaskWrapperViewModel> ArchivedItems { get; set; }

        private ReadOnlyObservableCollection<TaskWrapperViewModel> _FilteredItems;

        public TaskItemViewModel CurrentTaskItem { get; set; }
        public TaskWrapperViewModel CurrentItem { get; set; }
        public TaskWrapperViewModel CurrentUnlockedItem { get; set; }
        public TaskWrapperViewModel CurrentCompletedItem { get; set; }
        public TaskWrapperViewModel CurrentArchivedItem { get; set; }
        public TaskWrapperViewModel CurrentGraphItem { get; set; }

        public TaskWrapperViewModel CurrentItemContains { get; private set; }
        public TaskWrapperViewModel CurrentItemParents { get; private set; }
        public TaskWrapperViewModel CurrentItemBlocks { get; private set; }
        public TaskWrapperViewModel CurrentItemBlockedBy { get; private set; }

        public ICommand CreateSibling { get; set; }

        public ICommand CreateBlockedSibling { get; set; }

        public ICommand CreateInner { get; set; }

        public ICommand MoveToPath { get; set; }

        private IConfiguration _configuration;

        public ObservableCollection<SortDefinition> SortDefinitions { get; } = new(SortDefinition.GetDefinitions());
        public SortDefinition CurrentSortDefinition { get; set; }

        public bool ShowCompleted { get; set; }

        public bool ShowArchived { get; set; }

        public bool? ShowWanted { get; set; }

        public SettingsViewModel Settings { get; set; }
        public GraphViewModel Graph { get; set; }

        private ReadOnlyObservableCollection<EmojiFilter> _emojiFilters;
        public ReadOnlyObservableCollection<EmojiFilter> EmojiFilters { get; set; }

        public EmojiFilter AllEmojiFilter { get; } = new() { Emoji = "", Title = "All", ShowTasks = true, SortText = "\u0000"};

        public ReadOnlyObservableCollection<UnlockedTimeFilter> UnlockedTimeFilters { get; set; } = UnlockedTimeFilter.GetDefinitions();
    }

    [AddINotifyPropertyChangedInterface]
    public class EmojiFilter
    {
        public string Title { get; set; }
        public string Emoji { get; set; }
        public bool ShowTasks { get; set; }
        public string SortText { get; set; }
    }
}