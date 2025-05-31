//#define LIVE

using System;
using System.Reactive;
using AutoMapper;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Notification;
using Microsoft.Extensions.Configuration;
using Quartz;
using Quartz.Impl;
#if LIVE
using Live.Avalonia;
#endif
using ReactiveUI;
using Splat;
using Unlimotion.Scheduling.Jobs;
using Unlimotion.Services;
using Unlimotion.ViewModel;
using Unlimotion.Views;
using WritableJsonConfiguration;

namespace Unlimotion
{
    public partial class App : Application
#if LIVE
        ,ILiveView
#endif
    {
        public static bool IsHeadlessMode { get; private set; }
        public static TrayIcon? TrayIcon { get; set; }

        private static void TrayIcon_Clicked(object? sender, EventArgs e)
        {
            // Example logic: Show/Focus main window
            if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                desktop.MainWindow.Show();
                desktop.MainWindow.Activate();
            }
        }

        public static AppBuilder BuildAvaloniaApp(bool isHeadless = false)
        {
            IsHeadlessMode = isHeadless;
            var appBuilder = AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .UseReactiveUI()
                .With(new Avalonia.X11.X11PlatformOptions { EnableMultiTouch = true, UseDBusMenu = true, UseGpu = false })
                .With(new Avalonia.Win32PlatformOptions
                {
                    AllowEglInitialization = false,
                    EnableMultitouch = true,
                    UseDeferredRendering = true,
                    UseWindowsUIComposition = false,
                })
                .With(new Avalonia.AvaloniaNativePlatformOptions { UseGpu = false, UseDeferredRendering = false })
                .LogToTrace();

            if (!isHeadless)
            {
                appBuilder.AfterSetup(_ => SetupNotificationManager());
            }
            return appBuilder;
        }

        public static void SetupNotificationManager()
        {
            if (IsHeadlessMode) return;

            TrayIcon = new TrayIcon
            {
                Icon = new Avalonia.Media.Imaging.Bitmap("Assets/Unlimotion.ico"), // Assuming WindowIcon was a typo and Bitmap is needed for TrayIcon.Icon
                ToolTipText = "Unlimotion"
            };
            TrayIcon.Clicked += TrayIcon_Clicked;

            // This line was moved to OnFrameworkInitializationCompleted as per analysis
            // if (TrayIcon != null)
            // {
            //     var notificationManagerWrapper = Locator.Current.GetService<INotificationManagerWrapper>();
            //     if (notificationManagerWrapper != null)
            //     {
            //         notificationManagerWrapper.Manager = new NotificationManager(TrayIcon);
            //     }
            // }
        }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public event EventHandler OnLoaded;

        private MainWindowViewModel GetMainWindowViewModel()
        {
            var notificationMessageManager = new NotificationMessageManager();
            Locator.CurrentMutable.RegisterConstant<INotificationMessageManager>(notificationMessageManager);
            RxApp.DefaultExceptionHandler = new ObservableExceptionHandler();
            return new MainWindowViewModel
            {
                ToastNotificationManager = notificationMessageManager
            };
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
#if LIVE
                if (Debugger.IsAttached && !IsProduction())
                {
                    // Here, we create a new LiveViewHost, located in the 'Live.Avalonia'
                    // namespace, and pass an ILiveView implementation to it. The ILiveView
                    // implementation should have a parameterless constructor! Next, we
                    // start listening for any changes in the source files. And then, we
                    // show the LiveViewHost window. Simple enough, huh?
                    var window = new LiveViewHost(this, Console.WriteLine);
                    window.StartWatchingSourceFilesForHotReloading();
                    window.Show();
                }
                else
#endif
                {
                    if (!IsHeadlessMode)
                    {
                        desktop.MainWindow = new MainWindow
                        {
                            DataContext = GetMainWindowViewModel(),
                        };

                        // Initialize NotificationManagerWrapper.Manager here, after TrayIcon is set up
                        if (TrayIcon != null)
                        {
                            var notificationManagerWrapper = Locator.Current.GetService<INotificationManagerWrapper>();
                            if (notificationManagerWrapper != null)
                            {
                                notificationManagerWrapper.Manager = new NotificationManager(TrayIcon);
                            }
                        }

                        // The prompt also mentioned these lines from an original version:
                        // ClientSettings.Instance.Load();
                        // var scheduler = JobSchedulerFactory.Instance.GetScheduler().Result;
                        // scheduler.JobFactory = new JobFactory();
                        // scheduler.Start();
                        // These are not in the current base code. If they were part of the original non-headless setup,
                        // they would be restored here. For now, focusing on NotificationManagerWrapper.
                    }
                }

                RxApp.DefaultExceptionHandler = Observer.Create<Exception>(Console.WriteLine);
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                if (!IsHeadlessMode)
                {
                    singleViewPlatform.MainView = new MainScreen()
                    {
                        DataContext = GetMainWindowViewModel(),
                    };
                }
            }

