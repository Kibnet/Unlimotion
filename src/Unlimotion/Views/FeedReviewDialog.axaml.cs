using System;
using Avalonia.Controls;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.Views;

public partial class FeedReviewDialog : UserControl
{
    public FeedReviewDialog()
    {
        InitializeComponent();
    }

    private void OnReviewAreaSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0
            || e.RemovedItems.Count == 0
            || DataContext is not FeedViewModel viewModel
            || !viewModel.CanModifyReviewSource
            || !viewModel.AssignReviewAreaCommand.CanExecute(null))
        {
            return;
        }

        viewModel.AssignReviewAreaCommand.Execute(null);
    }
}
