﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Input;
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
        public GraphControl()
        {
            DataContextChanged += GraphControl_DataContextChanged;
            InitializeComponent();
            AddHandler(DragDrop.DropEvent, MainControl.Drop);
            AddHandler(DragDrop.DragOverEvent, MainControl.DragOver);
            if (this.ZoomBorder != null)
            {
                this.ZoomBorder.KeyDown += ZoomBorder_KeyDown;
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
                    dc.WhenAnyValue(m => m.UpdateGraph)
                        .Subscribe(t => { UpdateGraph(); })
                        .AddToDispose(disposableList);
                }
            }

        }


        private void UpdateGraph()
        {
            if (dc == null) return;

            if (dc.OnlyUnlocked)
            {
                BuildFromTasks(dc.UnlockedTasks);
            }
            else
            {
                BuildFromTasks(dc.Tasks);
            }
        }

        /// <summary>
        /// Создание и настройка графа, в который будут добавлены задачи и связи между ними
        /// </summary>
        /// <param name="tasks"></param>
        /// <param name="hideUnactual"></param>
        private void BuildFromTasks(ReadOnlyObservableCollection<TaskWrapperViewModel> tasks)
        {
            // Инициализация графа и установка его ориентации
            var graph = new AvaloniaGraphControl.Graph();
            graph.Orientation = AvaloniaGraphControl.Graph.Orientations.Horizontal;

            // Инициализация коллекций для хранения информации о задачах и связях между ними
            var hashSet = new HashSet<TaskItemViewModel>();
            var haveLinks = new HashSet<TaskItemViewModel>();
            var queue = new Queue<TaskWrapperViewModel>();

            // Добавление всех задач из списка tasks в очередь для обработки
            foreach (var task in tasks)
            {
                queue.Enqueue(task);
            }

            // Обработка задач из очереди, пока очередь не станет пустой
            while (queue.TryDequeue(out var task))
            {
                // Получение ID задач, содержащихся в текущей задаче
                var containsTaskIds = task.SubTasks.Select(e => e.TaskItem.Id);

                // Если задача еще не обработана
                if (!hashSet.Contains(task.TaskItem))
                {
                    // Обработка задач, содержащихся в текущей задаче
                    foreach (var containsTask in task.SubTasks)
                    {
                        // Проверка, блокирует ли содержащаяся задача другую задачу или имеет блокировщика
                        var childBlocksAnotherChild = containsTask.TaskItem.Blocks.Any(item => containsTaskIds.Where(id => id != containsTask.TaskItem.Id).Contains(item));
                        var hasChildBlocksBlocker = containsTask.TaskItem.Blocks.Any(item => task.TaskItem.BlockedBy.Contains(item));

                        // Если содержащаяся задача не блокирует другую задачу и не имеет блокировщика, добавляем связь
                        if (!hasChildBlocksBlocker && !childBlocksAnotherChild)
                        {
                            graph.Edges.Add(new ContainEdge(containsTask.TaskItem, task.TaskItem));
                        }

                        // Добавляем содержащуюся задачу и текущую задачу в список задач, имеющих связи
                        haveLinks.Add(containsTask.TaskItem);
                        haveLinks.Add(task.TaskItem);

                        // Если содержащаяся задача еще не обработана, добавляем ее в очередь для обработки
                        if (!hashSet.Contains(containsTask.TaskItem))
                        {
                            queue.Enqueue(containsTask);
                        }
                    }

                    // Обработка задач, блокирующих текущую задачу
                    foreach (var blocks in task.TaskItem.BlocksTasks)
                    {
                        // Добавление связи блокировки между текущей задачей и блокирующей задачей
                        graph.Edges.Add(new BlockEdge(task.TaskItem, blocks));

                        // Добавление блокирующей задачи и текущей задачи в список задач, имеющих связи
                        haveLinks.Add(blocks);
                        haveLinks.Add(task.TaskItem);
                    }

                    // Добавление текущей задачи в список обработанных задач
                    hashSet.Add(task.TaskItem);
                }
            }

            // Удаление задач, имеющих связи, из списка обработанных задач
            hashSet.ExceptWith(haveLinks);

            // Добавление связей для задач без связей (самих с собой)
            foreach (var task in hashSet)
            {
                graph.Edges.Add(new Edge(task, task));
            }

            // Обновление графического представления графа в пользовательском интерфейсе
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Graph.Graph = graph;
            });

        }

        private void ZoomBorder_KeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.F:
                    this.ZoomBorder?.Fill();
                    break;
                case Key.U:
                    this.ZoomBorder?.Uniform();
                    break;
                case Key.R:
                    this.ZoomBorder?.ResetMatrix();
                    break;
                case Key.T:
                    this.ZoomBorder?.ToggleStretchMode();
                    this.ZoomBorder?.AutoFit();
                    break;
            }
        }

        private async void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var pointer = e.GetCurrentPoint(this);
            if (pointer.Properties.IsLeftButtonPressed)
            {
                var dragData = new DataObject();
                var control = sender as Control;
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


        public const string CustomFormat = "application/xxx-unlimotion-task-item";

        private void TaskTree_OnDoubleTapped(object sender, TappedEventArgs e)
        {
            var mwm = Locator.Current.GetService<MainWindowViewModel>();
            if (mwm != null)
            {
                mwm.DetailsAreOpen = !mwm.DetailsAreOpen;
            }
        }
    }
}