using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using Avalonia.Controls;
using Avalonia.Input;
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
                        Expand(toExpand);
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
                var task = control?.FindParentDataContext<TaskWrapperViewModel>();
                var subItem = e.Data.Get(CustomFormat) as TaskWrapperViewModel;
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
                var task = control?.FindParentDataContext<TaskWrapperViewModel>();
                var subItem = e.Data.Get(CustomFormat) as TaskWrapperViewModel;
                if (subItem == null)
                {
                    e.DragEffects = DragDropEffects.None;
                    return;
                }
                if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
                {
                    e.DragEffects &= DragDropEffects.Copy;
                    subItem?.TaskItem.CloneInto(task.TaskItem);
                }
                else if (e.KeyModifiers == KeyModifiers.Shift)
                {
                    if (subItem.CanMoveInto(task))
                    {
                        e.DragEffects &= DragDropEffects.Move;
                        subItem?.TaskItem.MoveInto(task.TaskItem, subItem.Parent?.TaskItem);
                    }
                    else
                    {
                        e.DragEffects = DragDropEffects.None;
                    }

                }
                else if (e.KeyModifiers == KeyModifiers.Control)
                {
                    e.DragEffects &= DragDropEffects.Link;
                    task?.TaskItem.BlockBy(subItem.TaskItem);
                }
                else if (e.KeyModifiers == KeyModifiers.Alt)
                {
                    e.DragEffects &= DragDropEffects.Link;
                    subItem?.TaskItem.BlockBy(task.TaskItem);
                }
                else
                {
                    if (subItem.CanMoveInto(task))
                    {
                        e.DragEffects &= DragDropEffects.Copy;
                        subItem?.TaskItem.CopyInto(task.TaskItem);
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
            var treeView = this.Get<TreeView>("CurrentTree");
            
            var treeItem = treeView.ItemContainerGenerator.Containers.FirstOrDefault(info => info.Item == task.Parent);
            if (treeItem != null)
            {
                treeView.ExpandSubTree(treeItem.ContainerControl as TreeViewItem);
            }
        }
    }
}
