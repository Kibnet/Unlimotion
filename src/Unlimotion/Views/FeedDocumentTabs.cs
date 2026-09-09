using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Unlimotion.ViewModel.Feed;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Views;

/// <summary>Portable tab selector; document session ownership remains in the workspace.</summary>
public sealed class FeedDocumentTabs : UserControl
{
    private FeedViewModel? owner;
    private readonly HashSet<INotifyPropertyChanged> observedDocuments = [];
    private readonly StackPanel tabs = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly Button overflow = new() { Content = "⌄", Padding = new Thickness(8, 5) };
    public FeedDocumentTabs()
    {
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        root.Children.Add(new ScrollViewer { Content = tabs, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Grid.SetColumn(overflow, 1);
        AutomationProperties.SetAutomationId(overflow, "FeedDocumentTabList");
        root.Children.Add(overflow);
        overflow.Click += (_, _) => overflow.ContextMenu?.Open(overflow);
        Content = root;
        DataContextChanged += (_, _) => Observe();
        DetachedFromVisualTree += (_, _) => Unsubscribe();
        AttachedToVisualTree += (_, _) => Observe();
    }

    private void Observe()
    {
        Unsubscribe();
        owner = DataContext as FeedViewModel;
        if (owner is not null)
        {
            owner.DocumentWorkspace.Documents.CollectionChanged += Changed;
            owner.PropertyChanged += OnOwnerPropertyChanged;
        }
        Rebuild();
    }
    private void Unsubscribe()
    {
        foreach (var document in observedDocuments) document.PropertyChanged -= OnDocumentPropertyChanged;
        observedDocuments.Clear();
        if (owner is null) return;
        owner.DocumentWorkspace.Documents.CollectionChanged -= Changed;
        owner.PropertyChanged -= OnOwnerPropertyChanged;
        owner = null;
    }
    private void Changed(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();
    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FeedThematicDocumentViewModel.RelativePath)
            or nameof(FeedThematicDocumentViewModel.FullPath)) Rebuild();
    }
    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FeedViewModel.OpenedThematicFile)) Rebuild();
    }
    private void Rebuild()
    {
        tabs.Children.Clear();
        if (owner is null) return;
        var feed = owner;
        var current = feed.DocumentWorkspace.Documents.OfType<INotifyPropertyChanged>().ToHashSet();
        foreach (var removed in observedDocuments.Except(current).ToArray())
        {
            removed.PropertyChanged -= OnDocumentPropertyChanged;
            observedDocuments.Remove(removed);
        }
        foreach (var added in current.Except(observedDocuments).ToArray())
        {
            added.PropertyChanged += OnDocumentPropertyChanged;
            observedDocuments.Add(added);
        }
        var menu = new ContextMenu();
        var feedItem = new MenuItem { Header = L10n.Get("FeedTitle") };
        feedItem.Click += async (_, _) => await feed.ActivateDocumentAsync(null);
        menu.Items.Add(feedItem);
        var chronology = new Button { Content = L10n.Get("FeedTitle"), Padding = new Thickness(10, 5),
            FontWeight = feed.HasOpenedThematicFile ? Avalonia.Media.FontWeight.Normal : Avalonia.Media.FontWeight.SemiBold };
        AutomationProperties.SetAutomationId(chronology, "FeedChronologyTab");
        chronology.Click += async (_, _) => await feed.ActivateDocumentAsync(null);
        tabs.Children.Add(chronology);
        foreach (var document in feed.DocumentWorkspace.Documents)
        {
            var menuItem = new MenuItem { Header = document.RelativePath };
            menuItem.Click += async (_, _) => await feed.ActivateDocumentAsync(document);
            menu.Items.Add(menuItem);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var label = feed.DocumentWorkspace.Documents.Count(other => other.DisplayName == document.DisplayName) > 1
                ? document.RelativePath : document.DisplayName;
            var activate = new Button { Content = new TextBlock { Text = label, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis }, MaxWidth = 240, Padding = new Thickness(10, 5),
                FontWeight = ReferenceEquals(feed.OpenedThematicFile, document) ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal };
            ToolTip.SetTip(activate, document.FullPath);
            AutomationProperties.SetAutomationId(activate, "FeedDocumentTab-" + document.RelativePath);
            activate.Click += async (_, _) => await feed.ActivateDocumentAsync(document);
            var close = new Button { Content = "×", Padding = new Thickness(6, 5) };
            ToolTip.SetTip(close, L10n.Get("Close"));
            close.Click += async (_, _) => await feed.CloseDocumentAsync(document);
            row.Children.Add(activate);
            row.Children.Add(close);
            tabs.Children.Add(row);
        }
        overflow.ContextMenu = menu;
        IsVisible = feed.DocumentWorkspace.Documents.Count > 0;
    }
}
