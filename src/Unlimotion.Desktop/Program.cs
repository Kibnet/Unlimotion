using System;
using System.Linq;
using System.Threading.Tasks;
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
                    var serverTaskStorage = Locator.Current.GetService<ITaskStorage>() as ServerTaskStorage;
                    if (serverTaskStorage == null)
                    {
                        return;
                    }
                    var storagePath = configuration.Get<TaskStorageSettings>("TaskStorage").Path;
                    var fileTaskStorage = new FileTaskStorage(storagePath ?? "Tasks");
                    await serverTaskStorage.BulkInsert(fileTaskStorage.GetAll());
                });
                settingsViewModel.BackupCommand = ReactiveCommand.CreateFromTask(async () =>
                {
                    var serverTaskStorage = Locator.Current.GetService<ITaskStorage>() as ServerTaskStorage;
                    if (serverTaskStorage == null)
                    {
                        return;
                    }
                    var storagePath = configuration.Get<TaskStorageSettings>("TaskStorage").Path;
                    var fileTaskStorage = new FileTaskStorage(storagePath ?? "Tasks");
                    var tasks = serverTaskStorage.GetAll();
                    foreach (var task in tasks)
                    {
                        task.Id = task.Id.Replace("TaskItem/", "");
                        if (task.BlocksTasks != null)
                        {
                            task.BlocksTasks = task.BlocksTasks.Select(s => s.Replace("TaskItem/", "")).ToList();
                        }
                        if (task.ContainsTasks != null)
                        {
                            task.ContainsTasks = task.ContainsTasks.Select(s => s.Replace("TaskItem/", "")).ToList();
                        }
                        await fileTaskStorage.Save(task);
                    }
                });
                settingsViewModel.ResaveCommand = ReactiveCommand.CreateFromTask(async () =>
                {
                    var storagePath = configuration.Get<TaskStorageSettings>("TaskStorage").Path;
                    var fileTaskStorage = new FileTaskStorage(storagePath ?? "Tasks");
                    var tasks = fileTaskStorage.GetAll();
                    foreach (var task in tasks)
                    {
                        await fileTaskStorage.Save(task);
                    }
                });
            };

#endif
        }

        private static void RegisterStorage(bool isServerMode, IConfigurationRoot configuration)
        {
            var settings = configuration.Get<TaskStorageSettings>("TaskStorage");
            var prevStorage = Locator.Current.GetService<ITaskStorage>();
            if (prevStorage!= null)
            {
                prevStorage.Disconnect();
            }

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
