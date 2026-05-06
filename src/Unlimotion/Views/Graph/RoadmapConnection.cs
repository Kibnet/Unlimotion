using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;

namespace Unlimotion.Views.Graph;

public sealed class RoadmapConnection : INotifyPropertyChanged, System.IDisposable
{
    public const double BendSpacing = 24;

    public RoadmapConnection(RoadmapNode tail, RoadmapNode head, RoadmapConnectionKind kind)
    {
        Tail = tail;
        Head = head;
        Kind = kind;

        Tail.PropertyChanged += NodeOnPropertyChanged;
        Head.PropertyChanged += NodeOnPropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RoadmapNode Tail { get; }

    public RoadmapNode Head { get; }

    public RoadmapConnectionKind Kind { get; }

    public string Key => CreateKey(Tail.Id, Head.Id, Kind);

    public Point Source => Tail.RightAnchor;

    public Point RoutedSource => Tail.ConnectionRightAnchor;

    public Point Target => Head.LeftAnchor;

    public bool HasSourceExtension => RoutedSource.X > Source.X + 0.5;

    public bool IsLeftToRight => RoutedSource.X < Target.X;

    public IBrush Stroke => Kind == RoadmapConnectionKind.Blocks ? Brushes.Red : Brushes.Green;

    public static string CreateKey(string tailId, string headId, RoadmapConnectionKind kind)
    {
        return $"{kind}:{tailId}->{headId}";
    }

    public void Dispose()
    {
        Tail.PropertyChanged -= NodeOnPropertyChanged;
        Head.PropertyChanged -= NodeOnPropertyChanged;
    }

    private void NodeOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(RoadmapNode.Location)
            or nameof(RoadmapNode.Width)
            or nameof(RoadmapNode.ConnectionWidth)
            or nameof(RoadmapNode.LeftAnchor)
            or nameof(RoadmapNode.RightAnchor)
            or nameof(RoadmapNode.ConnectionRightAnchor)))
        {
            return;
        }

        OnPropertyChanged(nameof(Source));
        OnPropertyChanged(nameof(RoutedSource));
        OnPropertyChanged(nameof(Target));
        OnPropertyChanged(nameof(HasSourceExtension));
        OnPropertyChanged(nameof(IsLeftToRight));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