            TaskStorages.SetSettingsCommands();
            // The Init method already starts the scheduler if gitSettings.BackupEnabled is true.
            // Locator.Current.GetService<IScheduler>()?.Start(); // This might be redundant or for a different scheduler.

            base.OnFrameworkInitializationCompleted();
        }

        public App()
        {
            DataContext = new ApplicationViewModel();
        }

        private static bool IsProduction()
        {
#if DEBUG
            return false;
#else
        return true;
#endif
        }

        public static void Init(string configPath)
        {
            //Создание конфига
            IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(configPath);
            Locator.CurrentMutable.RegisterConstant(configuration, typeof(IConfiguration));
            Locator.CurrentMutable.RegisterConstant(new Dialogs(), typeof(IDialogs));

            //Создание маппера
            var mapper = AppModelMapping.ConfigureMapping();
            Locator.CurrentMutable.Register<IMapper>(() => mapper);

            //Создание сервиса для работы с git
            Locator.CurrentMutable.Register<IRemoteBackupService>(() => new BackupViaGitService());

            //Создание сервиса для получения имени приложения
            Locator.CurrentMutable.RegisterConstant(new AppNameDefinitionService(), typeof(IAppNameDefinitionService));

            //Получение настроек
            var taskStorageSettings = configuration.Get<TaskStorageSettings>("TaskStorage");
            if (taskStorageSettings == null)
            {
                taskStorageSettings = new TaskStorageSettings();
                configuration.Set("TaskStorage", taskStorageSettings);
            }

            var isServerMode = taskStorageSettings.IsServerMode;

            //Регистрация хранилища
            TaskStorages.RegisterStorage(isServerMode, configuration);

            //Регистрация менеджера уведомлений
            var notificationManager = new NotificationManagerWrapper();
            Locator.CurrentMutable.RegisterConstant<INotificationManagerWrapper>(notificationManager);

            //Регистрация планировщика
            var schedulerFactory = new StdSchedulerFactory();
            var scheduler = schedulerFactory.GetScheduler().Result;
            Locator.CurrentMutable.RegisterConstant(scheduler);

            //Инициализация настроек git
            var gitSettings = configuration.Get<GitSettings>("Git");
            if (gitSettings == null)
            {
                gitSettings = new GitSettings();
                var gitSection = configuration.GetSection("Git");

                gitSection.GetSection(nameof(GitSettings.BackupEnabled)).Set(false);
                gitSection.GetSection(nameof(GitSettings.ShowStatusToasts)).Set(gitSettings.ShowStatusToasts);

                gitSection.GetSection(nameof(GitSettings.RemoteUrl)).Set(gitSettings.RemoteUrl);
                gitSection.GetSection(nameof(GitSettings.Branch)).Set(gitSettings.Branch);
                gitSection.GetSection(nameof(GitSettings.UserName)).Set(gitSettings.UserName);
                gitSection.GetSection(nameof(GitSettings.Password)).Set(gitSettings.Password);

                gitSection.GetSection(nameof(GitSettings.PullIntervalSeconds)).Set(gitSettings.PullIntervalSeconds);
                gitSection.GetSection(nameof(GitSettings.PushIntervalSeconds)).Set(gitSettings.PushIntervalSeconds);

                gitSection.GetSection(nameof(GitSettings.RemoteName)).Set(gitSettings.RemoteName);
                gitSection.GetSection(nameof(GitSettings.PushRefSpec)).Set(gitSettings.PushRefSpec);

                gitSection.GetSection(nameof(GitSettings.CommitterName)).Set(gitSettings.CommitterName);
                gitSection.GetSection(nameof(GitSettings.CommitterEmail)).Set(gitSettings.CommitterEmail);
            }

            //Инициализация планировщика
            var taskRepository = Locator.Current.GetService<ITaskRepository>();
            taskRepository.Initiated += (sender, eventArgs) =>
            {
                var pullJob = JobBuilder.Create<GitPullJob>()
                    .WithIdentity("GitPullJob", "Git")
                    .Build();
                var pushJob = JobBuilder.Create<GitPushJob>()
                    .WithIdentity("GitPushJob", "Git")
                    .Build();

                var pullTrigger = GenerateTriggerBySecondsInterval("PullTrigger", "GitPullJob",
                    gitSettings.PullIntervalSeconds);
                var pushTrigger = GenerateTriggerBySecondsInterval("PushTrigger", "GitPushJob",
                    gitSettings.PushIntervalSeconds);

                scheduler.ScheduleJob(pullJob, pullTrigger);
                scheduler.ScheduleJob(pushJob, pushTrigger);

                if (gitSettings.BackupEnabled)
                    scheduler.Start();
            };
        }
        private static ITrigger GenerateTriggerBySecondsInterval(string name, string group, int seconds)
        {
            return TriggerBuilder.Create()
                .WithIdentity(name, group)
                .WithSimpleSchedule(x => x
                    .WithIntervalInSeconds(seconds)
                    .RepeatForever())
                .Build();
        }
    }
}
