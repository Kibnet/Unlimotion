using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Unlimotion.Domain;
using Unlimotion.ViewModel;

namespace Unlimotion;

public class TaskStatusPicker : Button
{
    private readonly TaskStatusIcon _icon = new()
    {
        Width = 20,
        Height = 20,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false
    };

    private TaskItemViewModel? _subscribedTask;
    private bool _openOnPointerRelease;

    public static readonly StyledProperty<TaskItemViewModel?> TaskProperty =
        AvaloniaProperty.Register<TaskStatusPicker, TaskItemViewModel?>(nameof(Task));

    public TaskStatusPicker()
    {
        Classes.Add("TaskStatusPicker");
        Content = _icon;
        Width = 20;
        Height = 20;
        MinWidth = 20;
        MinHeight = 20;
        Margin = new Thickness(0, 0, 8, 0);
        Padding = new Thickness(0);
        Background = Avalonia.Media.Brushes.Transparent;
        BorderBrush = Avalonia.Media.Brushes.Transparent;
        BorderThickness = new Thickness(0);
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
    }

    public TaskItemViewModel? Task
    {
        get => GetValue(TaskProperty);
        set => SetValue(TaskProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TaskProperty || change.Property == DataContextProperty)
        {
            SyncTaskSubscription();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ClearTaskSubscription();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnClick()
    {
        if (!IsStatusFlyoutOpen())
        {
            OpenStatusFlyout();
        }

        base.OnClick();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _openOnPointerRelease = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        base.OnPointerPressed(e);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var shouldOpen = _openOnPointerRelease;
        _openOnPointerRelease = false;
        base.OnPointerReleased(e);
        if (shouldOpen && !IsStatusFlyoutOpen())
        {
            OpenStatusFlyout();
        }

        e.Handled = true;
    }

    private void OpenStatusFlyout()
    {
        var task = GetEffectiveTask();
        if (task == null)
        {
            return;
        }

        var flyout = BuildStatusFlyout(task);
        Flyout = flyout;
        flyout.ShowAt(this);
    }

    private bool IsStatusFlyoutOpen() => Flyout is MenuFlyout { IsOpen: true };

    private TaskItemViewModel? GetEffectiveTask() => Task ?? DataContext as TaskItemViewModel;

    private void SyncTaskSubscription()
    {
        var task = GetEffectiveTask();
        if (ReferenceEquals(task, _subscribedTask))
        {
            UpdateIcon(task);
            return;
        }

        ClearTaskSubscription();

        _subscribedTask = task;

        if (_subscribedTask is INotifyPropertyChanged newTask)
        {
            newTask.PropertyChanged += TaskOnPropertyChanged;
        }

        UpdateIcon(task);
    }

    private void ClearTaskSubscription()
    {
        if (_subscribedTask is INotifyPropertyChanged oldTask)
        {
            oldTask.PropertyChanged -= TaskOnPropertyChanged;
        }

        _subscribedTask = null;
    }

    private void TaskOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName == nameof(TaskItemViewModel.Status) ||
            e.PropertyName == nameof(TaskItemViewModel.IsCanBeCompleted) ||
            e.PropertyName == nameof(TaskItemViewModel.AvailabilityOpacity) ||
            e.PropertyName == nameof(TaskItemViewModel.StatusOption) ||
            e.PropertyName == nameof(TaskItemViewModel.StatusToolTip))
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                UpdateIcon(_subscribedTask);
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (ReferenceEquals(sender, _subscribedTask))
                    {
                        UpdateIcon(_subscribedTask);
                    }
                });
            }
        }
    }

    private void UpdateIcon(TaskItemViewModel? task)
    {
        IsEnabled = task != null;
        Opacity = task?.AvailabilityOpacity ?? 1d;
        _icon.Status = task?.Status ?? TaskStatus.NotReady;
        ToolTip.SetTip(this, task?.StatusToolTip);
    }

    private static MenuFlyout BuildStatusFlyout(TaskItemViewModel task)
    {
        var flyout = new MenuFlyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft
        };

        foreach (var option in task.AvailableStatusTransitionOptions)
        {
            var menuItem = new MenuItem
            {
                Header = CreateStatusMenuHeader(option),
                DataContext = option
            };
            menuItem.Bind(InputElement.IsEnabledProperty, new Binding(nameof(TaskStatusOption.IsEnabled))
            {
                Source = option
            });
            menuItem.Bind(ToolTip.TipProperty, new Binding(nameof(TaskStatusOption.ToolTip))
            {
                Source = option
            });
            AutomationProperties.SetAutomationId(menuItem, $"TaskStatusOption{option.Status}");
            menuItem.Click += (_, _) =>
            {
                if (option.IsEnabled)
                {
                    task.StatusOption = option;
                }
            };

            flyout.Items.Add(menuItem);
        }

        return flyout;
    }

    private static Control CreateStatusMenuHeader(TaskStatusOption option)
    {
        var icon = new TaskStatusIcon
        {
            Status = option.Status,
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.Bind(InputElement.IsEnabledProperty, new Binding(nameof(TaskStatusOption.IsEnabled))
        {
            Source = option
        });

        var text = new TextBlock
        {
            Text = option.Title,
            VerticalAlignment = VerticalAlignment.Center
        };
        text.Bind(InputElement.IsEnabledProperty, new Binding(nameof(TaskStatusOption.IsEnabled))
        {
            Source = option
        });

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                icon,
                text
            }
        };
    }
}
