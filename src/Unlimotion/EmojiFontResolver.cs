using System.Runtime.InteropServices;
using Avalonia.Media;

namespace Unlimotion;

public static class EmojiFontResolver
{
    public static FontFamily GetEmojiFontFamily()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new FontFamily("fonts:SystemFonts#Segoe UI Emoji");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new FontFamily("fonts:SystemFonts#Apple Color Emoji");
        }

        return new FontFamily("avares://Unlimotion/Assets#Noto Color Emoji");
    }

    public static FontFallback[] GetEmojiFontFallbacks()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return
            [
                new FontFallback { FontFamily = new FontFamily("fonts:SystemFonts#Segoe UI Emoji") },
                new FontFallback { FontFamily = new FontFamily("avares://Unlimotion/Assets#Noto Color Emoji") }
            ];
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return
            [
                new FontFallback { FontFamily = new FontFamily("fonts:SystemFonts#Apple Color Emoji") },
                new FontFallback { FontFamily = new FontFamily("avares://Unlimotion/Assets#Noto Color Emoji") }
            ];
        }

        return
        [
            new FontFallback { FontFamily = new FontFamily("avares://Unlimotion/Assets#Noto Color Emoji") },
            new FontFallback { FontFamily = new FontFamily("fonts:SystemFonts#Segoe UI Emoji") }
        ];
    }
}
