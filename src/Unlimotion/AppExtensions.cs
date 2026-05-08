//#define LIVE

using System.Runtime.InteropServices;
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
                    FontFallbacks = CreateEmojiFallbacks()
                });
        }

        private static FontFallback[] CreateEmojiFallbacks()
        {
            var systemEmoji = new FontFallback
            {
                FontFamily = new FontFamily("fonts:SystemFonts#Segoe UI Emoji")
            };
            var embeddedEmoji = new FontFallback
            {
                FontFamily = new FontFamily("avares://Unlimotion/Assets#Noto Color Emoji")
            };

            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? [systemEmoji, embeddedEmoji]
                : [embeddedEmoji, systemEmoji];
        }
    }
}
