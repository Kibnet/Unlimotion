using Avalonia;
using Avalonia.Headless;
using Unlimotion;

namespace Unlimotion.UiTests.Headless.Infrastructure;

public static class RenderedHeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
