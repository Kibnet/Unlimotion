using Avalonia;
using Avalonia.Controls;
using DialogHostAvalonia;

namespace Unlimotion.Views;

public partial class ConflictResolutionControl : UserControl
{
    private const double CompactBreakpoint = 720;

    public ConflictResolutionControl()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < CompactBreakpoint;
        if (compact)
        {
            ResolverGrid.ColumnDefinitions = new ColumnDefinitions("*");
            ResolverGrid.RowDefinitions = new RowDefinitions("Auto,*");
            Grid.SetColumn(FileListPane, 0);
            Grid.SetRow(FileListPane, 0);
            Grid.SetColumn(DetailsPane, 0);
            Grid.SetRow(DetailsPane, 1);
            return;
        }

        ResolverGrid.ColumnDefinitions = new ColumnDefinitions("280,*");
        ResolverGrid.RowDefinitions = new RowDefinitions("*");
        Grid.SetColumn(FileListPane, 0);
        Grid.SetRow(FileListPane, 0);
        Grid.SetColumn(DetailsPane, 1);
        Grid.SetRow(DetailsPane, 0);
    }

    private void CloseButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DialogHost.GetDialogSession("Ask")?.Close(false);
    }
}
