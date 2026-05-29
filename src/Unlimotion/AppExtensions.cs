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
                    FontFallbacks = EmojiFontResolver.GetEmojiFontFallbacks()
                });
        }
    }
}
