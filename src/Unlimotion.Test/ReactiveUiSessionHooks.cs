using System;
using ReactiveUI.Avalonia;
using ReactiveUI.Builder;
using TUnit.Core;
using Unlimotion.ViewModel;

namespace Unlimotion.Test;

public static class ReactiveUiSessionHooks
{
    [Before(TestSession)]
    public static void InitializeReactiveUi()
    {
        TaskItemViewModel.DefaultThrottleTime = TimeSpan.FromMilliseconds(10);

        var builder = RxAppBuilder.CreateReactiveUIBuilder();
        builder.WithCoreServices();
        builder.WithMainThreadScheduler(AvaloniaScheduler.Instance);
        App.ConfigureReactiveUIBuilder(builder);
        builder.BuildApp();
    }
}
