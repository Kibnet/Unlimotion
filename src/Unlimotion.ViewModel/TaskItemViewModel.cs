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

namespace Unlimotion.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    public class TaskItemViewModel: DisposableList
    {
        public TaskItemViewModel(TaskItem model, ITaskRepository taskRepository)
        {
            Model = model;
            Init(taskRepository);
        }

        private bool GetCanBeCompleted() => (ContainsTasks.All(m => m.IsCompleted != false)) && (BlockedByTasks.All(m => m.IsCompleted != false));

        public ICommand SaveItemCommand;

        private bool _isInited;
        public bool NotHaveUncompletedContains { get; private set; }
        public bool NotHaveUncompletedBlockedBy { get; private set; }
        private ReadOnlyObservableCollection<TaskItemViewModel> _containsTasks;
        private ReadOnlyObservableCollection<TaskItemViewModel> _parentsTasks;
        private ReadOnlyObservableCollection<TaskItemViewModel> _blocksTasks;
        private ReadOnlyObservableCollection<TaskItemViewModel> _blockedByTasks;

        private void Init(ITaskRepository taskRepository)
        {
            SaveItemCommand = ReactiveCommand.Create(() =>
            {
                taskRepository.Save(Model);
            });

            
            //Subscribe ContainsTasks
            var containsFilter = Contains.ToObservableChangeSet()
                .ToCollection()
                .Select(items =>
                {
                    bool Predicate(TaskItemViewModel task) => items.Contains(task.Id);
                    return (Func<TaskItemViewModel, bool>)Predicate;
                });

            taskRepository.Tasks.Connect()
                .Filter(containsFilter)
                .Bind(out _containsTasks)
                .Subscribe()
                .AddToDispose(this);

            //Subscribe for set Parent for children
            ContainsTasks.ToObservableChangeSet()
                .Subscribe(set =>
                {
                    foreach (var change in set)
                    {
                        switch (change.Reason)
                        {
                            case ListChangeReason.Add:
                                change.Item.Current.Parents.Add(Id);
                                break;
                            case ListChangeReason.AddRange:
                                foreach (var model in change.Range)
                                {
                                    model.Parents.Add(Id);
                                }
                                break;
                            case ListChangeReason.Replace:
                                break;
                            case ListChangeReason.Remove:
                                change.Item.Current.Parents.Remove(Id);
                                break;
                            case ListChangeReason.RemoveRange:
                                foreach (var model in change.Range)
                                {
                                    model.Parents.Remove(Id);
                                }
                                break;
                            case ListChangeReason.Refresh:
                                break;
                            case ListChangeReason.Moved:
                                break;
                            case ListChangeReason.Clear:
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                }).AddToDispose(this);

            //Subscribe ParentsTasks
            var parentsFilter = Parents.ToObservableChangeSet()
                .ToCollection()
                .Select(items =>
                {
                    bool Predicate(TaskItemViewModel task) => items.Contains(task.Id);
                    return (Func<TaskItemViewModel, bool>)Predicate;
                });

            taskRepository.Tasks.Connect()
                .Filter(parentsFilter)
                .Bind(out _parentsTasks)
                .Subscribe()
                .AddToDispose(this);
            
            //Subscribe BlocksTasks
            var blocksFilter = Blocks.ToObservableChangeSet()
                .ToCollection()
                .Select(items =>
                {
                    bool Predicate(TaskItemViewModel task) => items.Contains(task.Id);
                    return (Func<TaskItemViewModel, bool>)Predicate;
                });

            taskRepository.Tasks.Connect()
                .Filter(blocksFilter)
                .Bind(out _blocksTasks)
                .Subscribe()
                .AddToDispose(this);

            //Subscribe for set BlockedBy for Blocks
            BlocksTasks.ToObservableChangeSet()
                .Subscribe(set =>
                {
                    foreach (var change in set)
                    {
                        switch (change.Reason)
                        {
                            case ListChangeReason.Add:
                                change.Item.Current.BlockedBy.Add(Id);
                                break;
                            case ListChangeReason.AddRange:
                                foreach (var model in change.Range)
                                {
                                    model.BlockedBy.Add(Id);
                                }
                                break;
                            case ListChangeReason.Replace:
                                break;
                            case ListChangeReason.Remove:
                                change.Item.Current.BlockedBy.Remove(Id);
                                break;
                            case ListChangeReason.RemoveRange:
                                foreach (var model in change.Range)
                                {
                                    model.BlockedBy.Remove(Id);
                                }
                                break;
                            case ListChangeReason.Refresh:
                                break;
                            case ListChangeReason.Moved:
                                break;
                            case ListChangeReason.Clear:
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                }).AddToDispose(this);

            //Subscribe BlockedBy
            var blockedByFilter = BlockedBy.ToObservableChangeSet()
                .ToCollection()
                .Select(items =>
                {
                    bool Predicate(TaskItemViewModel task) => items.Contains(task.Id);
                    return (Func<TaskItemViewModel, bool>)Predicate;
                });

            taskRepository.Tasks.Connect()
                .Filter(blockedByFilter)
                .Bind(out _blockedByTasks)
                .Subscribe()
                .AddToDispose(this);

            //Subscribe IsCompleted
            this.WhenAnyValue(m => m.IsCompleted).Subscribe(b =>
            {
                if (b == true && CompletedDateTime == null)
                {
                    CompletedDateTime = DateTimeOffset.UtcNow;
                    ArchiveDateTime = null;
                }
                if (b == false)
                {
                    ArchiveDateTime = null;
                    CompletedDateTime = null;
                }
                if (b == null && ArchiveDateTime == null)
                {
                    ArchiveDateTime = DateTimeOffset.UtcNow;
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

            //Set IsCanBeComplited
            this.WhenAnyValue(m => m.NotHaveUncompletedContains, m => m.NotHaveUncompletedBlockedBy)
                .Subscribe(tuple =>
                {
                    IsCanBeComplited = tuple.Item1&&tuple.Item2;
                    if (IsCanBeComplited && UnlockedDateTime == null)
                    {
                        UnlockedDateTime = DateTimeOffset.UtcNow;
                    }
                    if (!IsCanBeComplited && UnlockedDateTime != null)
                    {
                        UnlockedDateTime = null;
                    }
                })
                .AddToDispose(this);
            
            ArchiveCommand = ReactiveCommand.Create(() =>
            {
                if (IsCompleted == null)
                {
                    IsCompleted = false;
                }
                else if (IsCompleted == false)
                {
                    IsCompleted = null;
                }
            }, this.WhenAnyValue(m => m.IsCompleted, b => b != true));

            RemoveFunc = parent =>
            {
                //Удаление ссылки из родителя
                parent?.Contains.Remove(Id);
                //Если родителей не осталось, удаляется сама задача
                if (Parents.Count == 0)
                {
                    taskRepository.Remove(Id);

                    foreach (var containsTask in ContainsTasks.ToList())
                    {
                        containsTask.Parents.Remove(Id);
                    }
                    return true;
                }
                return false;
            };

            //Subscribe to Save when property changed
            this.WhenAnyValue(m => m.Title, 
                    m => m.IsCompleted, 
                    m => m.Description, 
                    m => m.ArchiveDateTime,
                    m => m.UnlockedDateTime
                    )
                .Subscribe((_) =>
                {
                    if (_isInited) SaveItemCommand.Execute(null);
                });
            _isInited = true;
        }

        public ICommand ArchiveCommand { get; set; }
        public Func<TaskItemViewModel,bool> RemoveFunc { get; set; }

        public TaskItem Model
        {
            get =>
                new TaskItem
                {
                    Id = Id,
                    Title = Title,
                    Description = Description,
                    IsCompleted = IsCompleted,
                    CreatedDateTime = CreatedDateTime,
                    UnlockedDateTime = UnlockedDateTime,
                    CompletedDateTime = CompletedDateTime,
                    ArchiveDateTime = ArchiveDateTime,
                    BlocksTasks = Blocks.ToList(),
                    ContainsTasks = Contains.ToList(),
                };
            set
            {
                Id = value.Id;
                Title = value.Title;
                Description = value.Description;
                IsCompleted = value.IsCompleted;
                CreatedDateTime = value.CreatedDateTime;
                UnlockedDateTime = value.UnlockedDateTime;
                CompletedDateTime = value.CompletedDateTime;
                ArchiveDateTime = value.ArchiveDateTime;
                Blocks.AddRange(value.BlocksTasks);
                Contains.AddRange(value.ContainsTasks);
            }
        }

        public string Id { get; private set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool IsCanBeComplited { get; private set; }
        public bool? IsCompleted { get; set; }
        public DateTimeOffset CreatedDateTime { get; set; }
        public DateTimeOffset? UnlockedDateTime { get; set; }
        public DateTimeOffset? CompletedDateTime { get; set; }
        public DateTimeOffset? ArchiveDateTime { get; set; }

        public ReadOnlyObservableCollection<TaskItemViewModel> ContainsTasks => _containsTasks;

        public ReadOnlyObservableCollection<TaskItemViewModel> ParentsTasks => _parentsTasks;

        public ReadOnlyObservableCollection<TaskItemViewModel> BlocksTasks => _blocksTasks;

        public ReadOnlyObservableCollection<TaskItemViewModel> BlockedByTasks => _blockedByTasks;

        public ObservableCollection<string> Contains { get; set; } = new();
        public ObservableCollection<string> Parents { get; set; } = new();
        public ObservableCollection<string> Blocks { get; set; } = new();
        public ObservableCollection<string> BlockedBy { get; set; } = new();

        public void CopyInto(TaskItemViewModel destination)
        {
            destination.Contains.Add(Id);
            destination.SaveItemCommand.Execute(null);
        }

        public void MoveInto(TaskItemViewModel destination, TaskItemViewModel source)
        {
            destination.Contains.Add(Id);
            source?.Contains?.Remove(Id);

            destination.SaveItemCommand.Execute(null);
            source?.SaveItemCommand.Execute(null);
        }

        public void BlockBy(TaskItemViewModel blocker)
        {
            blocker.Blocks.Add(Id);
        }

        public IEnumerable<TaskItemViewModel> GetAllParents()
        {
            var hashSet = new HashSet<string>();
            var queue = new Queue<TaskItemViewModel>();
            foreach (var task in ParentsTasks)
            {
                queue.Enqueue(task);
            }

            while (queue.TryDequeue(out var parent))
            {
                if (hashSet.Contains(parent.Id))
                {
                    continue;
                }

                hashSet.Add(parent.Id);
                yield return parent;
                if (parent.ParentsTasks.Count > 0)
                {
                    foreach (var task in parent.ParentsTasks)
                    {
                        queue.Enqueue(task);
                    }
                }
            }
        }
    }
}