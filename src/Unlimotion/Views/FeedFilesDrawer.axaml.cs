using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Unlimotion.ViewModel.Workspace;
using L10n = Unlimotion.ViewModel.Localization.Localization;
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

    private void OnFileContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        OpenFileMenu(sender);
        e.Handled = true;
    }

    private void OnFileActionsClick(object? sender, RoutedEventArgs e) => OpenFileMenu(sender);

    private void OpenFileMenu(object? sender)
    {
        if (sender is not Button { DataContext: FeedFileItemViewModel file } button
            || DataContext is not FeedFilesDrawerViewModel viewModel) return;
        var menu = new ContextMenu();
        foreach (var (key, disposition) in new[]
        {
            ("WorkspaceOpenHere", WorkspaceOpenDisposition.CurrentTab),
            ("WorkspaceOpenInNewTab", WorkspaceOpenDisposition.NewTab),
            ("WorkspaceOpenBeside", WorkspaceOpenDisposition.AdjacentPane)
        })
        {
            var item = new MenuItem { Header = L10n.Get(key), MinHeight = 44 };
            item.Click += async (_, _) => await viewModel.OpenFileAsync(file, disposition);
            menu.Items.Add(item);
        }
        button.ContextMenu = menu;
        menu.Open(button);
    }

    private async void OpenFile_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is FeedFilesDrawerViewModel viewModel
            && sender is Button { DataContext: FeedFileItemViewModel file })
        {
            await viewModel.OpenFileAsync(file);
        }
    }
}
