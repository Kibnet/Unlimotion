using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Unlimotion.ViewModel;

namespace Unlimotion.Views.Graph;

public sealed class RoadmapNode : INotifyPropertyChanged
{
    public const double MinWidth = 104;
    public const double MaxWidth = 340;
    public const double Height = 44;

    private Point location;
    private double width = MinWidth;
    private double connectionWidth = MaxWidth;
    private bool isSelected;
    private bool isCurrent;

    public RoadmapNode(TaskItemViewModel taskItem)
    {
        TaskItem = taskItem;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TaskItemViewModel TaskItem { get; }

    public string Id => TaskItem.Id;

    public double Width
    {
        get => width;
        private set
        {
            if (Math.Abs(width - value) < 0.5)
            {
                return;
            }

            width = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RightAnchor));
        }
    }

    public double ConnectionWidth
    {
        get => connectionWidth;
        private set
        {
            if (Math.Abs(connectionWidth - value) < 0.5)
            {
                return;
            }

            connectionWidth = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConnectionRightAnchor));
        }
    }

    public Point Location
    {
        get => location;
        set
        {
            if (location == value)
            {
                return;
            }

            location = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LeftAnchor));
            OnPropertyChanged(nameof(RightAnchor));
            OnPropertyChanged(nameof(ConnectionRightAnchor));
        }
    }

    public Point LeftAnchor => new(Location.X, Location.Y + Height / 2);

    public Point RightAnchor => new(Location.X + Width, Location.Y + Height / 2);

    public Point ConnectionRightAnchor => new(Location.X + ConnectionWidth, Location.Y + Height / 2);

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsCurrent
    {
        get => isCurrent;
        set
        {
            if (isCurrent == value)
            {
                return;
            }

            isCurrent = value;
            OnPropertyChanged();
        }
    }

    public bool SetMeasuredWidth(double measuredWidth)
    {
        if (double.IsNaN(measuredWidth) || double.IsInfinity(measuredWidth) || measuredWidth <= 0)
        {
            return false;
        }

        var previousWidth = Width;
        Width = Math.Clamp(
            Math.Ceiling(measuredWidth),
            MinWidth,
            MaxWidth);
        return Math.Abs(previousWidth - Width) >= 0.5;
    }

    public bool SetConnectionWidth(double width)
    {
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
        {
            return false;
        }

        var previousWidth = ConnectionWidth;
        ConnectionWidth = Math.Clamp(
            Math.Ceiling(Math.Max(width, Width)),
            MinWidth,
            MaxWidth);
        return Math.Abs(previousWidth - ConnectionWidth) >= 0.5;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
