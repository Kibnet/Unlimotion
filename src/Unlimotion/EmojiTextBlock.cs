using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Unlimotion.ViewModel;

namespace Unlimotion;

public class EmojiTextBlock : TextBlock
{
    private static readonly FontFamily EmojiFontFamily = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? new FontFamily("fonts:SystemFonts#Segoe UI Emoji")
        : new FontFamily("avares://Unlimotion/Assets#Noto Color Emoji");

    public static readonly StyledProperty<string?> EmojiTextProperty =
        AvaloniaProperty.Register<EmojiTextBlock, string?>(nameof(EmojiText));

    public string? EmojiText
    {
        get => GetValue(EmojiTextProperty);
        set => SetValue(EmojiTextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == EmojiTextProperty)
        {
            UpdateInlines();
        }
    }

    private void UpdateInlines()
    {
        var inlines = Inlines;
        if (inlines == null)
        {
            return;
        }

        inlines.Clear();

        foreach (var segment in EmojiTextHelper.Split(EmojiText))
        {
            var run = new Run(segment.Text);

            if (segment.IsEmoji)
            {
                run.FontFamily = EmojiFontFamily;
                run.FontWeight = FontWeight.Normal;
            }

            inlines.Add(run);
        }
    }
}
