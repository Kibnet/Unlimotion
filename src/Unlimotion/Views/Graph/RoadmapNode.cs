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
        }
    }

    public Point LeftAnchor => new(Location.X, Location.Y + Height / 2);

    public Point RightAnchor => new(Location.X + Width, Location.Y + Height / 2);

    public void SetMeasuredWidth(double measuredWidth)
    {
        if (double.IsNaN(measuredWidth) || double.IsInfinity(measuredWidth) || measuredWidth <= 0)
        {
            return;
        }

        Width = Math.Clamp(
            Math.Ceiling(measuredWidth),
            MinWidth,
            MaxWidth);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
