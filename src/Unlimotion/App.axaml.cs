//#define LIVE

using System;
using System.Linq;
using System.Reactive;
using AutoMapper;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Notification;
using Microsoft.Extensions.Configuration;
using Quartz;
using Quartz.Impl;
using ReactiveUI;
using Unlimotion.Scheduling;
using Unlimotion.Scheduling.Jobs;
using Unlimotion.Services;
using Unlimotion.ViewModel;
using Unlimotion.Views;
using WritableJsonConfiguration;
#if LIVE
using Live.Avalonia;
#endif

namespace Unlimotion;

public class App : Application
{
    // Static service instances for the application
    private static IConfiguration? _configuration;
    private static IMapper? _mapper;
    private static IDialogs? _dialogs;
    private static INotificationMessageManager? _notificationMessageManager;
    private static INotificationManagerWrapper? _notificationManager;
    private static IRemoteBackupService? _backupService;
    private static IAppNameDefinitionService? _appNameService;
    private static ITaskStorageFactory? _storageFactory;
    private static IScheduler? _scheduler;
    private static MainWindowViewModel? _mainWindowViewModel;
    
    /// <summary>
    /// Default storage path for tasks (used as fallback when config doesn't specify one)
    /// </summary>
    public static string? DefaultStoragePath { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public event EventHandler OnLoaded;

    private MainWindowViewModel GetMainWindowViewModel()
    {
        if (_mainWindowViewModel != null)
        {
            return _mainWindowViewModel;
        }

        // Create notification message manager (requires Avalonia UI thread)
        _notificationMessageManager ??= new NotificationMessageManager();

        // Ensure wrapper exists and is wired to the UI manager
        if (_notificationManager == null)
        {
            _notificationManager = new NotificationManagerWrapper(_notificationMessageManager);
        }
        else if (_notificationManager is NotificationManagerWrapper wrapper)
        {
            wrapper.SetManager(_notificationMessageManager);
        }

        // Set up exception handler
        RxApp.DefaultExceptionHandler = new ObservableExceptionHandler(_notificationManager);

        // Create SettingsViewModel
        var settingsViewModel = new SettingsViewModel(_configuration!, _backupService);

        // Create GraphViewModel
        var graphViewModel = new GraphViewModel();

        // Create MainWindowViewModel with all dependencies
        _mainWindowViewModel = new MainWindowViewModel(
            _appNameService,
            _notificationManager,
            _configuration!,
            () => _storageFactory?.CurrentStorage,
            settingsViewModel,
            graphViewModel
        )
        {
            ToastNotificationManager = _notificationMessageManager
        };

        // Set up commands on SettingsViewModel
        SetupSettingsCommands(settingsViewModel);

        // Set up static instances for TaskItemViewModel and MainControl
        TaskItemViewModel.NotificationManagerInstance = _notificationManager;
        TaskItemViewModel.MainWindowInstance = _mainWindowViewModel;
        MainControl.DialogsInstance = _dialogs;

        return _mainWindowViewModel;
    }

    private void SetupSettingsCommands(SettingsViewModel settings)
    {
        settings.ConnectCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            _storageFactory?.SwitchStorage(settings.IsServerMode, _configuration!);
            if (_mainWindowViewModel != null)
            {
                await _mainWindowViewModel.Connect();
            }
            _notificationManager?.SuccessToast("Хранилище задач подключено и все задачи из него загружены");
        });

        settings.ObservableForProperty(m => m.GitBackupEnabled, skipInitial: true)
            .Subscribe(c =>
            {
                if (_scheduler == null) return;
                if (c.Value)
                    _scheduler.ResumeAll();
                else
                    _scheduler.PauseAll();
            });

        settings.ObservableForProperty(m => m.GitPullIntervalSeconds, skipInitial: true)
            .Subscribe(c =>
            {
                if (c.Value == 0 || _scheduler == null) return;
                var triggerKey = new TriggerKey("PullTrigger", "GitPullJob");
                _scheduler.RescheduleJob(triggerKey, GenerateTriggerBySecondsInterval("PullTrigger", "GitPullJob", c.Value));
            });

        settings.ObservableForProperty(m => m.GitPushIntervalSeconds, skipInitial: true)
            .Subscribe(c =>
            {
                if (c.Value == 0 || _scheduler == null) return;
                var triggerKey = new TriggerKey("PushTrigger", "GitPushJob");
                _scheduler.RescheduleJob(triggerKey, GenerateTriggerBySecondsInterval("PushTrigger", "GitPushJob", c.Value));
            });

