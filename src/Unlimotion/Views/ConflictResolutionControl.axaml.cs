using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using DialogHostAvalonia;
using Unlimotion.ViewModel;

namespace Unlimotion.Views;

public partial class ConflictResolutionControl : UserControl
{
    private const double CompactBreakpoint = 720;
    private INotifyPropertyChanged? _dataContextNotifier;

    public ConflictResolutionControl()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout(e.NewSize.Width);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_dataContextNotifier != null)
        {
            _dataContextNotifier.PropertyChanged -= OnDataContextPropertyChanged;
        }

        _dataContextNotifier = DataContext as INotifyPropertyChanged;
        if (_dataContextNotifier != null)
        {
            _dataContextNotifier.PropertyChanged += OnDataContextPropertyChanged;
        }

        UpdateResponsiveLayout(Bounds.Width);
    }

    private void OnDataContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(SettingsViewModel.HasBackupConflictFiles))
        {
            UpdateResponsiveLayout(Bounds.Width);
        }
    }

    private void UpdateResponsiveLayout(double width)
    {
        var hasBackupConflictFiles = (DataContext as SettingsViewModel)?.HasBackupConflictFiles ?? true;
        FileListPane.IsVisible = hasBackupConflictFiles;

        var compact = width < CompactBreakpoint;
        if (!hasBackupConflictFiles)
        {
            ResolverGrid.ColumnDefinitions = new ColumnDefinitions("*");
            ResolverGrid.RowDefinitions = new RowDefinitions("*");
            Grid.SetColumn(FileListPane, 0);
            Grid.SetRow(FileListPane, 0);
            Grid.SetColumn(DetailsPane, 0);
            Grid.SetRow(DetailsPane, 0);
            return;
        }

        if (compact)
        {
            ResolverGrid.ColumnDefinitions = new ColumnDefinitions("*");
            ResolverGrid.RowDefinitions = new RowDefinitions("2*,3*");
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
