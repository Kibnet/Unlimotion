using System;
using System.Linq;
using Avalonia;
using Avalonia.ReactiveUI;
using ReactiveUI;
using System.Reactive;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Logging;
using Microsoft.Extensions.Configuration;
using Splat;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;

namespace Unlimotion.Desktop
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
#if DEBUG
                .LogToTrace(LogEventLevel.Debug, LogArea.Binding)
#else
                .LogToTrace()
#endif

                .UseReactiveUI();

        private static void AfterSetup(AppBuilder obj)
        {
            IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create("Settings.json");
            Locator.CurrentMutable.RegisterConstant(configuration, typeof(IConfiguration));
            var storagePath = configuration.GetSection("TaskStorage:Path").Get<string>();
            var taskStorage = new FileTaskStorage(storagePath ?? "Tasks");
            Locator.CurrentMutable.RegisterConstant<ITaskStorage>(taskStorage);
            var taskRepository = new TaskRepository(taskStorage);
            Locator.CurrentMutable.RegisterConstant<ITaskRepository>(taskRepository);
            var mapper = AppModelMapping.ConfigureMapping();
            Locator.CurrentMutable.Register<IMapper>(() => mapper);
            var notificationManager = new NotificationManagerWrapperWrapper();
            Locator.CurrentMutable.RegisterConstant<INotificationManagerWrapper>(notificationManager);

#if DEBUG
            (obj.Instance as App).OnLoaded += (sender, args) =>
            {
                if (obj.Instance.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.MainWindow?.AttachDevTools();
                    desktop.Windows.FirstOrDefault()?.AttachDevTools();
                }
            };

#endif
        }
    }
}
