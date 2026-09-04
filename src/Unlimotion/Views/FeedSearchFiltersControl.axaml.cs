using Avalonia.Controls;
using Avalonia.Interactivity;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.Views;

public partial class FeedSearchFiltersControl : UserControl
{
    public FeedSearchFiltersControl() => InitializeComponent();

    private void OnClearSearchPeriodClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FeedViewModel feed) return;
        feed.SearchFromDate = null;
        feed.SearchToDate = null;
    }
}
