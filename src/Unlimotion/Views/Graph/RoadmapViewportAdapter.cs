using Avalonia;
using Avalonia.Controls;

namespace Unlimotion.Views.Graph;

internal interface IRoadmapViewportAdapter
{
    Control Control { get; }

    Point Location { get; set; }

    double Zoom { get; set; }

    void FitToScreen();

    void Reset();

    void ZoomIn();

    void ZoomOut();

    void ZoomAtPosition(double zoom, Point location);

    void PanBy(double deltaX, double deltaY);
}

internal sealed class NodifyRoadmapViewportAdapter : IRoadmapViewportAdapter
{
    private readonly Nodify.NodifyEditor editor;

    public NodifyRoadmapViewportAdapter(Nodify.NodifyEditor editor)
    {
        this.editor = editor;
    }

    public Control Control => editor;

    public Point Location
    {
        get => editor.ViewportLocation;
        set => editor.ViewportLocation = value;
    }

    public double Zoom
    {
        get => editor.ViewportZoom;
        set => editor.ViewportZoom = value;
    }

    public void FitToScreen()
    {
        editor.FitToScreen();
    }

    public void Reset()
    {
        Zoom = 1;
        Location = new Point(0, 0);
    }

    public void ZoomIn()
    {
        editor.ZoomIn();
    }

    public void ZoomOut()
    {
        editor.ZoomOut();
    }

    public void ZoomAtPosition(double zoom, Point location)
    {
        editor.ZoomAtPosition(zoom, location);
    }

    public void PanBy(double deltaX, double deltaY)
    {
        var current = Location;
        Location = new Point(current.X + deltaX, current.Y + deltaY);
    }
}
