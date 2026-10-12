using Avalonia.Controls;
using Avalonia.Interactivity;
using Unlimotion.ViewModel;

namespace Unlimotion.Views;

public partial class EmojiFilterCopyControl : UserControl
{
    public EmojiFilterCopyControl() => InitializeComponent();

    private void Source_OnClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not EmojiFilterCopyPanel destination || sender is not Button { DataContext: EmojiFilterCopyPanel source }) return;
        destination.CopyFrom(source);
        CopySourceButton.Flyout?.Hide();
        CopySourceButton.Focus();
    }

    private void Undo_OnClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is EmojiFilterCopyPanel panel) panel.Undo();
        CopySourceButton.Focus();
    }
}
