using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Splat;
using Unlimotion.ViewModel;
using Unlimotion.Views.Graph;

namespace Unlimotion.Views
{
    public partial class GraphControl : UserControl
    {
        private readonly ZoomBorder? _zoomBorder;
        public GraphControl()
        {
            DataContextChanged += GraphControl_DataContextChanged;
            InitializeComponent();
            _zoomBorder = this.Find<ZoomBorder>("ZoomBorder");
            if (_zoomBorder != null)
            {
                _zoomBorder.KeyDown += ZoomBorder_KeyDown;
            }
        }

        private void GraphControl_DataContextChanged(object sender, System.EventArgs e)
        {
            var dc = DataContext as GraphViewModel;
            if (dc != null)
            {
                
                var graph = new AvaloniaGraphControl.Graph();
                dc.MyGraph = graph;
                
                var hashSet = new HashSet<TaskItemViewModel>();
                var queue = new Queue<TaskItemViewModel>();
                foreach (var task in dc.Tasks)
                {
                    queue.Enqueue(task.TaskItem);
                }

                while (queue.TryDequeue(out var task))
                {
                    if (!hashSet.Contains(task))
                    {
                        foreach (var containsTask in task.ContainsTasks)
                        {
                            graph.Edges.Add(new ContainEdge(containsTask, task));
                            if (!hashSet.Contains(containsTask))
                            {
                                queue.Enqueue(containsTask);
                            }
                        }
                        foreach (var blocks in task.BlocksTasks)
                        {
                            graph.Edges.Add(new BlockEdge(task, blocks));
                        }

                        hashSet.Add(task);
                    }
                }

                //graph.Parent[b1] = b;
                //graph.Parent[b2] = b;
                //graph.Parent[b3] = b;
                //graph.Parent[b4] = b;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            AddHandler(DragDrop.DropEvent, MainControl.Drop);
            AddHandler(DragDrop.DragOverEvent, MainControl.DragOver);
        }

        private void ZoomBorder_KeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.F:
                    _zoomBorder?.Fill();
                    break;
                case Key.U:
                    _zoomBorder?.Uniform();
                    break;
                case Key.R:
                    _zoomBorder?.ResetMatrix();
                    break;
                case Key.T:
                    _zoomBorder?.ToggleStretchMode();
                    _zoomBorder?.AutoFit();
                    break;
            }
        }
        
        private async void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var pointer = e.GetCurrentPoint(this);
            if (pointer.Properties.IsLeftButtonPressed)
            {
                var dragData = new DataObject();
                var control = sender as IControl;
                var dc = control?.DataContext;
                if (dc == null)
                {
                    return;
                }

                var mwm = Locator.Current.GetService<MainWindowViewModel>();
                mwm.CurrentTaskItem = dc as TaskItemViewModel;

                dragData.Set(CustomFormat, dc);

                var result = await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            }
        }


        private const string CustomFormat = "application/xxx-unlimotion-task-item";
    }
}