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

        private void Init()
        {
            
            var taskRepository = Locator.Current.GetService<TaskRepository>();

            if (Model.BlocksTasks.Any() || Model.ContainsTasks.Any())
            {
                FillTaskViewModelCollection(taskRepository.GetById(Model.ContainsTasks), ContainsTasks);
                FillTaskViewModelCollection(taskRepository.GetById(Model.BlocksTasks), BlocksTasks);
            }

            FillTaskViewModelCollection(taskRepository.GetParentsById(Model.Id), ParentsTasks);
            FillTaskViewModelCollection(taskRepository.GetBlockedById(Model.Id), BlockedByTasks);
            this.WhenAnyValue(m => m.IsCompleted).Subscribe(b =>
            {
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
            BlockedByTasks.CollectionChanged += (sender, args) => Update();
            ContainsTasks.CollectionChanged += (sender, args) => Update();
        }

        private void Update()
        {
            IsCanBeComplited = GetCanBeCompleted();
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
        public DateTimeOffset CreatedDateTime => Model.CreatedDateTime;
        public DateTimeOffset? UnlockedDateTime => Model.UnlockedDateTime;
        public DateTimeOffset? CompletedDateTime => Model.CompletedDateTime;
        public DateTimeOffset? ArchiveDateTime => Model.ArchiveDateTime;
        public ObservableCollection<TaskItemViewModel> ContainsTasks { get; set; } = new();
        public ObservableCollection<TaskItemViewModel> ParentsTasks { get; set; } = new();
        public ObservableCollection<TaskItemViewModel> BlocksTasks { get; set; } = new();
        public ObservableCollection<TaskItemViewModel> BlockedByTasks { get; set; } = new();
    }
}