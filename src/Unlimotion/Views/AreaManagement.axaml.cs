using System;
using Avalonia;
using Avalonia.Controls;

namespace Unlimotion.Views;

public partial class AreaManagement : UserControl
{
    private const double CompactBreakpoint = 680;

    public AreaManagement()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs eventArgs)
    {
        if (eventArgs.NewSize.Width < CompactBreakpoint)
        {
            ContentGrid.ColumnDefinitions = new ColumnDefinitions("*");
            ContentGrid.RowDefinitions = new RowDefinitions("2*,3*");
            Grid.SetColumn(AreaListPane, 0);
            Grid.SetRow(AreaListPane, 0);
            Grid.SetColumn(AreaEditorPane, 0);
            Grid.SetRow(AreaEditorPane, 1);
            return;
        }

        ContentGrid.ColumnDefinitions = new ColumnDefinitions("260,*");
        ContentGrid.RowDefinitions = new RowDefinitions("*");
        Grid.SetColumn(AreaListPane, 0);
        Grid.SetRow(AreaListPane, 0);
        Grid.SetColumn(AreaEditorPane, 1);
        Grid.SetRow(AreaEditorPane, 0);
    }
}
