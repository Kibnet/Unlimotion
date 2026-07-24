//#define LIVE

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using DialogHostAvalonia;
using Microsoft.Extensions.Configuration;
using Quartz;
using Quartz.Impl;
using ReactiveUI;
using ReactiveUI.Avalonia;
using Unlimotion.Scheduling;
using Unlimotion.Scheduling.Jobs;
using Unlimotion.Services;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Localization;
using Unlimotion.Views;
using WritableJsonConfiguration;
using L10n = Unlimotion.ViewModel.Localization.Localization;
#if LIVE
using Live.Avalonia;
#endif

namespace Unlimotion;

public class App : Application
{
    private const string AutomationCurrentTaskIdEnvironmentVariable = "UNLIMOTION_AUTOMATION_CURRENT_TASK_ID";
    private const string AutomationOpenDetailsEnvironmentVariable = "UNLIMOTION_AUTOMATION_OPEN_DETAILS";
    private const string AutomationOpenedTaskIdsEnvironmentVariable = "UNLIMOTION_AUTOMATION_OPENED_TASK_IDS";
    private const string AutomationWindowTitleEnvironmentVariable = "UNLIMOTION_AUTOMATION_WINDOW_TITLE";
    private const string AutomationExpandAllTaskTreesEnvironmentVariable = "UNLIMOTION_AUTOMATION_EXPAND_ALL_TASK_TREES";
    private const string AutomationDesktopMonitorEnvironmentVariable = "UNLIMOTION_AUTOMATION_DESKTOP_MONITOR";
    private const string AutomationWindowWidthEnvironmentVariable = "UNLIMOTION_AUTOMATION_WINDOW_WIDTH";
    private const string AutomationWindowHeightEnvironmentVariable = "UNLIMOTION_AUTOMATION_WINDOW_HEIGHT";
    private const string AppFontSizeResourceKey = "AppFontSize";
    private const string AppSmallFontSizeResourceKey = "AppSmallFontSize";
    private const string AppTabFontSizeResourceKey = "AppTabFontSize";
    private const string AppTabMinHeightResourceKey = "AppTabMinHeight";
    private const string AppSearchControlHeightResourceKey = "AppSearchControlHeight";
    private const string AppSearchClearButtonSizeResourceKey = "AppSearchClearButtonSize";
    private const string AppSearchClearIconFontSizeResourceKey = "AppSearchClearIconFontSize";
    private const string AppSearchBarMinWidthResourceKey = "AppSearchBarMinWidth";
    private const string AppFloatingControlMinHeightResourceKey = "AppFloatingControlMinHeight";

    private static string? _pendingConfigPath;
    private static UnlimotionClientOptions _pendingClientOptions = new();
    private static IApplicationUpdateService? _pendingUpdateService;

    private IConfiguration? _configuration;
    private IMapper? _mapper;
    private IDialogs? _dialogs;
    private AppToastNotificationManager? _toastNotificationManager;
    private INotificationManagerWrapper? _notificationManager;
    private IRemoteBackupService? _backupService;
    private IApplicationUpdateService? _applicationUpdateService;
    private IAppNameDefinitionService? _appNameService;
    private ITaskStorageFactory? _storageFactory;
    private TaskMoveService? _taskMoveService;
    private readonly ITaskSpaceOperationRunner _taskSpaceOperationRunner = new TaskSpaceOperationRunner();
    private IActiveTaskSpaceConfiguration? _activeTaskSpaceConfiguration;
    private ITaskSpaceSettingsPersistenceQueue? _taskSpaceSettingsQueue;
    private TaskSpaceCoordinator? _taskSpaceCoordinator;
    private UnlimotionClientOptions _clientOptions = new();
    private string? _configPath;
    private IScheduler? _scheduler;
    private MainWindowViewModel? _mainWindowViewModel;
    private EventHandler? _cultureChangedHandler;
    private SettingsViewModel? _startupUpdateSettings;
    private bool _startupUpdateCheckPending;
    private ServerStorage? _wiredServerStorage;
    private Action? _serverConnectedHandler;
    private Action<Exception?>? _serverConnectionErrorHandler;
    private EventHandler? _serverSignOutHandler;
    private bool _isConflictResolutionDialogOpen;
    private DispatcherTimer? _automaticUpdateTimer;
    private SettingsViewModel? _automaticUpdateTimerSettings;
    private IDisposable? _updateAutoCheckEnabledSubscription;
    private IDisposable? _updateCheckIntervalSubscription;
    private IDisposable? _updateStateSubscription;
    private bool _isAutomaticUpdateCheckRunning;
    private TaskSpaceCatalogException? _startupTaskSpaceCatalogError;
    private Exception? _lastReportedTaskSpaceSettingsPersistenceError;
    
    public override void Initialize()
    {
        RxSchedulers.MainThreadScheduler = AvaloniaScheduler.Instance;
        AvaloniaXamlLoader.Load(this);
        ApplyLocalizedResources();
        if (_cultureChangedHandler != null)
        {
            LocalizationService.Current.CultureChanged -= _cultureChangedHandler;
        }

        _cultureChangedHandler = (_, __) =>
        {
            if (ReferenceEquals(Current, this))
            {
                ApplyLocalizedResources();
            }
        };
        LocalizationService.Current.CultureChanged += _cultureChangedHandler;
        ApplyConfiguredTheme();
        ApplyConfiguredFontSize();
        Styles.Add(new DialogHostStyles());
    }

    public static void ConfigureReactiveUIBuilder(ReactiveUI.Builder.ReactiveUIBuilder builder)
    {
        builder.WithExceptionHandler(Observer.Create<Exception>(HandleReactiveException));
    }

    public event EventHandler OnLoaded
    {
        add { }
        remove { }
    }

    private MainWindowViewModel GetMainWindowViewModel()
    {
        if (_mainWindowViewModel != null)
        {
            return _mainWindowViewModel;
        }

        // Create notification message manager (requires Avalonia UI thread)
        _toastNotificationManager ??= new AppToastNotificationManager();

        // Ensure wrapper exists and is wired to the UI manager
        if (_notificationManager == null)
        {
            _notificationManager = new NotificationManagerWrapper(_toastNotificationManager);
        }
        else if (_notificationManager is NotificationManagerWrapper wrapper)
        {
            wrapper.SetManager(_toastNotificationManager);
        }

        // Create SettingsViewModel
        var settingsViewModel = new SettingsViewModel(
            _configuration!,
            _backupService,
            GetCurrentThemeIsDark(),
            ResolveDefaultTaskStoragePath);
        settingsViewModel.UseDeferredTaskSpaceSettingsPersistence();
        settingsViewModel.ConfigureUpdateService(_applicationUpdateService);
        ApplyStartupTaskSpaceRecoveryState(settingsViewModel);

        // Create GraphViewModel
        var graphViewModel = new GraphViewModel();

        // Create MainWindowViewModel with all dependencies
        _mainWindowViewModel = new MainWindowViewModel(
            _appNameService,
            _notificationManager,
            _configuration!,
            () => _storageFactory?.SourceManager.ActiveStorage,
            settingsViewModel,
            graphViewModel,
            TaskTreeExpansionStateStore.GetDefaultPath(_configPath)
        )
        {
            ToastNotificationManager = _toastNotificationManager,
            Dialogs = _dialogs,
            MoveTaskTreeToFileStorageAsync = MoveTaskTreeViaServiceAsync
        };

        if (_storageFactory != null && _taskSpaceSettingsQueue != null)
        {
            _taskSpaceCoordinator = new TaskSpaceCoordinator(
                _storageFactory.SourceManager,
                _taskSpaceOperationRunner,
                _taskSpaceSettingsQueue,
                async runtime =>
                {
                    runtime.TaskContext.MainWindow = _mainWindowViewModel;
                    await RunOnUiThreadAsync(
                        () => _mainWindowViewModel.BindInitializedStorage(runtime.Storage));
                },
                () => RunOnUiThreadAsync(_mainWindowViewModel.ClearTaskSpaceSurface),
                PauseTaskSpaceSchedulerAsync,
                ApplyActiveTaskSpaceSchedulerAsync);
        }

        // Set up commands on SettingsViewModel
        SetupSettingsCommands(settingsViewModel);
        WireTaskSpaceSettingsPersistenceState(settingsViewModel);
        RefreshTaskSpaces(settingsViewModel);
        WireSettingsToActiveStorage(settingsViewModel);
        SetupAutomaticUpdateTimer(settingsViewModel);
        WireActiveTaskContext();

        return _mainWindowViewModel;
    }

