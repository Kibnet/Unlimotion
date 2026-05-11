using ReactiveUI.Avalonia;
using ReactiveUI.Builder;
using TUnit.Core;

namespace Unlimotion.Test;

public static class ReactiveUiSessionHooks
{
    [Before(TestSession)]
    public static void InitializeReactiveUi()
    {
        var builder = RxAppBuilder.CreateReactiveUIBuilder();
        builder.WithCoreServices();
        builder.WithMainThreadScheduler(AvaloniaScheduler.Instance);
        App.ConfigureReactiveUIBuilder(builder);
        builder.BuildApp();
    }
}
