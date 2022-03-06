using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.ReactiveUI;
using System;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using Splat;
using Unlimotion.ViewModel;

namespace Unlimotion
{
    class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .AfterSetup(AfterSetup)
                .UsePlatformDetect()
                .LogToTrace()
                .UseReactiveUI();

        private static void AfterSetup(AppBuilder obj)
        {
            var storage = new FileTaskStorage("Tasks");
            Locator.CurrentMutable.RegisterConstant<ITaskStorage>(storage);
        }
    }
}
