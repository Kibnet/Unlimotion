using Avalonia;
using Avalonia.Headless;

namespace Unlimotion.Test;

/// <summary>Uses production text metrics for multiline layout tests, with no native window.</summary>
public static class SkiaHeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
