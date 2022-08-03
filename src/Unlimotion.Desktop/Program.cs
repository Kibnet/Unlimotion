using System;
using System.Linq;
using Avalonia;
using Avalonia.ReactiveUI;
using ReactiveUI;
using AutoMapper;
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
            var mapper = AppModelMapping.ConfigureMapping();
            Locator.CurrentMutable.Register<IMapper>(() => mapper);

            var isServerMode = configuration.Get<TaskStorageSettings>("TaskStorage").IsServerMode;
            RegisterStorage(isServerMode, configuration);

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

                var settingsViewModel = Locator.Current.GetService<SettingsViewModel>();
                settingsViewModel.ObservableForProperty(m => m.IsServerMode)
                    .Subscribe(c =>
                    {
                        RegisterStorage(c.Value, configuration);
                    });
                settingsViewModel.MigrateCommand = ReactiveCommand.CreateFromTask(async () =>
                {
                    var storage = Locator.Current.GetService<ITaskStorage>() as ServerTaskStorage;
                    if (storage == null)
                    {
                        return;
                    }
                    var storagePath = configuration.Get<TaskStorageSettings>("TaskStorage").Path;
                    var taskStorage = new FileTaskStorage(storagePath ?? "Tasks");
                    await storage.BulkInsert(taskStorage.GetAll());
                });
            };

#endif
        }

        private static void RegisterStorage(bool isServerMode, IConfigurationRoot configuration)
        {
            var settings = configuration.Get<TaskStorageSettings>("TaskStorage");
            ITaskStorage taskStorage;
            if (isServerMode)
            {
                var url = settings.URL;
                taskStorage = new ServerTaskStorage(settings.URL);
            }
            else
            {
                var storagePath = settings.Path;
                taskStorage = new FileTaskStorage(storagePath ?? "Tasks");
            }

            Locator.CurrentMutable.RegisterConstant<ITaskStorage>(taskStorage);
            var taskRepository = new TaskRepository(taskStorage);
            Locator.CurrentMutable.RegisterConstant<ITaskRepository>(taskRepository);
        }
    }
}
