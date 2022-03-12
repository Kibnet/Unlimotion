using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reactive;
using System.Windows.Input;
using System.Xml.Serialization;
using DynamicData;
using PropertyChanged;
using ReactiveUI;
using Splat;

namespace Unlimotion.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    public class TaskItemViewModel
    {
        private TaskItemViewModel(TaskItem model, bool needInit = true)
        {
            Model = model;
            if (needInit)
            {
                Init();
            }
        }

        private bool GetCanBeCompleted() => (ContainsTasks.All(m => m.IsCompleted != false)) && (BlockedByTasks.All(m => m.IsCompleted != false));

        private ICommand SaveItemCommand;

        private bool _isInited;

        private void Init()
        {
            var taskRepository = Locator.Current.GetService<TaskRepository>();

            SaveItemCommand = ReactiveCommand.Create(() =>
            {
                taskRepository.Save(Model);
            });

            if (Model.BlocksTasks.Any() || Model.ContainsTasks.Any())
            {
                FillTaskViewModelCollection(taskRepository.GetById(Model.ContainsTasks), ContainsTasks);
                FillTaskViewModelCollection(taskRepository.GetById(Model.BlocksTasks), BlocksTasks);
            }

            FillTaskViewModelCollection(taskRepository.GetParentsById(Model.Id), ParentsTasks);
            FillTaskViewModelCollection(taskRepository.GetBlockedById(Model.Id), BlockedByTasks);
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
                foreach (var task in BlocksTasks)
                {
                    task.Update();
                }
                foreach (var task in ParentsTasks)
                {
                    task.Update();
                }
            });

            Update();
            BlockedByTasks.CollectionChanged += (sender, args) =>
            {
                Update();
                taskRepository.Save(Model);
            };
            ContainsTasks.CollectionChanged += (sender, args) =>
            {
                switch (args.Action)
                {
                    case NotifyCollectionChangedAction.Add:
                        {
                            var items = args.NewItems.GetEnumerable().Cast<TaskItemViewModel>().ToList();
                            var ids = items.Select(t => t.Id);
                            Model.ContainsTasks.InsertRange(args.NewStartingIndex, ids);
                            foreach (var item in items)
                            {
                                TaskItem taskItem = Model;
                                taskRepository.AddParent(item.Id, taskItem.Id);
                                item.ParentsTasks.Add(this);
                            }
                        }
                        break;
                    case NotifyCollectionChangedAction.Move:
                        break;
                    case NotifyCollectionChangedAction.Remove:
                        {
                            var items = args.OldItems.GetEnumerable().Cast<TaskItemViewModel>().ToList();
                            var ids = items.Select(t => t.Id);
                            Model.ContainsTasks.Remove(ids);
                            foreach (var item in items)
                            {
                                TaskItem taskItemId = Model;
                                taskRepository.RemoveParent(item.Id, taskItemId.Id);
                                item.ParentsTasks.Remove(this);
                            }
                        }
                        break;
                    case NotifyCollectionChangedAction.Replace:
                        break;
                    case NotifyCollectionChangedAction.Reset:
                        break;
                }

                Update();
                taskRepository.Save(Model);
            };
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

        private void Update()
        {
            IsCanBeComplited = GetCanBeCompleted();
            if (IsCanBeComplited && UnlockedDateTime == null)
            {
                UnlockedDateTime = DateTimeOffset.UtcNow;
            }
            if (!IsCanBeComplited && UnlockedDateTime != null)
            {
                UnlockedDateTime = null;
            }
        }

        private static void FillTaskViewModelCollection(IEnumerable<TaskItem> sourceTaskIds, ObservableCollection<TaskItemViewModel> destinationVMs)
        {
            if (sourceTaskIds != null)
            {
                foreach (var task in sourceTaskIds)
                {
                    destinationVMs.Add(GetViewModel(task));
                }
            }
        }

        private static Dictionary<string, TaskItemViewModel> viewModelStorage = new();

        public static TaskItemViewModel GetViewModel(TaskItem item)
        {
            if (viewModelStorage.TryGetValue(item.Id, out var vm))
            {
                return vm;
            }

            vm = new TaskItemViewModel(item, false);
            viewModelStorage[item.Id] = vm;
            vm.Init();
            return vm;
        }

        private TaskItem Model { get; set; }
        public string Id => Model.Id;
        public string Title { get => Model.Title; set => Model.Title = value; }
        public string Description { get => Model.Description; set => Model.Description = value; }
        public bool IsCanBeComplited { get; private set; }


        public bool? IsCompleted { get => Model.IsCompleted; set => Model.IsCompleted = value; }
        public DateTimeOffset CreatedDateTime
        {
            get => Model.CreatedDateTime;
            set => Model.CreatedDateTime = value;
        }

        public DateTimeOffset? UnlockedDateTime
        {
            get => Model.UnlockedDateTime;
            set => Model.UnlockedDateTime = value;
        }

        public DateTimeOffset? CompletedDateTime
        {
            get => Model.CompletedDateTime;
            set => Model.CompletedDateTime = value;
        }

        public DateTimeOffset? ArchiveDateTime
        {
            get => Model.ArchiveDateTime;
            set => Model.ArchiveDateTime = value;
        }

        public ObservableCollection<TaskItemViewModel> ContainsTasks { get; set; } = new();
        public ObservableCollection<TaskItemViewModel> ParentsTasks { get; set; } = new();
        public ObservableCollection<TaskItemViewModel> BlocksTasks { get; set; } = new();
        public ObservableCollection<TaskItemViewModel> BlockedByTasks { get; set; } = new();

        public void CopyInto(TaskItemViewModel destination)
        {
            destination.ContainsTasks.Add(this);
        }

        public void MoveInto(TaskItemViewModel destination, TaskItemViewModel source)
        {
            destination.ContainsTasks.Add(this);
            source?.ContainsTasks?.Remove(this);
        }

        public void BlockBy(TaskItemViewModel blocker)
        {
            throw new NotImplementedException();
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
                if (parent.ParentsTasks.Count>0)
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