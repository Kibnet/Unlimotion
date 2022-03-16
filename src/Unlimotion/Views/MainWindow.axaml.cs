using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Unlimotion.ViewModel;

namespace Unlimotion.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

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
                if (e.KeyModifiers == KeyModifiers.Shift)
                {
                    var dc = (e.Source as IControl)?.Parent?.DataContext;
                    var task = dc as TaskWrapperViewModel;
                    if (e.Data.Get(CustomFormat) is TaskWrapperViewModel subItem)
                    {
                        if (task == subItem || task.TaskItem.GetAllParents().Any(m => m.Id == subItem.TaskItem.Id))
                        {
                            e.DragEffects = DragDropEffects.None;
                            return;
                        }
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
                    var dc = (e.Source as IControl)?.Parent?.DataContext;
                    var task = dc as TaskWrapperViewModel;
                    if (e.Data.Get(CustomFormat) is TaskWrapperViewModel subItem)
                    {
                        if (task == subItem || task.TaskItem.GetAllParents().Any(m => m.Id == subItem.TaskItem.Id))
                        {
                            e.DragEffects = DragDropEffects.None;
                            return;
                        }
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
                if (e.KeyModifiers == KeyModifiers.Shift)
                {
                    var control = (e.Source as IControl)?.Parent;
                    var task = control?.DataContext as TaskWrapperViewModel;
                    if (e.Data.Get(CustomFormat) is TaskWrapperViewModel subItem)
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
                }
                else if (e.KeyModifiers == KeyModifiers.Alt)
                {
                    e.DragEffects &= DragDropEffects.Link;
                }
                else
                {
                    var dc = (e.Source as IControl)?.Parent?.DataContext;
                    var task = dc as TaskWrapperViewModel;
                    if (e.Data.Get(CustomFormat) is TaskWrapperViewModel subItem)
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
    }
}
