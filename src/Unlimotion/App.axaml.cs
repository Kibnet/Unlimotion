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
                    var vm = GetMainWindowViewModel();

                    var window = new MainWindow
                    {
                        DataContext = vm
                    };

                    desktop.MainWindow = window;

                    // Когда окно загрузится — вызовем инициализацию
                    window.Opened += async (_, __) =>
                    {
                        try
                        {
                            await vm.Connect();
                        }
                        catch (Exception ex)
                        {
                           //TODO: Уведомить пользователя
                        }
                    };
                }

                RxApp.DefaultExceptionHandler = Observer.Create<Exception>(Console.WriteLine);
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                singleViewPlatform.MainView = new MainScreen()
                {
                    DataContext = GetMainWindowViewModel(),
                };
            }

            TaskStorages.SetSettingsCommands();

            base.OnFrameworkInitializationCompleted();
        }

        public App()
        {
            DataContext = new ApplicationViewModel();
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
            if (!isServerMode)
            {
                var taskRepository = Locator.Current.GetService<FileTaskStorage>();
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

        private static bool IsProduction()
        {
#if DEBUG
            return false;
#else
        return true;
#endif
        }
    }
}
