using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PropertyChanged;
using ReactiveUI;
using Splat;

namespace Unlimotion.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    public class TaskItemViewModel:ReactiveObject
    {
        private TaskItemViewModel(TaskItem model, bool needInit = true)
        {
            Model = model;
            if (needInit)
            {
                Init();
            }
        }

        private void Init()
        {
            BlockedByTasks.CollectionChanged += (sender, args) => this.RaisePropertyChanged(nameof(IsCanBeComplited));
            ContainsTasks.CollectionChanged += (sender, args) => this.RaisePropertyChanged(nameof(IsCanBeComplited));
            if (Model.BlocksTasks.Any() || Model.ContainsTasks.Any())
            {
                var taskRepository = Locator.Current.GetService<TaskRepository>();

                FillTaskViewModelCollection(taskRepository.GetById(Model.ContainsTasks), ContainsTasks);
                FillTaskViewModelCollection(taskRepository.GetById(Model.BlocksTasks), BlocksTasks);

                FillTaskViewModelCollection(taskRepository.GetParentsById(Model.Id), ParentsTasks);
                FillTaskViewModelCollection(taskRepository.GetBlockedById(Model.Id), BlockedByTasks);
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
        public bool IsCanBeComplited => (!ContainsTasks.Any()) && (!BlockedByTasks.Any());
        
        public bool? IsCompleted { get => Model.IsCompleted; set => Model.IsCompleted = value; }
        public DateTimeOffset CreatedDateTime => Model.CreatedDateTime;
        public DateTimeOffset? UnlockedDateTime => Model.UnlockedDateTime;
        public DateTimeOffset? CompletedDateTime => Model.CompletedDateTime;
        public DateTimeOffset? ArchiveDateTime => Model.ArchiveDateTime;
        public ObservableCollection<TaskItemViewModel> ContainsTasks { get; set; } = new();
        public ObservableCollection<TaskItemViewModel> ParentsTasks { get; set; } = new();
        public ObservableCollection<TaskItemViewModel> BlocksTasks { get; set; } = new();
        public ObservableCollection<TaskItemViewModel> BlockedByTasks { get; set; } = new();
    }
    
    public class TaskItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; }
        public string Description { get; set; }
        public bool? IsCompleted { get; set; } = false;
        public DateTimeOffset CreatedDateTime { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? UnlockedDateTime { get; set; }
        public DateTimeOffset? CompletedDateTime { get; set; }
        public DateTimeOffset? ArchiveDateTime { get; set; }
        public List<string> ContainsTasks { get; set; } = new();
        public List<string> BlocksTasks { get; set; } = new();
    }
}
