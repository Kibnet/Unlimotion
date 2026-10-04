using Avalonia;
using Avalonia.Headless;

namespace Unlimotion.Test;

// Use the real app fonts and Skia so the title regressions also produce inspectable pixels.
public static class EmojiTitleTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .WithCustomFont()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
