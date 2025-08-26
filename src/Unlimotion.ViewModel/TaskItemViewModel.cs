using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using DynamicData;
using DynamicData.Binding;
using PropertyChanged;
using ReactiveUI;
using Splat;
using Unlimotion.Domain;

namespace Unlimotion.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    public class TaskItemViewModel : DisposableList
    {
        public TaskItemViewModel(TaskItem model, ITaskStorage taskStorage)
        {
            Model = model;
            _taskStorage = taskStorage;
            Init(taskStorage);
        }

        private ITaskStorage _taskStorage;
        private bool GetCanBeCompleted() => (ContainsTasks.All(m => m.IsCompleted != false)) &&
                                            (BlockedByTasks.All(m => m.IsCompleted != false));

        public ReactiveCommand<Unit, Unit> SaveItemCommand;        

        private bool _isInited;
        public bool NotHaveUncompletedContains { get; private set; }
        public bool NotHaveUncompletedBlockedBy { get; private set; }
        private ReadOnlyObservableCollection<TaskItemViewModel> _containsTasks;
        private ReadOnlyObservableCollection<TaskItemViewModel> _parentsTasks;
        private ReadOnlyObservableCollection<TaskItemViewModel> _blocksTasks;
        private ReadOnlyObservableCollection<TaskItemViewModel> _blockedByTasks;
        private TimeSpan? plannedPeriod;
        private DateCommands commands = null;
        public SetDurationCommands SetDurationCommands { get; set; }
        public static TimeSpan DefaultThrottleTime = TimeSpan.FromSeconds(10);
        public TimeSpan PropertyChangedThrottleTimeSpanDefault { get; set; } = DefaultThrottleTime;

        private void Init(ITaskStorage taskStorage)
        {
            SetDurationCommands = new SetDurationCommands(this);
            SaveItemCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                 await taskStorage.Update(this);
            });

            //Subscribe ContainsTasks
            var containsFilter = Contains.ToObservableChangeSet()
                .ToCollection()
                .Select(items =>
                {
                    bool Predicate(TaskItemViewModel task) => items.Contains(task.Id);
                    return (Func<TaskItemViewModel, bool>)Predicate;
                });

            taskStorage.Tasks.Connect()
                .Filter(containsFilter)
                .Bind(out _containsTasks)
                .Subscribe()
                .AddToDispose(this);            

            //Subscribe ParentsTasks
            var parentsFilter = Parents.ToObservableChangeSet()
                .ToCollection()
                .Select(items =>
                {
                    bool Predicate(TaskItemViewModel task) => items.Contains(task.Id);
                    return (Func<TaskItemViewModel, bool>)Predicate;
                });

            taskStorage.Tasks.Connect()
                .Filter(parentsFilter)
                .Bind(out _parentsTasks)
                .Subscribe()
                .AddToDispose(this);

            //GetAllParents Emoji
            ParentsTasks.ToObservableChangeSet()
                .AutoRefreshOnObservable(m => m.WhenAnyValue(m => m.Emoji, m => m.GetAllEmoji))
                .StartWithEmpty()
                .ToCollection()
                .Subscribe(result =>
                {
                    var list = new List<string>();
                    foreach (var task in GetAllParents())
                    {
                        list.Add(task.Emoji);
                    }

                    GetAllEmoji = string.Join("", list);
                })
                .AddToDispose(this);

            //Subscribe BlocksTasks
            var blocksFilter = Blocks.ToObservableChangeSet()
                .ToCollection()
                .Select(items =>
                {
                    bool Predicate(TaskItemViewModel task) => items.Contains(task.Id);
                    return (Func<TaskItemViewModel, bool>)Predicate;
                });

            taskStorage.Tasks.Connect()
                .Filter(blocksFilter)
                .Bind(out _blocksTasks)
                .Subscribe()
                .AddToDispose(this);            

            //Subscribe BlockedBy
            var blockedByFilter = BlockedBy.ToObservableChangeSet()
                .ToCollection()
                .Select(items =>
                {
                    bool Predicate(TaskItemViewModel task) => items.Contains(task.Id);
                    return (Func<TaskItemViewModel, bool>)Predicate;
                });

            taskStorage.Tasks.Connect()
                .Filter(blockedByFilter)
                .Bind(out _blockedByTasks)
                .Subscribe()
                .AddToDispose(this);

            //Subscribe IsCompleted
            this.WhenAnyValue(m => m.IsCompleted).Subscribe(async b =>
            {
                if (b == true && CompletedDateTime == null)
                {
                    CompletedDateTime ??= DateTimeOffset.UtcNow;
                    ArchiveDateTime = null;
                    if (Repeater != null && Repeater.Type != RepeaterType.None && PlannedBeginDateTime.HasValue)
                    {
                        var clone = new TaskItem
                        {
                            BlocksTasks = Model.BlocksTasks.ToList(),
                            BlockedByTasks = Model.BlockedByTasks.ToList(),
                            ContainsTasks = Model.ContainsTasks.ToList(),
                            Description = Model.Description,
                            Title = Model.Title,
                            PlannedDuration = Model.PlannedDuration,
                            Repeater = Model.Repeater,
                        };
                        clone.PlannedBeginDateTime = Repeater.GetNextOccurrence(PlannedBeginDateTime.Value);
                        if (PlannedEndDateTime.HasValue)
                        {
                            clone.PlannedEndDateTime =
                                clone.PlannedBeginDateTime.Value.Add(PlannedEndDateTime.Value - PlannedBeginDateTime.Value);
                        }
                        var cloned = await taskStorage.Clone(new TaskItemViewModel(clone, taskStorage), ParentsTasks.ToArray());
                    }
                }

                if (b == false)
                {
                    ArchiveDateTime = null;
                    CompletedDateTime = null;
                }

                if (b == null && ArchiveDateTime == null)
                {
                    ArchiveDateTime ??= DateTimeOffset.UtcNow;
                }
            });

            //Subscribe NotHaveUncompletedContains
            ContainsTasks.ToObservableChangeSet()
                .AutoRefreshOnObservable(m => m.WhenAnyValue(m => m.IsCompleted))
                .StartWithEmpty()
                .ToCollection()
                .Select(items =>
                {
                    return items.All(i => i.IsCompleted != false);

                }).Subscribe(result =>
                {
                    NotHaveUncompletedContains = result;
                })
                .AddToDispose(this);

            //Subscribe NotHaveUncompletedBlockedBy
            BlockedByTasks.ToObservableChangeSet()
                .AutoRefreshOnObservable(m => m.WhenAnyValue(m => m.IsCompleted))
                .StartWithEmpty()
                .ToCollection()
                .Select(items =>
                {
                    return items.All(i => i.IsCompleted != false);

                }).Subscribe(result =>
                {
                    NotHaveUncompletedBlockedBy = result;
                })
                .AddToDispose(this);

            //Set IsCanBeCompleted
            this.WhenAnyValue(m => m.NotHaveUncompletedContains, m => m.NotHaveUncompletedBlockedBy)
                .Subscribe(tuple =>
                {
                    IsCanBeCompleted = tuple.Item1 && tuple.Item2;
                    if (IsCanBeCompleted && UnlockedDateTime == null)
                    {
                        UnlockedDateTime = DateTimeOffset.UtcNow;
                    }

                    if (!IsCanBeCompleted && UnlockedDateTime != null)
                    {
                        UnlockedDateTime = null;
                    }
                })
                .AddToDispose(this);

            ArchiveCommand = ReactiveCommand.Create(() =>
            {
                var notificationManager = Locator.Current.GetService<INotificationManagerWrapper>();

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

            RemoveFunc = async () => await (taskStorage.Delete(this));            

            CloneFunc = async destination =>
            {
                var clone = new TaskItem
                {
                    BlocksTasks = Model.BlocksTasks.ToList(),
                    BlockedByTasks = Model.BlockedByTasks.ToList(),
                    ContainsTasks = Model.ContainsTasks.ToList(),
                    Description = Model.Description,
                    Title = Model.Title,
                    PlannedDuration = Model.PlannedDuration,
                    Repeater = Model.Repeater,
                    Wanted = Model.Wanted,
                };
                var vm = new TaskItemViewModel(clone, taskStorage);
                return await taskStorage.Clone(vm, destination);
            };

            UnblockCommand = ReactiveCommand.Create<TaskItemViewModel, Unit>(
                (m) =>
                {
                    taskStorage.Unblock(this, m);
                    return Unit.Default;
                });

            DeleteParentChildRelationCommand = ReactiveCommand.Create<TaskItemViewModel, Unit> (
                (m) =>
                {
                    taskStorage.RemoveParentChildConnection(this, m);
                    return Unit.Default;
                });            

            //Subscribe to Save when property changed
            if (this is INotifyPropertyChanged inpc)
            {
                Observable.FromEventPattern(inpc, nameof(INotifyPropertyChanged.PropertyChanged))
                    .Where(changed =>
                    {
                        var args = changed.EventArgs as PropertyChangedEventArgs;
                        switch (args.PropertyName)
                        {
                            case nameof(Title):
                            case nameof(IsCompleted):
                            case nameof(Description):
                            case nameof(ArchiveDateTime):
                            case nameof(UnlockedDateTime):
                            case nameof(PlannedBeginDateTime):
                            case nameof(PlannedEndDateTime):
                            case nameof(PlannedDuration):
                            case nameof(Repeater):
                            case nameof(Importance):
                            case nameof(Wanted):
                                return true;
                        }

                        return false;
                    })
                    .Throttle(PropertyChangedThrottleTimeSpanDefault)
                    .Subscribe(x =>
                    {
                        if (_isInited) SaveItemCommand.Execute();                                                                        
                    }
                    )
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
                .Subscribe(r =>
                {
                    if (r is INotifyPropertyChanged repeater)
                    {
                        Observable.FromEventPattern(repeater, nameof(INotifyPropertyChanged.PropertyChanged))
                            .Where(changed =>
                            {
                                var args = changed.EventArgs as PropertyChangedEventArgs;
                                switch (args.PropertyName)
                                {
                                    //case nameof(Repeater.Type)://TODO из интерфейса прилетает изменение при переключении
                                    case nameof(Repeater.AfterComplete):
                                    case nameof(Repeater.Period):
                                    case nameof(Repeater.Monday):
                                    case nameof(Repeater.Tuesday):
                                    case nameof(Repeater.Wednesday):
                                    case nameof(Repeater.Thursday):
                                    case nameof(Repeater.Friday):
                                    case nameof(Repeater.Saturday):
                                    case nameof(Repeater.Sunday):
                                        return true;
                                }
                                return false;
                            })
                            .Throttle(TimeSpan.FromSeconds(2))
                            .Subscribe(x =>
                            {
                                if (_isInited) SaveItemCommand.Execute();
                            }
                            )
                            .AddToDispose(this);
                    }
                })
                .AddToDispose(this);

            _isInited = true;
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

        public ICommand ArchiveCommand { get; set; }
        public Func<Task<bool>> RemoveFunc { get; set; }
        public Func<TaskItemViewModel, Task<TaskItemViewModel>> CloneFunc { get; set; }

        public bool RemoveRequiresConfirmation(string parentId) => parentId == null || (Parents.Contains(parentId) ? Parents.Count == 1 : Parents.Count == 0);

        public TaskItem Model
        {
            get =>
                new TaskItem
                {
                    Id = Id,
                    Title = Title,
                    Description = Description,
                    CreatedDateTime = CreatedDateTime,
                    UnlockedDateTime = UnlockedDateTime,
                    CompletedDateTime = CompletedDateTime,
                    ArchiveDateTime = ArchiveDateTime,
                    PlannedBeginDateTime = PlannedBeginDateTime,
                    PlannedEndDateTime = PlannedEndDateTime,
                    PlannedDuration = PlannedDuration,
                    Importance = Importance,
                    Wanted = Wanted,
                    IsCompleted = IsCompleted,
                    PrevVersion = PrevVersion,
                    BlocksTasks = Blocks.ToList(),
                    BlockedByTasks = BlockedBy.ToList(),
                    ContainsTasks = Contains.ToList(),
                    ParentTasks = Parents.ToList(),
                    Repeater = Repeater?.Model,
                };
            set
            {
                Id = value.Id;
                Update(value);
            }
        }

        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool IsCanBeCompleted { get; private set; }
        public bool? IsCompleted { get; set; }
        public bool PrevVersion { get; set; }
        public DateTimeOffset CreatedDateTime { get; set; }
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

        public TaskWrapperViewModel CurrentItemContains => Locator.Current.GetService<MainWindowViewModel>()?.CurrentItemContains;
        public TaskWrapperViewModel CurrentItemParents => Locator.Current.GetService<MainWindowViewModel>()?.CurrentItemParents;
        public TaskWrapperViewModel CurrentItemBlocks => Locator.Current.GetService<MainWindowViewModel>()?.CurrentItemBlocks;
        public TaskWrapperViewModel CurrentItemBlockedBy => Locator.Current.GetService<MainWindowViewModel>()?.CurrentItemBlockedBy;

        public ObservableCollection<string> Contains { get; set; } = new();
        public ObservableCollection<string> Parents { get; set; } = new();
        public ObservableCollection<string> Blocks { get; set; } = new();
        public ObservableCollection<string> BlockedBy { get; set; } = new();

        public ICommand UnblockCommand { get; set; }        
        public ICommand DeleteParentChildRelationCommand { get; set; }
        
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

        public string GetAllEmoji { get; set; }

        const string EmojiPattern = @"[#*0-9]\uFE0F?\u20E3|©\uFE0F?|[®\u203C\u2049\u2122\u2139\u2194-\u2199\u21A9\u21AA]\uFE0F?|[\u231A\u231B]|[\u2328\u23CF]\uFE0F?|[\u23E9-\u23EC]|[\u23ED-\u23EF]\uFE0F?|\u23F0|[\u23F1\u23F2]\uFE0F?|\u23F3|[\u23F8-\u23FA\u24C2\u25AA\u25AB\u25B6\u25C0\u25FB\u25FC]\uFE0F?|[\u25FD\u25FE]|[\u2600-\u2604\u260E\u2611]\uFE0F?|[\u2614\u2615]|\u2618\uFE0F?|\u261D(?:\uD83C[\uDFFB-\uDFFF]|\uFE0F)?|[\u2620\u2622\u2623\u2626\u262A\u262E\u262F\u2638-\u263A\u2640\u2642]\uFE0F?|[\u2648-\u2653]|[\u265F\u2660\u2663\u2665\u2666\u2668\u267B\u267E]\uFE0F?|\u267F|\u2692\uFE0F?|\u2693|[\u2694-\u2697\u2699\u269B\u269C\u26A0]\uFE0F?|\u26A1|\u26A7\uFE0F?|[\u26AA\u26AB]|[\u26B0\u26B1]\uFE0F?|[\u26BD\u26BE\u26C4\u26C5]|\u26C8\uFE0F?|\u26CE|[\u26CF\u26D1\u26D3]\uFE0F?|\u26D4|\u26E9\uFE0F?|\u26EA|[\u26F0\u26F1]\uFE0F?|[\u26F2\u26F3]|\u26F4\uFE0F?|\u26F5|[\u26F7\u26F8]\uFE0F?|\u26F9(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?|\uFE0F(?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\u26FA\u26FD]|\u2702\uFE0F?|\u2705|[\u2708\u2709]\uFE0F?|[\u270A\u270B](?:\uD83C[\uDFFB-\uDFFF])?|[\u270C\u270D](?:\uD83C[\uDFFB-\uDFFF]|\uFE0F)?|\u270F\uFE0F?|[\u2712\u2714\u2716\u271D\u2721]\uFE0F?|\u2728|[\u2733\u2734\u2744\u2747]\uFE0F?|[\u274C\u274E\u2753-\u2755\u2757]|\u2763\uFE0F?|\u2764(?:\u200D(?:\uD83D\uDD25|\uD83E\uDE79)|\uFE0F(?:\u200D(?:\uD83D\uDD25|\uD83E\uDE79))?)?|[\u2795-\u2797]|\u27A1\uFE0F?|[\u27B0\u27BF]|[\u2934\u2935\u2B05-\u2B07]\uFE0F?|[\u2B1B\u2B1C\u2B50\u2B55]|[\u3030\u303D\u3297\u3299]\uFE0F?|\uD83C(?:[\uDC04\uDCCF]|[\uDD70\uDD71\uDD7E\uDD7F]\uFE0F?|[\uDD8E\uDD91-\uDD9A]|\uDDE6\uD83C[\uDDE8-\uDDEC\uDDEE\uDDF1\uDDF2\uDDF4\uDDF6-\uDDFA\uDDFC\uDDFD\uDDFF]|\uDDE7\uD83C[\uDDE6\uDDE7\uDDE9-\uDDEF\uDDF1-\uDDF4\uDDF6-\uDDF9\uDDFB\uDDFC\uDDFE\uDDFF]|\uDDE8\uD83C[\uDDE6\uDDE8\uDDE9\uDDEB-\uDDEE\uDDF0-\uDDF5\uDDF7\uDDFA-\uDDFF]|\uDDE9\uD83C[\uDDEA\uDDEC\uDDEF\uDDF0\uDDF2\uDDF4\uDDFF]|\uDDEA\uD83C[\uDDE6\uDDE8\uDDEA\uDDEC\uDDED\uDDF7-\uDDFA]|\uDDEB\uD83C[\uDDEE-\uDDF0\uDDF2\uDDF4\uDDF7]|\uDDEC\uD83C[\uDDE6\uDDE7\uDDE9-\uDDEE\uDDF1-\uDDF3\uDDF5-\uDDFA\uDDFC\uDDFE]|\uDDED\uD83C[\uDDF0\uDDF2\uDDF3\uDDF7\uDDF9\uDDFA]|\uDDEE\uD83C[\uDDE8-\uDDEA\uDDF1-\uDDF4\uDDF6-\uDDF9]|\uDDEF\uD83C[\uDDEA\uDDF2\uDDF4\uDDF5]|\uDDF0\uD83C[\uDDEA\uDDEC-\uDDEE\uDDF2\uDDF3\uDDF5\uDDF7\uDDFC\uDDFE\uDDFF]|\uDDF1\uD83C[\uDDE6-\uDDE8\uDDEE\uDDF0\uDDF7-\uDDFB\uDDFE]|\uDDF2\uD83C[\uDDE6\uDDE8-\uDDED\uDDF0-\uDDFF]|\uDDF3\uD83C[\uDDE6\uDDE8\uDDEA-\uDDEC\uDDEE\uDDF1\uDDF4\uDDF5\uDDF7\uDDFA\uDDFF]|\uDDF4\uD83C\uDDF2|\uDDF5\uD83C[\uDDE6\uDDEA-\uDDED\uDDF0-\uDDF3\uDDF7-\uDDF9\uDDFC\uDDFE]|\uDDF6\uD83C\uDDE6|\uDDF7\uD83C[\uDDEA\uDDF4\uDDF8\uDDFA\uDDFC]|\uDDF8\uD83C[\uDDE6-\uDDEA\uDDEC-\uDDF4\uDDF7-\uDDF9\uDDFB\uDDFD-\uDDFF]|\uDDF9\uD83C[\uDDE6\uDDE8\uDDE9\uDDEB-\uDDED\uDDEF-\uDDF4\uDDF7\uDDF9\uDDFB\uDDFC\uDDFF]|\uDDFA\uD83C[\uDDE6\uDDEC\uDDF2\uDDF3\uDDF8\uDDFE\uDDFF]|\uDDFB\uD83C[\uDDE6\uDDE8\uDDEA\uDDEC\uDDEE\uDDF3\uDDFA]|\uDDFC\uD83C[\uDDEB\uDDF8]|\uDDFD\uD83C\uDDF0|\uDDFE\uD83C[\uDDEA\uDDF9]|\uDDFF\uD83C[\uDDE6\uDDF2\uDDFC]|\uDE01|\uDE02\uFE0F?|[\uDE1A\uDE2F\uDE32-\uDE36]|\uDE37\uFE0F?|[\uDE38-\uDE3A\uDE50\uDE51\uDF00-\uDF20]|[\uDF21\uDF24-\uDF2C]\uFE0F?|[\uDF2D-\uDF35]|\uDF36\uFE0F?|[\uDF37-\uDF7C]|\uDF7D\uFE0F?|[\uDF7E-\uDF84]|\uDF85(?:\uD83C[\uDFFB-\uDFFF])?|[\uDF86-\uDF93]|[\uDF96\uDF97\uDF99-\uDF9B\uDF9E\uDF9F]\uFE0F?|[\uDFA0-\uDFC1]|\uDFC2(?:\uD83C[\uDFFB-\uDFFF])?|[\uDFC3\uDFC4](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDFC5\uDFC6]|\uDFC7(?:\uD83C[\uDFFB-\uDFFF])?|[\uDFC8\uDFC9]|\uDFCA(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDFCB\uDFCC](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?|\uFE0F(?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDFCD\uDFCE]\uFE0F?|[\uDFCF-\uDFD3]|[\uDFD4-\uDFDF]\uFE0F?|[\uDFE0-\uDFF0]|\uDFF3(?:\u200D(?:\u26A7\uFE0F?|\uD83C\uDF08)|\uFE0F(?:\u200D(?:\u26A7\uFE0F?|\uD83C\uDF08))?)?|\uDFF4(?:\u200D\u2620\uFE0F?|\uDB40\uDC67\uDB40\uDC62\uDB40(?:\uDC65\uDB40\uDC6E\uDB40\uDC67|\uDC73\uDB40\uDC63\uDB40\uDC74|\uDC77\uDB40\uDC6C\uDB40\uDC73)\uDB40\uDC7F)?|[\uDFF5\uDFF7]\uFE0F?|[\uDFF8-\uDFFF])|\uD83D(?:[\uDC00-\uDC07]|\uDC08(?:\u200D\u2B1B)?|[\uDC09-\uDC14]|\uDC15(?:\u200D\uD83E\uDDBA)?|[\uDC16-\uDC3A]|\uDC3B(?:\u200D\u2744\uFE0F?)?|[\uDC3C-\uDC3E]|\uDC3F\uFE0F?|\uDC40|\uDC41(?:\u200D\uD83D\uDDE8\uFE0F?|\uFE0F(?:\u200D\uD83D\uDDE8\uFE0F?)?)?|[\uDC42\uDC43](?:\uD83C[\uDFFB-\uDFFF])?|[\uDC44\uDC45]|[\uDC46-\uDC50](?:\uD83C[\uDFFB-\uDFFF])?|[\uDC51-\uDC65]|[\uDC66\uDC67](?:\uD83C[\uDFFB-\uDFFF])?|\uDC68(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:\uDC8B\u200D\uD83D)?\uDC68|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D(?:\uDC66(?:\u200D\uD83D\uDC66)?|\uDC67(?:\u200D\uD83D[\uDC66\uDC67])?|[\uDC68\uDC69]\u200D\uD83D(?:\uDC66(?:\u200D\uD83D\uDC66)?|\uDC67(?:\u200D\uD83D[\uDC66\uDC67])?)|[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92])|\uD83E[\uDDAF-\uDDB3\uDDBC\uDDBD])|\uD83C(?:\uDFFB(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:\uDC8B\u200D\uD83D)?\uDC68\uD83C[\uDFFB-\uDFFF]|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83D\uDC68\uD83C[\uDFFC-\uDFFF]|[\uDDAF-\uDDB3\uDDBC\uDDBD])))?|\uDFFC(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:\uDC8B\u200D\uD83D)?\uDC68\uD83C[\uDFFB-\uDFFF]|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83D\uDC68\uD83C[\uDFFB\uDFFD-\uDFFF]|[\uDDAF-\uDDB3\uDDBC\uDDBD])))?|\uDFFD(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:\uDC8B\u200D\uD83D)?\uDC68\uD83C[\uDFFB-\uDFFF]|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83D\uDC68\uD83C[\uDFFB\uDFFC\uDFFE\uDFFF]|[\uDDAF-\uDDB3\uDDBC\uDDBD])))?|\uDFFE(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:\uDC8B\u200D\uD83D)?\uDC68\uD83C[\uDFFB-\uDFFF]|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83D\uDC68\uD83C[\uDFFB-\uDFFD\uDFFF]|[\uDDAF-\uDDB3\uDDBC\uDDBD])))?|\uDFFF(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:\uDC8B\u200D\uD83D)?\uDC68\uD83C[\uDFFB-\uDFFF]|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83D\uDC68\uD83C[\uDFFB-\uDFFE]|[\uDDAF-\uDDB3\uDDBC\uDDBD])))?))?|\uDC69(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:\uDC8B\u200D\uD83D)?[\uDC68\uDC69]|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D(?:\uDC66(?:\u200D\uD83D\uDC66)?|\uDC67(?:\u200D\uD83D[\uDC66\uDC67])?|\uDC69\u200D\uD83D(?:\uDC66(?:\u200D\uD83D\uDC66)?|\uDC67(?:\u200D\uD83D[\uDC66\uDC67])?)|[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92])|\uD83E[\uDDAF-\uDDB3\uDDBC\uDDBD])|\uD83C(?:\uDFFB(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFF]|\uDC8B\u200D\uD83D[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFF])|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83D[\uDC68\uDC69]\uD83C[\uDFFC-\uDFFF]|[\uDDAF-\uDDB3\uDDBC\uDDBD])))?|\uDFFC(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFF]|\uDC8B\u200D\uD83D[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFF])|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83D[\uDC68\uDC69]\uD83C[\uDFFB\uDFFD-\uDFFF]|[\uDDAF-\uDDB3\uDDBC\uDDBD])))?|\uDFFD(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFF]|\uDC8B\u200D\uD83D[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFF])|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83D[\uDC68\uDC69]\uD83C[\uDFFB\uDFFC\uDFFE\uDFFF]|[\uDDAF-\uDDB3\uDDBC\uDDBD])))?|\uDFFE(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFF]|\uDC8B\u200D\uD83D[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFF])|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83D[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFD\uDFFF]|[\uDDAF-\uDDB3\uDDBC\uDDBD])))?|\uDFFF(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D\uD83D(?:[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFF]|\uDC8B\u200D\uD83D[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFF])|\uD83C[\uDF3E\uDF73\uDF7C\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83D[\uDC68\uDC69]\uD83C[\uDFFB-\uDFFE]|[\uDDAF-\uDDB3\uDDBC\uDDBD])))?))?|\uDC6A|[\uDC6B-\uDC6D](?:\uD83C[\uDFFB-\uDFFF])?|\uDC6E(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDC6F(?:\u200D[\u2640\u2642]\uFE0F?)?|[\uDC70\uDC71](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDC72(?:\uD83C[\uDFFB-\uDFFF])?|\uDC73(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDC74-\uDC76](?:\uD83C[\uDFFB-\uDFFF])?|\uDC77(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDC78(?:\uD83C[\uDFFB-\uDFFF])?|[\uDC79-\uDC7B]|\uDC7C(?:\uD83C[\uDFFB-\uDFFF])?|[\uDC7D-\uDC80]|[\uDC81\uDC82](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDC83(?:\uD83C[\uDFFB-\uDFFF])?|\uDC84|\uDC85(?:\uD83C[\uDFFB-\uDFFF])?|[\uDC86\uDC87](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDC88-\uDC8E]|\uDC8F(?:\uD83C[\uDFFB-\uDFFF])?|\uDC90|\uDC91(?:\uD83C[\uDFFB-\uDFFF])?|[\uDC92-\uDCA9]|\uDCAA(?:\uD83C[\uDFFB-\uDFFF])?|[\uDCAB-\uDCFC]|\uDCFD\uFE0F?|[\uDCFF-\uDD3D]|[\uDD49\uDD4A]\uFE0F?|[\uDD4B-\uDD4E\uDD50-\uDD67]|[\uDD6F\uDD70\uDD73]\uFE0F?|\uDD74(?:\uD83C[\uDFFB-\uDFFF]|\uFE0F)?|\uDD75(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?|\uFE0F(?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDD76-\uDD79]\uFE0F?|\uDD7A(?:\uD83C[\uDFFB-\uDFFF])?|[\uDD87\uDD8A-\uDD8D]\uFE0F?|\uDD90(?:\uD83C[\uDFFB-\uDFFF]|\uFE0F)?|[\uDD95\uDD96](?:\uD83C[\uDFFB-\uDFFF])?|\uDDA4|[\uDDA5\uDDA8\uDDB1\uDDB2\uDDBC\uDDC2-\uDDC4\uDDD1-\uDDD3\uDDDC-\uDDDE\uDDE1\uDDE3\uDDE8\uDDEF\uDDF3\uDDFA]\uFE0F?|[\uDDFB-\uDE2D]|\uDE2E(?:\u200D\uD83D\uDCA8)?|[\uDE2F-\uDE34]|\uDE35(?:\u200D\uD83D\uDCAB)?|\uDE36(?:\u200D\uD83C\uDF2B\uFE0F?)?|[\uDE37-\uDE44]|[\uDE45-\uDE47](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDE48-\uDE4A]|\uDE4B(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDE4C(?:\uD83C[\uDFFB-\uDFFF])?|[\uDE4D\uDE4E](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDE4F(?:\uD83C[\uDFFB-\uDFFF])?|[\uDE80-\uDEA2]|\uDEA3(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDEA4-\uDEB3]|[\uDEB4-\uDEB6](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDEB7-\uDEBF]|\uDEC0(?:\uD83C[\uDFFB-\uDFFF])?|[\uDEC1-\uDEC5]|\uDECB\uFE0F?|\uDECC(?:\uD83C[\uDFFB-\uDFFF])?|[\uDECD-\uDECF]\uFE0F?|[\uDED0-\uDED2\uDED5-\uDED7]|[\uDEE0-\uDEE5\uDEE9]\uFE0F?|[\uDEEB\uDEEC]|[\uDEF0\uDEF3]\uFE0F?|[\uDEF4-\uDEFC\uDFE0-\uDFEB])|\uD83E(?:\uDD0C(?:\uD83C[\uDFFB-\uDFFF])?|[\uDD0D\uDD0E]|\uDD0F(?:\uD83C[\uDFFB-\uDFFF])?|[\uDD10-\uDD17]|[\uDD18-\uDD1C](?:\uD83C[\uDFFB-\uDFFF])?|\uDD1D|[\uDD1E\uDD1F](?:\uD83C[\uDFFB-\uDFFF])?|[\uDD20-\uDD25]|\uDD26(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDD27-\uDD2F]|[\uDD30-\uDD34](?:\uD83C[\uDFFB-\uDFFF])?|\uDD35(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDD36(?:\uD83C[\uDFFB-\uDFFF])?|[\uDD37-\uDD39](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDD3A|\uDD3C(?:\u200D[\u2640\u2642]\uFE0F?)?|[\uDD3D\uDD3E](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDD3F-\uDD45\uDD47-\uDD76]|\uDD77(?:\uD83C[\uDFFB-\uDFFF])?|[\uDD78\uDD7A-\uDDB4]|[\uDDB5\uDDB6](?:\uD83C[\uDFFB-\uDFFF])?|\uDDB7|[\uDDB8\uDDB9](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDDBA|\uDDBB(?:\uD83C[\uDFFB-\uDFFF])?|[\uDDBC-\uDDCB]|[\uDDCD-\uDDCF](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDDD0|\uDDD1(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\uD83C[\uDF3E\uDF73\uDF7C\uDF84\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83E\uDDD1|[\uDDAF-\uDDB3\uDDBC\uDDBD]))|\uD83C(?:\uDFFB(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D(?:\uD83D\uDC8B\u200D)?\uD83E\uDDD1\uD83C[\uDFFC-\uDFFF]|\uD83C[\uDF3E\uDF73\uDF7C\uDF84\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83E\uDDD1\uD83C[\uDFFB-\uDFFF]|[\uDDAF-\uDDB3\uDDBC\uDDBD])))?|\uDFFC(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D(?:\uD83D\uDC8B\u200D)?\uD83E\uDDD1\uD83C[\uDFFB\uDFFD-\uDFFF]|\uD83C[\uDF3E\uDF73\uDF7C\uDF84\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83E\uDDD1\uD83C[\uDFFB-\uDFFF]|[\uDDAF-\uDDB3\uDDBC\uDDBD])))?|\uDFFD(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D(?:\uD83D\uDC8B\u200D)?\uD83E\uDDD1\uD83C[\uDFFB\uDFFC\uDFFE\uDFFF]|\uD83C[\uDF3E\uDF73\uDF7C\uDF84\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83E\uDDD1\uD83C[\uDFFB-\uDFFF]|[\uDDAF-\uDDB3\uDDBC\uDDBD])))?|\uDFFE(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D(?:\uD83D\uDC8B\u200D)?\uD83E\uDDD1\uD83C[\uDFFB-\uDFFD\uDFFF]|\uD83C[\uDF3E\uDF73\uDF7C\uDF84\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83E\uDDD1\uD83C[\uDFFB-\uDFFF]|[\uDDAF-\uDDB3\uDDBC\uDDBD])))?|\uDFFF(?:\u200D(?:[\u2695\u2696\u2708]\uFE0F?|\u2764\uFE0F?\u200D(?:\uD83D\uDC8B\u200D)?\uD83E\uDDD1\uD83C[\uDFFB-\uDFFE]|\uD83C[\uDF3E\uDF73\uDF7C\uDF84\uDF93\uDFA4\uDFA8\uDFEB\uDFED]|\uD83D[\uDCBB\uDCBC\uDD27\uDD2C\uDE80\uDE92]|\uD83E(?:\uDD1D\u200D\uD83E\uDDD1\uD83C[\uDFFB-\uDFFF]|[\uDDAF-\uDDB3\uDDBC\uDDBD])))?))?|[\uDDD2\uDDD3](?:\uD83C[\uDFFB-\uDFFF])?|\uDDD4(?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|\uDDD5(?:\uD83C[\uDFFB-\uDFFF])?|[\uDDD6-\uDDDD](?:\u200D[\u2640\u2642]\uFE0F?|\uD83C[\uDFFB-\uDFFF](?:\u200D[\u2640\u2642]\uFE0F?)?)?|[\uDDDE\uDDDF](?:\u200D[\u2640\u2642]\uFE0F?)?|[\uDDE0-\uDDFF\uDE70-\uDE74\uDE78-\uDE7A\uDE80-\uDE86\uDE90-\uDEA8\uDEB0-\uDEB6\uDEC0-\uDEC2\uDED0-\uDED6])";


        public string Emoji => Regex.Match(Title ?? "", EmojiPattern).Value;

        public string OnlyTextTitle => Regex.Replace(Title ?? "", EmojiPattern, "");

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

        public RepeaterPatternViewModel Repeater { get; set; }

        public bool IsHaveRepeater => Repeater != null && Repeater.Type != RepeaterType.None;

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
        
        private void ShowModalAndChangeChildrenStatuses(INotificationManagerWrapper notificationManager, string taskName,
            List<TaskItemViewModel> childrenTasks, ArchiveMethodType methodType)
        {
            if (childrenTasks.Count == 0) return;

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

            var methodTypeString = methodType.ToString();
            notificationManager.Ask($"{methodTypeString} contained tasks",
                $"Are you sure you want to {methodTypeString.ToLower()} the {childrenTasks.Count} contained tasks from \"{taskName}\"?",
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

            if (Title != taskItem.Title) Title = taskItem.Title;
            if (Description != taskItem.Description) Description = taskItem.Description;
            if (CreatedDateTime != taskItem.CreatedDateTime) CreatedDateTime = taskItem.CreatedDateTime;
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
            if (PrevVersion != taskItem.PrevVersion) PrevVersion = taskItem.PrevVersion;


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