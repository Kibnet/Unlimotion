//#define LIVE

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using AutoMapper;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Notification;
using Avalonia.Styling;
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
    private const string AppFontSizeResourceKey = "AppFontSize";
    private const string AppSmallFontSizeResourceKey = "AppSmallFontSize";
    private const string AppTabFontSizeResourceKey = "AppTabFontSize";
    private const string AppTabMinHeightResourceKey = "AppTabMinHeight";
    private const string AppSearchControlHeightResourceKey = "AppSearchControlHeight";
    private const string AppSearchClearButtonSizeResourceKey = "AppSearchClearButtonSize";
    private const string AppSearchClearIconFontSizeResourceKey = "AppSearchClearIconFontSize";
    private const string AppSearchBarMinWidthResourceKey = "AppSearchBarMinWidth";
    private const string AppFloatingControlMinHeightResourceKey = "AppFloatingControlMinHeight";

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
    private ServerStorage? _wiredServerStorage;
    private Action? _serverConnectedHandler;
    private Action<Exception?>? _serverConnectionErrorHandler;
    private EventHandler? _serverSignOutHandler;
    
    /// <summary>
    /// Default storage path for tasks (used as fallback when config doesn't specify one)
    /// </summary>
    public static string? DefaultStoragePath { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ApplyConfiguredTheme();
        ApplyConfiguredFontSize();
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
        var settingsViewModel = new SettingsViewModel(
            _configuration!,
            _backupService,
            GetCurrentThemeIsDark(),
            () => TaskStorageFactory.DefaultStoragePath);

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
        WireSettingsToCurrentStorage(settingsViewModel);

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
            settings.SetStorageConnectionState(SettingsConnectionState.Connecting);
            try
            {
                _storageFactory?.SwitchStorage(settings.IsServerMode, _configuration!);
                WireSettingsToCurrentStorage(settings);
                if (_mainWindowViewModel != null)
                {
                    await _mainWindowViewModel.Connect();
                }

                if (!settings.IsServerMode || settings.StorageConnectionState == SettingsConnectionState.Connecting)
                {
                    settings.SetStorageConnectionState(SettingsConnectionState.Connected);
                }

                if (settings.StorageConnectionState == SettingsConnectionState.Connected)
                {
                    _notificationManager?.SuccessToast("Хранилище задач подключено и все задачи из него загружены");
                }
            }
            catch (Exception ex)
            {
                settings.SetStorageConnectionState(SettingsConnectionState.Error);
                var hint = OperatingSystem.IsAndroid() ? " Проверьте разрешение \"Доступ ко всем файлам\"." : string.Empty;
                _notificationManager?.ErrorToast($"Не удалось подключить хранилище задач: {ex.Message}{hint}");
            }
        });

        settings.SignOutCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var storage = _storageFactory?.CurrentStorage?.TaskTreeManager.Storage as ServerStorage;
            if (storage == null)
            {
                return;
            }

            settings.SetStorageConnectionState(SettingsConnectionState.Connecting, "Выход из серверного аккаунта...");
            await storage.SignOut();
            settings.MarkSignedOut();
        });

        settings.SyncNowCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            settings.SetBackupConnectionState(BackupStatusState.Syncing, "Синхронизация с репозиторием...");
            try
            {
                await Task.Run(() =>
                {
                    _backupService?.Pull();
                    _backupService?.Push("Manual backup");
                });

                settings.ReloadGitMetadata();
                settings.SetBackupConnectionState(BackupStatusState.Connected, "Синхронизация завершена.");
                ShowBackupSuccessToast(settings, "Синхронизация с репозиторием завершена.");
            }
            catch (Exception ex)
            {
                settings.SetBackupConnectionState(BackupStatusState.Error, $"Ошибка синхронизации: {ex.Message}");
                _notificationManager?.ErrorToast($"Не удалось синхронизировать репозиторий: {ex.Message}");
            }
        });

        settings.ObservableForProperty(m => m.GitBackupEnabled, skipInitial: true)
            .Subscribe(c =>
            {
                EnsureScheduler();
                if (_scheduler == null) return;

                if (c.Value)
                    _scheduler.ResumeAll();
                else
                    _scheduler.PauseAll();
            });

        settings.ObservableForProperty(m => m.ThemeMode, skipInitial: true)
            .Subscribe(c => RequestedThemeVariant = c.Value switch
            {
                ThemeMode.Dark => ThemeVariant.Dark,
                ThemeMode.Light => ThemeVariant.Light,
                _ => ThemeVariant.Default
            });

        settings.ObservableForProperty(m => m.FontSize, skipInitial: true)
            .Subscribe(c => ApplyFontSize(c.Value));

        settings.ObservableForProperty(m => m.GitPullIntervalSeconds, skipInitial: true)
            .Subscribe(c =>
            {
                if (c.Value == 0) return;

                EnsureScheduler();
                if (_scheduler == null) return;

                var triggerKey = new TriggerKey("PullTrigger", "GitPullJob");
                _scheduler.RescheduleJob(triggerKey, GenerateTriggerBySecondsInterval("PullTrigger", "GitPullJob", c.Value));
            });

        settings.ObservableForProperty(m => m.GitPushIntervalSeconds, skipInitial: true)
            .Subscribe(c =>
            {
                if (c.Value == 0) return;

                EnsureScheduler();
                if (_scheduler == null) return;

                var triggerKey = new TriggerKey("PushTrigger", "GitPushJob");
                _scheduler.RescheduleJob(triggerKey, GenerateTriggerBySecondsInterval("PushTrigger", "GitPushJob", c.Value));
            });

        settings.MigrateCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            ConfirmAndRun(
                "Перенести локальные задачи на сервер",
                "Локальные задачи будут загружены в текущее серверное хранилище. Продолжить?",
                async () =>
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
                    {
                        tasks.Add(task);
                    }

                    await serverTaskStorage.TaskTreeManager.Storage.BulkInsert(tasks);
                    _notificationManager?.SuccessToast("Локальные задачи перенесены на сервер.");
                },
                ex => _notificationManager?.ErrorToast($"Не удалось перенести локальные задачи на сервер: {ex.Message}"));

            await Task.CompletedTask;
        });

        settings.BackupCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            ConfirmAndRun(
                "Скопировать задачи с сервера в локальное хранилище",
                "Текущие серверные задачи будут сохранены в локальное хранилище на этом устройстве. Продолжить?",
                async () =>
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

                    _notificationManager?.SuccessToast("Задачи с сервера скопированы в локальное хранилище.");
                },
                ex => _notificationManager?.ErrorToast($"Не удалось скопировать задачи с сервера: {ex.Message}"));

            await Task.CompletedTask;
        });

        settings.ResaveCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            ConfirmAndRun(
                "Пересохранить все задачи",
                "Все задачи будут перечитаны и сохранены заново в локальном хранилище. Продолжить?",
                async () =>
                {
                    var storagePath = _configuration?.Get<TaskStorageSettings>("TaskStorage")?.Path;
                    var fileStorage = new FileStorage(storagePath ?? TaskStorageFactory.DefaultStoragePath, watcher: false);
                    var taskTreeManager = new Unlimotion.TaskTree.TaskTreeManager(fileStorage);
                    var fileTaskStorage = new UnifiedTaskStorage(taskTreeManager);
                    foreach (var task in fileTaskStorage.Tasks.Items)
                    {
                        task.SaveItemCommand.Execute();
                    }

                    _notificationManager?.SuccessToast("Все задачи пересохранены.");
                    await Task.CompletedTask;
                },
                ex => _notificationManager?.ErrorToast($"Не удалось пересохранить задачи: {ex.Message}"));

            await Task.CompletedTask;
        });

        settings.BrowseTaskStoragePathCommand = ReactiveCommand.CreateFromTask(async param =>
        {
            if (_dialogs == null) return;
            var path = await _dialogs.ShowOpenFolderDialogAsync("Папка с данными");
            if (!string.IsNullOrWhiteSpace(path))
            {
                settings.TaskStoragePath = path;
            }
        });

        settings.CloneCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                var preview = await Task.Run(() => _backupService?.PreviewConnectRepository());
                if (preview?.RequiresConfirmation == true)
                {
                    settings.SetBackupConnectionState(
                        BackupStatusState.NotConfigured,
                        "Подтвердите объединение локальных задач с репозиторием.");

                    if (_notificationManager == null)
                    {
                        _notificationManager?.ErrorToast("Требуется подтверждение объединения локальных задач с репозиторием.");
                        return;
                    }

                    _notificationManager.Ask(
                        "Подключить непустой репозиторий?",
                        "Папка с задачами не пустая, и удаленный репозиторий уже содержит данные. При подключении локальные задачи будут объединены с содержимым репозитория. Продолжить?",
                        () => _ = ConnectBackupRepositoryAsync(settings, allowMergeWithNonEmptyRemote: true),
                        () => settings.SetBackupConnectionState(BackupStatusState.NotConfigured, "Подключение репозитория отменено."));
                    return;
                }

                await ConnectBackupRepositoryAsync(settings, allowMergeWithNonEmptyRemote: false);
            }
            catch (Exception ex)
            {
                settings.SetBackupConnectionState(BackupStatusState.Error, $"Ошибка подключения репозитория: {ex.Message}");
                _notificationManager?.ErrorToast($"Не удалось подключить репозиторий: {ex.Message}");
            }
        });

        settings.PullCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            settings.SetBackupConnectionState(BackupStatusState.Syncing, "Получение изменений из репозитория...");
            try
            {
                await Task.Run(() => _backupService?.Pull());
                settings.ReloadGitMetadata();
                settings.SetBackupConnectionState(BackupStatusState.Connected, "Изменения из репозитория получены.");
                ShowBackupSuccessToast(settings, "Изменения из репозитория получены.");
            }
            catch (Exception ex)
            {
                settings.SetBackupConnectionState(BackupStatusState.Error, $"Ошибка получения изменений: {ex.Message}");
                _notificationManager?.ErrorToast($"Не удалось получить изменения из репозитория: {ex.Message}");
            }
        });

        settings.PushCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            settings.SetBackupConnectionState(BackupStatusState.Syncing, "Отправка изменений в репозиторий...");
            try
            {
                await Task.Run(() => _backupService?.Push("Manual backup"));
                settings.ReloadGitMetadata();
                settings.SetBackupConnectionState(BackupStatusState.Connected, "Изменения отправлены в репозиторий.");
                ShowBackupSuccessToast(settings, "Изменения отправлены в репозиторий.");
            }
            catch (Exception ex)
            {
                settings.SetBackupConnectionState(BackupStatusState.Error, $"Ошибка отправки изменений: {ex.Message}");
                _notificationManager?.ErrorToast($"Не удалось отправить изменения в репозиторий: {ex.Message}");
            }
        });

        settings.RefreshSshKeysCommand = ReactiveCommand.Create(() =>
        {
            settings.ReloadSshPublicKeys();
            settings.ReloadGitMetadata();
        });

        settings.RefreshGitMetadataCommand = ReactiveCommand.Create(settings.ReloadGitMetadata);

        settings.GenerateSshKeyCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_backupService == null)
            {
                return;
            }

            try
            {
                var publicKeyPath = await Task.Run(() =>
                    _backupService.GenerateSshKey(settings.NewSshKeyName ?? string.Empty));
                settings.ReloadSshPublicKeys(publicKeyPath);
                settings.ReloadGitMetadata();
                _notificationManager?.SuccessToast($"SSH-ключ создан: {publicKeyPath}");
            }
            catch (Exception ex)
            {
                _notificationManager?.ErrorToast($"Не удалось создать SSH-ключ: {ex.Message}");
            }
        });

        settings.CopySelectedSshKeyCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_backupService == null || string.IsNullOrWhiteSpace(settings.SelectedSshPublicKeyPath))
            {
                _notificationManager?.ErrorToast("Сначала выберите публичный SSH-ключ.");
                return;
            }

            var keyContent = _backupService.ReadPublicKey(settings.SelectedSshPublicKeyPath);
            if (string.IsNullOrWhiteSpace(keyContent))
            {
                _notificationManager?.ErrorToast("Публичный SSH-ключ пустой или не найден.");
                return;
            }

            var topLevel = DialogExtensions.GetTopLevel();
            if (topLevel?.Clipboard == null)
            {
                _notificationManager?.ErrorToast("Буфер обмена недоступен.");
                return;
            }

            await topLevel.Clipboard.SetTextAsync(keyContent);
            _notificationManager?.SuccessToast("Публичный SSH-ключ скопирован в буфер обмена.");
        });
    }

    private async Task ConnectBackupRepositoryAsync(
        SettingsViewModel settings,
        bool allowMergeWithNonEmptyRemote)
    {
        settings.SetBackupConnectionState(BackupStatusState.Connecting, "Подключение репозитория...");
        try
        {
            await Task.Run(() => _backupService?.ConnectRepository(allowMergeWithNonEmptyRemote));
            settings.ReloadGitMetadata();
            await ReloadCurrentTaskStorageAsync(settings);
            settings.SetBackupConnectionState(BackupStatusState.Connected, "Репозиторий подключен.");
            ShowBackupSuccessToast(settings, "Репозиторий подключен.");
        }
        catch (Exception ex)
        {
            settings.SetBackupConnectionState(BackupStatusState.Error, $"Ошибка подключения репозитория: {ex.Message}");
            _notificationManager?.ErrorToast($"Не удалось подключить репозиторий: {ex.Message}");
        }
    }

    private async Task ReloadCurrentTaskStorageAsync(SettingsViewModel settings)
    {
        if (_storageFactory == null || _configuration == null || _mainWindowViewModel == null || settings.IsServerMode)
        {
            return;
        }

        _storageFactory.SwitchStorage(isServerMode: false, _configuration);
        WireSettingsToCurrentStorage(settings);
        await _mainWindowViewModel.Connect();
        settings.SetStorageConnectionState(SettingsConnectionState.Connected);
    }

    private void ConfirmAndRun(
        string header,
        string message,
        Func<Task> action,
        Action<Exception>? onError = null)
    {
        void Run()
        {
            _ = ExecuteConfirmedActionAsync(action, onError);
        }

        if (_notificationManager == null)
        {
            Run();
            return;
        }

        _notificationManager.Ask(header, message, Run);
    }

    private async Task ExecuteConfirmedActionAsync(
        Func<Task> action,
        Action<Exception>? onError = null)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
        }
    }

    private void ShowBackupSuccessToast(SettingsViewModel settings, string message)
    {
        if (settings.GitShowStatusToasts)
        {
            _notificationManager?.SuccessToast(message);
        }
    }

    private void WireSettingsToCurrentStorage(SettingsViewModel settings)
    {
        if (_wiredServerStorage != null)
        {
            if (_serverConnectedHandler != null)
            {
                _wiredServerStorage.OnConnected -= _serverConnectedHandler;
            }

            if (_serverConnectionErrorHandler != null)
            {
                _wiredServerStorage.OnConnectionError -= _serverConnectionErrorHandler;
            }

            if (_serverSignOutHandler != null)
            {
                _wiredServerStorage.OnSignOut -= _serverSignOutHandler;
            }
        }

        _wiredServerStorage = null;
        _serverConnectedHandler = null;
        _serverConnectionErrorHandler = null;
        _serverSignOutHandler = null;

        var storage = _storageFactory?.CurrentStorage?.TaskTreeManager.Storage;
        if (storage is ServerStorage serverStorage)
        {
            settings.SetStorageConnectionState(
                serverStorage.IsConnected ? SettingsConnectionState.Connected : SettingsConnectionState.Disconnected);

            _serverConnectedHandler = () =>
            {
                settings.SetStorageConnectionState(SettingsConnectionState.Connected);
            };

            _serverConnectionErrorHandler = _ =>
            {
                settings.SetStorageConnectionState(SettingsConnectionState.Error);
            };

            _serverSignOutHandler = (_, __) =>
            {
                settings.MarkSignedOut();
            };

            serverStorage.OnConnected += _serverConnectedHandler;
            serverStorage.OnConnectionError += _serverConnectionErrorHandler;
            serverStorage.OnSignOut += _serverSignOutHandler;

            _wiredServerStorage = serverStorage;
            return;
        }

        if (storage != null)
        {
            settings.SetStorageConnectionState(SettingsConnectionState.Connected);
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        LibGit2Interop.DisableOwnerValidationOnAndroid();

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
                    var window = new LiveViewHost(this, Debug.WriteLine);
                    window.StartWatchingSourceFilesForHotReloading();
                    window.Show();
                }
            else
#endif
            {
                // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
                // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
                DisableAvaloniaDataAnnotationValidation();
                var vm = GetMainWindowViewModel();
                var window = new MainWindow
                {
                    DataContext = vm
                };

                desktop.MainWindow = window;

                // Когда окно загрузится — вызовем инициализацию
                window.Opened += async (_, __) =>
                {
                    _ = vm.Connect().ContinueWith(_ =>
                    {
                        if (_.Exception != null)
                        {
                            //TODO: Уведомить пользователя
                        }
                    });
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

        RxApp.DefaultExceptionHandler = Observer.Create<Exception>(HandleReactiveException);

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

    private const bool ShouldLogStartup = false;

    private void ApplyConfiguredTheme()
    {
        var configuredTheme = _configuration?
            .GetSection(AppearanceSettings.SectionName)
            .GetSection(AppearanceSettings.ThemeKey)
            .Get<string>();
        var themeMode = AppearanceSettings.ParseThemeMode(configuredTheme);
        switch (themeMode)
        {
            case ThemeMode.Dark:
                RequestedThemeVariant = ThemeVariant.Dark;
                break;
            case ThemeMode.Light:
                RequestedThemeVariant = ThemeVariant.Light;
                break;
            default:
                RequestedThemeVariant = ThemeVariant.Default;
                break;
        }
    }

    private void ApplyConfiguredFontSize()
    {
        var configuredFontSize = _configuration?
            .GetSection(AppearanceSettings.SectionName)
            .GetSection(AppearanceSettings.FontSizeKey)
            .Get<double?>();

        ApplyFontSize(AppearanceSettings.NormalizeFontSize(configuredFontSize));
    }

    private void ApplyFontSize(double fontSize)
    {
        var normalizedFontSize = AppearanceSettings.NormalizeFontSize(fontSize);
        Resources[AppFontSizeResourceKey] = normalizedFontSize;
        Resources[AppSmallFontSizeResourceKey] = AppearanceSettings.GetFloatingWatermarkFontSize(normalizedFontSize);
        Resources[AppTabFontSizeResourceKey] = AppearanceSettings.GetTabFontSize(normalizedFontSize);
        Resources[AppTabMinHeightResourceKey] = AppearanceSettings.GetTabMinHeight(normalizedFontSize);
        Resources[AppSearchControlHeightResourceKey] = AppearanceSettings.GetSearchControlHeight(normalizedFontSize);
        Resources[AppSearchClearButtonSizeResourceKey] = AppearanceSettings.GetSearchClearButtonSize(normalizedFontSize);
        Resources[AppSearchClearIconFontSizeResourceKey] = AppearanceSettings.GetSearchClearIconFontSize(normalizedFontSize);
        Resources[AppSearchBarMinWidthResourceKey] = AppearanceSettings.GetSearchBarMinWidth(normalizedFontSize);
        Resources[AppFloatingControlMinHeightResourceKey] = AppearanceSettings.GetFloatingControlMinHeight(normalizedFontSize);
    }

    private bool GetCurrentThemeIsDark()
    {
        return RequestedThemeVariant switch
        {
            var variant when variant == ThemeVariant.Dark => true,
            var variant when variant == ThemeVariant.Light => false,
            _ => ActualThemeVariant == ThemeVariant.Dark
        };
    }

    private static void Log(string message)
    {
        if (!ShouldLogStartup)
        {
            return;
        }

        Debug.WriteLine($"[App.Init] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}");
    }

    private static void EnsureScheduler()
    {
        if (_scheduler != null)
        {
            return;
        }

        if (_configuration == null || _backupService == null)
        {
            return;
        }

        var schedulerFactory = new StdSchedulerFactory();
        _scheduler = schedulerFactory.GetScheduler().Result;
        _scheduler.JobFactory = new DependencyInjectionJobFactory(_configuration, _backupService);
        Log("[App.Init] Scheduler created lazily");
        Log("[App.Init] Scheduler job factory set");
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

            EnsureDefaultTaskStoragePath(_configuration, ResolveDefaultTaskStoragePath());
            taskStorageSettings = _configuration.Get<TaskStorageSettings>("TaskStorage") ?? taskStorageSettings;

            // Create storage factory
            Log($"[App.Init] Creating storage factory with DefaultStoragePath={TaskStorageFactory.DefaultStoragePath}");
            _storageFactory = new TaskStorageFactory(_configuration, _mapper, _notificationManager);
            TaskStorageFactory.DefaultStoragePath = string.IsNullOrWhiteSpace(taskStorageSettings.Path)
                ? ResolveDefaultTaskStoragePath()
                : taskStorageSettings.Path;
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
                gitSection.GetSection(nameof(GitSettings.SshPrivateKeyPath)).Set(gitSettings.SshPrivateKeyPath);
                gitSection.GetSection(nameof(GitSettings.SshPublicKeyPath)).Set(gitSettings.SshPublicKeyPath);

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
                        EnsureScheduler();
                        if (_scheduler == null)
                        {
                            return;
                        }

                        var currentGitSettings = _configuration?.Get<GitSettings>("Git");
                        if (currentGitSettings?.BackupEnabled != true)
                        {
                            return;
                        }

                        var pullJob = JobBuilder.Create<GitPullJob>()
                            .WithIdentity("GitPullJob", "Git")
                            .Build();
                        var pushJob = JobBuilder.Create<GitPushJob>()
                            .WithIdentity("GitPushJob", "Git")
                            .Build();

                        var pullTrigger = GenerateTriggerBySecondsInterval("PullTrigger", "GitPullJob",
                            currentGitSettings.PullIntervalSeconds);
                        var pushTrigger = GenerateTriggerBySecondsInterval("PushTrigger", "GitPushJob",
                            currentGitSettings.PushIntervalSeconds);

                        _scheduler.ScheduleJob(pullJob, pullTrigger);
                        _scheduler.ScheduleJob(pushJob, pushTrigger);

                        if (currentGitSettings.BackupEnabled)
                        {
                            _scheduler.Start();
                        }
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

    public static void EnsureDefaultTaskStoragePath(IConfiguration configuration, string defaultPath)
    {
        var taskStorageSection = configuration.GetSection("TaskStorage");
        if (taskStorageSection.GetSection(nameof(TaskStorageSettings.IsServerMode)).Get<bool>())
        {
            return;
        }

        var currentPath = taskStorageSection.GetSection(nameof(TaskStorageSettings.Path)).Get<string>();
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            return;
        }

        taskStorageSection.GetSection(nameof(TaskStorageSettings.Path)).Set(defaultPath);
    }

    private static string ResolveDefaultTaskStoragePath()
    {
        if (!string.IsNullOrWhiteSpace(DefaultStoragePath))
        {
            return DefaultStoragePath;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Unlimotion",
            "Tasks");
    }

    private static void HandleReactiveException(Exception ex)
    {
        if (_notificationManager != null)
        {
            _notificationManager.ErrorToast(ex.Message);
        }
        else
        {
            Debug.WriteLine($"[ReactiveUI] {ex}");
        }
    }
}
