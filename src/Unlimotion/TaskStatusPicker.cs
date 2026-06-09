using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Unlimotion.Domain;
using Unlimotion.ViewModel;

namespace Unlimotion;

public class TaskStatusPicker : Button
{
    private readonly TaskStatusIcon _icon = new()
    {
        Width = 16,
        Height = 16,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private TaskItemViewModel? _subscribedTask;
    private bool _ignoreNextClick;

    public static readonly StyledProperty<TaskItemViewModel?> TaskProperty =
        AvaloniaProperty.Register<TaskStatusPicker, TaskItemViewModel?>(nameof(Task));

    public TaskStatusPicker()
    {
        Classes.Add("TaskStatusPicker");
        Content = _icon;
        Width = 28;
        MinWidth = 28;
        MinHeight = 24;
        Padding = new Thickness(3, 0);
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        AddHandler(PointerPressedEvent, OpenOnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
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

    protected override void OnClick()
    {
        if (_ignoreNextClick)
        {
            _ignoreNextClick = false;
            return;
        }

        OpenStatusFlyout();
        base.OnClick();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Handled = true;
    }

    private void OpenOnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _ignoreNextClick = true;
        OpenStatusFlyout();
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

    private TaskItemViewModel? GetEffectiveTask() => Task ?? DataContext as TaskItemViewModel;

    private void SyncTaskSubscription()
    {
        var task = GetEffectiveTask();
        if (ReferenceEquals(task, _subscribedTask))
        {
            UpdateIcon(task);
            return;
        }

        if (_subscribedTask is INotifyPropertyChanged oldTask)
        {
            oldTask.PropertyChanged -= TaskOnPropertyChanged;
        }

        _subscribedTask = task;

        if (_subscribedTask is INotifyPropertyChanged newTask)
        {
            newTask.PropertyChanged += TaskOnPropertyChanged;
        }

        UpdateIcon(task);
    }

    private void TaskOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName == nameof(TaskItemViewModel.Status) ||
            e.PropertyName == nameof(TaskItemViewModel.StatusOption) ||
            e.PropertyName == nameof(TaskItemViewModel.StatusToolTip))
        {
            UpdateIcon(_subscribedTask);
        }
    }

    private void UpdateIcon(TaskItemViewModel? task)
    {
        IsEnabled = task != null;
        _icon.Status = task?.Status ?? TaskStatus.NotReady;
        ToolTip.SetTip(this, task?.StatusToolTip);
    }

    private static MenuFlyout BuildStatusFlyout(TaskItemViewModel task)
    {
        var flyout = new MenuFlyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft
        };

        foreach (var option in task.StatusOptions)
        {
            var menuItem = new MenuItem
            {
                Header = CreateStatusMenuHeader(option),
                IsEnabled = option.IsEnabled
            };
            ToolTip.SetTip(menuItem, option.ToolTip);
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
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TaskStatusIcon
                {
                    Status = option.Status,
                    Width = 16,
                    Height = 16,
                    IsEnabled = option.IsEnabled,
                    VerticalAlignment = VerticalAlignment.Center
                },
                new TextBlock
                {
                    Text = option.Title,
                    IsEnabled = option.IsEnabled,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
    }
}
