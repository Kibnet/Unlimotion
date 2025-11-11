using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Avalonia.Controls;
using Avalonia.Input;
using ReactiveUI;
using Splat;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion.Views
{
    public partial class MainControl : UserControl
    {
        public MainControl()
        {
            InitializeComponent();
            AddHandler(DragDrop.DropEvent, Drop);
            AddHandler(DragDrop.DragOverEvent, DragOver);
            DataContextChanged += MainWindow_DataContextChanged;
        }

        private void MainWindow_DataContextChanged(object? sender, EventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.MoveToPath = ReactiveCommand.CreateFromTask(async () =>
                {
                    if (vm.CurrentTaskItem == null)
                        return;
                    var dialogs = Locator.Current.GetService<IDialogs>();
                    var path = await dialogs.ShowOpenFolderDialogAsync("Task Storage Path");

                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        var fileStorage = new FileStorage(path);
                        var set = new HashSet<string>();
                        var queue = new Queue<TaskItemViewModel>();
                        queue.Enqueue(vm.CurrentTaskItem);
                        while (queue.Count > 0)
                        {
                            var task = queue.Dequeue();
                            if (!set.Contains(task.Id))
                            {
                                set.Add(task.Id);
                                await fileStorage.Save(task.Model);
                                foreach (var item in task.ContainsTasks)
                                {
                                    queue.Enqueue(item);
                                }
                            }
                        }

                        var currentTaskStorage = Locator.Current.GetService<IStorage>();
                        foreach (var id in set)
                        {
                            await currentTaskStorage.Remove(id);
                        }
                    }
                });
            }
        }

        private const string CustomFormat = "application/xxx-unlimotion-task";

        private async void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var dragData = new DataObject();
            var control = sender as Control;
            var dc = control?.DataContext;
            if (dc == null)
            {
                return;
            }

            dragData.Set(CustomFormat, dc);

            var result = await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }

        public static void DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.Contains(CustomFormat) || e.Data.Contains(GraphControl.CustomFormat))
            {
                if (GetTasks(e, out var task, out var subItem)) return;

                if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
                {
                    e.DragEffects &= DragDropEffects.Copy;
                }
                else if (e.KeyModifiers == KeyModifiers.Shift)
                {
                    if (subItem.CanMoveInto(task))
                    {
                        e.DragEffects &= DragDropEffects.Move;
                    }
                    else
                    {
                        e.DragEffects = DragDropEffects.None;
                    }
                }
                else if (e.KeyModifiers == KeyModifiers.Control)
                {
                    e.DragEffects &= DragDropEffects.Link;
                }
                else if (e.KeyModifiers == KeyModifiers.Alt)
                {
                    e.DragEffects &= DragDropEffects.Link;
                }
                else
                {
                    if (subItem.CanMoveInto(task))
                    {
                        e.DragEffects &= DragDropEffects.Copy;
                    }
                    else
                    {
                        e.DragEffects = DragDropEffects.None;
                    }
                }
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
        }

        private static bool GetTasks(DragEventArgs e, out TaskItemViewModel? task, out TaskItemViewModel? subItem)
        {
            var control = e.Source as Control;
            task = control?.FindParentDataContext<TaskWrapperViewModel>()?.TaskItem ??
                   control?.FindParentDataContext<TaskItemViewModel>();

            var sub = e.Data.Get(CustomFormat) ?? e.Data.Get(GraphControl.CustomFormat);
            subItem = sub switch
            {
                TaskWrapperViewModel taskWrapperViewModel => taskWrapperViewModel?.TaskItem,
                TaskItemViewModel taskItemViewModel => taskItemViewModel,
                _ => null
            };

            if (subItem == null || task == null)
            {
                e.DragEffects = DragDropEffects.None;
                return true;
            }

            return false;
        }

        public static async Task Drop(object sender, DragEventArgs e)
        {
            if (e.Data.Contains(CustomFormat) || e.Data.Contains(GraphControl.CustomFormat))
            {
                if (GetTasks(e, out var task, out var subItem)) return;

                if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
                {
                    e.DragEffects &= DragDropEffects.Copy;
                    await subItem.CloneInto(task);
                    UpdateGraph(e.Source);
                    e.Handled = true;
                }
                else if (e.KeyModifiers == KeyModifiers.Shift)
                {
                    if (subItem.CanMoveInto(task))
                    {
                        TaskItemViewModel parent = null;
                        var breakFlag = false;
                        if (subItem.Parents.Count <= 1)
                        {
                            parent = subItem.ParentsTasks.FirstOrDefault();
                        }
                        else if ((e.Data.Get(CustomFormat) ?? e.Data.Get(GraphControl.CustomFormat)) is TaskWrapperViewModel parentWrapper)
                        {
                            parent = parentWrapper.Parent.TaskItem;
                        }
                        else
                        {
                            e.DragEffects &= DragDropEffects.None;
                            breakFlag = true;
                        }

                        if (!breakFlag)
                        {
                            e.DragEffects &= DragDropEffects.Move;
                            await subItem.MoveInto(task, parent);
                            UpdateGraph(e.Source);
                            e.Handled = true;
                        }
                    }
                    else
                    {
                        e.DragEffects = DragDropEffects.None;
                    }

                }
                else if (e.KeyModifiers == KeyModifiers.Control) //The dragged task blocks the target task
                {
                    e.DragEffects &= DragDropEffects.Link;
                    task.BlockBy(subItem); //subItem блокирует task
                    UpdateGraph(e.Source);
                    e.Handled = true;
                }
                else if (e.KeyModifiers == KeyModifiers.Alt) //The target task blocks the dragged task
                {
                    e.DragEffects &= DragDropEffects.Link;
                    subItem.BlockBy(task); //task блокирует subItem
                    UpdateGraph(e.Source);
                    e.Handled = true;
                }
                else
                {
                    if (subItem.CanMoveInto(task))
                    {
                        e.DragEffects &= DragDropEffects.Copy;
                        await subItem.CopyInto(task);
                        UpdateGraph(e.Source);
                        e.Handled = true;
                    }
                    else
                    {
                        e.DragEffects = DragDropEffects.None;
                    }
                }
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
        }

        private async void BreadScrumbs_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var dragData = new DataObject();
            var dc = (DataContext as MainWindowViewModel)?.CurrentTaskItem;
            if (dc == null)
            {
                return;
            }

            dragData.Set(CustomFormat, dc);

            var result = await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }

        private void Task_OnDoubleTapped(object sender, TappedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                var control = sender as Control;
                if (control?.DataContext is TaskWrapperViewModel wrapper)
                {
                    vm.CurrentTaskItem = wrapper.TaskItem;
                }
            }
        }
        
        private void TaskTree_OnDoubleTapped(object sender, TappedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.DetailsAreOpen = !vm.DetailsAreOpen;
            }
        }

        private static void UpdateGraph(object? eSource)
        {
            var control = eSource as Control;
            var vm = control?.FindParentDataContext<MainWindowViewModel>();
            if (vm?.Graph?.UpdateGraph != null)
            {
                vm.Graph.UpdateGraph = !vm.Graph.UpdateGraph;
            }
        }
    }
}
