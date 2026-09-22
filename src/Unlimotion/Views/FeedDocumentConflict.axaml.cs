using System;
using Avalonia;
using Avalonia.Controls;

namespace Unlimotion.Views;

public partial class FeedDocumentConflict : UserControl
{
    private const double CompactBreakpoint = 700;
    private bool? isCompactLayout;

    public FeedDocumentConflict()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs eventArgs)
    {
        var useCompactLayout = eventArgs.NewSize.Width < CompactBreakpoint;
        if (isCompactLayout == useCompactLayout)
        {
            return;
        }

        isCompactLayout = useCompactLayout;
        if (useCompactLayout)
        {
            VersionsGrid.ColumnDefinitions = new ColumnDefinitions("*");
            VersionsGrid.RowDefinitions = new RowDefinitions("*,*");
            Grid.SetColumn(EditorVersionPane, 0);
            Grid.SetRow(EditorVersionPane, 0);
            Grid.SetColumn(DiskVersionPane, 0);
            Grid.SetRow(DiskVersionPane, 1);
            return;
        }

        VersionsGrid.ColumnDefinitions = new ColumnDefinitions("*,*");
        VersionsGrid.RowDefinitions = new RowDefinitions("*");
        Grid.SetColumn(EditorVersionPane, 0);
        Grid.SetRow(EditorVersionPane, 0);
        Grid.SetColumn(DiskVersionPane, 1);
        Grid.SetRow(DiskVersionPane, 0);
    }
}
