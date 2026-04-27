using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using DynamicData;
using DynamicData.Binding;
using PropertyChanged;
using ReactiveUI;
using Unlimotion.Domain;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    public class TaskItemViewModel : DisposableList
    {
        // Static dependencies - set once during app initialization
        public static INotificationManagerWrapper? NotificationManagerInstance { get; set; }
        public static MainWindowViewModel? MainWindowInstance { get; set; }
        public INotificationManagerWrapper? NotificationManager { get; set; }
        public MainWindowViewModel? MainWindow { get; set; }
        public Func<bool>? IsInitializedProvider { get; set; }

        public TaskItemViewModel(
            TaskItem model,
            ITaskStorage taskStorage,
            Func<bool>? isInitializedProvider = null)
        {
            IsInitializedProvider = isInitializedProvider ?? (() => true);
            Model = model;
            _taskStorage = taskStorage;
            _containsTasks = new ReadOnlyObservableCollection<TaskItemViewModel>(_containsTasksSource);
            _parentsTasks = new ReadOnlyObservableCollection<TaskItemViewModel>(_parentsTasksSource);
            _blocksTasks = new ReadOnlyObservableCollection<TaskItemViewModel>(_blocksTasksSource);
            _blockedByTasks = new ReadOnlyObservableCollection<TaskItemViewModel>(_blockedByTasksSource);
            Init(taskStorage);
        }

        private ITaskStorage _taskStorage = null!;

        public ReactiveCommand<Unit, Unit> SaveItemCommand = null!;        

        private readonly ObservableCollectionExtended<TaskItemViewModel> _containsTasksSource = new();
        private readonly ObservableCollectionExtended<TaskItemViewModel> _parentsTasksSource = new();
        private readonly ObservableCollectionExtended<TaskItemViewModel> _blocksTasksSource = new();
        private readonly ObservableCollectionExtended<TaskItemViewModel> _blockedByTasksSource = new();
        private readonly ReadOnlyObservableCollection<TaskItemViewModel> _containsTasks;
        private readonly ReadOnlyObservableCollection<TaskItemViewModel> _parentsTasks;
        private readonly ReadOnlyObservableCollection<TaskItemViewModel> _blocksTasks;
        private readonly ReadOnlyObservableCollection<TaskItemViewModel> _blockedByTasks;
        private readonly SerialDisposable _repeaterPropertyChangedSubscription = new();
        public bool IsHighlighted { get; set; }
        private TimeSpan? plannedPeriod;
        private DateCommands commands;
        public SetDurationCommands SetDurationCommands { get; set; } = null!;
        public static TimeSpan DefaultThrottleTime = TimeSpan.FromSeconds(10);
        public TimeSpan PropertyChangedThrottleTimeSpanDefault { get; set; } = DefaultThrottleTime;
        private bool IsInitialized => IsInitializedProvider?.Invoke() ?? true;

        private void Init(ITaskStorage taskStorage)
        {
            _repeaterPropertyChangedSubscription.AddToDispose(this);
            SetDurationCommands = new SetDurationCommands(this);
            SaveItemCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                 await taskStorage.Update(this);
            });

            // Пересчитываем вычисляемые поля при локальном изменении заголовка.
            this.WhenAnyValue(t => t.Title)
                .Subscribe(_ => RecalculateEmoji())
                .AddToDispose(this);

            //Subscribe IsCompleted
            this.WhenAnyValue(m => m.IsCompleted).Subscribe(async b =>
            {
                // Use TaskTreeManager to handle IsCompleted changes
                if (IsInitialized)
                {
                    SaveItemCommand.Execute();
                }
            });

            ArchiveCommand = ReactiveCommand.Create(() =>
            {
                var notificationManager = NotificationManager ?? NotificationManagerInstance;

                switch (IsCompleted)
                {
                    case null:
                        {
                            IsCompleted = false;

                            var archivedChildrenTasks = GetChildrenTasks(e => e.IsCompleted == null).ToList();

                            ShowModalAndChangeChildrenStatuses(notificationManager, Model.Title, archivedChildrenTasks,
                                ArchiveMethodType.Unarchive);

                            break;
                        }
                    case false:
                        {
                            IsCompleted = null;

                            var notCompletedChildrenTasks = GetChildrenTasks(e => e.IsCompleted == false).ToList();

                            ShowModalAndChangeChildrenStatuses(notificationManager, Model.Title, notCompletedChildrenTasks,
                                ArchiveMethodType.Archive);

                            break;
                        }
                }
            }, this.WhenAnyValue(m => m.IsCompleted, b => b != true));

            RemoveFunc = async parent =>
            {
                if (parent != null && Parents.Count > 1)
                    return await taskStorage.Delete(this, parent);
                return await taskStorage.Delete(this);
            };

            CloneFunc = async destination =>
            {
                var clone = await taskStorage.Clone(this, destination);
                return clone;
            };

            UnblockCommand = ReactiveCommand.Create<TaskItemViewModel, Unit>(
                m =>
                {
                    taskStorage.Unblock(this, m);
                    return Unit.Default;
                });

            DeleteParentChildRelationCommand = ReactiveCommand.Create<TaskItemViewModel, Unit> (
                m =>
                {
                    taskStorage.RemoveParentChildConnection(this, m);
                    return Unit.Default;
                });            

            //Subscribe to Save when property changed
            if (this is INotifyPropertyChanged inpc)
            {
                var propertyChanged = Observable
                    .FromEventPattern<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                        h => inpc.PropertyChanged += h,
                        h => inpc.PropertyChanged -= h)
                    .Where(changed =>
                    {
                        switch (changed.EventArgs.PropertyName)
                        {
                            case nameof(Title):
                            case nameof(Description):
                            case nameof(PlannedBeginDateTime):
                            case nameof(PlannedEndDateTime):
                            case nameof(PlannedDuration):
                            case nameof(Repeater):
                            case nameof(Importance):
                            case nameof(Wanted):
                                return true;
                            default:
                                return false;
                        }
                    })
                    .Publish(shared =>
                        shared.Where(_ => !IsInitialized)
                              .Merge(
                                  shared.Where(_ => IsInitialized)
                                        .Throttle(PropertyChangedThrottleTimeSpanDefault)
                              )
                    );

                propertyChanged
                    .Subscribe(_ =>
                    {
                        if (IsInitialized)
                            SaveItemCommand.Execute();
                    })
                    .AddToDispose(this);
            }

            //При изменении начала
            this.WhenAnyValue(m => m.PlannedBeginDateTime).Subscribe(b =>
            {
                //Если есть начальная и конечная дата
                if (b.HasValue)
                {
                    if (PlannedEndDateTime != null)
                    {
                        //Если есть вычисленный период
                        if (plannedPeriod.HasValue)
                        {
                            //Вычисляется новая конечная дата
                            var newValue = b.Value.Add(plannedPeriod.Value);
                            //Если есть изменения
                            if (PlannedEndDateTime != newValue)
                            {
                                //Меняем дату
                                PlannedEndDateTime = newValue;
                            }
                        }
                        //Если нет вычисленного периода
                        else
                        {
                            //Если начало раньше либо равно концу
                            if (b.Value <= PlannedEndDateTime)
                                //Вычисляем период
                                plannedPeriod = PlannedEndDateTime - b.Value;
                            //Если начало позже конца
                            else
                                //Обнуляем период
                                plannedPeriod = null;
                        }
                    }
                    else
                    {
                        PlannedEndDateTime = b.Value;
                    }
                }
            });

            this.WhenAnyValue(m => m.PlannedEndDateTime).Subscribe(b =>
            {
                //Если есть начальная и конечная дата
                if (PlannedBeginDateTime != null && b.HasValue)
                {
                    //Если начало раньше либо равно концу
                    if (PlannedBeginDateTime <= b.Value)
                        //Вычисляем период
                        plannedPeriod = b.Value - PlannedBeginDateTime;
                    //Если начало позже конца
                    else
                        //Обнуляем период
                        plannedPeriod = null;
                }
            });            

            this.WhenAnyValue(t => t.Repeater)
                .Subscribe(RegisterRepeaterPropertyChangedSubscription)
                .AddToDispose(this);
        }

        private void RegisterRepeaterPropertyChangedSubscription(RepeaterPatternViewModel? repeater)
        {
            NotifyRepeaterListMarkerChanged();

            if (repeater is not INotifyPropertyChanged inpc)
            {
                _repeaterPropertyChangedSubscription.Disposable = Disposable.Empty;
                return;
            }

            var repeaterChanges = Observable.FromEventPattern<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                handler => inpc.PropertyChanged += handler,
                handler => inpc.PropertyChanged -= handler);

            var markerSubscription = repeaterChanges
                .Where(changed => IsRepeaterPatternMarkerProperty(changed.EventArgs.PropertyName))
                .Subscribe(_ => NotifyRepeaterListMarkerChanged());

            var saveSubscription = repeaterChanges
                .Where(changed => IsRepeaterPatternPersistenceProperty(changed.EventArgs.PropertyName))
                .Throttle(TimeSpan.FromSeconds(2))
                .Subscribe(_ =>
                {
                    if (IsInitialized) SaveItemCommand.Execute();
                });

            _repeaterPropertyChangedSubscription.Disposable = new CompositeDisposable(markerSubscription, saveSubscription);
        }

        private void NotifyRepeaterListMarkerChanged()
        {
            OnPropertyChanged(nameof(IsHaveRepeater));
            OnPropertyChanged(nameof(RepeaterListMarker));
            OnPropertyChanged(nameof(RepeaterListMarkerToolTip));
        }

        private void OnPropertyChanged(string propertyName)
        {
            if (this is not INotifyPropertyChanged propertyChangedSource)
            {
                return;
            }

            var propertyChangedHandler = propertyChangedSource
                .GetType()
                .GetField(
                    nameof(INotifyPropertyChanged.PropertyChanged),
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?.GetValue(propertyChangedSource) as PropertyChangedEventHandler;

            propertyChangedHandler?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static bool IsRepeaterPatternMarkerProperty(string? propertyName)
        {
            return string.IsNullOrEmpty(propertyName) ||
                   propertyName is nameof(RepeaterPatternViewModel.Type)
                       or nameof(RepeaterPatternViewModel.SelectedRepeaterType)
                       or nameof(RepeaterPatternViewModel.Period)
                       or nameof(RepeaterPatternViewModel.WorkDays)
                       or nameof(RepeaterPatternViewModel.Monday)
                       or nameof(RepeaterPatternViewModel.Tuesday)
                       or nameof(RepeaterPatternViewModel.Wednesday)
                       or nameof(RepeaterPatternViewModel.Thursday)
                       or nameof(RepeaterPatternViewModel.Friday)
                       or nameof(RepeaterPatternViewModel.Saturday)
                       or nameof(RepeaterPatternViewModel.Sunday)
                       or nameof(RepeaterPatternViewModel.AfterComplete)
                       or nameof(RepeaterPatternViewModel.Title);
        }

        private static bool IsRepeaterPatternPersistenceProperty(string? propertyName)
        {
            return string.IsNullOrEmpty(propertyName) ||
                   propertyName is nameof(RepeaterPatternViewModel.AfterComplete)
                       or nameof(RepeaterPatternViewModel.Period)
                       or nameof(RepeaterPatternViewModel.Monday)
                       or nameof(RepeaterPatternViewModel.Tuesday)
                       or nameof(RepeaterPatternViewModel.Wednesday)
                       or nameof(RepeaterPatternViewModel.Thursday)
                       or nameof(RepeaterPatternViewModel.Friday)
                       or nameof(RepeaterPatternViewModel.Saturday)
                       or nameof(RepeaterPatternViewModel.Sunday);
        }

        public void ApplyRelations(
            IReadOnlyList<TaskItemViewModel> containsTasks,
            IReadOnlyList<TaskItemViewModel> parentsTasks,
            IReadOnlyList<TaskItemViewModel> blocksTasks,
            IReadOnlyList<TaskItemViewModel> blockedByTasks,
            bool refreshComputed = true)
        {
            SynchronizeTaskCollection(_containsTasksSource, containsTasks);
            SynchronizeTaskCollection(_parentsTasksSource, parentsTasks);
            SynchronizeTaskCollection(_blocksTasksSource, blocksTasks);
            SynchronizeTaskCollection(_blockedByTasksSource, blockedByTasks);

            if (refreshComputed)
            {
                RefreshComputedFields();
            }
        }

        public void RefreshComputedFields()
        {
            RecalculateEmoji();
        }

        private void RecalculateEmoji()
        {
            var parents = GetAllParents().ToList();

            if (!parents.Any())
            {
                GetAllEmoji = Emoji;
                return;
            }

            GetAllEmoji = string.Concat(parents.Select(p => p.Emoji).Where(e => !string.IsNullOrEmpty(e)));
        }

        private static void SynchronizeTaskCollection(
            ObservableCollectionExtended<TaskItemViewModel> source,
            IReadOnlyList<TaskItemViewModel>? target)
        {
            if (target == null || target.Count == 0)
            {
                if (source.Count > 0)
                {
                    source.Clear();
                }
                return;
            }

            var orderedDistinct = target
                .Where(item => item != null && !string.IsNullOrEmpty(item.Id))
                .GroupBy(item => item.Id)
                .Select(group => group.First())
                .ToList();

            var targetIds = new HashSet<string>(orderedDistinct.Select(item => item.Id));
            for (var i = source.Count - 1; i >= 0; i--)
            {
                if (!targetIds.Contains(source[i].Id))
                {
                    source.RemoveAt(i);
                }
            }

            for (var i = 0; i < orderedDistinct.Count; i++)
            {
                var desired = orderedDistinct[i];
                if (i < source.Count && source[i].Id == desired.Id)
                {
                    continue;
                }

                var existingIndex = -1;
                for (var j = 0; j < source.Count; j++)
                {
                    if (source[j].Id == desired.Id)
                    {
                        existingIndex = j;
                        break;
                    }
                }

                if (existingIndex >= 0)
                {
                    source.Move(existingIndex, i);
                }
                else
                {
                    source.Insert(i, desired);
                }
            }

            while (source.Count > orderedDistinct.Count)
            {
                source.RemoveAt(source.Count - 1);
            }
        }

        private IEnumerable<TaskItemViewModel> GetChildrenTasks(Func<TaskItemViewModel, bool> predicate)
        {
            var queue = new Queue<TaskItemViewModel>();

            foreach (var child in ContainsTasks.Where(predicate))
            {
                queue.Enqueue(child);
            }

            while (queue.TryDequeue(out var current))
            {
                yield return current;
                foreach (var child in current.ContainsTasks.Where(predicate))
                {
                    queue.Enqueue(child);
                }
            }
        }

        public ICommand ArchiveCommand { get; set; } = null!;
        public Func<TaskItemViewModel, Task<bool>> RemoveFunc { get; set; } = null!;
        public Func<TaskItemViewModel, Task<TaskItemViewModel>> CloneFunc { get; set; } = null!;

        public bool RemoveRequiresConfirmation(string parentId) => parentId == null || (Parents.Contains(parentId) ? Parents.Count == 1 : Parents.Count == 0);

        public TaskItem Model
        {
            get
            {
                return new TaskItem
                {
                    Id = Id,
                    Title = Title,
                    Description = Description,
                    CreatedDateTime = CreatedDateTime,
                    UpdatedDateTime = UpdatedDateTime,
                    UnlockedDateTime = UnlockedDateTime,
                    CompletedDateTime = CompletedDateTime,
                    ArchiveDateTime = ArchiveDateTime,
                    PlannedBeginDateTime = PlannedBeginDateTime,
                    PlannedEndDateTime = PlannedEndDateTime,
                    PlannedDuration = PlannedDuration,
                    Importance = Importance,
                    Wanted = Wanted,
                    IsCanBeCompleted = IsCanBeCompleted,
                    IsCompleted = IsCompleted,
                    Version = Version,
                    BlocksTasks = Blocks.ToList(),
                    BlockedByTasks = BlockedBy.ToList(),
                    ContainsTasks = Contains.ToList(),
                    ParentTasks = Parents.ToList(),
                    Repeater = Repeater?.Model,
                };
            }
            set
            {
                Id = value.Id;
                Update(value);
            }
        }

        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsCanBeCompleted { get; set; }
        public bool? IsCompleted { get; set; }
        public int Version { get; set; }
        public DateTimeOffset CreatedDateTime { get; set; }
        public DateTimeOffset? UpdatedDateTime { get; set; }
        public DateTimeOffset? UnlockedDateTime { get; set; }
        public DateTimeOffset? CompletedDateTime { get; set; }
        public DateTimeOffset? ArchiveDateTime { get; set; }
        public DateTime? PlannedBeginDateTime { get; set; }
        public DateTime? PlannedEndDateTime { get; set; }

        /// <summary>
        /// Период планируемых дат, вычислимое поле
        /// </summary>
        public TimeSpan? PlannedPeriod
        {
            get => plannedPeriod;
            set => plannedPeriod = value;
        }

        public TimeSpan? PlannedDuration { get; set; }
        public int Importance { get; set; }
        public bool Wanted { get; set; }        

        public ReadOnlyObservableCollection<TaskItemViewModel> ContainsTasks => _containsTasks;

        public ReadOnlyObservableCollection<TaskItemViewModel> ParentsTasks => _parentsTasks;

        public ReadOnlyObservableCollection<TaskItemViewModel> BlocksTasks => _blocksTasks;

        public ReadOnlyObservableCollection<TaskItemViewModel> BlockedByTasks => _blockedByTasks;

        public ObservableCollection<string> Contains { get; set; } = new();
        public ObservableCollection<string> Parents { get; set; } = new();
        public ObservableCollection<string> Blocks { get; set; } = new();
        public ObservableCollection<string> BlockedBy { get; set; } = new();

        public ICommand UnblockCommand { get; set; } = null!;        
        public ICommand DeleteParentChildRelationCommand { get; set; } = null!;
        
        public async Task CopyInto(TaskItemViewModel destination)
        {
            await _taskStorage.CopyInto(this, [destination]);
        }

        public async Task MoveInto(TaskItemViewModel destination, TaskItemViewModel source)
        {
            await _taskStorage.MoveInto(this, [destination], source);
        }

        public async Task<TaskItemViewModel> CloneInto(TaskItemViewModel destination)
        {
            return await CloneFunc.Invoke(destination);
        }

        public async void BlockBy(TaskItemViewModel blocker)
        {
            await _taskStorage.Block(this, blocker);
        }

        public IEnumerable<TaskItemViewModel> GetFirstParentsPath()
        {
            var stack = new Stack<TaskItemViewModel>();
            var curent = this;
            while (curent.ParentsTasks.Any())
            {
                curent = curent.ParentsTasks.First();
                stack.Push(curent);
            }

            while (stack.TryPop(out var result))
            {
                yield return result;
            }
        }

        public string GetAllEmoji { get; set; } = "";

        public string TitleWithoutEmoji => EmojiTextHelper.RemoveEmoji(Title, trimStart: true);


        public string Emoji => EmojiTextHelper.ExtractEmoji(Title);

        public string OnlyTextTitle => EmojiTextHelper.RemoveEmoji(Title);

        public IEnumerable<TaskItemViewModel> GetAllParents()
        {
            var visited = new HashSet<string>();
            var orderedParents = new List<TaskItemViewModel>();

            void Traverse(TaskItemViewModel task)
            {
                if (visited.Contains(task.Id))
                    return;

                visited.Add(task.Id);

                // Сначала рекурсивно обходим родительские задачи
                foreach (var parent in task.ParentsTasks.OrderBy(m => m.Title))
                {
                    Traverse(parent);
                }

                // Затем добавляем текущую задачу в список
                orderedParents.Add(task);
            }

            // Начинаем обход с непосредственных родителей текущей задачи
            foreach (var task in ParentsTasks.OrderBy(m => m.Title))
            {
                Traverse(task);
            }

            return orderedParents;
        }

        [AlsoNotifyFor(nameof(IsHaveRepeater), nameof(RepeaterListMarker), nameof(RepeaterListMarkerToolTip))]
        public RepeaterPatternViewModel Repeater { get; set; } = null!;

        public bool IsHaveRepeater => Repeater != null && Repeater.Type != RepeaterType.None;

        public string RepeaterListMarker => IsHaveRepeater ? "↻" : string.Empty;

        public string? RepeaterListMarkerToolTip => IsHaveRepeater ? Repeater.Title : null;

        public List<RepeaterPatternViewModel> Repeaters => new()
        {
            new RepeaterPatternViewModel { Type = RepeaterType.None },
            new RepeaterPatternViewModel { Type = RepeaterType.Daily },
            new RepeaterPatternViewModel { Type = RepeaterType.Weekly, WorkDays = true },
            new RepeaterPatternViewModel { Type = RepeaterType.Weekly },
            new RepeaterPatternViewModel { Type = RepeaterType.Monthly },
            new RepeaterPatternViewModel { Type = RepeaterType.Yearly },
        };

        /// <summary>
        /// Команды для быстрого выбора дат, ленивая загрузка
        /// </summary>
        public DateCommands Commands => commands ??= new DateCommands(this);
        
        private void ShowModalAndChangeChildrenStatuses(INotificationManagerWrapper? notificationManager, string taskName,
            List<TaskItemViewModel> childrenTasks, ArchiveMethodType methodType)
        {
            if (childrenTasks.Count == 0 || notificationManager == null) return;

            Action yesAction = methodType switch
            {
                ArchiveMethodType.Archive => () =>
                {
                    foreach (var task in childrenTasks)
                    {
                        task.IsCompleted = null;
                    }
                }
                ,
                ArchiveMethodType.Unarchive => () =>
                {
                    foreach (var task in childrenTasks)
                    {
                        task.IsCompleted = false;
                    }
                }
                ,
                _ => throw new Exception("Undefined ArchiveMethodType")
            };

            var headerKey = methodType == ArchiveMethodType.Archive
                ? "ArchiveContainedTasksHeader"
                : "UnarchiveContainedTasksHeader";
            var messageKey = methodType == ArchiveMethodType.Archive
                ? "ArchiveContainedTasksMessage"
                : "UnarchiveContainedTasksMessage";

            notificationManager.Ask(L10n.Get(headerKey),
                L10n.Format(messageKey, childrenTasks.Count, taskName),
                yesAction);
        }

        private enum ArchiveMethodType
        {
            Archive = 1,
            Unarchive = 2
        }

        public void Update(TaskItem taskItem)
        {
            if (taskItem == null) throw new ArgumentNullException(nameof(taskItem));
            if (Id != taskItem.Id) throw new InvalidDataException("Id don't match");

            // Update the backing model
            if (IsCanBeCompleted != taskItem.IsCanBeCompleted) IsCanBeCompleted = taskItem.IsCanBeCompleted;
            if (Title != taskItem.Title) Title = taskItem.Title;
            if (Description != taskItem.Description) Description = taskItem.Description;
            if (CreatedDateTime != taskItem.CreatedDateTime) CreatedDateTime = taskItem.CreatedDateTime;
            if (UpdatedDateTime != taskItem.UpdatedDateTime) UpdatedDateTime = taskItem.UpdatedDateTime;
            if (UnlockedDateTime != taskItem.UnlockedDateTime) UnlockedDateTime = taskItem.UnlockedDateTime;
            if (CompletedDateTime != taskItem.CompletedDateTime) CompletedDateTime = taskItem.CompletedDateTime;
            if (ArchiveDateTime != taskItem.ArchiveDateTime) ArchiveDateTime = taskItem.ArchiveDateTime;
            if (PlannedBeginDateTime != taskItem.PlannedBeginDateTime?.LocalDateTime)
                PlannedBeginDateTime = taskItem.PlannedBeginDateTime?.LocalDateTime;
            if (PlannedEndDateTime != taskItem.PlannedEndDateTime?.LocalDateTime)
                PlannedEndDateTime = taskItem.PlannedEndDateTime?.LocalDateTime;
            if (PlannedDuration != taskItem.PlannedDuration) PlannedDuration = taskItem.PlannedDuration;
            if (Importance != taskItem.Importance) Importance = taskItem.Importance;
            if (Wanted != taskItem.Wanted) Wanted = taskItem.Wanted;
            if (IsCompleted != taskItem.IsCompleted) IsCompleted = taskItem.IsCompleted;
            if (Version != taskItem.Version) Version = taskItem.Version;


            SynchronizeCollections(Blocks, taskItem.BlocksTasks);
            SynchronizeCollections(BlockedBy, taskItem.BlockedByTasks);
            SynchronizeCollections(Contains, taskItem.ContainsTasks);
            SynchronizeCollections(Parents, taskItem.ParentTasks);

            if (taskItem.Repeater != null)
            {
                if (Repeater != null)
                {
                    if (!taskItem.Repeater.Equals(Repeater.Model))
                    {
                        Repeater.Model = taskItem.Repeater;
                    }
                }
                else
                {
                    Repeater = new RepeaterPatternViewModel();
                    Repeater.Model = taskItem.Repeater;
                }
            }
            else
            {
                if (Repeater != null)
                {
                    Repeater = null;
                }
            }
        }

        public static void SynchronizeCollections(ObservableCollection<string> observableCollection, List<string> list)
        {
            if (observableCollection == null)
            {
                throw new ArgumentNullException(nameof(observableCollection));
            }

            if (list == null || list.Count == 0)
            {
                if(observableCollection.Count > 0)
                    observableCollection.Clear();
                return;
            }

            if (observableCollection.Count == 0)
            {
                observableCollection.AddRange(list);
                return;
            }

            var set = new HashSet<string>(list);

            // Удаление элементов, которых нет в HashSet (и, следовательно, в List)
            for (int i = observableCollection.Count - 1; i >= 0; i--)
            {
                if (!set.Contains(observableCollection[i]))
                {
                    observableCollection.RemoveAt(i);
                }
            }

            // Добавление элементов из List, которых нет в ObservableCollection
            // Используем HashSet для проверки наличия элемента для повышения производительности
            var existingItems = new HashSet<string>(observableCollection);
            foreach (var item in list)
            {
                if (!existingItems.Contains(item))
                {
                    observableCollection.Add(item);
                }
            }
        }
    }
}
