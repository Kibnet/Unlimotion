using System;
using Avalonia;
using Avalonia.Controls;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.Views;

/// <summary>Platform boundary: mobile never references the proprietary desktop host.</summary>
public sealed class FeedDocumentHost : UserControl
{
    public static Func<FeedViewModel, Control, Control>? DesktopHostFactory { get; set; }
    private Control? viewport;
    private FeedViewModel? owner;
    private Control? host;
    private bool showDocumentTabs = true;

    public bool ShowDocumentTabs
    {
        get => showDocumentTabs;
        set
        {
            if (showDocumentTabs == value) return;
            showDocumentTabs = value;
            owner = null;
            BuildHost();
        }
    }

    public Control? Viewport
    {
        get => viewport;
        set { viewport = value; BuildHost(); }
    }

    public FeedDocumentHost()
    {
        DataContextChanged += (_, _) => BuildHost();
        AttachedToVisualTree += (_, _) => BuildHost();
    }

    private void BuildHost()
    {
        if (viewport is null || DataContext is not FeedViewModel feed || ReferenceEquals(owner, feed)) return;
        Content = null;
        if (host is IDisposable disposable) disposable.Dispose();
        if (viewport.Parent is Panel oldPanel) oldPanel.Children.Remove(viewport);
        owner = feed;
        if (!ShowDocumentTabs)
        {
            host = viewport;
            Content = host;
            return;
        }
        if (DesktopHostFactory is { } factory) host = factory(feed, viewport);
        else
        {
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            grid.Children.Add(new FeedDocumentTabs { DataContext = feed });
            Grid.SetRow(viewport, 1);
            grid.Children.Add(viewport);
            host = grid;
        }
        Content = host;
    }
}
