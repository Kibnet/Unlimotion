using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using System.Threading.Tasks;
using Unlimotion.ViewModel.Feed;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Views;

public partial class FeedControl
{
    private void OnDateContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { DataContext: FeedDayViewModel day } date) return;
        var copy = new MenuItem { Header = L10n.Get("FeedCopyPath") };
        copy.Click += async (_, _) =>
        {
            var feed = DataContext as FeedViewModel;
            try
            {
                if (day.FullPath is { } path)
                    await WriteNotePathToClipboardAsync(path);
            }
            catch (System.Exception exception)
            {
                System.Diagnostics.Trace.TraceError("Copy note path: {0}", exception);
                if (ReferenceEquals(DataContext, feed)) feed?.ReportClipboardUnavailable();
            }
        };
        var menu = new ContextMenu();
        menu.Items.Add(copy);
        date.ContextMenu = menu;
        menu.Open(date);
        e.Handled = true;
    }

    protected virtual Task WriteNotePathToClipboardAsync(string path) =>
        TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard
            ? clipboard.SetTextAsync(path)
            : Task.FromException(new System.InvalidOperationException("Clipboard unavailable"));
}
