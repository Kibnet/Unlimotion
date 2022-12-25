using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AvaloniaGraphControl;
using DynamicData.Binding;
using ReactiveUI;
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

        private GraphViewModel? dc;
        private DisposableList disposableList = new DisposableListRealization();

        private void GraphControl_DataContextChanged(object sender, System.EventArgs e)
        {
            var newdc = DataContext as GraphViewModel;
            if (dc != newdc)
            {
                dc = newdc;
                disposableList.Dispose();
                disposableList.Disposables.Clear();
                
                if (dc != null)
                {
                    dc.WhenAnyValue(
                            m => m.OnlyUnlocked,
                            m => m.HideUnactual,
                            m => m.ShowArchived,
                            m => m.ShowCompleted,
                            m => m.ShowWanted)
                        .Subscribe(t => { UpdateGraph(); })
                        .AddToDispose(disposableList);

                    dc.UnlockedTasks.ObserveCollectionChanges()
                        .Throttle(TimeSpan.FromMilliseconds(100))
                        .Subscribe(p => UpdateGraph())
                        .AddToDispose(disposableList);

                    dc.Tasks.ObserveCollectionChanges()
                        .Throttle(TimeSpan.FromMilliseconds(100))
                        .Subscribe(p => UpdateGraph())
                        .AddToDispose(disposableList);
                }
            }
           
        }


        private void UpdateGraph()
        {
            if (dc == null) return;

            if (dc.OnlyUnlocked)
            {
                BuildFromTasks(dc.UnlockedTasks, dc.HideUnactual);
            }
            else
            {
                BuildFromTasks(dc.Tasks, dc.HideUnactual);
            }
        }

        private void BuildFromTasks(ReadOnlyObservableCollection<TaskWrapperViewModel> tasks, bool hideUnactual)
        {
            var graph = new AvaloniaGraphControl.Graph();
            graph.Orientation = AvaloniaGraphControl.Graph.Orientations.Horizontal;
            var hashSet = new HashSet<TaskItemViewModel>();
            var haveLinks = new HashSet<TaskItemViewModel>();
            var queue = new Queue<TaskItemViewModel>();
            foreach (var task in tasks)
            {
                queue.Enqueue(task.TaskItem);
            }

            while (queue.TryDequeue(out var task))
            {
                if (hideUnactual && task.IsCompleted != false)
                {
                    continue;
                }
                if (!hashSet.Contains(task))
                {
                    foreach (var containsTask in task.ContainsTasks)
                    {
                        if (hideUnactual && containsTask.IsCompleted != false)
                        {
                            continue;
                        }
                        graph.Edges.Add(new ContainEdge(containsTask, task));
                        haveLinks.Add(containsTask);
                        haveLinks.Add(task);
                        if (!hashSet.Contains(containsTask))
                        {
                            queue.Enqueue(containsTask);
                        }
                    }

                    foreach (var blocks in task.BlocksTasks)
                    {
                        if (hideUnactual && blocks.IsCompleted != false)
                        {
                            continue;
                        }
                        graph.Edges.Add(new BlockEdge(task, blocks));
                        haveLinks.Add(blocks);
                        haveLinks.Add(task);
                    }

                    hashSet.Add(task);
                }
            }

            hashSet.ExceptWith(haveLinks);
            foreach (var task in hashSet)
            {
                graph.Edges.Add(new Edge(task, task));
            }

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var control = this.GetControl<GraphPanel>("Graph");
                control.Graph = graph;
            });
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

        private void TaskTree_OnDoubleTapped(object? sender, RoutedEventArgs e)
        {
            var mwm = Locator.Current.GetService<MainWindowViewModel>();
            if (mwm != null)
            {
                mwm.DetailsAreOpen = !mwm.DetailsAreOpen;
            }
        }
    }
}