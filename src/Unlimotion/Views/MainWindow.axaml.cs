using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
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

        void DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.Contains(CustomFormat))
            {
                if (e.KeyModifiers == KeyModifiers.Shift)
                {
                    e.DragEffects = e.DragEffects & DragDropEffects.Move;
                }
                else if (e.KeyModifiers == KeyModifiers.Control)
                {
                    e.DragEffects = e.DragEffects & DragDropEffects.Link;
                }
                else if (e.KeyModifiers == KeyModifiers.Alt)
                {
                    e.DragEffects = e.DragEffects & DragDropEffects.Link;
                }
                else
                {
                    var dc = (e.Source as IControl)?.Parent?.DataContext;
                    var task = dc as TaskItemViewModel;
                    var data = e.Data.Get(CustomFormat);
                    if (data is TaskItemViewModel subItem)
                    {
                        if (task == subItem || task.GetAllParents().Any(m => m.Id == subItem.Id))
                        {
                            e.DragEffects = DragDropEffects.None;
                            return;
                        }
                        e.DragEffects = e.DragEffects & DragDropEffects.Copy;
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
                    e.DragEffects = e.DragEffects & DragDropEffects.Move;

                }
                else if (e.KeyModifiers == KeyModifiers.Control)
                {
                    e.DragEffects = e.DragEffects & DragDropEffects.Link;
                }
                else if (e.KeyModifiers == KeyModifiers.Alt)
                {
                    e.DragEffects = e.DragEffects & DragDropEffects.Link;
                }
                else
                {
                    var dc = (e.Source as IControl)?.Parent?.DataContext;
                    var task = dc as TaskItemViewModel;
                    var data = e.Data.Get(CustomFormat);
                    if (data is TaskItemViewModel subItem)
                    {
                        e.DragEffects = e.DragEffects & DragDropEffects.Copy;
                        subItem?.CopyInto(task);
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
    }
}
