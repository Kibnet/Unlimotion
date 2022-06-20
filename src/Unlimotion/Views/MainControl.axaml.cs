using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ReactiveUI;
using Unlimotion.ViewModel;

namespace Unlimotion.Views
{
    public partial class MainControl : UserControl
    {
        public MainControl()
        {
            InitializeComponent(); 
            DataContextChanged += MainWindow_DataContextChanged;
        }

        private void MainWindow_DataContextChanged(object? sender, EventArgs e)
        {
            var vm = DataContext as MainWindowViewModel;
            if (vm != null)
            {
                var innerCommand = vm.CreateInner as ReactiveCommand<Unit, Unit>;
                if (innerCommand != null)
                {
                    var subscription = innerCommand.Subscribe(unit =>
                    {
                        var toExpand = vm.CurrentItem;
                        if (toExpand!= null)
                        {
                            Expand(toExpand);
                        }
                    });
                    disposables.Add(subscription);
                }
            }
        }

        private List<IDisposable> disposables = new();

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this); 

            AddHandler(DragDrop.DropEvent, Drop);
            AddHandler(DragDrop.DragOverEvent, DragOver);
        }

        private const string CustomFormat = "application/xxx-unlimotion-task";
        
        private async void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var dragData = new DataObject();
            var control = sender as IControl;
            var dc = control?.DataContext;
            if (dc == null)
            {
                return;
            }

            dragData.Set(CustomFormat, dc);

            var result = await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }

        void DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.Contains(CustomFormat))
            {
                var control = e.Source as IControl;
                var task = control?.FindParentDataContext<TaskWrapperViewModel>()?.TaskItem;
                if (task == null)
                {
                    task = control?.FindParentDataContext<TaskItemViewModel>();
                }
                var subItem = e.Data.Get(CustomFormat) switch
                {
                    TaskWrapperViewModel taskWrapperViewModel => taskWrapperViewModel?.TaskItem,
                    TaskItemViewModel taskItemViewModel => taskItemViewModel,
                    _ => null
                };
                if (subItem == null)
                {
                    e.DragEffects = DragDropEffects.None;
                    return;
                }

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

        void Drop(object sender, DragEventArgs e)
        {
            if (e.Data.Contains(CustomFormat))
            {
                var control = e.Source as IControl;
                var task = control?.FindParentDataContext<TaskWrapperViewModel>()?.TaskItem;
                var sub = e.Data.Get(CustomFormat);
                var subItem = sub switch
                {
                    TaskWrapperViewModel taskWrapperViewModel => taskWrapperViewModel?.TaskItem,
                    TaskItemViewModel taskItemViewModel => taskItemViewModel,
                    _ => null
                };
                if (subItem == null)
                {
                    e.DragEffects = DragDropEffects.None;
                    return;
                }
                if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
                {
                    e.DragEffects &= DragDropEffects.Copy;
                    subItem.CloneInto(task);
                }
                else if (e.KeyModifiers == KeyModifiers.Shift)
                {
                    if (subItem.CanMoveInto(task))
                    {
                        TaskItemViewModel parent = null;
                        var breakFlag = false;
                        if (subItem.Parents.Count<=1)
                        {
                            parent = subItem.ParentsTasks.FirstOrDefault();
                        }
                        else if (sub is TaskWrapperViewModel parentWrapper)
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
                            subItem.MoveInto(task, parent);
                        }
                    }
                    else
                    {
                        e.DragEffects = DragDropEffects.None;
                    }

                }
                else if (e.KeyModifiers == KeyModifiers.Control)
                {
                    e.DragEffects &= DragDropEffects.Link;
                    task.BlockBy(subItem);
                }
                else if (e.KeyModifiers == KeyModifiers.Alt)
                {
                    e.DragEffects &= DragDropEffects.Link;
                    subItem.BlockBy(task);
                }
                else
                {
                    if (subItem.CanMoveInto(task))
                    {
                        e.DragEffects &= DragDropEffects.Copy;
                        subItem.CopyInto(task);
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

        public void Expand(TaskWrapperViewModel task)
        {
            if (task == null) return;
            var treeView = this.Get<TreeView>("CurrentTree");
            var treeItem = treeView.ItemContainerGenerator.Containers.FirstOrDefault(info => info.Item == task.Parent);
            if (treeItem == null) return;
            if (treeItem.ContainerControl is TreeViewItem item)
            {
                treeView.ExpandSubTree(item);
            }
        }

        private async void BreadScrumbs_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var dragData = new DataObject();
            var dc = (this.DataContext as MainWindowViewModel)?.CurrentTaskItem;
            if (dc == null)
            {
                return;
            }

            dragData.Set(CustomFormat, dc);

            var result = await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }

        private void Task_OnDoubleTapped(object? sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainWindowViewModel;
            if (vm != null)
            {
                var control = sender as IControl;
                var wrapper = control?.DataContext as TaskWrapperViewModel;
                if (wrapper!=null)
                {
                    vm.CurrentTaskItem = wrapper.TaskItem;
                }
            }
        }
    }
}
