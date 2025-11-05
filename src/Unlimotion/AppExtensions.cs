//#define LIVE

using Avalonia;
using Avalonia.Media;

namespace Unlimotion
{
    public static class AppExtensions
    {
        public static AppBuilder WithCustomFont(this AppBuilder builder)
        {
            return
                builder.With(new FontManagerOptions
                {
                    DefaultFamilyName = "avares://Unlimotion/Assets#Roboto",
                    FontFallbacks = new[]
                    {
                        // 1. Встроенный Noto Color Emoji
                        new FontFallback
                        {
                            FontFamily = new FontFamily("avares://Unlimotion/Assets#Noto Color Emoji")
                        },

                        // 2. На Windows fallback в системный Segoe UI Emoji
                        new FontFallback
                        {
                            FontFamily = new FontFamily("fonts:SystemFonts#Segoe UI Emoji")
                        }
                    }
                });
        }
    }
}
