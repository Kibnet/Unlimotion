using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.Views;

public partial class FeedFilesDrawer : UserControl
{
    private const double PreferredWidth = 420;

    public FeedFilesDrawer()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs eventArgs) =>
        DrawerBorder.Width = Math.Min(PreferredWidth, Math.Max(0, eventArgs.NewSize.Width));

    private async void OpenFile_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is FeedFilesDrawerViewModel viewModel
            && sender is Button { DataContext: FeedFileItemViewModel file })
        {
            await viewModel.OpenFileAsync(file);
        }
    }
}
