using DynamicData;
using DynamicData.Binding;
using Microsoft.Extensions.Configuration;
using PropertyChanged;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Unlimotion.ViewModel.Search;
using Unlimotion.ViewModel.Localization;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel
{
    public enum TreeCommandKind
    {
        ExpandCurrentNested,
        CollapseCurrentNested,
        ExpandAll,
        CollapseAll,
        DeleteSelection,
        SelectAll
    }

    [AddINotifyPropertyChangedInterface]
    public class MainWindowViewModel : DisposableList
    {
        public bool IsInitialized { get; private set; }
        private DisposableList connectionDisposableList = new DisposableListRealization();
        private bool _isCompletedTabInitialized;
        private bool _isArchivedTabInitialized;
        private bool _isLastCreatedTabInitialized;
        private bool _isLastUpdatedTabInitialized;
        private bool _isRoadmapTabInitialized;
        private bool _isUnlockedTabInitialized;
        private bool _isLastOpenedTabInitialized;
        private static readonly ReadOnlyObservableCollection<TaskWrapperViewModel> EmptyTaskWrappers =
            new(new ObservableCollectionExtended<TaskWrapperViewModel>());

        public ITaskStorage? taskRepository;
        private readonly Func<ITaskStorage?>? _getTaskStorage;

        public MainWindowViewModel(
            IAppNameDefinitionService? appNameService,
            INotificationManagerWrapper managerWrapper,
            IConfiguration configuration,
            Func<ITaskStorage?>? getTaskStorage = null,
            SettingsViewModel? settings = null,
            GraphViewModel? graph = null)
        {
            Title = appNameService?.GetAppName() ?? "";
            connectionDisposableList.AddToDispose(this);
            ManagerWrapper = managerWrapper;
            _configuration = configuration;
            _getTaskStorage = getTaskStorage;
            Settings = settings ?? new SettingsViewModel(_configuration);
            Graph = graph ?? new GraphViewModel();
            CurrentAllTasksItems = EmptyTaskWrappers;
            UnlockedItems = EmptyTaskWrappers;
            CompletedItems = EmptyTaskWrappers;
            ArchivedItems = EmptyTaskWrappers;
            LastCreatedItems = EmptyTaskWrappers;
            LastUpdatedItems = EmptyTaskWrappers;
            LastOpenedItems = EmptyTaskWrappers;
            Graph.SetMainWindowViewModel(this);
            Graph.Search = Search;
            Search.IsFuzzySearch = Settings.IsFuzzySearch;
            ShowCompleted = _configuration?.GetSection("AllTasks:ShowCompleted").Get<bool?>() == true;
            ShowArchived = _configuration?.GetSection("AllTasks:ShowArchived").Get<bool?>() == true;
            ShowWanted = _configuration?.GetSection("AllTasks:ShowWanted").Get<bool?>() == true;
            var sortName = _configuration?.GetSection("AllTasks:CurrentSortDefinition").Get<string>();
            var sortNameForUnlocked = _configuration?.GetSection("AllTasks:CurrentSortDefinitionForUnlocked").Get<string>();
            CurrentSortDefinition = SortDefinitions.FirstOrDefault(s => s.MatchesPersistedValue(sortName)) ?? SortDefinitions.First();
            CurrentSortDefinitionForUnlocked = SortDefinitions.FirstOrDefault(s => s.MatchesPersistedValue(sortNameForUnlocked)) ?? SortDefinitions.First();

            this.WhenAnyValue(m => m.ShowCompleted)
                .Subscribe(b => _configuration?.GetSection("AllTasks:ShowCompleted").Set(b))
                .AddToDispose(this);
            this.WhenAnyValue(m => m.ShowArchived)
                .Subscribe(b => _configuration?.GetSection("AllTasks:ShowArchived").Set(b))
                .AddToDispose(this);
            this.WhenAnyValue(m => m.ShowWanted)
                .Subscribe(b => _configuration?.GetSection("AllTasks:ShowWanted").Set(b))
                .AddToDispose(this);
            this.WhenAnyValue(m => m.CurrentSortDefinition)
                .Subscribe(b =>
                {
                    if (b != null)
                    {
                        _configuration?.GetSection("AllTasks:CurrentSortDefinition").Set(b.Id);
                    }
                })
                .AddToDispose(this);
            this.WhenAnyValue(m => m.CurrentSortDefinitionForUnlocked)
                .Subscribe(b =>
                {
                    if (b != null)
                    {
                        _configuration?.GetSection("AllTasks:CurrentSortDefinitionForUnlocked").Set(b.Id);
                    }
                })
                .AddToDispose(this);
            this.WhenAnyValue(m => m.Settings.IsFuzzySearch)
                .Subscribe(b => Search.IsFuzzySearch = b)
                .AddToDispose(this);
            CurrentRelationEditor = new TaskRelationEditorViewModel(
                () => taskRepository?.Tasks.Items ?? Enumerable.Empty<TaskItemViewModel>(),
                FindTaskById,
                () => Settings.IsFuzzySearch,
                IsRelationCandidateValid,
                TryAddRelationAsync,
                GetRelationCandidateContext,
                ManagerWrapper,
                LocalizationService.Current);
            var localization = LocalizationService.Current;
            EventHandler localizationChanged = (_, __) => RefreshLocalizedCollections();
            localization.CultureChanged += localizationChanged;
            Disposable.Create(() => localization.CultureChanged -= localizationChanged).AddToDispose(this);
            Disposable.Create(() => CurrentRelationEditor.Dispose()).AddToDispose(this);
        }

        private void RefreshLocalizedCollections()
        {
            var currentSortId = CurrentSortDefinition?.Id;
            var currentUnlockedSortId = CurrentSortDefinitionForUnlocked?.Id;

            SortDefinitions.Clear();
            foreach (var sortDefinition in SortDefinition.GetDefinitions())
            {
                SortDefinitions.Add(sortDefinition);
            }

            CurrentSortDefinition = SortDefinitions.FirstOrDefault(s => s.Id == currentSortId) ?? SortDefinitions.First();
            CurrentSortDefinitionForUnlocked = SortDefinitions.FirstOrDefault(s => s.Id == currentUnlockedSortId) ?? SortDefinitions.First();

            var completedDateFilterId = CompletedDateFilter.CurrentOption?.Id;
            var archivedDateFilterId = ArchivedDateFilter.CurrentOption?.Id;
            var lastCreatedDateFilterId = LastCreatedDateFilter.CurrentOption?.Id;
            var lastUpdatedDateFilterId = LastUpdatedDateFilter.CurrentOption?.Id;

            DateFilterDefinitions = DateFilterDefinition.GetDefinitions();
            CompletedDateFilter.CurrentOption = DateFilterDefinition.FindById(completedDateFilterId);
            ArchivedDateFilter.CurrentOption = DateFilterDefinition.FindById(archivedDateFilterId);
            LastCreatedDateFilter.CurrentOption = DateFilterDefinition.FindById(lastCreatedDateFilterId);
            LastUpdatedDateFilter.CurrentOption = DateFilterDefinition.FindById(lastUpdatedDateFilterId);

            foreach (var filter in UnlockedTimeFilters)
            {
                filter.RefreshLocalization();
            }

            foreach (var filter in DurationFilters)
            {
                filter.RefreshLocalization();
            }
        }

        private void RegisterCommands()
        {
            Create = ReactiveCommand.CreateFromTask(async () =>
            {
                var newTask = await taskRepository?.Add();
                CurrentTaskItem = newTask;
                SelectCurrentTask();
                if (newTask != null)
                {
                    RequestTitleFocusForCurrentTask();
                }

            }).AddToDisposeAndReturn(connectionDisposableList);
            CreateSibling = ReactiveCommand.CreateFromTask(async (bool isBlocked = false) =>
            {
                if (CurrentTaskItem != null && string.IsNullOrWhiteSpace(CurrentTaskItem.Title))
                    return;

                TaskItemViewModel? newTask = null;
                if (CurrentTaskItem != null)
                {
                    if (AllTasksMode)
                    {
                        newTask = await taskRepository?.Add(CurrentTaskItem, isBlocked);
                        CurrentTaskItem = newTask;
                    }
                }
                
                SelectCurrentTask();
                if (newTask != null)
                {
                    RequestTitleFocusForCurrentTask();
                }
            }).AddToDisposeAndReturn(connectionDisposableList);

            CreateBlockedSibling = ReactiveCommand.CreateFromTask(async () =>
            {
                var parent = CurrentTaskItem;
                if (CurrentTaskItem != null)
                {
                    CreateSibling.Execute(true);
                }
            }).AddToDisposeAndReturn(connectionDisposableList);

            CreateInner = ReactiveCommand.CreateFromTask(async () =>
            {
                if (CurrentTaskItem == null)
                    return;
                if (string.IsNullOrWhiteSpace(CurrentTaskItem.Title))
                    return;

                var parent = CurrentTaskItem;
                
                var newTask = await taskRepository?.AddChild(parent);
                CurrentTaskItem = newTask;
                SelectCurrentTask();

                var wrapper = FindTaskWrapperViewModel(parent, CurrentAllTasksItems);
                if (wrapper != null)
                {
                    wrapper.IsExpanded = true;
                    var p = wrapper.Parent;
                    while (p != null)
                    {
                        p.IsExpanded = true;
                        p = p.Parent;
                    }
                }

                if (newTask != null)
                {
                    RequestTitleFocusForCurrentTask();
                }
            }).AddToDisposeAndReturn(connectionDisposableList);

            Remove = ReactiveCommand.CreateFromTask(async () => await RemoveTaskItem(CurrentTaskItem));
            ExpandCurrentNestedCommand = ReactiveCommand.Create(() =>
                ExecuteTreeCommandAction?.Invoke(TreeCommandKind.ExpandCurrentNested))
                .AddToDisposeAndReturn(connectionDisposableList);
            CollapseCurrentNestedCommand = ReactiveCommand.Create(() =>
                ExecuteTreeCommandAction?.Invoke(TreeCommandKind.CollapseCurrentNested))
                .AddToDisposeAndReturn(connectionDisposableList);
            ExpandAllTreeNodesCommand = ReactiveCommand.Create(() =>
                ExecuteTreeCommandAction?.Invoke(TreeCommandKind.ExpandAll))
                .AddToDisposeAndReturn(connectionDisposableList);
            CollapseAllTreeNodesCommand = ReactiveCommand.Create(() =>
                ExecuteTreeCommandAction?.Invoke(TreeCommandKind.CollapseAll))
                .AddToDisposeAndReturn(connectionDisposableList);
            DeleteSelectedTreeItemsCommand = ReactiveCommand.Create(() =>
                ExecuteTreeCommandAction?.Invoke(TreeCommandKind.DeleteSelection))
                .AddToDisposeAndReturn(connectionDisposableList);
            SelectAllTreeItemsCommand = ReactiveCommand.Create(() =>
                ExecuteTreeCommandAction?.Invoke(TreeCommandKind.SelectAll))
                .AddToDisposeAndReturn(connectionDisposableList);

            //Select CurrentTaskItem from all tabs
            this.WhenAnyValue(m => m.CurrentAllTasksItem)
                .Subscribe(m =>
                {
                    if (m != null && CurrentTaskItem != m?.TaskItem)
                        CurrentTaskItem = m?.TaskItem;
                })
                .AddToDispose(connectionDisposableList);

            this.WhenAnyValue(m => m.CurrentUnlockedItem)
                .Subscribe(m =>
                {
                    if (m != null && CurrentTaskItem != m?.TaskItem)
                        CurrentTaskItem = m?.TaskItem;
                })
                .AddToDispose(connectionDisposableList);

            this.WhenAnyValue(m => m.CurrentCompletedItem)
                .Subscribe(m =>
                {
                    if (m != null && CurrentTaskItem != m?.TaskItem)
                        CurrentTaskItem = m?.TaskItem;
                })
                .AddToDispose(connectionDisposableList);

            this.WhenAnyValue(m => m.CurrentArchivedItem)
                .Subscribe(m =>
                {
                    if (m != null && CurrentTaskItem != m?.TaskItem)
                        CurrentTaskItem = m?.TaskItem;
                })
                .AddToDispose(connectionDisposableList);

            this.WhenAnyValue(m => m.CurrentGraphItem)
                .Subscribe(m =>
                {
                    if (m != null && CurrentTaskItem != m?.TaskItem)
                        CurrentTaskItem = m?.TaskItem;
                })
                .AddToDispose(connectionDisposableList);

            this.WhenAnyValue(m => m.CurrentLastCreated)
                .Subscribe(m =>
                {
                    if (m != null && CurrentTaskItem != m?.TaskItem)
                        CurrentTaskItem = m?.TaskItem;
                })
                .AddToDispose(connectionDisposableList);

            this.WhenAnyValue(m => m.CurrentLastUpdated)
                .Subscribe(m =>
                {
                    if (m != null && CurrentTaskItem != m?.TaskItem)
                        CurrentTaskItem = m?.TaskItem;
                })
                .AddToDispose(connectionDisposableList);

            this.WhenAnyValue(m => m.CurrentLastOpenedItem)
                .Subscribe(m =>
                {
                    if (m != null && CurrentTaskItem != m?.TaskItem)
                        CurrentTaskItem = m?.TaskItem;
                })
                .AddToDispose(connectionDisposableList);

            Observable.Merge(
                    this.WhenAnyValue(m => m.AllTasksMode),
                    this.WhenAnyValue(m => m.UnlockedMode),
                    this.WhenAnyValue(m => m.CompletedMode),
                    this.WhenAnyValue(m => m.ArchivedMode),
                    this.WhenAnyValue(m => m.GraphMode),
                    this.WhenAnyValue(m => m.LastCreatedMode),
                    this.WhenAnyValue(m => m.LastUpdatedMode),
                    this.WhenAnyValue(m => m.LastOpenedMode))
                .Subscribe(_ =>
                {
                    SelectCurrentTask();
                })
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

        private void RequestTitleFocusForCurrentTask()
        {
            if (CurrentTaskItem == null)
                return;

            DetailsAreOpen = true;
            TitleFocusRequestVersion++;
        }

        private void AttachTaskContext(TaskItemViewModel taskItem)
        {
            taskItem.NotificationManager = ManagerWrapper;
            taskItem.MainWindow = this;
            taskItem.IsInitializedProvider = () => IsInitialized;
        }

        public async Task Connect()
        {
            IsTasksLoading = true;
            await Task.Yield();

            try
            {
                IsInitialized = false;
                connectionDisposableList.Dispose();
                connectionDisposableList.Disposables.Clear();
                _isCompletedTabInitialized = false;
                _isArchivedTabInitialized = false;
                _isLastCreatedTabInitialized = false;
                _isLastUpdatedTabInitialized = false;
                _isRoadmapTabInitialized = false;
                _isUnlockedTabInitialized = false;
                _isLastOpenedTabInitialized = false;

                //Set sort definition
                var sortObservable = this.WhenAnyValue(m => m.CurrentSortDefinition)
                    .Where(d => d != null)
                    .Select(d => d.Comparer);
                var sortObservableForUnlocked =
                    this.WhenAnyValue(m => m.CurrentSortDefinitionForUnlocked)
                        .Where(d => d != null)
                        .Select(d => d.Comparer);

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

                var taskStorage = _getTaskStorage?.Invoke();

                if (taskStorage == null)
                {
                    ManagerWrapper?.ErrorToast(L10n.Get("TaskStorageNotConfigured"));
                    return;
                }

                if (Settings.IsServerMode)
                {
                    taskStorage.TaskTreeManager.Storage.OnConnectionError += ex =>
                    {
                        ManagerWrapper?.ErrorToast(L10n.Get("ServerConnectionError"));
                    };
                }

                await taskStorage.TaskTreeManager.Storage.Connect();
                taskRepository = taskStorage;

                //Если из коллекции пропадает итем, то очищаем выделенный итем.
                taskRepository.Tasks.Connect()
                    .OnItemAdded(AttachTaskContext)
                    .OnItemRemoved(x =>
                    {
                        if (CurrentTaskItem?.Id == x.Id) CurrentTaskItem = null;
                    })
                    .OnItemUpdated((newItem, oldItem) =>
                    {
                        AttachTaskContext(newItem);
                        if (newItem.Id == CurrentTaskItem?.Id && newItem.Id == oldItem.Id) CurrentTaskItem = newItem;
                    })
                    .Subscribe()
                    .AddToDispose(connectionDisposableList);

            //Bind Emoji

            #region Emoji

            taskRepository.Tasks
                .Connect()
                .AutoRefreshOnObservable(m => m.WhenAny(m => m.Emoji, c => c.Value == null))
                .Group(m => m.Emoji)
                .Transform(m =>
                {
                    if (m.Key == "")
                    {
                        return AllEmojiFilter;
                    }

                    var first = m.Cache.Items.First();
                    var filter = new EmojiFilter();
                    filter.Source = first;
                    filter.ShowTasks = false;
                    filter.Title = first.Title;
                    filter.Emoji = first.Emoji;
                    filter.SortText = (first.Title ?? "").Replace(first.Emoji, "").Trim();
                    return filter;
                })
                .SortBy(f => f.SortText)
                .Bind(out _emojiFilters)
                .Subscribe()
                .AddToDispose(connectionDisposableList);

            EmojiFilters = _emojiFilters;
            Graph.EmojiFilters = _emojiFilters;

            taskRepository.Tasks
                .Connect()
                .AutoRefreshOnObservable(m => m.WhenAny(m => m.Emoji, c => c.Value == null))
                .Group(m => m.Emoji)
                .Transform(m =>
                {
                    if (m.Key == "")
                    {
                        return AllEmojiExcludeFilter;
                    }

                    var first = m.Cache.Items.First();
                    var filter = new EmojiFilter();
                    filter.Source = first;
                    filter.ShowTasks = false;
                    filter.Title = first.Title;
                    filter.Emoji = first.Emoji;
                    filter.SortText = (first.Title ?? "").Replace(first.Emoji, "").Trim();
                    return filter;
                })
                .SortBy(f => f.SortText)
                .Bind(out _emojiExcludeFilters)
                .Subscribe()
                .AddToDispose(connectionDisposableList);

            EmojiExcludeFilters = _emojiExcludeFilters;
            Graph.EmojiExcludeFilters = _emojiExcludeFilters;

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

            var emojiExcludeFilter = _emojiExcludeFilters.ToObservableChangeSet()
                .AutoRefreshOnObservable(filter => filter.WhenAnyValue(e => e.ShowTasks))
                .ToCollection()
                .Select(filter =>
                {
                    bool Predicate(TaskItemViewModel task)
                    {
                        if (filter.All(e => !e.ShowTasks))
                        {
                            return true;
                        }

                        foreach (var item in filter.Where(e => e.ShowTasks))
                        {
                            if (string.IsNullOrEmpty(item?.Emoji)) continue;

                            if (task.GetAllEmoji.Contains(item.Emoji) || (task.Title ?? "").Contains(item.Emoji))
                                return false;
                        }

                        return true;
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
                        if (filter.All(e => !e.ShowTasks))
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
                               (filter.All(e => !e.ShowTasks) ||
                                filter.Where(e => e.ShowTasks).Any(item => item.Predicate(task)));
                    }

                    return (Func<TaskItemViewModel, bool>)Predicate;
                });

            var durationFilter = DurationFilters.ToObservableChangeSet()
                .AutoRefreshOnObservable(filter => filter.WhenAnyValue(e => e.ShowTasks))
                .ToCollection()
                .Select(filter =>
                {
                    bool Predicate(TaskItemViewModel task)
                    {
                        return (filter.All(e => !e.ShowTasks) ||
                                filter.Where(e => e.ShowTasks).Any(item => item.Predicate(task)));
                    }

                    return (Func<TaskItemViewModel, bool>)Predicate;
                });

            this.WhenAnyValue(m => m.ArchivedDateFilter.CurrentOption, m => m.ArchivedDateFilter.IsCustom)
                .Subscribe(filter =>
                {
                    if (!filter.Item2 && filter.Item1 != null)
                        ArchivedDateFilter.SetDateTimes(filter.Item1);
                });

            var archiveDateFilter = this.WhenAnyValue(m => m.ArchivedDateFilter.From, m => m.ArchivedDateFilter.To,
                    m => m.ArchivedDateFilter.IsCustom)
                .Select(filter =>
                {
                    bool Predicate(TaskItemViewModel task)
                    {
                        if (filter.Item1 == null || filter.Item2 == null)
                            return true;

                        var dateTime = task.ArchiveDateTime?.Add(DateTimeOffset.Now.Offset).Date;
                        return filter.Item1 <= dateTime && dateTime <= filter.Item2;
                    }

                    return (Func<TaskItemViewModel, bool>)Predicate;
                });

            #endregion Emoji

            //
            // Поиск
            //
            #region Поиск

            var searchTopFilter = this.WhenAnyValue(vm => vm.Search.SearchText, vm => vm.Search.IsFuzzySearch)
                .Throttle(TimeSpan.FromMilliseconds(SearchDefinition.DefaultThrottleMs), RxApp.MainThreadScheduler)
                .DistinctUntilChanged()
                .Select(searchText =>
                {
                    var userText = (searchText.Item1 ?? "").Trim();
                    var fuzzyText = searchText.Item2;

                    if (string.IsNullOrEmpty(userText))
                        return new Func<TaskItemViewModel, bool>(_ => true);

                    var words = SearchDefinition.NormalizeText(userText)
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    if (words.Length == 0)
                        return (_ => true);

                    if (fuzzyText)
                    {
                        return task =>
                        {
                            var source = SearchDefinition.NormalizeText($"{task.OnlyTextTitle} {task.Description} {task.GetAllEmoji} {task.Id}");
                            foreach (var w in words)
                            {
                                var maxDist = FuzzyMatcher.GetMaxDistanceForWord(w);
                                if (!FuzzyMatcher.IsFuzzyMatch(source, w, maxDist))
                                    return false;
                            }
                            return true;
                        };
                    }

                    return task =>
                    {
                        var source = SearchDefinition.NormalizeText(
                            $"{task.OnlyTextTitle} {task.Description} {task.GetAllEmoji} {task.Id}");

                        foreach (var w in words)
                            if (!source.Contains(w))
                                return false;
                        return true;
                    };
                });

            #endregion Поиск

            //Bind Roots

            #region Roots

            var emojiRootFilter = _emojiFilters.ToObservableChangeSet()
                .AutoRefreshOnObservable(filter => filter.WhenAnyValue(e => e.ShowTasks))
                .AutoRefreshOnObservable(filter => this.Search.WhenAnyValue(s => s.SearchText)
                    .Throttle(TimeSpan.FromMilliseconds(SearchDefinition.DefaultThrottleMs))
                    .DistinctUntilChanged())
                .ToCollection()
                .Select(filter =>
                {
                    bool Predicate(TaskItemViewModel task)
                    {
                        if (!string.IsNullOrEmpty(this.Search.SearchText))
                        {
                            if (filter.All(e => !e.ShowTasks))
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
                        
                        if (filter.All(e => !e.ShowTasks))
                        {
                            return task.Parents.Count == 0;
                        }

                        foreach (var item in filter.Where(e => e.ShowTasks))
                        {
                            if (task.Id == item.Source?.Id)
                                return true;
                        }

                        return false;
                    }

                    return (Func<TaskItemViewModel, bool>)Predicate;
                });

            taskRepository.Tasks
                .Connect()
                .AutoRefreshOnObservable(m => m.Parents.ToObservableChangeSet())
                .AutoRefreshOnObservable(m => m.WhenAny(
                    m => m.IsCanBeCompleted,
                    m => m.IsCompleted,
                    m => m.UnlockedDateTime, (c, d, u) => c.Value && (d.Value == false)))
                .Filter(taskFilter)
                .Filter(searchTopFilter)
                .Filter(emojiRootFilter)
                .Filter(emojiExcludeFilter)
                .Transform(item =>
                {
                    var actions = new TaskWrapperActions
                    {
                        ChildSelector = m => m.ContainsTasks.ToObservableChangeSet(),
                        RemoveAction = RemoveTask,
                        GetBreadScrumbs = BredScrumbsAlgorithms.WrapperParent,
                        SortComparer = sortObservable,
                        Filter = new() { taskFilter, emojiExcludeFilter },
                    };
                    var wrapper = new TaskWrapperViewModel(null, item, actions);
                    return wrapper;
                })
                .Sort(sortObservable)
                .TreatMovesAsRemoveAdd()
                .Bind(out _currentItems)
                .Subscribe(/*set => ExpandParentNodesForTask(CurrentTaskItem)*/)
                .AddToDispose(connectionDisposableList);

            CurrentAllTasksItems = _currentItems;

            #endregion Roots

            //Bind Unlocked

            #region Unlocked

            void ActivateUnlockedProjection()
            {
                if (_isUnlockedTabInitialized)
                {
                    return;
                }

                _isUnlockedTabInitialized = true;
                taskRepository.Tasks
                    .Connect()
                    .AutoRefreshOnObservable(m => m.WhenAnyValue(
                        m => m.IsCanBeCompleted,
                        m => m.IsCompleted,
                        m => m.UnlockedDateTime,
                        m => m.PlannedBeginDateTime,
                        m => m.Wanted,
                        m => m.PlannedDuration,
                        m => m.PlannedEndDateTime))
                    .AutoRefreshOnObservable(m => m.WhenAnyValue(
                        x => x.Title,
                        x => x.Description,
                        x => x.GetAllEmoji))
                    .Filter(unlockedTimeFilter)
                    .Filter(durationFilter)
                    .Filter(emojiFilter)
                    .Filter(emojiExcludeFilter)
                    .Filter(wantedFilter)
                    .Filter(searchTopFilter)
                    .Transform(item =>
                    {
                        var actions = new TaskWrapperActions
                        {
                            ChildSelector = m => m.ContainsTasks.ToObservableChangeSet(),
                            RemoveAction = RemoveTask,
                            GetBreadScrumbs = BredScrumbsAlgorithms.FirstTaskParent,
                        };
                        var wrapper = new TaskWrapperViewModel(null, item, actions);
                        return wrapper;
                    })
                    .Sort(sortObservableForUnlocked)
                    .Bind(out _unlockedItems)
                    .Subscribe()
                    .AddToDispose(connectionDisposableList);

                UnlockedItems = _unlockedItems;
            }

            this.WhenAnyValue(m => m.CompletedDateFilter.CurrentOption, m => m.CompletedDateFilter.IsCustom)
                .Subscribe(filter =>
                {
                    if (!filter.Item2 && filter.Item1 != null)
                        CompletedDateFilter.SetDateTimes(filter.Item1);
                });

            var completedDateFilter = this.WhenAnyValue(m => m.CompletedDateFilter.From, m => m.CompletedDateFilter.To,
                    m => m.CompletedDateFilter.IsCustom)
                .Select(filter =>
                {
                    bool Predicate(TaskItemViewModel task)
                    {
                        if (filter.Item1 == null || filter.Item2 == null)
                            return true;

                        var dateTime = task.CompletedDateTime?.Add(DateTimeOffset.Now.Offset).Date;
                        return filter.Item1 <= dateTime && dateTime <= filter.Item2;
                    }

                    return (Func<TaskItemViewModel, bool>)Predicate;
                });

            #endregion Unlocked
            
            this.WhenAnyValue(m => m.LastCreatedDateFilter.CurrentOption, m => m.LastCreatedDateFilter.IsCustom)
                .Subscribe(filter =>
                {
                    if (!filter.Item2 && filter.Item1 != null)
                        LastCreatedDateFilter.SetDateTimes(filter.Item1);
                });

            var lastCreatedDateFilter = this.WhenAnyValue(m => m.LastCreatedDateFilter.From,
                    m => m.LastCreatedDateFilter.To, m => m.LastCreatedDateFilter.IsCustom)

                .Select(filter =>
                {
                    bool Predicate(TaskItemViewModel task)
                    {
                        if (filter.Item1 == null || filter.Item2 == null)
                            return true;

                        var dateTime = task.CreatedDateTime.Add(DateTimeOffset.Now.Offset).Date;
                        return filter.Item1 <= dateTime && dateTime <= filter.Item2;
                    }

                    return (Func<TaskItemViewModel, bool>)Predicate;
                });

            this.WhenAnyValue(m => m.LastUpdatedDateFilter.CurrentOption, m => m.LastUpdatedDateFilter.IsCustom)
                .Subscribe(filter =>
                {
                    if (!filter.Item2 && filter.Item1 != null)
                        LastUpdatedDateFilter.SetDateTimes(filter.Item1);
                });

            var lastUpdatedDateFilter = this.WhenAnyValue(m => m.LastUpdatedDateFilter.From,
                    m => m.LastUpdatedDateFilter.To, m => m.LastUpdatedDateFilter.IsCustom)
                .Select(filter =>
                {
                    bool Predicate(TaskItemViewModel task)
                    {
                        if (filter.Item1 == null || filter.Item2 == null)
                            return true;

                        var dateTime = task.UpdatedDateTime?.LocalDateTime.Date;
                        return filter.Item1 <= dateTime && dateTime <= filter.Item2;
                    }

                    return (Func<TaskItemViewModel, bool>)Predicate;
                });

            //Bind Completed

            void ActivateCompletedProjection()
            {
                if (_isCompletedTabInitialized)
                {
                    return;
                }

                _isCompletedTabInitialized = true;
                taskRepository.Tasks
                    .Connect()
                    .AutoRefreshOnObservable(m => m.WhenAny(m => m.IsCompleted, (c) => c.Value == true))
                    .AutoRefreshOnObservable(m => m.WhenAnyValue(
                        x => x.Title,
                        x => x.Description,
                        x => x.GetAllEmoji))
                    .Filter(m => m.IsCompleted == true)
                    .Filter(completedDateFilter)
                    .Filter(emojiFilter)
                    .Filter(emojiExcludeFilter)
                    .Filter(searchTopFilter)
                    .Transform(item =>
                    {
                        var actions = new TaskWrapperActions
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
            }

            void ActivateArchivedProjection()
            {
                if (_isArchivedTabInitialized)
                {
                    return;
                }

                _isArchivedTabInitialized = true;
                taskRepository.Tasks
                    .Connect()
                    .AutoRefreshOnObservable(m => m.WhenAny(m => m.IsCompleted, (c) => c.Value == null))
                    .AutoRefreshOnObservable(m => m.WhenAnyValue(
                        x => x.Title,
                        x => x.Description,
                        x => x.GetAllEmoji))
                    .Filter(m => m.IsCompleted == null)
                    .Filter(archiveDateFilter)
                    .Filter(emojiFilter)
                    .Filter(emojiExcludeFilter)
                    .Filter(searchTopFilter)
                    .Transform(item =>
                    {
                        var actions = new TaskWrapperActions
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
            }

            void ActivateLastCreatedProjection()
            {
                if (_isLastCreatedTabInitialized)
                {
                    return;
                }

                _isLastCreatedTabInitialized = true;
                taskRepository.Tasks
                    .Connect()
                    .AutoRefreshOnObservable(m => m.WhenAny(m => m.IsCanBeCompleted, m => m.IsCompleted,
                        m => m.UnlockedDateTime, (c, d, u) => c.Value && (d.Value == false)))
                    .AutoRefreshOnObservable(m => m.WhenAnyValue(
                        x => x.Title,
                        x => x.Description,
                        x => x.GetAllEmoji))
                    .Filter(taskFilter)
                    .Filter(lastCreatedDateFilter)
                    .Filter(emojiFilter)
                    .Filter(emojiExcludeFilter)
                    .Filter(searchTopFilter)
                    .Transform(item =>
                    {
                        var actions = new TaskWrapperActions
                        {
                            ChildSelector = m => m.ContainsTasks.ToObservableChangeSet(),
                            RemoveAction = RemoveTask,
                            GetBreadScrumbs = BredScrumbsAlgorithms.FirstTaskParent,
                        };
                        var wrapper = new TaskWrapperViewModel(null, item, actions);
                        return wrapper;
                    })
                    .SortBy(e => e.TaskItem.CreatedDateTime, SortDirection.Descending)
                    .Bind(out _lastCreatedItems)
                    .Subscribe()
                    .AddToDispose(connectionDisposableList);

                LastCreatedItems = _lastCreatedItems;
            }

            void ActivateLastUpdatedProjection()
            {
                if (_isLastUpdatedTabInitialized)
                {
                    return;
                }

                _isLastUpdatedTabInitialized = true;
                taskRepository.Tasks
                    .Connect()
                    .AutoRefreshOnObservable(m => m.WhenAny(m => m.IsCanBeCompleted, m => m.IsCompleted,
                        m => m.UnlockedDateTime, (c, d, u) => c.Value && (d.Value == false)))
                    .AutoRefreshOnObservable(m => m.WhenAnyValue(x => x.UpdatedDateTime))
                    .AutoRefreshOnObservable(m => m.WhenAnyValue(
                        x => x.Title,
                        x => x.Description,
                        x => x.GetAllEmoji))
                    .Filter(taskFilter)
                    .Filter(lastUpdatedDateFilter)
                    .Filter(emojiFilter)
                    .Filter(emojiExcludeFilter)
                    .Filter(searchTopFilter)
                    .Transform(item =>
                    {
                        var actions = new TaskWrapperActions
                        {
                            ChildSelector = m => m.ContainsTasks.ToObservableChangeSet(),
                            RemoveAction = RemoveTask,
                            GetBreadScrumbs = BredScrumbsAlgorithms.FirstTaskParent,
                        };
                        var wrapper = new TaskWrapperViewModel(null, item, actions);
                        return wrapper;
                    })
                    .SortBy(e => e.TaskItem.UpdatedDateTime, SortDirection.Descending)
                    .Bind(out _lastUpdatedItems)
                    .Subscribe()
                    .AddToDispose(connectionDisposableList);

                LastUpdatedItems = _lastUpdatedItems;
            }

            void ActivateRoadmapProjection()
            {
                if (_isRoadmapTabInitialized)
                {
                    return;
                }

                _isRoadmapTabInitialized = true;
                taskRepository.Tasks
                    .Connect()
                    .Filter(taskFilter)
                    .Filter(emojiFilter)
                    .Filter(emojiExcludeFilter)
                    .Filter(wantedFilter)
                    .Transform(item =>
                    {
                        var actions = new TaskWrapperActions
                        {
                            ChildSelector = m => m.ContainsTasks.ToObservableChangeSet(),
                            RemoveAction = RemoveTask,
                            GetBreadScrumbs = BredScrumbsAlgorithms.FirstTaskParent,
                            Filter = new() { taskFilter, emojiExcludeFilter },
                        };
                        var wrapper = new TaskWrapperViewModel(null, item, actions);
                        return wrapper;
                    })
                    .Bind(out _FilteredItems)
                    .Subscribe()
                    .AddToDispose(connectionDisposableList);

                taskRepository.Tasks
                    .Connect()
                    .AutoRefreshOnObservable(m => m.Parents.ToObservableChangeSet())
                    .AutoRefreshOnObservable(m => m.WhenAny(
                        m => m.IsCanBeCompleted,
                        m => m.IsCompleted,
                        m => m.UnlockedDateTime, (c, d, u) => c.Value && (d.Value == false)))
                    .Filter(taskFilter)
                    .Filter(emojiRootFilter)
                    .Filter(emojiExcludeFilter)
                    .Transform(item =>
                    {
                        var actions = new TaskWrapperActions
                        {
                            ChildSelector = m => m.ContainsTasks.ToObservableChangeSet(),
                            RemoveAction = RemoveTask,
                            GetBreadScrumbs = BredScrumbsAlgorithms.WrapperParent,
                            SortComparer = sortObservable,
                            Filter = new() { taskFilter, emojiExcludeFilter },
                        };
                        var wrapper = new TaskWrapperViewModel(null, item, actions);
                        return wrapper;
                    })
                    .Bind(out _graphItems)
                    .Subscribe(set => ExpandParentNodesForTask(CurrentTaskItem))
                    .AddToDispose(connectionDisposableList);

                Graph.UnlockedTasks = _FilteredItems;
                Graph.Tasks = _graphItems;
            }

            var lastOpenedSearchFilter =
                searchTopFilter.Select(p => new Func<TaskWrapperViewModel, bool>(w => p(w.TaskItem)));

            this.WhenAnyValue(m => m.CompletedMode)
                .Where(mode => mode)
                .Take(1)
                .Subscribe(_ => ActivateCompletedProjection())
                .AddToDispose(connectionDisposableList);

            this.WhenAnyValue(m => m.ArchivedMode)
                .Where(mode => mode)
                .Take(1)
                .Subscribe(_ => ActivateArchivedProjection())
                .AddToDispose(connectionDisposableList);

            this.WhenAnyValue(m => m.LastCreatedMode)
                .Where(mode => mode)
                .Take(1)
                .Subscribe(_ => ActivateLastCreatedProjection())
                .AddToDispose(connectionDisposableList);

            this.WhenAnyValue(m => m.LastUpdatedMode)
                .Where(mode => mode)
                .Take(1)
                .Subscribe(_ => ActivateLastUpdatedProjection())
                .AddToDispose(connectionDisposableList);

            this.WhenAnyValue(m => m.UnlockedMode)
                .Where(mode => mode)
                .Take(1)
                .Subscribe(_ => ActivateUnlockedProjection())
                .AddToDispose(connectionDisposableList);

            this.WhenAnyValue(m => m.GraphMode)
                .Where(mode => mode)
                .Take(1)
                .Subscribe(_ => ActivateRoadmapProjection())
                .AddToDispose(connectionDisposableList);

            this.WhenAnyValue(m => m.LastOpenedMode)
                .Where(mode => mode)
                .Take(1)
                .Subscribe(_ => ActivateLastOpenedProjection())
                .AddToDispose(connectionDisposableList);

            //Bind LastOpened

            #region LastOpened

            void ActivateLastOpenedProjection()
            {
                if (_isLastOpenedTabInitialized)
                {
                    return;
                }

                _isLastOpenedTabInitialized = true;
                LastOpenedSource
                    .Connect()
                    .AutoRefreshOnObservable(w => w.TaskItem.WhenAnyValue(
                        x => x.IsCompleted,
                        x => x.CompletedDateTime,
                        x => x.ArchiveDateTime))
                    .Filter(lastOpenedSearchFilter)
                    .Reverse()
                    .Bind(out _lastOpenedItems)
                    .Subscribe()
                    .AddToDispose(connectionDisposableList);

                LastOpenedItems = _lastOpenedItems;
            }

            this.WhenAnyValue(m => m.CurrentTaskItem, m => m.DetailsAreOpen)
                .Subscribe(item =>
                {
                    if (DetailsAreOpen && item.Item1 != null && LastTaskItem != item.Item1)
                    {
                        LastTaskItem = item.Item1;
                        var actions = new TaskWrapperActions
                        {
                            ChildSelector = m => m.ContainsTasks.ToObservableChangeSet(),
                            RemoveAction = m => RemoveTask(m),
                            GetBreadScrumbs = BredScrumbsAlgorithms.FirstTaskParent,
                        };
                        var wrapper = new TaskWrapperViewModel(null, item.Item1, actions)
                        {
                            SpecialDateTime = DateTimeOffset.Now
                        };
                        LastOpenedSource.Add(wrapper);
                    }
                })
                .AddToDispose(connectionDisposableList);
            #endregion LastOpened

            this.WhenAnyValue(m => m.CurrentTaskItem)
                .Subscribe(item =>
                {
                    SelectCurrentTask();
                })
                .AddToDispose(connectionDisposableList);
            
            //Bind Current Item Contains
            #region Current Item Contains
            this.WhenAnyValue(m => m.CurrentTaskItem)
                .Subscribe(item =>
                {
                    if (item != null)
                    {
                        var actions = new TaskWrapperActions
                        {
                            ChildSelector = m => m.ContainsTasks.ToObservableChangeSet(),
                            RemoveAction = m =>
                            {

                                m.Parent.TaskItem.DeleteParentChildRelationCommand.Execute(m.TaskItem);
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
            #endregion Current Item Contains

            //Bind Current Item Parents
            this.WhenAnyValue(m => m.CurrentTaskItem)
                .Subscribe(item =>
                {
                    if (item != null)
                    {
                        var actions = new TaskWrapperActions
                        {
                            ChildSelector = m => m.ParentsTasks.ToObservableChangeSet(),
                            RemoveAction = m =>
                            {

                                m.TaskItem.DeleteParentChildRelationCommand.Execute(m.Parent.TaskItem);
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
                                m.Parent.TaskItem.UnblockCommand.Execute(m.TaskItem);
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

            this.WhenAnyValue(m => m.CurrentTaskItem)
                .Subscribe(item =>
                {
                    CurrentRelationEditor.SyncCurrentTask(item);
                })
                .AddToDispose(connectionDisposableList);
            this.WhenAnyValue(m => m.DetailsAreOpen)
                .Where(isOpen => !isOpen)
                .Subscribe(_ => CurrentRelationEditor.Close())
                .AddToDispose(connectionDisposableList);

                await taskStorage.Init();
                foreach (var taskItem in taskRepository.Tasks.Items)
                {
                    AttachTaskContext(taskItem);
                }
                RegisterCommands();

                IsInitialized = true;
            }
            finally
            {
                IsTasksLoading = false;
            }
        }

        public void SelectCurrentTask()
        {
            if (AllTasksMode ^ UnlockedMode ^ CompletedMode ^ ArchivedMode ^ GraphMode ^ LastCreatedMode ^ LastUpdatedMode ^ LastOpenedMode)
            {
                if (AllTasksMode)
                {
                    if (CurrentAllTasksItem?.TaskItem != CurrentTaskItem)
                    {
                        CurrentAllTasksItem = FindTaskWrapperViewModel(CurrentTaskItem, CurrentAllTasksItems);
                        if (CurrentTaskItem != null)
                        {
                            ExpandParentNodesForTask(CurrentTaskItem);
                        }
                    }
                }
                else if (UnlockedMode)
                {
                    if (CurrentUnlockedItem?.TaskItem != CurrentTaskItem)
                        CurrentUnlockedItem = FindTaskWrapperViewModel(CurrentTaskItem, UnlockedItems);
                }
                else if (CompletedMode)
                {
                    if (CurrentCompletedItem?.TaskItem != CurrentTaskItem)
                        CurrentCompletedItem = FindTaskWrapperViewModel(CurrentTaskItem, CompletedItems);
                }
                else if (ArchivedMode)
                {
                    if (CurrentArchivedItem?.TaskItem != CurrentTaskItem)
                        CurrentArchivedItem = FindTaskWrapperViewModel(CurrentTaskItem, ArchivedItems);
                }
                else if (GraphMode)
                {
                    if (CurrentGraphItem?.TaskItem != CurrentTaskItem)
                        CurrentGraphItem = FindTaskWrapperViewModel(CurrentTaskItem, Graph.Tasks);
                }
                else if (LastCreatedMode)
                {
                    if (CurrentLastCreated?.TaskItem != CurrentTaskItem)
                        CurrentLastCreated = FindTaskWrapperViewModel(CurrentTaskItem, LastCreatedItems);
                }
                else if (LastUpdatedMode)
                {
                    if (CurrentLastUpdated?.TaskItem != CurrentTaskItem)
                        CurrentLastUpdated = FindTaskWrapperViewModel(CurrentTaskItem, LastUpdatedItems);
                }
                else if (LastOpenedMode)
                {
                    if (CurrentLastOpenedItem?.TaskItem != CurrentTaskItem)
                        CurrentLastOpenedItem = FindTaskWrapperViewModel(CurrentTaskItem, LastOpenedItems);
                }
            }
        }

        private async void RemoveTask(TaskWrapperViewModel task)
        {
            if (task.TaskItem.RemoveRequiresConfirmation(task.Parent?.TaskItem.Id))
            {
                ManagerWrapper.Ask(L10n.Get("RemoveTaskHeader"),
                    L10n.Format("RemoveTaskMessage", task.TaskItem.Title),
                    async () =>
                    {
                        if (await task.TaskItem.RemoveFunc.Invoke(task.Parent?.TaskItem))
                        {
                            CurrentTaskItem = null;
                        }
                    });
            }
            else
            {
                if (await task.TaskItem.RemoveFunc.Invoke(task.Parent?.TaskItem))
                {
                    CurrentTaskItem = null;
                }
            }
        }

        private async Task RemoveTaskItem(TaskItemViewModel task)
        {
            ManagerWrapper.Ask(L10n.Get("RemoveTaskHeader"),
                L10n.Format("RemoveTaskMessage", task.Title),
                async () =>
                {
                    if (task.Parents?.Count > 0)
                    {
                        foreach (var parent in task.ParentsTasks.ToList())
                        {
                            await task.RemoveFunc.Invoke(parent);
                        }
                    }
                    else
                        await task.RemoveFunc.Invoke(null);

                        CurrentTaskItem = null;
                });
        }

        public void RemoveSelectedWrappers(IEnumerable<TaskWrapperViewModel>? wrappers)
        {
            var orderedWrappers = wrappers
                .NormalizeForDeleteBatch()
                .OrderByDescending(static wrapper => wrapper.GetWrapperDepth())
                .ToList();

            if (orderedWrappers.Count == 0)
            {
                return;
            }

            if (orderedWrappers.Count == 1)
            {
                RemoveTask(orderedWrappers[0]);
                return;
            }

            ManagerWrapper.Ask(
                L10n.Get("RemoveTasksHeader"),
                L10n.Format("RemoveTasksMessage", orderedWrappers.Count),
                async () => await RemoveWrappersInternalAsync(orderedWrappers));
        }

        private async Task RemoveWrappersInternalAsync(IReadOnlyList<TaskWrapperViewModel> wrappers)
        {
            if (wrappers == null || wrappers.Count == 0)
            {
                return;
            }

            var selectedTaskIds = wrappers
                .Select(static wrapper => wrapper.TaskItem?.Id)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet();

            foreach (var wrapper in wrappers)
            {
                if (wrapper?.TaskItem == null)
                {
                    continue;
                }

                await wrapper.TaskItem.RemoveFunc.Invoke(wrapper.Parent?.TaskItem);
            }

            if (CurrentTaskItem != null && selectedTaskIds.Contains(CurrentTaskItem.Id))
            {
                CurrentTaskItem = null;
            }
        }

        public void OpenRelationEditor(TaskRelationKind kind)
        {
            CurrentRelationEditor.Open(kind, CurrentTaskItem);
        }

        private TaskItemViewModel? FindTaskById(string taskId)
        {
            if (taskRepository == null || string.IsNullOrWhiteSpace(taskId))
            {
                return null;
            }

            var lookup = taskRepository.Tasks.Lookup(taskId);
            return lookup.HasValue ? lookup.Value : null;
        }

        private async Task<bool> TryAddRelationAsync(
            TaskRelationKind kind,
            TaskItemViewModel currentTask,
            TaskItemViewModel candidateTask)
        {
            if (taskRepository == null)
            {
                ManagerWrapper.ErrorToast(L10n.Get("TaskStorageNotConfigured"));
                return false;
            }

            if (!IsRelationCandidateValid(kind, currentTask, candidateTask))
            {
                ManagerWrapper.ErrorToast(L10n.Get("InvalidRelation"));
                return false;
            }

            return kind switch
            {
                TaskRelationKind.Parents => await taskRepository.CopyInto(currentTask, [candidateTask]),
                TaskRelationKind.Containing => await taskRepository.CopyInto(candidateTask, [currentTask]),
                TaskRelationKind.Blocking => await taskRepository.Block(currentTask, candidateTask),
                TaskRelationKind.Blocked => await taskRepository.Block(candidateTask, currentTask),
                _ => false
            };
        }

        private static bool IsRelationCandidateValid(
            TaskRelationKind kind,
            TaskItemViewModel currentTask,
            TaskItemViewModel candidateTask)
        {
            if (currentTask == null || candidateTask == null || string.IsNullOrWhiteSpace(currentTask.Id) ||
                string.IsNullOrWhiteSpace(candidateTask.Id) || currentTask.Id == candidateTask.Id)
            {
                return false;
            }

            return kind switch
            {
                TaskRelationKind.Parents => currentTask.CanMoveInto(candidateTask),
                TaskRelationKind.Containing => candidateTask.CanMoveInto(currentTask),
                TaskRelationKind.Blocking =>
                    !currentTask.BlockedBy.Contains(candidateTask.Id) &&
                    !currentTask.Blocks.Contains(candidateTask.Id),
                TaskRelationKind.Blocked =>
                    !currentTask.Blocks.Contains(candidateTask.Id) &&
                    !currentTask.BlockedBy.Contains(candidateTask.Id),
                _ => false
            };
        }

        private static string GetRelationCandidateContext(TaskItemViewModel task)
        {
            var breadcrumbs = BredScrumbsAlgorithms.FirstTaskParent(task);
            return string.IsNullOrWhiteSpace(breadcrumbs)
                ? task.Id
                : $"{breadcrumbs} [{task.Id}]";
        }

        public void ExpandNodeAndDescendants(TaskWrapperViewModel? current)
        {
            if (current == null)
            {
                return;
            }

            SetExpandedRecursive(current, true);
        }

        public void CollapseNodeDescendants(TaskWrapperViewModel? current)
        {
            SetExpandedRecursive(current, false);
        }

        public void ExpandAllNodes(IEnumerable<TaskWrapperViewModel>? roots)
        {
            SetExpandedRecursive(roots, true);
        }

        public void CollapseAllNodes(IEnumerable<TaskWrapperViewModel>? roots)
        {
            SetExpandedRecursive(roots, false);
        }

        private static void SetExpandedRecursive(IEnumerable<TaskWrapperViewModel>? roots, bool isExpanded)
        {
            if (roots == null)
            {
                return;
            }

            foreach (var root in roots)
            {
                SetExpandedRecursive(root, isExpanded);
            }
        }

        private static void SetExpandedRecursive(TaskWrapperViewModel? current, bool isExpanded)
        {
            if (current == null)
            {
                return;
            }

            current.IsExpanded = isExpanded;

            foreach (var child in current.SubTasks)
            {
                SetExpandedRecursive(child, isExpanded);
            }
        }

        public TaskWrapperViewModel FindTaskWrapperViewModel(TaskItemViewModel taskItemViewModel, ReadOnlyObservableCollection<TaskWrapperViewModel> source)
        {
            if (taskItemViewModel == null)
            {
                return null;
            }

            //Прямой поиск по коллекции
            var finded = source.FirstOrDefault(t => t?.TaskItem == taskItemViewModel);
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

        private void ExpandParentNodesForTask(TaskItemViewModel taskItem)
        {
            if (taskItem == null)
                return;

            // Get the parent chain from root to the selected task
            var parentChain = taskItem.GetFirstParentsPath().ToList();

            // Expand each parent in the chain
            foreach (var parentTask in parentChain)
            {
                var parentWrapper = FindTaskWrapperViewModel(parentTask, CurrentAllTasksItems);
                if (parentWrapper != null)
                {
                    parentWrapper.IsExpanded = true;
                    // Access SubTasks to ensure collection is initialized
                    var _ = parentWrapper.SubTasks;
                }
            }
            var wrapper = FindTaskWrapperViewModel(taskItem, CurrentAllTasksItems);
            // Ensure the selected item is refreshed for AutoScrollToSelectedItem
            if (wrapper != null)
            {
                CurrentAllTasksItem = wrapper;
            }
        }
        public string Title { get; set; }
        public bool IsTasksLoading { get; private set; }
        public bool AllTasksMode { get; set; }
        public bool UnlockedMode { get; set; }
        public bool CompletedMode { get; set; }
        public bool ArchivedMode { get; set; }
        public bool GraphMode { get; set; }
        public bool SettingsMode { get; set; }
        public bool LastCreatedMode { get; set; }
        public bool LastUpdatedMode { get; set; }
        public bool LastOpenedMode { get; set; }

        public INotificationManagerWrapper ManagerWrapper { get; }

        public string BreadScrumbs => AllTasksMode ? CurrentAllTasksItem?.BreadScrumbs : BredScrumbsAlgorithms.FirstTaskParent(CurrentTaskItem);

        private ReadOnlyObservableCollection<TaskWrapperViewModel> _currentItems;
        public ReadOnlyObservableCollection<TaskWrapperViewModel> CurrentAllTasksItems { get; set; }

        private ReadOnlyObservableCollection<TaskWrapperViewModel> _unlockedItems;
        public ReadOnlyObservableCollection<TaskWrapperViewModel> UnlockedItems { get; set; }

        private ReadOnlyObservableCollection<TaskWrapperViewModel> _completedItems;
        public ReadOnlyObservableCollection<TaskWrapperViewModel> CompletedItems { get; set; }

        private ReadOnlyObservableCollection<TaskWrapperViewModel> _archivedItems;
        public ReadOnlyObservableCollection<TaskWrapperViewModel> ArchivedItems { get; set; }

        private ReadOnlyObservableCollection<TaskWrapperViewModel> _FilteredItems;

        private ReadOnlyObservableCollection<TaskWrapperViewModel> _lastCreatedItems;
        public ReadOnlyObservableCollection<TaskWrapperViewModel> LastCreatedItems { get; set; }

        private ReadOnlyObservableCollection<TaskWrapperViewModel> _lastUpdatedItems;
        public ReadOnlyObservableCollection<TaskWrapperViewModel> LastUpdatedItems { get; set; }

        public ReadOnlyObservableCollection<TaskWrapperViewModel> _lastOpenedItems;
        public ReadOnlyObservableCollection<TaskWrapperViewModel> LastOpenedItems { get; set; }

        private ReadOnlyObservableCollection<TaskWrapperViewModel> _graphItems;
        
        public SourceList<TaskWrapperViewModel> LastOpenedSource { get; set; } = new SourceList<TaskWrapperViewModel>();

        public TaskItemViewModel? CurrentTaskItem { get; set; }
        public TaskItemViewModel LastTaskItem { get; set; } = null!;
        public TaskWrapperViewModel CurrentAllTasksItem { get; set; } = null!;
        public TaskWrapperViewModel CurrentUnlockedItem { get; set; } = null!;
        public TaskWrapperViewModel CurrentCompletedItem { get; set; } = null!;
        public TaskWrapperViewModel CurrentArchivedItem { get; set; } = null!;
        public TaskWrapperViewModel CurrentLastCreated { get; set; } = null!;
        public TaskWrapperViewModel CurrentLastUpdated { get; set; } = null!;
        public TaskWrapperViewModel CurrentGraphItem { get; set; } = null!;
        public TaskWrapperViewModel CurrentLastOpenedItem { get; set; } = null!;

        public TaskWrapperViewModel CurrentItemContains { get; private set; } = null!;
        public TaskWrapperViewModel CurrentItemParents { get; private set; } = null!;
        public TaskWrapperViewModel CurrentItemBlocks { get; private set; } = null!;
        public TaskWrapperViewModel CurrentItemBlockedBy { get; private set; } = null!;
        public TaskRelationEditorViewModel CurrentRelationEditor { get; }
        public SearchDefinition Search { get; set; } = new();

        public ICommand Create { get; set; }

        public ICommand CreateSibling { get; set; }

        public ICommand CreateBlockedSibling { get; set; }

        public ICommand CreateInner { get; set; }

        public ICommand MoveToPath { get; set; }

        public ICommand Remove { get; set; }

        public ICommand ExpandCurrentNestedCommand { get; set; }

        public ICommand CollapseCurrentNestedCommand { get; set; }

        public ICommand ExpandAllTreeNodesCommand { get; set; }

        public ICommand CollapseAllTreeNodesCommand { get; set; }

        public ICommand DeleteSelectedTreeItemsCommand { get; set; }

        public ICommand SelectAllTreeItemsCommand { get; set; }

        public Action<TreeCommandKind>? ExecuteTreeCommandAction { get; set; }

        private IConfiguration _configuration = null!;

        public ObservableCollection<SortDefinition> SortDefinitions { get; } = new(SortDefinition.GetDefinitions());
        public SortDefinition CurrentSortDefinition { get; set; }
        public SortDefinition CurrentSortDefinitionForUnlocked { get; set; }

        public bool ShowCompleted { get; set; }

        public bool ShowArchived { get; set; }

        public bool? ShowWanted { get; set; }

        public SettingsViewModel Settings { get; set; }
        public GraphViewModel Graph { get; set; }

        private ReadOnlyObservableCollection<EmojiFilter> _emojiFilters;
        public ReadOnlyObservableCollection<EmojiFilter> EmojiFilters { get; set; }

        public EmojiFilter AllEmojiFilter { get; } = new() { Emoji = "", Title = "All", ShowTasks = false, SortText = "\u0000" };

        private ReadOnlyObservableCollection<EmojiFilter> _emojiExcludeFilters;
        public ReadOnlyObservableCollection<EmojiFilter> EmojiExcludeFilters { get; set; }

        public EmojiFilter AllEmojiExcludeFilter { get; } = new() { Emoji = "", Title = "All", ShowTasks = false, SortText = "\u0000" };

        public ReadOnlyObservableCollection<UnlockedTimeFilter> UnlockedTimeFilters { get; set; } = UnlockedTimeFilter.GetDefinitions();
        public ReadOnlyObservableCollection<DurationFilter> DurationFilters { get; set; } = DurationFilter.GetDefinitions();
        public bool DetailsAreOpen { get; set; }
        public long TitleFocusRequestVersion { get; private set; }

        public DateFilter CompletedDateFilter { get; set; } = new();
        public DateFilter ArchivedDateFilter { get; set; } = new();
        public DateFilter LastCreatedDateFilter { get; set; } = new();
        public DateFilter LastUpdatedDateFilter { get; set; } = new();

        public ReadOnlyObservableCollection<DateFilterOption> DateFilterDefinitions { get; set; } = DateFilterDefinition.GetDefinitions();
        public object TabItems { get; } = null!;
        public object ToastNotificationManager { get; set; } = null!;
    }

    [AddINotifyPropertyChangedInterface]
    public class EmojiFilter
    {
        public string Title { get; set; } = "";
        public string Emoji { get; set; } = "";
        public bool ShowTasks { get; set; }
        public string SortText { get; set; } = "";
        public TaskItemViewModel Source { get; set; } = null!;
    }
}