        settings.MigrateCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var serverTaskStorage = _storageFactory?.CurrentStorage;
            if (serverTaskStorage == null || serverTaskStorage.TaskTreeManager.Storage is FileStorage)
            {
                return;
            }
            var storagePath = _configuration?.Get<TaskStorageSettings>("TaskStorage")?.Path;
            var fileStorage = new FileStorage(storagePath ?? TaskStorageFactory.DefaultStoragePath, watcher: false);
            var tasks = new System.Collections.Generic.List<Unlimotion.Domain.TaskItem>();
            await foreach (var task in fileStorage.GetAll())
                tasks.Add(task);
            await serverTaskStorage.TaskTreeManager.Storage.BulkInsert(tasks);
        });

        settings.BackupCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var serverTaskStorage = _storageFactory?.CurrentStorage;
            if (serverTaskStorage == null || serverTaskStorage.TaskTreeManager.Storage is FileStorage)
            {
                return;
            }
            var storagePath = _configuration?.Get<TaskStorageSettings>("TaskStorage")?.Path;
            var fileStorage = new FileStorage(storagePath ?? TaskStorageFactory.DefaultStoragePath, watcher: false);
            await foreach (var task in serverTaskStorage.TaskTreeManager.Storage.GetAll())
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
                await fileStorage.Save(task);
            }
        });

        settings.ResaveCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var storagePath = _configuration?.Get<TaskStorageSettings>("TaskStorage")?.Path;
            var fileStorage = new FileStorage(storagePath ?? TaskStorageFactory.DefaultStoragePath, watcher: false);
            var taskTreeManager = new Unlimotion.TaskTree.TaskTreeManager(fileStorage);
            var fileTaskStorage = new UnifiedTaskStorage(taskTreeManager);
            foreach (var task in fileTaskStorage.Tasks.Items)
            {
                task.SaveItemCommand.Execute();
            }
        });

        settings.BrowseTaskStoragePathCommand = ReactiveCommand.CreateFromTask(async param =>
        {
            if (_dialogs == null) return;
            var path = await _dialogs.ShowOpenFolderDialogAsync("Task Storage Path");
            if (!string.IsNullOrWhiteSpace(path))
            {
                settings.TaskStoragePath = path;
            }
        });

        settings.CloneCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            _backupService?.CloneOrUpdateRepo();
        });

        settings.PullCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            _backupService?.Pull();
        });

        settings.PushCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            _backupService?.Push("Manual backup");
        });
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
                // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
                // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
                DisableAvaloniaDataAnnotationValidation();
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
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainScreen
            {
                DataContext = GetMainWindowViewModel(),
            };
        }

        RxApp.DefaultExceptionHandler = Observer.Create<Exception>(Console.WriteLine);

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();
        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    public App()
    {
        DataContext = new ApplicationViewModel();
    }

    private static void Log(string message)
    {
        var logPath = System.IO.Path.Combine(Environment.CurrentDirectory, "app_debug.log");
        System.IO.File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}\n");
    }

    public static void Init(string configPath)
    {
        try
        {
            Log($"[App.Init] Starting with configPath: {configPath}");
            
            // Create configuration
            _configuration = WritableJsonConfigurationFabric.Create(configPath);
            Log("[App.Init] Configuration created");

            // Create mapper
            _mapper = AppModelMapping.ConfigureMapping();
            Log("[App.Init] Mapper created");

            // Create dialogs
            _dialogs = new Dialogs();
            Log("[App.Init] Dialogs created");

            // Create app name service
            _appNameService = new AppNameDefinitionService();
            Log("[App.Init] AppNameService created");

            // Create notification wrapper placeholder (UI manager will be attached after Avalonia init)
            _notificationManager ??= new NotificationManagerWrapper(null);
            Log("[App.Init] Notification wrapper created (manager deferred)");

            // Get storage settings
            var taskStorageSettings = _configuration.Get<TaskStorageSettings>("TaskStorage");
            if (taskStorageSettings == null)
            {
                taskStorageSettings = new TaskStorageSettings();
                _configuration.Set("TaskStorage", taskStorageSettings);
            }
            Log($"[App.Init] Storage settings: Path={taskStorageSettings.Path}, IsServerMode={taskStorageSettings.IsServerMode}");

            var isServerMode = taskStorageSettings.IsServerMode;

            // Create storage factory
            Log($"[App.Init] Creating storage factory with DefaultStoragePath={TaskStorageFactory.DefaultStoragePath}");
            _storageFactory = new TaskStorageFactory(_configuration, _mapper, _notificationManager);
            if (!string.IsNullOrWhiteSpace(taskStorageSettings.Path))
            {
                TaskStorageFactory.DefaultStoragePath = taskStorageSettings.Path;
            }
            Log("[App.Init] Storage factory created");

            // Create backup service
            _backupService = new BackupViaGitService(_configuration, _notificationManager, _storageFactory);
            BackupViaGitService.GetAbsolutePath = path => System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Unlimotion",
                path);
            Log("[App.Init] Backup service created");

            // Create initial storage
            Log($"[App.Init] Creating initial storage, isServerMode={isServerMode}");
            if (isServerMode)
            {
                _storageFactory.CreateServerStorage(taskStorageSettings.URL);
            }
            else
            {
                _storageFactory.CreateFileStorage(taskStorageSettings.Path);
            }
            Log("[App.Init] Initial storage created");

            // Create scheduler
            var schedulerFactory = new StdSchedulerFactory();
            _scheduler = schedulerFactory.GetScheduler().Result;
            Log("[App.Init] Scheduler created");

            // Set up job factory for DI
            _scheduler.JobFactory = new DependencyInjectionJobFactory(_configuration, _backupService);
            Log("[App.Init] Job factory set");

            // Initialize git settings
            var gitSettings = _configuration.Get<GitSettings>("Git");
            if (gitSettings == null)
            {
                gitSettings = new GitSettings();
                var gitSection = _configuration.GetSection("Git");

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
            Log("[App.Init] Git settings initialized");

            // Initialize scheduler for file mode
            if (!isServerMode)
            {
                var taskRepository = _storageFactory.CurrentStorage;
                if (taskRepository != null)
                {
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

                        _scheduler.ScheduleJob(pullJob, pullTrigger);
                        _scheduler.ScheduleJob(pushJob, pushTrigger);

                        if (gitSettings.BackupEnabled)
                            _scheduler.Start();
                    };
                }
            }
            Log("[App.Init] Completed successfully");
        }
        catch (Exception ex)
        {
            Log($"[App.Init] ERROR: {ex}");
            throw;
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
