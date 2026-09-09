using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.Views;

public partial class MarkdownBlockLivePreviewEditor
{
    // One class handler covers recycled/generated block templates without scanning the visual tree
    // on every edit or layout pass. It captures no editor/window instance.
    private static readonly IDisposable BlockEditorLayoutSubscription =
        DataContextProperty.Changed.AddClassHandler<SafeClipboardTextBox>(static (editor, _) => ApplyBlockTypography(editor));

    private void InitializeBlockEditorTypography()
    {
        _ = BlockEditorLayoutSubscription;
        foreach (var editor in this.GetVisualDescendants().OfType<SafeClipboardTextBox>()) ApplyBlockTypography(editor);
    }

    private static void ApplyBlockTypography(SafeClipboardTextBox editor)
    {
        if (!editor.Classes.Contains("MarkdownBlockEditor") || editor.DataContext is not MarkdownLiveBlockViewModel block) return;
        // Keep the source editor on the same typographic line as CreateHeading/CreateBlockQuote.
        // Markdown markers stay editable; only presentation metrics follow the rendered block kind.
        editor.ClearValue(TextBox.FontSizeProperty);
        editor.ClearValue(TextBox.FontWeightProperty);
        var left = block.ListDepth * 16;
        editor.Margin = new Thickness(left, 0, 0, 0);
        if (block.RenderKind == MarkdownLiveBlockRenderKind.Heading)
        {
            editor.FontSize = block.HeadingLevel switch { <= 1 => 22, 2 => 18, 3 => 16, _ => 14 };
            editor.FontWeight = block.HeadingLevel <= 2 ? FontWeight.SemiBold : FontWeight.Medium;
            editor.Margin = new Thickness(left, block.HeadingLevel <= 2 ? 8 : 4, 0, 2);
        }
        else if (block.RenderKind == MarkdownLiveBlockRenderKind.BlockQuote)
        {
            editor.Margin = new Thickness(left, 2, 0, 2);
        }
    }
}