    private static async Task RunOnUiThreadAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }

    private static async Task RunOnUiThreadAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            await action().ConfigureAwait(true);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }

    public static bool TryHandleTaskCardBackGesture()
    {
        var viewModel = (Current as App)?._mainWindowViewModel;
        if (viewModel == null)
        {
            return false;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            return viewModel.TryHandleTaskCardBackGesture();
        }

        return Dispatcher.UIThread
            .InvokeAsync(() => viewModel.TryHandleTaskCardBackGesture())
            .GetAwaiter()
            .GetResult();
    }

    private void SetupSettingsCommands(SettingsViewModel settings)
    {
        settings.AddTaskSpaceCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddTaskSpaceAsync(settings).ConfigureAwait(true);
        });

        settings.SwitchTaskSpaceCommand = ReactiveCommand.CreateFromTask<TaskSpaceOptionViewModel?>(async selected =>
        {
            if (selected != null)
            {
                await SwitchTaskSpaceAsync(settings, selected.SourceId).ConfigureAwait(true);
            }
        });

        settings.RenameTaskSpaceCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var selected = settings.SelectedTaskSpace;
            if (selected == null || string.IsNullOrWhiteSpace(selected.DisplayName))
            {
                return;
            }

            if (_taskSpaceCoordinator == null)
            {
                return;
            }

            settings.IsTaskSpaceSwitching = true;
            try
            {
                await _taskSpaceCoordinator
                    .RenameAsync(selected.SourceId, selected.DisplayName)
                    .ConfigureAwait(true);
            }
            finally
            {
                RefreshTaskSpaces(settings);
                settings.IsTaskSpaceSwitching = false;
            }
        });

        settings.RemoveTaskSpaceCommand = ReactiveCommand.Create(() =>
        {
            var selected = settings.SelectedTaskSpace;
            var manager = _storageFactory?.SourceManager;
            if (selected == null || manager == null || manager.ConfiguredSources.Count <= 1)
            {
                return;
            }

            ConfirmAndRun(
                L10n.Get("TaskSpaceRemoveConfirmTitle"),
                L10n.Format("TaskSpaceRemoveConfirmMessage", selected.DisplayName),
                async () =>
                {
                    if (_taskSpaceCoordinator == null)
                    {
                        return;
                    }

                    settings.IsTaskSpaceSwitching = true;
                    try
                    {
                        await _taskSpaceCoordinator
                            .RemoveAsync(selected.SourceId)
                            .ConfigureAwait(true);
                    }
                    catch (TaskSpaceRecoveryException ex)
                    {
                        SetTaskSpaceRecoveryState(settings, ex);
                    }
                    finally
                    {
                        settings.ReloadActiveTaskSpaceSettings();
                        WireSettingsToActiveStorage(settings);
                        RefreshTaskSpaces(settings);
                        settings.IsTaskSpaceSwitching = false;
                    }
                },
                ex => _notificationManager?.ErrorToast(ex.Message));
        });

        settings.RetryTaskSpaceSettingsPersistenceCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_taskSpaceSettingsQueue == null)
            {
                return;
            }

            try
            {
                await _taskSpaceSettingsQueue.RetryAsync().ConfigureAwait(true);
            }
            catch
            {
                // StateChanged already exposes the persisted error and keeps the draft pending.
            }
        });

        settings.ObservableForProperty(m => m.TaskStoragePath, false, true)
            .Subscribe(_ => EnqueueActiveTaskSpaceSettings(settings));
        settings.ObservableForProperty(m => m.TaskStorageURL, false, true)
            .Subscribe(_ => EnqueueActiveTaskSpaceSettings(settings));
        settings.ObservableForProperty(m => m.Login, false, true)
            .Subscribe(_ => EnqueueActiveTaskSpaceSettings(settings));
        settings.ObservableForProperty(m => m.Password, false, true)
            .Subscribe(_ => EnqueueActiveTaskSpaceSettings(settings));
        settings.ObservableForProperty(m => m.IsServerMode, false, true)
            .Subscribe(_ => EnqueueActiveTaskSpaceSettings(settings));
        settings.ObservableForProperty(m => m.GitBackupEnabled, false, true)
            .Subscribe(_ => EnqueueActiveTaskSpaceSettings(settings));
        settings.ObservableForProperty(m => m.GitShowStatusToasts, false, true)
            .Subscribe(_ => EnqueueActiveTaskSpaceSettings(settings));
        settings.ObservableForProperty(m => m.GitRemoteUrl, false, true)
            .Subscribe(_ => EnqueueActiveTaskSpaceSettings(settings));
        settings.ObservableForProperty(m => m.GitBranch, false, true)
            .Subscribe(_ => EnqueueActiveTaskSpaceSettings(settings));
        settings.ObservableForProperty(m => m.GitUserName, false, true)
            .Subscribe(_ => EnqueueActiveTaskSpaceSettings(settings));
        settings.ObservableForProperty(m => m.GitPassword, false, true)
            .Subscribe(_ => EnqueueActiveTaskSpaceSettings(settings));
        settings.ObservableForProperty(m => m.GitPullIntervalSeconds, false, true)
            .Subscribe(_ => EnqueueActiveTaskSpaceSettings(settings));
        settings.ObservableForProperty(m => m.GitPushIntervalSeconds, false, true)
            .Subscribe(_ => EnqueueActiveTaskSpaceSettings(settings));
        settings.ObservableForProperty(m => m.GitRemoteName, false, true)
            .Subscribe(_ => EnqueueActiveTaskSpaceSettings(settings));
        settings.ObservableForProperty(m => m.GitPushRefSpec, false, true)
            .Subscribe(_ => EnqueueActiveTaskSpaceSettings(settings));
        settings.ObservableForProperty(m => m.GitCommitterName, false, true)
            .Subscribe(_ => EnqueueActiveTaskSpaceSettings(settings));
        settings.ObservableForProperty(m => m.GitCommitterEmail, false, true)
            .Subscribe(_ => EnqueueActiveTaskSpaceSettings(settings));
        settings.ObservableForProperty(m => m.GitSshPrivateKeyPath, false, true)
            .Subscribe(_ => EnqueueActiveTaskSpaceSettings(settings));
        settings.ObservableForProperty(m => m.GitSshPublicKeyPath, false, true)
            .Subscribe(_ => EnqueueActiveTaskSpaceSettings(settings));
        settings.ObservableForProperty(m => m.SshKeyStoragePath, false, true)
            .Subscribe(_ => EnqueueActiveTaskSpaceSettings(settings));

        settings.ConnectCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            settings.SetStorageConnectionState(SettingsConnectionState.Connecting);
            try
            {
                if (!settings.IsServerMode)
                {
                    var shouldContinue = await PrepareLocalStorageConnectionAsync(
                        settings,
                        _backupService,
                        GetCurrentLocalStoragePath(),
                        PrepareFileStoragePathAsync,
                        EnterConflictResolutionMode,
                        operation => RunBackupOperationAsync("PrepareLocalGitRepository", operation));
                    if (!shouldContinue)
                    {
                        return;
                    }
                }

                if (_taskSpaceCoordinator != null)
                {
                    await _taskSpaceCoordinator.ReconnectActiveAsync().ConfigureAwait(true);
                }
                else if (_storageFactory != null && _configuration != null)
                {
                    // Preserve the pre-task-spaces composition contract used by lightweight
                    // hosts that provide a storage factory without constructing the coordinator.
                    await _storageFactory.SourceManager
                        .SwitchStorageAsync(settings.IsServerMode, _configuration)
                        .ConfigureAwait(true);
                    WireActiveTaskContext();
                    if (_mainWindowViewModel != null)
                    {
                        await _mainWindowViewModel.Connect().ConfigureAwait(true);
                    }
                }

                WireSettingsToActiveStorage(settings);

                if (!settings.IsServerMode || settings.StorageConnectionState == SettingsConnectionState.Connecting)
                {
                    settings.SetStorageConnectionState(SettingsConnectionState.Connected);
                }

                if (settings.StorageConnectionState == SettingsConnectionState.Connected)
                {
                    _notificationManager?.SuccessToast(L10n.Get("StorageConnectedToast"));
                }
            }
            catch (Exception ex)
            {
                if (ex is TaskSpaceRecoveryException recoveryError)
                {
                    SetTaskSpaceRecoveryState(settings, recoveryError);
                    return;
                }

                settings.SetStorageConnectionState(SettingsConnectionState.Error);
                var hint = OperatingSystem.IsAndroid() ? L10n.Get("AndroidAllFilesHint") : string.Empty;
                _notificationManager?.ErrorToast(L10n.Format("ConnectStorageFailed", ex.Message, hint));
            }
        });

        settings.SignOutCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var storage = _storageFactory?.SourceManager.ActiveStorage?.TaskTreeManager.Storage as ServerStorage;
            if (storage == null)
            {
                return;
            }

            settings.SetStorageConnectionState(SettingsConnectionState.Connecting, L10n.Get("SignOutInProgress"));
            await storage.SignOut();
            settings.MarkSignedOut();
        });

        settings.SyncNowCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            settings.SetBackupConnectionState(BackupStatusState.Syncing, L10n.Get("SyncingRepository"));
            try
            {
                await RunBackupOperationAsync("ManualGitSync", () =>
                {
                    _backupService?.Pull();
                    if (_backupService?.GetConflictStatus().IsInProgress != true)
                    {
                        _backupService?.Push("Manual backup");
                    }
                });

                settings.ReloadGitMetadata();
                if (settings.IsConflictResolutionMode)
                {
                    EnterConflictResolutionMode(settings);
                    return;
                }

                settings.SetBackupConnectionState(BackupStatusState.Connected, L10n.Get("SyncComplete"));
                ShowBackupSuccessToast(settings, L10n.Get("SyncComplete"));
            }
            catch (Exception ex)
            {
                settings.ReloadGitMetadata();
                if (settings.IsConflictResolutionMode)
                {
                    EnterConflictResolutionMode(settings);
                    return;
                }

                settings.SetBackupConnectionState(BackupStatusState.Error, L10n.Format("SyncErrorStatus", ex.Message));
                _notificationManager?.ErrorToast(L10n.Format("SyncErrorToast", ex.Message));
            }
        });

        settings.ObservableForProperty(m => m.ThemeMode, false, true)
            .Subscribe(c => RequestedThemeVariant = c.Value switch
            {
                ThemeMode.Dark => ThemeVariant.Dark,
                ThemeMode.Light => ThemeVariant.Light,
                _ => ThemeVariant.Default
            });

        settings.ObservableForProperty(m => m.FontSize, false, true)
            .Subscribe(c => ApplyFontSize(c.Value));

        settings.MigrateCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            ConfirmAndRun(
                L10n.Get("MigrateConfirmHeader"),
                L10n.Get("MigrateConfirmMessage"),
                async () =>
                {
                    var serverTaskStorage = _storageFactory?.SourceManager.ActiveStorage;
                    if (serverTaskStorage == null || serverTaskStorage.TaskTreeManager.Storage is FileStorage)
                    {
                        return;
                    }

                    var fileStorage = new FileStorage(ResolveDefaultLocalFileStoragePath(), watcher: false);
                    var tasks = new System.Collections.Generic.List<Unlimotion.Domain.TaskItem>();
                    await foreach (var task in fileStorage.GetAll())
                    {
                        tasks.Add(task);
                    }

                    await serverTaskStorage.TaskTreeManager.Storage.BulkInsert(tasks);
                    _notificationManager?.SuccessToast(L10n.Get("MigrateLocalTasksSuccess"));
                },
                ex => _notificationManager?.ErrorToast(L10n.Format("MigrateLocalTasksFailed", ex.Message)));

            await Task.CompletedTask;
        });

        settings.BackupCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            ConfirmAndRun(
                L10n.Get("BackupConfirmHeader"),
                L10n.Get("BackupConfirmMessage"),
                async () =>
                {
                    var serverTaskStorage = _storageFactory?.SourceManager.ActiveStorage;
                    if (serverTaskStorage == null || serverTaskStorage.TaskTreeManager.Storage is FileStorage)
                    {
                        return;
                    }

                    var fileStorage = new FileStorage(ResolveDefaultLocalFileStoragePath(), watcher: false);
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

                    _notificationManager?.SuccessToast(L10n.Get("ServerTasksCopiedToLocal"));
                },
                ex => _notificationManager?.ErrorToast(L10n.Format("CopyServerTasksFailed", ex.Message)));

            await Task.CompletedTask;
        });

        settings.ResaveCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            ConfirmAndRun(
                L10n.Get("ResaveConfirmHeader"),
                L10n.Get("ResaveConfirmMessage"),
                async () =>
                {
                    var fileStorage = new FileStorage(ResolveDefaultLocalFileStoragePath(), watcher: false);
                    var taskTreeManager = new Unlimotion.TaskTree.TaskTreeManager(fileStorage);
                    var fileTaskStorage = new UnifiedTaskStorage(taskTreeManager);
                    foreach (var task in fileTaskStorage.Tasks.Items)
                    {
                        task.SaveItemCommand.Execute();
                    }

                    _notificationManager?.SuccessToast(L10n.Get("AllTasksResaved"));
                    await Task.CompletedTask;
                },
                ex => _notificationManager?.ErrorToast(L10n.Format("ResaveTasksFailed", ex.Message)));

            await Task.CompletedTask;
        });

        settings.BrowseTaskStoragePathCommand = ReactiveCommand.CreateFromTask(async param =>
        {
            if (_dialogs == null) return;
            try
            {
                var path = await _dialogs.ShowOpenFolderDialogAsync(
                    L10n.Get("FolderPickerDataFolder"),
                    settings.TaskStoragePath);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    await PrepareFileStoragePathAsync(path);
                    settings.TaskStoragePath = path;
                }
            }
            catch (Exception ex)
            {
                settings.SetStorageConnectionState(SettingsConnectionState.Error);
                var hint = OperatingSystem.IsAndroid() ? L10n.Get("AndroidAllFilesHint") : string.Empty;
                _notificationManager?.ErrorToast(L10n.Format("ConnectStorageFailed", ex.Message, hint));
            }
        });

        settings.CloneCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                var preview = await RunBackupOperationAsync(
                    "PreviewGitRepositoryConnection",
                    () => _backupService?.PreviewConnectRepository());
                if (preview?.RequiresConfirmation == true)
                {
                    settings.SetBackupConnectionState(
                        BackupStatusState.NotConfigured,
                        L10n.Get("BackupMergeConfirmStatus"));

                    if (_notificationManager == null)
                    {
                        _notificationManager?.ErrorToast(L10n.Get("BackupMergeConfirmationRequired"));
                        return;
                    }

                    _notificationManager.Ask(
                        L10n.Get("BackupMergeConfirmHeader"),
                        L10n.Get("BackupMergeConfirmMessage"),
                        () => _ = ConnectBackupRepositoryAsync(settings, allowMergeWithNonEmptyRemote: true),
                        () => settings.SetBackupConnectionState(BackupStatusState.NotConfigured, L10n.Get("RepositoryConnectCanceled")));
                    return;
                }

                await ConnectBackupRepositoryAsync(settings, allowMergeWithNonEmptyRemote: false);
            }
            catch (Exception ex)
            {
                settings.ReloadGitMetadata();
                if (settings.IsConflictResolutionMode)
                {
                    EnterConflictResolutionMode(settings);
                    return;
                }

                settings.SetBackupConnectionState(BackupStatusState.Error, L10n.Format("RepositoryConnectErrorStatus", ex.Message));
                _notificationManager?.ErrorToast(L10n.Format("RepositoryConnectErrorToast", ex.Message));
            }
        });

        settings.PullCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            settings.SetBackupConnectionState(BackupStatusState.Syncing, L10n.Get("PullingChanges"));
            try
            {
                await RunBackupOperationAsync("ManualGitPull", () => _backupService?.Pull());
                settings.ReloadGitMetadata();
                if (settings.IsConflictResolutionMode)
                {
                    EnterConflictResolutionMode(settings);
                    return;
                }

                settings.SetBackupConnectionState(BackupStatusState.Connected, L10n.Get("PulledChanges"));
                ShowBackupSuccessToast(settings, L10n.Get("PulledChanges"));
            }
            catch (Exception ex)
            {
                settings.ReloadGitMetadata();
                if (settings.IsConflictResolutionMode)
                {
                    EnterConflictResolutionMode(settings);
                    return;
                }

                settings.SetBackupConnectionState(BackupStatusState.Error, L10n.Format("PullErrorStatus", ex.Message));
                _notificationManager?.ErrorToast(L10n.Format("PullErrorToast", ex.Message));
            }
        });

        settings.PushCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            settings.SetBackupConnectionState(BackupStatusState.Syncing, L10n.Get("PushingChanges"));
            try
            {
                await RunBackupOperationAsync("ManualGitPush", () => _backupService?.Push("Manual backup"));
                settings.ReloadGitMetadata();
                settings.SetBackupConnectionState(BackupStatusState.Connected, L10n.Get("PushedChanges"));
                ShowBackupSuccessToast(settings, L10n.Get("PushedChanges"));
            }
            catch (Exception ex)
            {
                settings.SetBackupConnectionState(BackupStatusState.Error, L10n.Format("PushErrorStatus", ex.Message));
                _notificationManager?.ErrorToast(L10n.Format("PushErrorToast", ex.Message));
            }
        });

        settings.ResolveConflictUseCurrentCommand = ReactiveCommand.CreateFromTask<BackupConflictFile?>(conflict =>
            ResolveBackupConflictAsync(settings, conflict, BackupConflictResolution.UseCurrent));

        settings.ResolveConflictUseIncomingCommand = ReactiveCommand.CreateFromTask<BackupConflictFile?>(conflict =>
            ResolveBackupConflictAsync(settings, conflict, BackupConflictResolution.UseIncoming));

        settings.ResolveConflictUseFieldSelectionCommand = ReactiveCommand.CreateFromTask<BackupConflictFile?>(conflict =>
            ResolveBackupConflictFieldsAsync(settings, conflict));

        settings.RefreshBackupConflictsCommand = ReactiveCommand.Create(() =>
        {
            settings.ReloadGitMetadata();
            if (settings.IsConflictResolutionMode)
            {
                EnterConflictResolutionMode(settings);
            }
        });

        settings.OpenConflictResolutionWindowCommand = ReactiveCommand.Create(() =>
        {
            settings.ReloadGitMetadata();
            if (settings.IsConflictResolutionMode)
            {
                EnterConflictResolutionMode(settings);
            }
        });

        settings.CommitConflictResolutionCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            settings.SetBackupConnectionState(BackupStatusState.Syncing, L10n.Get("CommittingConflictResolution"));
            try
            {
                await RunBackupOperationAsync("CommitGitConflictResolution", () =>
                {
                    _backupService?.CommitResolvedConflicts(L10n.Get("ResolveSyncConflictsCommitMessage"));
                });

                settings.ReloadGitMetadata();
                if (settings.IsConflictResolutionMode)
                {
                    settings.SetBackupConnectionState(BackupStatusState.ConflictResolution);
                    return;
                }

                settings.CompleteConflictResolution();
                CloseConflictResolutionDialog();
                await ReloadCurrentTaskStorageAsync(settings);
                settings.ReloadGitMetadata();
                if (settings.IsConflictResolutionMode)
                {
                    EnterConflictResolutionMode(settings);
                    return;
                }

                settings.CompleteConflictResolution();
                settings.SetBackupConnectionState(BackupStatusState.Connected, L10n.Get("ConflictResolutionComplete"));
                ResumeBackupScheduler(settings);
                ShowBackupSuccessToast(settings, L10n.Get("ConflictResolutionComplete"));
            }
            catch (Exception ex)
            {
                settings.ReloadGitMetadata();
                if (settings.IsConflictResolutionMode)
                {
                    settings.SetBackupConnectionState(BackupStatusState.ConflictResolution);
                    return;
                }

                settings.SetBackupConnectionState(
                    BackupStatusState.Error,
                    L10n.Format("CommitConflictResolutionErrorStatus", ex.Message));
                _notificationManager?.ErrorToast(L10n.Format("CommitConflictResolutionErrorToast", ex.Message));
            }
        });

        settings.RefreshSshKeysCommand = ReactiveCommand.Create(() =>
        {
            settings.ReloadSshPublicKeys();
            settings.ReloadGitMetadata();
        });

        settings.BrowseSshKeyStoragePathCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_dialogs == null) return;

            var path = await _dialogs.ShowOpenFolderDialogAsync(
                L10n.Get("FolderPickerSshKeyStoragePath"),
                settings.SshKeyStoragePath);
            if (!string.IsNullOrWhiteSpace(path))
            {
                settings.SshKeyStoragePath = path;
                settings.ReloadGitMetadata();
            }
        });

        settings.RefreshGitMetadataCommand = ReactiveCommand.Create(settings.ReloadGitMetadata);
        SettingsRemoteConnectionTypeCommands.Configure(settings, _backupService, _notificationManager);

        settings.GenerateSshKeyCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_backupService == null)
            {
                return;
            }

            try
            {
                var publicKeyPath = await RunBackupOperationAsync(
                    "GenerateGitSshKey",
                    () =>
                    _backupService.GenerateSshKey(settings.NewSshKeyName ?? string.Empty));
                settings.ReloadSshPublicKeys(publicKeyPath);
                settings.ReloadGitMetadata();
                _notificationManager?.SuccessToast(L10n.Format("SshKeyCreated", publicKeyPath));
            }
            catch (Exception ex)
            {
                _notificationManager?.ErrorToast(L10n.Format("SshKeyCreateFailed", ex.Message));
            }
        });

        settings.CopySelectedSshKeyCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_backupService == null || string.IsNullOrWhiteSpace(settings.SelectedSshPublicKeyPath))
            {
                _notificationManager?.ErrorToast(L10n.Get("SelectSshKey"));
                return;
            }

            var keyContent = _backupService.ReadPublicKey(settings.SelectedSshPublicKeyPath);
            if (string.IsNullOrWhiteSpace(keyContent))
            {
                _notificationManager?.ErrorToast(L10n.Get("EmptySshKey"));
                return;
            }

            var topLevel = DialogExtensions.GetTopLevel();
            if (topLevel?.Clipboard == null)
            {
                _notificationManager?.ErrorToast(L10n.Get("ClipboardUnavailable"));
                return;
            }

            await topLevel.Clipboard.SetTextAsync(keyContent);
            _notificationManager?.SuccessToast(L10n.Get("SshKeyCopied"));
        });

        settings.CheckForUpdatesCommand = ReactiveCommand.CreateFromTask(() => settings.CheckForUpdatesAsync());
        settings.DownloadUpdateCommand = ReactiveCommand.CreateFromTask(() => settings.DownloadUpdateAsync());
        settings.ApplyUpdateCommand = ReactiveCommand.CreateFromTask(() => settings.ApplyUpdateAsync());
    }

    private void EnterConflictResolutionMode(SettingsViewModel settings)
    {
        PauseBackupScheduler(settings);
        settings.SetBackupConnectionState(BackupStatusState.ConflictResolution);
        ShowConflictResolutionDialog(settings);
    }

    private void ShowConflictResolutionDialog(SettingsViewModel settings)
    {
        if (!settings.IsConflictResolutionMode || _isConflictResolutionDialogOpen)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!settings.IsConflictResolutionMode || _isConflictResolutionDialogOpen)
            {
                return;
            }

            try
            {
                _isConflictResolutionDialogOpen = true;
                var dialogTask = DialogHost.Show(settings, "Ask");
                _ = dialogTask.ContinueWith(
                    _ => _isConflictResolutionDialogOpen = false,
                    TaskScheduler.Default);
            }
            catch
            {
                _isConflictResolutionDialogOpen = false;
            }
        }, DispatcherPriority.Background);
    }

    private void ResumeBackupScheduler(SettingsViewModel settings)
    {
        if (!settings.GitBackupEnabled)
        {
            return;
        }

        EnsureScheduler();
        if (_scheduler == null)
        {
            return;
        }

        _ = _scheduler.ResumeAll();
        if (!_scheduler.IsStarted)
        {
            _ = _scheduler.Start();
        }
    }

    private void PauseBackupScheduler(SettingsViewModel settings)
    {
        if (!settings.GitBackupEnabled)
        {
            return;
        }

        EnsureScheduler();
        if (_scheduler == null)
        {
            return;
        }

        _ = _scheduler.PauseAll();
    }

    private void CloseConflictResolutionDialog()
    {
        Dispatcher.UIThread.Post(() =>
        {
            DialogHost.GetDialogSession("Ask")?.Close(false);
            _isConflictResolutionDialogOpen = false;
        });
    }

    private async Task ResolveBackupConflictFieldsAsync(
        SettingsViewModel settings,
        BackupConflictFile? conflict)
    {
        var targetConflict = conflict ?? settings.SelectedBackupConflict;
        if (targetConflict == null || !targetConflict.CanResolveByFields)
        {
            return;
        }

        var selections = settings.GetSelectedBackupConflictFieldSelections();

        settings.SetBackupConnectionState(BackupStatusState.Syncing, L10n.Get("ResolvingConflict"));
        try
        {
            await RunBackupOperationAsync(
                "ResolveGitConflictFields",
                () => _backupService?.ResolveConflictFields(targetConflict.Path, selections));
            settings.ReloadGitMetadata();
            if (!settings.IsConflictResolutionMode)
            {
                settings.MarkConflictResolutionPendingCommit();
            }

            settings.SetBackupConnectionState(
                BackupStatusState.ConflictResolution,
                L10n.Get("FieldConflictResolutionComplete"));
        }
        catch (Exception ex)
        {
            settings.ReloadGitMetadata();
            if (settings.IsConflictResolutionMode)
            {
                settings.SetBackupConnectionState(
                    BackupStatusState.ConflictResolution,
                    L10n.Format("ConflictResolveErrorStatus", ex.Message));
                return;
            }

            settings.SetBackupConnectionState(BackupStatusState.Error, L10n.Format("ConflictResolveErrorStatus", ex.Message));
            _notificationManager?.ErrorToast(L10n.Format("ConflictResolveErrorToast", ex.Message));
        }
    }

    private async Task ResolveBackupConflictAsync(
        SettingsViewModel settings,
        BackupConflictFile? conflict,
        BackupConflictResolution resolution)
    {
        var targetConflict = conflict ?? settings.SelectedBackupConflict;
        if (targetConflict == null)
        {
            return;
        }

        settings.SetBackupConnectionState(BackupStatusState.Syncing, L10n.Get("ResolvingConflict"));
        try
        {
            await RunBackupOperationAsync(
                "ResolveGitConflict",
                () => _backupService?.ResolveConflict(targetConflict.Path, resolution));
            settings.ReloadGitMetadata();
            if (!settings.IsConflictResolutionMode)
            {
                settings.MarkConflictResolutionPendingCommit();
            }

            settings.SetBackupConnectionState(BackupStatusState.ConflictResolution);
        }
        catch (Exception ex)
        {
            settings.ReloadGitMetadata();
            if (settings.IsConflictResolutionMode)
            {
                settings.SetBackupConnectionState(
                    BackupStatusState.ConflictResolution,
                    L10n.Format("ConflictResolveErrorStatus", ex.Message));
                return;
            }

            settings.SetBackupConnectionState(BackupStatusState.Error, L10n.Format("ConflictResolveErrorStatus", ex.Message));
            _notificationManager?.ErrorToast(L10n.Format("ConflictResolveErrorToast", ex.Message));
        }
    }

    private void RefreshTaskSpaces(SettingsViewModel settings)
    {
        var manager = _storageFactory?.SourceManager;
        if (manager?.ActiveSource == null)
        {
            return;
        }

        settings.ReloadTaskSpaces(manager.ConfiguredSources, manager.ActiveSource.Descriptor.Id);
    }

    private void EnqueueActiveTaskSpaceSettings(SettingsViewModel settings)
    {
        if (settings.IsTaskSpaceSwitching)
        {
            return;
        }

        var sourceId = _storageFactory?.SourceManager.ActiveSource?.Descriptor.Id;
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return;
        }

        try
        {
            if (_taskSpaceSettingsQueue != null)
            {
                _taskSpaceSettingsQueue.Enqueue(settings.CreateTaskSpaceSettingsDraft(sourceId));
            }
            else
            {
                _storageFactory?.SourceManager.PersistActiveSourceSettings();
            }
        }
        catch (NotSupportedException)
        {
            // Test doubles that predate task spaces do not persist per-space settings.
        }
    }

    private async Task<bool> SwitchTaskSpaceAsync(SettingsViewModel settings, string sourceId)
    {
        var manager = _storageFactory?.SourceManager;
        if (manager == null || string.Equals(manager.ActiveSource?.Descriptor.Id, sourceId, StringComparison.Ordinal))
        {
            RefreshTaskSpaces(settings);
            return false;
        }

        if (settings.IsConflictResolutionMode)
        {
            RefreshTaskSpaces(settings);
            _notificationManager?.ErrorToast(L10n.Get("TaskSpaceSwitchBlockedByConflict"));
            return false;
        }

        settings.IsTaskSpaceSwitching = true;
        try
        {
            var descriptor = manager.ConfiguredSources.FirstOrDefault(source =>
                string.Equals(source.Id, sourceId, StringComparison.Ordinal));
            if (descriptor?.Kind == TaskSourceKind.File)
            {
                await PrepareFileStoragePathAsync(descriptor.Path).ConfigureAwait(true);
            }

            if (_taskSpaceCoordinator == null)
            {
                throw new InvalidOperationException("Task-space coordinator is not initialized.");
            }

            await _taskSpaceCoordinator.SwitchAsync(sourceId).ConfigureAwait(true);
            settings.IsTaskSpaceRecoveryRequired = false;
            settings.TaskSpaceRecoveryMessage = string.Empty;
            settings.ReloadActiveTaskSpaceSettings();
            WireSettingsToActiveStorage(settings);
            settings.SetStorageConnectionState(SettingsConnectionState.Connected);
            RefreshTaskSpaces(settings);
            return true;
        }
        catch (TaskSpaceRecoveryException ex)
        {
            settings.IsTaskSpaceRecoveryRequired = true;
            settings.TaskSpaceRecoveryMessage = L10n.Format(
                "TaskSpaceRecoveryRequired",
                ex.ActivationError.Message,
                ex.RestorationError.Message);
            settings.SetStorageConnectionState(SettingsConnectionState.Error);
            RefreshTaskSpaces(settings);
            _notificationManager?.ErrorToast(settings.TaskSpaceRecoveryMessage);
            return false;
        }
        catch (Exception ex)
        {
            RefreshTaskSpaces(settings);
            _notificationManager?.ErrorToast(L10n.Format("ConnectStorageFailed", ex.Message, string.Empty));
            return false;
        }
        finally
        {
            settings.IsTaskSpaceSwitching = false;
        }
    }

    private async Task AddTaskSpaceAsync(SettingsViewModel settings)
    {
        if (_taskSpaceCoordinator == null)
        {
            return;
        }

        if (settings.IsConflictResolutionMode)
        {
            _notificationManager?.ErrorToast(L10n.Get("TaskSpaceSwitchBlockedByConflict"));
            return;
        }

        settings.IsTaskSpaceSwitching = true;
        try
        {
            await _taskSpaceCoordinator.AddLocalAsync(settings.NewTaskSpaceName).ConfigureAwait(true);
            settings.IsTaskSpaceRecoveryRequired = false;
            settings.TaskSpaceRecoveryMessage = string.Empty;
            settings.ReloadActiveTaskSpaceSettings();
            WireSettingsToActiveStorage(settings);
            settings.SetStorageConnectionState(SettingsConnectionState.Connected);
            RefreshTaskSpaces(settings);
        }
        catch (TaskSpaceRecoveryException ex)
        {
            settings.IsTaskSpaceRecoveryRequired = true;
            settings.TaskSpaceRecoveryMessage = L10n.Format(
                "TaskSpaceRecoveryRequired",
                ex.ActivationError.Message,
                ex.RestorationError.Message);
            settings.SetStorageConnectionState(SettingsConnectionState.Error);
            RefreshTaskSpaces(settings);
            _notificationManager?.ErrorToast(settings.TaskSpaceRecoveryMessage);
        }
        catch (Exception ex)
        {
            RefreshTaskSpaces(settings);
            _notificationManager?.ErrorToast(L10n.Format("ConnectStorageFailed", ex.Message, string.Empty));
        }
        finally
        {
            settings.IsTaskSpaceSwitching = false;
        }
    }

    private async Task ConnectBackupRepositoryAsync(
        SettingsViewModel settings,
        bool allowMergeWithNonEmptyRemote)
    {
        settings.SetBackupConnectionState(BackupStatusState.Connecting, L10n.Get("ConnectingRepository"));
        try
        {
            await RunBackupOperationAsync(
                "ConnectGitRepository",
                () => _backupService?.ConnectRepository(allowMergeWithNonEmptyRemote));
            settings.ReloadGitMetadata();
            if (settings.IsConflictResolutionMode)
            {
                EnterConflictResolutionMode(settings);
                return;
            }

            await ReloadCurrentTaskStorageAsync(settings);
            settings.SetBackupConnectionState(BackupStatusState.Connected, L10n.Get("RepositoryConnected"));
            ShowBackupSuccessToast(settings, L10n.Get("RepositoryConnected"));
        }
        catch (Exception ex)
        {
            settings.ReloadGitMetadata();
            if (settings.IsConflictResolutionMode)
            {
                EnterConflictResolutionMode(settings);
                return;
            }

            settings.SetBackupConnectionState(BackupStatusState.Error, L10n.Format("RepositoryConnectErrorStatus", ex.Message));
            _notificationManager?.ErrorToast(L10n.Format("RepositoryConnectErrorToast", ex.Message));
        }
    }

    private async Task ReloadCurrentTaskStorageAsync(SettingsViewModel settings)
    {
        if (_storageFactory == null || _configuration == null || _mainWindowViewModel == null ||
            _taskSpaceCoordinator == null || settings.IsServerMode)
        {
            return;
        }

        await PrepareFileStoragePathAsync(settings.TaskStoragePath);
        try
        {
            await _taskSpaceCoordinator.ReconnectActiveAsync();
        }
        catch (TaskSpaceRecoveryException ex)
        {
            SetTaskSpaceRecoveryState(settings, ex);
            throw;
        }

        WireSettingsToActiveStorage(settings);
        settings.SetStorageConnectionState(SettingsConnectionState.Connected);
    }

    internal static async Task<bool> PrepareLocalStorageConnectionAsync(
        SettingsViewModel settings,
        IRemoteBackupService? backupService,
        string? currentLocalStoragePath,
        Func<string?, Task> prepareFileStoragePathAsync,
        Action<SettingsViewModel> enterConflictResolutionMode,
        Func<Action, Task>? runBackupOperationAsync = null)
    {
        await prepareFileStoragePathAsync(settings.TaskStoragePath);

        if (!ShouldAutoPullExistingTaskRepository(settings.TaskStoragePath, currentLocalStoragePath))
        {
            return true;
        }

        try
        {
            var pullOperation = () => backupService?.PullExistingRepository();
            if (runBackupOperationAsync != null)
            {
                await runBackupOperationAsync(pullOperation);
            }
            else
            {
                await Task.Run(pullOperation);
            }
        }
        catch (Exception ex)
        {
            settings.SetBackupConnectionState(BackupStatusState.Error, L10n.Format("PullErrorStatus", ex.Message));
            return true;
        }

        settings.ReloadGitMetadata();
        if (!settings.IsConflictResolutionMode)
        {
            return true;
        }

        settings.SetStorageConnectionState(SettingsConnectionState.Disconnected);
        enterConflictResolutionMode(settings);
        return false;
    }

    internal static bool ShouldAutoPullExistingTaskRepository(
        string? selectedLocalStoragePath,
        string? currentLocalStoragePath)
    {
        if (string.IsNullOrWhiteSpace(currentLocalStoragePath))
        {
            return true;
        }

        return !string.Equals(
            NormalizeLocalStoragePathForComparison(selectedLocalStoragePath, null),
            NormalizeLocalStoragePathForComparison(currentLocalStoragePath, null),
            GetPathComparison());
    }

    private string? GetCurrentLocalStoragePath()
    {
        return (_storageFactory?.SourceManager.ActiveStorage?.TaskTreeManager.Storage as FileStorage)?.Path;
    }

    private Task PrepareFileStoragePathAsync(string? path)
    {
        var prepareFileStoragePathAsync = _clientOptions.PrepareFileStoragePathAsync;
        return prepareFileStoragePathAsync == null
            ? Task.CompletedTask
            : prepareFileStoragePathAsync(path);
    }

    public static void ConfigureFileStoragePathPreparation(Func<string?, Task>? prepareFileStoragePathAsync)
    {
        _pendingClientOptions.PrepareFileStoragePathAsync = prepareFileStoragePathAsync;
        if (Current is App app)
        {
            app._clientOptions.PrepareFileStoragePathAsync = prepareFileStoragePathAsync;
        }
    }

    private static string NormalizeLocalStoragePathForComparison(string? path, string? defaultStoragePath)
    {
        var effectivePath = string.IsNullOrWhiteSpace(path)
            ? defaultStoragePath
            : path.Trim();

        if (string.IsNullOrWhiteSpace(effectivePath))
        {
            effectivePath = "Tasks";
        }

        try
        {
            return Path.GetFullPath(effectivePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return effectivePath;
        }
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
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

    private Task RunBackupOperationAsync(string operationName, Action operation) =>
        _taskSpaceOperationRunner.RunExclusiveAsync(
            operationName,
            _storageFactory?.SourceManager.ActiveSource?.Descriptor.Id,
            async context =>
            {
                using var backupScope =
                    (_backupService as ITaskSpaceBackupOperationScope)?.BeginTaskSpaceOperation(context);
                try
                {
                    await Task.Run(operation).ConfigureAwait(false);
                }
                finally
                {
                    PersistActiveProjectionCore(context);
                }
            });

    private Task<T> RunBackupOperationAsync<T>(string operationName, Func<T> operation) =>
        _taskSpaceOperationRunner.RunExclusiveAsync(
            operationName,
            _storageFactory?.SourceManager.ActiveSource?.Descriptor.Id,
            async context =>
            {
                using var backupScope =
                    (_backupService as ITaskSpaceBackupOperationScope)?.BeginTaskSpaceOperation(context);
                try
                {
                    return await Task.Run(operation).ConfigureAwait(false);
                }
                finally
                {
                    PersistActiveProjectionCore(context);
                }
            });

    private void PersistActiveProjectionCore(TaskSpaceOperationContext context)
    {
        var sourceId = _storageFactory?.SourceManager.ActiveSource?.Descriptor.Id;
        if (!string.IsNullOrWhiteSpace(sourceId) && _activeTaskSpaceConfiguration != null)
        {
            _activeTaskSpaceConfiguration.PersistCore(
                context,
                _activeTaskSpaceConfiguration.CaptureActiveProjection(sourceId));
        }
    }

    private void SetTaskSpaceRecoveryState(
        SettingsViewModel settings,
        TaskSpaceRecoveryException error)
    {
        settings.IsTaskSpaceRecoveryRequired = true;
        settings.TaskSpaceRecoveryMessage = L10n.Format(
            "TaskSpaceRecoveryRequired",
            error.ActivationError.Message,
            error.RestorationError.Message);
        settings.SetStorageConnectionState(SettingsConnectionState.Error);
        _notificationManager?.ErrorToast(settings.TaskSpaceRecoveryMessage);
    }

    private void ApplyStartupTaskSpaceRecoveryState(SettingsViewModel settings)
    {
        if (_startupTaskSpaceCatalogError == null)
        {
            return;
        }

        settings.IsTaskSpaceRecoveryRequired = true;
        settings.TaskSpaceRecoveryMessage = L10n.Format(
            "TaskSpaceStartupRecoveryRequired",
            string.Join(", ", _startupTaskSpaceCatalogError.ProblemSourceIds));
        settings.SetStorageConnectionState(SettingsConnectionState.Error);
    }

    private void WireTaskSpaceSettingsPersistenceState(SettingsViewModel settings)
    {
        if (_taskSpaceSettingsQueue == null)
        {
            return;
        }

        _taskSpaceSettingsQueue.StateChanged += (_, state) =>
        {
            _ = RunOnUiThreadAsync(() =>
                ApplyTaskSpaceSettingsPersistenceState(settings, state));
        };
        ApplyTaskSpaceSettingsPersistenceState(
            settings,
            new TaskSpaceSettingsPersistenceStateChangedEventArgs(
                _taskSpaceSettingsQueue.HasPendingChanges,
                _taskSpaceSettingsQueue.LastError));
    }

    private void ApplyTaskSpaceSettingsPersistenceState(
        SettingsViewModel settings,
        TaskSpaceSettingsPersistenceStateChangedEventArgs state)
    {
        settings.IsTaskSpaceSettingsPersistenceError = state.LastError != null;
        settings.IsTaskSpaceSettingsPersistenceStatusVisible =
            state.HasPendingChanges || state.LastError != null;
        settings.TaskSpaceSettingsPersistenceStatus = state.LastError != null
            ? L10n.Format("TaskSpaceSettingsSaveFailed", state.LastError.Message)
            : state.HasPendingChanges
                ? L10n.Get("TaskSpaceSettingsSaving")
                : string.Empty;

        if (state.LastError != null &&
            !ReferenceEquals(_lastReportedTaskSpaceSettingsPersistenceError, state.LastError))
        {
            _lastReportedTaskSpaceSettingsPersistenceError = state.LastError;
            _notificationManager?.ErrorToast(settings.TaskSpaceSettingsPersistenceStatus);
        }
        else if (state.LastError == null)
        {
            _lastReportedTaskSpaceSettingsPersistenceError = null;
        }
    }

    private void WireSettingsToActiveStorage(SettingsViewModel settings)
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

        var storage = _storageFactory?.SourceManager.ActiveStorage?.TaskTreeManager.Storage;
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

    private void WireActiveTaskContext()
    {
        var activeSource = _storageFactory?.SourceManager.ActiveSource;
        if (activeSource != null)
        {
            activeSource.TaskContext.MainWindow = _mainWindowViewModel;
        }
    }

    private Task MoveTaskTreeViaServiceAsync(
        TaskItemViewModel rootTask,
        ITaskStorage? sourceStorage,
        string destinationPath)
    {
        return _taskMoveService?.MoveTaskTreeToFileStorageAsync(rootTask, sourceStorage, destinationPath)
               ?? Task.CompletedTask;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        LibGit2Interop.DisableOwnerValidationOnAndroid();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += (_, args) =>
            {
                try
                {
                    _taskSpaceSettingsQueue?.DrainAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    args.Cancel = true;
                    Debug.WriteLine($"Task-space settings drain blocked shutdown: {ex}");
                    if (_mainWindowViewModel?.Settings is { } settings)
                    {
                        ApplyTaskSpaceSettingsPersistenceState(
                            settings,
                            new TaskSpaceSettingsPersistenceStateChangedEventArgs(
                                hasPendingChanges: true,
                                _taskSpaceSettingsQueue?.LastError ?? ex));
                    }
                }
            };
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
                ApplyAutomationWindowSize(window);
                ApplyAutomationWindowPlacement(window);

                desktop.MainWindow = window;

                // Когда окно загрузится — вызовем инициализацию
                window.Opened += (_, __) =>
                {
                    ApplyAutomationWindowPlacement(window);
                    _ = InitializeStartupViewModelAsync(vm);
                };
            }
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            var vm = GetMainWindowViewModel();
            singleViewPlatform.MainView = CreateSingleViewMainView(vm);
            _ = InitializeStartupViewModelAsync(vm);
        }

        base.OnFrameworkInitializationCompleted();
    }

    internal MainScreen CreateSingleViewMainView(MainWindowViewModel vm)
    {
        return new MainScreen
        {
            DataContext = vm,
        };
    }

    internal async Task InitializeStartupViewModelAsync(MainWindowViewModel vm)
    {
        try
        {
            ApplyAutomationTaskWrapperDefaults();
            if (_startupTaskSpaceCatalogError != null)
            {
                vm.ClearTaskSpaceSurface();
                ApplyStartupTaskSpaceRecoveryState(vm.Settings);
            }
            else if (!vm.IsInitialized || vm.taskRepository == null)
            {
                await vm.Connect();
            }

            ApplyAutomationStartupState(vm);
            if (vm.Settings.IsConflictResolutionMode)
            {
                EnterConflictResolutionMode(vm.Settings);
            }
        }
        catch (Exception ex)
        {
            vm.Settings.SetStorageConnectionState(SettingsConnectionState.Error);
            var hint = OperatingSystem.IsAndroid() ? L10n.Get("AndroidAllFilesHint") : string.Empty;
            vm.ManagerWrapper?.ErrorToast(L10n.Format("ConnectStorageFailed", ex.Message, hint));
        }

        _startupUpdateSettings = vm.Settings;
        RequestStartupUpdateCheck(vm.Settings);
    }

    private static void ApplyAutomationStartupState(MainWindowViewModel vm)
    {
        ApplyAutomationWindowTitle(vm);

        var openDetails = Environment.GetEnvironmentVariable(AutomationOpenDetailsEnvironmentVariable);
        if (bool.TryParse(openDetails, out var shouldOpenDetails) && shouldOpenDetails)
        {
            vm.DetailsAreOpen = true;
        }

        var openedTaskIds = Environment
            .GetEnvironmentVariable(AutomationOpenedTaskIdsEnvironmentVariable)?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (openedTaskIds is { Length: > 0 })
        {
            foreach (var openedTaskId in openedTaskIds)
            {
                SelectAutomationTask(vm, openedTaskId);
            }

            ApplyAutomationTreeExpansion(vm);
            return;
        }

        var taskId = Environment.GetEnvironmentVariable(AutomationCurrentTaskIdEnvironmentVariable);
        SelectAutomationTask(vm, taskId);
        ApplyAutomationTreeExpansion(vm);
    }

    private static void ApplyAutomationTaskWrapperDefaults()
    {
        TaskWrapperViewModel.DefaultIsExpanded = ShouldExpandAutomationTaskTrees();
    }

    private static void ApplyAutomationWindowTitle(MainWindowViewModel vm)
    {
        var title = Environment.GetEnvironmentVariable(AutomationWindowTitleEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(title))
        {
            vm.Title = title;
        }
    }

    private static void ApplyAutomationWindowSize(MainWindow window)
    {
        if (TryReadAutomationWindowDimension(AutomationWindowWidthEnvironmentVariable, out var width))
        {
            window.Width = width;
        }

        if (TryReadAutomationWindowDimension(AutomationWindowHeightEnvironmentVariable, out var height))
        {
            window.Height = height;
        }
    }

    private static bool TryReadAutomationWindowDimension(string environmentVariable, out double dimension)
    {
        var configured = Environment.GetEnvironmentVariable(environmentVariable);
        return double.TryParse(
                   configured,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out dimension) &&
               double.IsFinite(dimension) &&
               dimension > 0;
    }

    private static void ApplyAutomationWindowPlacement(MainWindow window)
    {
        var configured = Environment.GetEnvironmentVariable(AutomationDesktopMonitorEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        var screens = window.Screens.All
            .OrderByDescending(screen => screen.IsPrimary)
            .ThenBy(screen => screen.Bounds.Y)
            .ThenBy(screen => screen.Bounds.X)
            .ThenBy(screen => screen.Bounds.Bottom)
            .ThenBy(screen => screen.Bounds.Right)
            .ToArray();
        if (screens.Length == 0)
        {
            return;
        }

        var selector = configured.Trim();
        var target = selector switch
        {
            var value when string.Equals(value, "primary", StringComparison.OrdinalIgnoreCase) =>
                screens.FirstOrDefault(screen => screen.IsPrimary),
            var value when string.Equals(value, "right", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(value, "last", StringComparison.OrdinalIgnoreCase) =>
                screens[^1],
            var value when int.TryParse(value, out var index) && index >= 0 && index < screens.Length =>
                screens[index],
            var value => screens.FirstOrDefault(
                screen => string.Equals(screen.DisplayName, value, StringComparison.OrdinalIgnoreCase))
        };
        if (target == null)
        {
            return;
        }

        var area = target.WorkingArea;
        var width = double.IsFinite(window.Width) && window.Width > 0 ? window.Width : 1000;
        var height = double.IsFinite(window.Height) && window.Height > 0 ? window.Height : 500;
        var pixelWidth = Math.Min(area.Width, (int)Math.Ceiling(width * target.Scaling));
        var pixelHeight = Math.Min(area.Height, (int)Math.Ceiling(height * target.Scaling));

        window.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.Manual;
        window.Position = new PixelPoint(
            area.X + Math.Max(0, (area.Width - pixelWidth) / 2),
            area.Y + Math.Max(0, (area.Height - pixelHeight) / 2));
    }

    private static void ApplyAutomationTreeExpansion(MainWindowViewModel vm)
    {
        if (!ShouldExpandAutomationTaskTrees())
        {
            return;
        }

        Dispatcher.UIThread.Post(() => ExpandAllTaskTrees(vm), DispatcherPriority.Background);
    }

    private static bool ShouldExpandAutomationTaskTrees()
    {
        var expandAll = Environment.GetEnvironmentVariable(AutomationExpandAllTaskTreesEnvironmentVariable);
        return bool.TryParse(expandAll, out var shouldExpandAll) && shouldExpandAll;
    }

    private static void ExpandAllTaskTrees(MainWindowViewModel vm)
    {
        var allTasksMode = vm.AllTasksMode;
        var unlockedMode = vm.UnlockedMode;
        var completedMode = vm.CompletedMode;
        var archivedMode = vm.ArchivedMode;
        var graphMode = vm.GraphMode;
        var settingsMode = vm.SettingsMode;
        var lastCreatedMode = vm.LastCreatedMode;
        var lastUpdatedMode = vm.LastUpdatedMode;
        var lastOpenedMode = vm.LastOpenedMode;

        try
        {
            vm.ExpandAllNodes(vm.CurrentAllTasksItems);
            ExpandCurrentTaskRelationTrees(vm);

            vm.LastCreatedMode = true;
            vm.ExpandAllNodes(vm.LastCreatedItems);

            vm.LastUpdatedMode = true;
            vm.ExpandAllNodes(vm.LastUpdatedItems);

            vm.UnlockedMode = true;
            vm.ExpandAllNodes(vm.UnlockedItems);

            vm.CompletedMode = true;
            vm.ExpandAllNodes(vm.CompletedItems);

            vm.ArchivedMode = true;
            vm.ExpandAllNodes(vm.ArchivedItems);

            vm.LastOpenedMode = true;
            vm.ExpandAllNodes(vm.LastOpenedItems);
        }
        finally
        {
            vm.AllTasksMode = allTasksMode;
            vm.UnlockedMode = unlockedMode;
            vm.CompletedMode = completedMode;
            vm.ArchivedMode = archivedMode;
            vm.GraphMode = graphMode;
            vm.SettingsMode = settingsMode;
            vm.LastCreatedMode = lastCreatedMode;
            vm.LastUpdatedMode = lastUpdatedMode;
            vm.LastOpenedMode = lastOpenedMode;
            vm.SelectCurrentTask();
        }
    }

    private static void ExpandCurrentTaskRelationTrees(MainWindowViewModel vm)
    {
        vm.ExpandNodeAndDescendants(vm.CurrentItemContains);
        vm.ExpandNodeAndDescendants(vm.CurrentItemParents);
        vm.ExpandNodeAndDescendants(vm.CurrentItemBlocks);
        vm.ExpandNodeAndDescendants(vm.CurrentItemBlockedBy);
    }

    private static void SelectAutomationTask(MainWindowViewModel vm, string? taskId)
    {
        if (!string.IsNullOrWhiteSpace(taskId) && vm.taskRepository != null)
        {
            var lookup = vm.taskRepository.Tasks.Lookup(taskId);
            if (lookup.HasValue)
            {
                vm.AllTasksMode = true;
                vm.CurrentTaskItem = lookup.Value;
                vm.SelectCurrentTask();
            }
        }
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        var bindingPluginsType = typeof(AvaloniaObject).Assembly.GetType("Avalonia.Data.Core.Plugins.BindingPlugins");
        var dataValidators = bindingPluginsType?
            .GetProperty("DataValidators", BindingFlags.Public | BindingFlags.Static)?
            .GetValue(null) as System.Collections.IList;

        if (dataValidators == null)
        {
            return;
        }

        var dataValidationPluginsToRemove = dataValidators
            .Cast<object>()
            .OfType<DataAnnotationsValidationPlugin>()
            .ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            dataValidators.Remove(plugin);
        }
    }

    public App()
    {
        DataContext = new ApplicationViewModel();
        if (!string.IsNullOrWhiteSpace(_pendingConfigPath))
        {
            InitializeRuntime(_pendingConfigPath, _pendingClientOptions);
        }
    }

    internal void ConfigureRuntimeForTests(
        IConfiguration configuration,
        IRemoteBackupService? backupService,
        ITaskStorageFactory storageFactory,
        INotificationManagerWrapper? notificationManager = null)
    {
        _configuration = configuration;
        _backupService = backupService;
        _storageFactory = storageFactory;
        _taskMoveService = new TaskMoveService(storageFactory);
        _notificationManager = notificationManager;
        _clientOptions = new UnlimotionClientOptions();
    }

    public static void ConfigureUpdateService(IApplicationUpdateService? updateService)
    {
        _pendingUpdateService = updateService;
        if (Current is App app)
        {
            app.ConfigureUpdateServiceInstance(updateService);
        }
    }

    private void ConfigureUpdateServiceInstance(IApplicationUpdateService? updateService)
    {
        _applicationUpdateService = updateService;
        var mainSettings = _mainWindowViewModel?.Settings;
        var startupSettings = _startupUpdateSettings;

        mainSettings?.ConfigureUpdateService(updateService);
        if (mainSettings != null)
        {
            _startupUpdateSettings = mainSettings;
        }

        if (startupSettings != null && !ReferenceEquals(startupSettings, mainSettings))
        {
            startupSettings.ConfigureUpdateService(updateService);
        }

        var settings = mainSettings ?? startupSettings;
        if (settings != null)
        {
            RescheduleAutomaticUpdateTimer(settings);
            if (_startupUpdateCheckPending && updateService?.IsSupported == true)
            {
                RequestStartupUpdateCheck(settings);
            }
        }
    }

    private void RequestStartupUpdateCheck(SettingsViewModel settings)
    {
        if (_applicationUpdateService?.IsSupported != true)
        {
            _startupUpdateCheckPending = true;
            return;
        }

        _startupUpdateCheckPending = false;
        _ = CheckForUpdatesOnStartupAsync(settings);
    }

    private async Task CheckForUpdatesOnStartupAsync(SettingsViewModel settings)
    {
        if (_applicationUpdateService?.IsSupported != true)
        {
            _startupUpdateCheckPending = true;
            return;
        }

        await RunAutomaticUpdateCheckAsync(settings);
    }

    internal async Task RunAutomaticUpdateCheckAsync(SettingsViewModel settings)
    {
        if (!settings.UpdateAutoCheckEnabled ||
            settings.IsUpdateBusy ||
            settings.UpdateState is ApplicationUpdateState.Unsupported
                or ApplicationUpdateState.ReadyToApply
                or ApplicationUpdateState.Applying)
        {
            return;
        }

        if (_isAutomaticUpdateCheckRunning)
        {
            return;
        }

        _isAutomaticUpdateCheckRunning = true;

        try
        {
            await settings.CheckForUpdatesAsync(silent: true);

            if (settings.UpdateState != ApplicationUpdateState.UpdateAvailable)
            {
                return;
            }

            await settings.DownloadUpdateAsync();

            if (settings.UpdateState != ApplicationUpdateState.ReadyToApply)
            {
                return;
            }

            _notificationManager?.Ask(
                L10n.Get("UpdateReadyHeader"),
                L10n.Format("UpdateReadyMessage", settings.AvailableUpdateVersion ?? L10n.Get("Unknown")),
                () => _ = settings.ApplyUpdateAsync());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Updates] Automatic update check failed: {ex}");
        }
        finally
        {
            _isAutomaticUpdateCheckRunning = false;
        }
    }

    private void SetupAutomaticUpdateTimer(SettingsViewModel settings)
    {
        AttachAutomaticUpdateSettingsSubscriptions(settings);
        RescheduleAutomaticUpdateTimer(settings);
    }

    private void AttachAutomaticUpdateSettingsSubscriptions(SettingsViewModel settings)
    {
        if (ReferenceEquals(_automaticUpdateTimerSettings, settings))
        {
            return;
        }

        _updateAutoCheckEnabledSubscription?.Dispose();
        _updateCheckIntervalSubscription?.Dispose();
        _updateStateSubscription?.Dispose();

        _automaticUpdateTimerSettings = settings;
        _updateAutoCheckEnabledSubscription = settings
            .ObservableForProperty(m => m.UpdateAutoCheckEnabled, false, true)
            .Subscribe(_ => RescheduleAutomaticUpdateTimer(settings));
        _updateCheckIntervalSubscription = settings
            .ObservableForProperty(m => m.UpdateCheckInterval, false, true)
            .Subscribe(_ => RescheduleAutomaticUpdateTimer(settings));
        _updateStateSubscription = settings
            .ObservableForProperty(m => m.UpdateState, false, true)
            .Subscribe(_ => RescheduleAutomaticUpdateTimer(settings));
    }

    private void RescheduleAutomaticUpdateTimer(SettingsViewModel settings)
    {
        if (!settings.UpdateAutoCheckEnabled ||
            settings.UpdateState is ApplicationUpdateState.Unsupported
                or ApplicationUpdateState.ReadyToApply
                or ApplicationUpdateState.Applying)
        {
            _automaticUpdateTimer?.Stop();
            return;
        }

        var interval = settings.UpdateCheckInterval;
        if (interval <= TimeSpan.Zero)
        {
            _automaticUpdateTimer?.Stop();
            return;
        }

        EnsureAutomaticUpdateTimer();
        _automaticUpdateTimer!.Stop();
        _automaticUpdateTimer.Interval = interval;
        _automaticUpdateTimer.Start();
    }

    private void EnsureAutomaticUpdateTimer()
    {
        if (_automaticUpdateTimer != null)
        {
            return;
        }

        _automaticUpdateTimer = new DispatcherTimer();
        _automaticUpdateTimer.Tick += OnAutomaticUpdateTimerTick;
    }

    private void OnAutomaticUpdateTimerTick(object? sender, EventArgs e)
    {
        var settings = _automaticUpdateTimerSettings ?? _mainWindowViewModel?.Settings;
        if (settings == null)
        {
            return;
        }

        _ = RunAutomaticUpdateCheckAsync(settings);
    }

    private static readonly bool ShouldLogStartup = false;

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

    private void ApplyLocalizedResources()
    {
        foreach (var key in LocalizationService.Current.GetResourceKeys(CultureInfo.InvariantCulture))
        {
            Resources[key] = L10n.Get(key);
        }
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

    private async Task PauseTaskSpaceSchedulerAsync()
    {
        if (_scheduler != null)
        {
            await _scheduler.PauseAll().ConfigureAwait(false);
        }
    }

    private async Task ApplyActiveTaskSpaceSchedulerAsync(TaskSourceRuntime? runtime)
    {
        EnsureScheduler();
        if (_scheduler == null)
        {
            return;
        }

        await _scheduler.PauseAll().ConfigureAwait(false);
        var git = _configuration?.Get<GitSettings>("Git") ?? new GitSettings();
        if (runtime?.Storage.TaskTreeManager.Storage is not FileStorage || !git.BackupEnabled)
        {
            return;
        }

        var pullJobKey = new JobKey("GitPullJob", "Git");
        if (!await _scheduler.CheckExists(pullJobKey).ConfigureAwait(false))
        {
            var pullJob = JobBuilder.Create<GitPullJob>()
                .WithIdentity(pullJobKey)
                .Build();
            await _scheduler.ScheduleJob(
                pullJob,
                GenerateTriggerBySecondsInterval(
                    "PullTrigger",
                    "GitPullJob",
                    Math.Max(1, git.PullIntervalSeconds))).ConfigureAwait(false);
        }
        else
        {
            await _scheduler.RescheduleJob(
                new TriggerKey("PullTrigger", "GitPullJob"),
                GenerateTriggerBySecondsInterval(
                    "PullTrigger",
                    "GitPullJob",
                    Math.Max(1, git.PullIntervalSeconds))).ConfigureAwait(false);
        }

        var pushJobKey = new JobKey("GitPushJob", "Git");
        if (!await _scheduler.CheckExists(pushJobKey).ConfigureAwait(false))
        {
            var pushJob = JobBuilder.Create<GitPushJob>()
                .WithIdentity(pushJobKey)
                .Build();
            await _scheduler.ScheduleJob(
                pushJob,
                GenerateTriggerBySecondsInterval(
                    "PushTrigger",
                    "GitPushJob",
                    Math.Max(1, git.PushIntervalSeconds))).ConfigureAwait(false);
        }
        else
        {
            await _scheduler.RescheduleJob(
                new TriggerKey("PushTrigger", "GitPushJob"),
                GenerateTriggerBySecondsInterval(
                    "PushTrigger",
                    "GitPushJob",
                    Math.Max(1, git.PushIntervalSeconds))).ConfigureAwait(false);
        }

        if (!_scheduler.IsStarted)
        {
            await _scheduler.Start().ConfigureAwait(false);
        }

        await _scheduler.ResumeAll().ConfigureAwait(false);
    }

    private void EnsureScheduler()
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
        _scheduler.JobFactory = new DependencyInjectionJobFactory(
            _configuration,
            _backupService,
            _taskSpaceOperationRunner,
            () => _storageFactory?.SourceManager.ActiveSource?.Descriptor.Id,
            _activeTaskSpaceConfiguration);
        Log("[App.Init] Scheduler created lazily");
        Log("[App.Init] Scheduler job factory set");
    }

    public static void Init(string configPath, UnlimotionClientOptions? clientOptions = null)
    {
        var effectiveOptions = clientOptions ?? new UnlimotionClientOptions();
        _pendingConfigPath = configPath;
        _pendingClientOptions = effectiveOptions;
        if (Current is App app)
        {
            app.InitializeRuntime(configPath, effectiveOptions);
        }
    }

    private void InitializeRuntime(string configPath, UnlimotionClientOptions clientOptions)
    {
        try
        {
            _startupTaskSpaceCatalogError = null;
            _clientOptions = clientOptions;
            _applicationUpdateService = _pendingUpdateService;
            Log($"[App.Init] Starting with configPath: {configPath}");
            _configPath = configPath;

            // Create configuration
            // This provider persists every Set via ReadAllText + File.WriteAllText.
            // Watching its own writes can reload a transient file between the staged
            // task-space mutation writes, so the application owns a stable in-memory
            // view and persists changes explicitly instead.
            _configuration = WritableJsonConfigurationFabric.Create(
                configPath,
                reloadOnChange: false);
            Log("[App.Init] Configuration created");

            LocalizationService.Current = new LocalizationService(new DefaultLocalizationSystemCultureProvider());
            L10n.SetLanguage(_configuration
                .GetSection(AppearanceSettings.SectionName)
                .GetSection(AppearanceSettings.LanguageKey)
                .Get<string>());
            Log("[App.Init] Localization initialized");

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
            }
            Log($"[App.Init] Storage settings: Path={taskStorageSettings.Path}, IsServerMode={taskStorageSettings.IsServerMode}");

            var isServerMode = taskStorageSettings.IsServerMode;

            // Create storage factory
            _clientOptions.DefaultTaskStoragePath = string.IsNullOrWhiteSpace(taskStorageSettings.Path)
                ? ResolveDefaultTaskStoragePath()
                : taskStorageSettings.Path;
            Log($"[App.Init] Creating storage factory with DefaultStoragePath={ResolveDefaultTaskStoragePath()}");
            _storageFactory = new TaskStorageFactory(
                _configuration,
                _mapper,
                _notificationManager,
                ResolveDefaultTaskStoragePath,
                _taskSpaceOperationRunner);
            _taskMoveService = new TaskMoveService(_storageFactory);
            _activeTaskSpaceConfiguration = new ActiveTaskSpaceConfiguration(
                _configuration,
                _storageFactory.SourceManager,
                _taskSpaceOperationRunner);
            _taskSpaceSettingsQueue = new TaskSpaceSettingsPersistenceQueue(
                _activeTaskSpaceConfiguration,
                _taskSpaceOperationRunner,
                async draft =>
                {
                    if (string.Equals(
                            _storageFactory.SourceManager.ActiveSource?.Descriptor.Id,
                            draft.SourceId,
                            StringComparison.Ordinal))
                    {
                        var activeRuntime = _storageFactory.SourceManager.ActiveSource;
                        if (activeRuntime == null ||
                            activeRuntime.Descriptor.Kind !=
                            (draft.Storage.IsServerMode
                                ? TaskSourceKind.Server
                                : TaskSourceKind.File) ||
                            !string.Equals(
                                activeRuntime.Descriptor.Path,
                                draft.Storage.Path,
                                StringComparison.Ordinal) ||
                            !string.Equals(
                                activeRuntime.Descriptor.Url,
                                draft.Storage.URL,
                                StringComparison.Ordinal))
                        {
                            await PauseTaskSpaceSchedulerAsync().ConfigureAwait(false);
                            return;
                        }

                        await ApplyActiveTaskSpaceSchedulerAsync(
                                activeRuntime)
                            .ConfigureAwait(false);
                    }
                });
            Log("[App.Init] Storage factory created");

            // Create backup service
            _backupService = new BackupViaGitService(
                _configuration,
                _notificationManager,
                _storageFactory,
                _clientOptions.GetAbsolutePath ?? GetDefaultAbsolutePath,
                _taskSpaceOperationRunner,
                () => _storageFactory?.SourceManager.ActiveSource?.Descriptor.Id,
                _activeTaskSpaceConfiguration);
            Log("[App.Init] Backup service created");

            // Create initial storage
            Log($"[App.Init] Creating initial storage, isServerMode={isServerMode}");
            _storageFactory.CreateConfiguredStorage();
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
            var taskRepository = _storageFactory.SourceManager.ActiveStorage;
            if (taskRepository?.TaskTreeManager.Storage is FileStorage)
            {
                taskRepository.Initiated += async (_, _) =>
                {
                    await ApplyActiveTaskSpaceSchedulerAsync(
                        _storageFactory.SourceManager.ActiveSource).ConfigureAwait(false);
                };
            }
            Log("[App.Init] Completed successfully");
        }
        catch (TaskSpaceCatalogException ex)
        {
            _startupTaskSpaceCatalogError = ex;
            _storageFactory = null;
            _taskMoveService = null;
            _backupService = null;
            _activeTaskSpaceConfiguration = null;
            _taskSpaceSettingsQueue = null;
            _taskSpaceCoordinator = null;
            Log($"[App.Init] TASK SPACE RECOVERY: {ex}");
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

    private string ResolveDefaultTaskStoragePath()
    {
        if (!string.IsNullOrWhiteSpace(_clientOptions.DefaultTaskStoragePath))
        {
            return _clientOptions.DefaultTaskStoragePath;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Unlimotion",
            "Tasks");
    }

    private string ResolveLocalFileStoragePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? ResolveDefaultTaskStoragePath() : path;

    private string ResolveDefaultLocalFileStoragePath()
    {
        var defaultSourcePath = _storageFactory?.SourceManager.ConfiguredSources
            .FirstOrDefault(source =>
                string.Equals(source.Id, TaskSourceDescriptor.DefaultSourceId, StringComparison.Ordinal))
            ?.Path;
        if (!string.IsNullOrWhiteSpace(defaultSourcePath))
        {
            return ResolveLocalFileStoragePath(defaultSourcePath);
        }

        var legacyPath = _configuration?.Get<TaskStorageSettings>("TaskStorage")?.Path;
        return ResolveLocalFileStoragePath(legacyPath);
    }

    private static string GetDefaultAbsolutePath(string path) =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Unlimotion",
            path);

    private static void HandleReactiveException(Exception ex)
    {
        var notificationManager = (Current as App)?._notificationManager;
        if (notificationManager != null)
        {
            notificationManager.ErrorToast(L10n.Format("ReactiveUnhandledError", ex.Message));
        }
        else
        {
            Debug.WriteLine($"[ReactiveUI] {ex}");
        }
    }
}
