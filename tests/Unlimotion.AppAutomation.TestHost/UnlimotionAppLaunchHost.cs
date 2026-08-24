using AppAutomation.Session.Contracts;
using AppAutomation.TestHost.Avalonia;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using ReactiveUI;
using ReactiveUI.Avalonia;
using ReactiveUI.Builder;
using System.Runtime.ExceptionServices;
using Unlimotion;
using Unlimotion.Services;
using Unlimotion.ViewModel;
using Unlimotion.Views;
using WritableJsonConfiguration;

namespace Unlimotion.AppAutomation.TestHost;

public static class UnlimotionAppLaunchHost
{
    public const string AutomationCurrentTaskIdEnvironmentVariable = "UNLIMOTION_AUTOMATION_CURRENT_TASK_ID";
    public const string AutomationOpenDetailsEnvironmentVariable = "UNLIMOTION_AUTOMATION_OPEN_DETAILS";
    public const string AutomationOpenedTaskIdsEnvironmentVariable = "UNLIMOTION_AUTOMATION_OPENED_TASK_IDS";
    public const string AutomationWindowTitleEnvironmentVariable = "UNLIMOTION_AUTOMATION_WINDOW_TITLE";
    public const string AutomationExpandAllTaskTreesEnvironmentVariable = "UNLIMOTION_AUTOMATION_EXPAND_ALL_TASK_TREES";
    public const string AutomationDesktopMonitorEnvironmentVariable = "UNLIMOTION_AUTOMATION_DESKTOP_MONITOR";
    public const string AutomationWindowWidthEnvironmentVariable = "UNLIMOTION_AUTOMATION_WINDOW_WIDTH";
    public const string AutomationWindowHeightEnvironmentVariable = "UNLIMOTION_AUTOMATION_WINDOW_HEIGHT";
    public const string CurrentTaskId = UnlimotionAutomationScenarioData.SmokeCurrentTaskId;
    public const string CurrentTaskTitle = UnlimotionAutomationScenarioData.SmokeCurrentTaskTitle;

    public static Type AvaloniaAppType => typeof(App);
    private static int _reactiveUiInitialized;

    private static readonly AvaloniaDesktopAppDescriptor DesktopApp = new(
        solutionFileNames:
        [
            "src\\Unlimotion.sln"
        ],
        desktopProjectRelativePaths:
        [
            "src\\Unlimotion.Desktop\\Unlimotion.Desktop.csproj"
        ],
        desktopTargetFramework: "net10.0",
        executableName: "Unlimotion.Desktop.exe");

    public static DesktopAppLaunchOptions CreateDesktopLaunchOptions(
        UnlimotionAutomationScenario scenario = UnlimotionAutomationScenario.Smoke,
        string? language = null,
        string? currentTaskId = null,
        string? buildConfiguration = null,
        bool buildBeforeLaunch = true,
        bool buildOncePerProcess = true,
        TimeSpan? buildTimeout = null,
        TimeSpan? mainWindowTimeout = null,
        TimeSpan? pollInterval = null,
        string? theme = null,
        DesktopWindowPlacement? windowPlacement = null,
        Action<string>? feedVaultPrepared = null)
    {
        var launchData = UnlimotionAutomationLaunchData.Create(scenario, language, currentTaskId, theme);
        var environmentVariables = CreateEnvironmentVariables(launchData);

        try
        {
            if (scenario == UnlimotionAutomationScenario.Feed)
            {
                feedVaultPrepared?.Invoke(launchData.VaultPath);
            }

            var launchOptions = AvaloniaDesktopLaunchHost.CreateLaunchOptions(
                DesktopApp,
                new AvaloniaDesktopLaunchOptions
                {
                    BuildConfiguration = buildConfiguration ?? BuildConfigurationDefaults.ForAssembly(typeof(UnlimotionAppLaunchHost).Assembly),
                    BuildBeforeLaunch = buildBeforeLaunch,
                    BuildOncePerProcess = buildOncePerProcess,
                    BuildTimeout = buildTimeout ?? TimeSpan.FromMinutes(5),
                    MainWindowTimeout = mainWindowTimeout ?? TimeSpan.FromSeconds(30),
                    PollInterval = pollInterval ?? TimeSpan.FromMilliseconds(200),
                    UseIsolatedBuildOutput = buildBeforeLaunch,
                    WindowPlacement = ResolveDesktopWindowPlacement(scenario, windowPlacement),
                    Arguments = [$"--config={launchData.ConfigPath}"],
                    EnvironmentVariables = environmentVariables
                },
                launchData.RepositoryRoot);

            return WithCleanup(launchOptions, launchData);
        }
        catch
        {
            launchData.Dispose();
            throw;
        }
    }

    public static HeadlessAppLaunchOptions CreateHeadlessLaunchOptions(
        UnlimotionAutomationScenario scenario = UnlimotionAutomationScenario.Smoke,
        string? language = null,
        Action<MainWindowViewModel>? afterViewModelPrepared = null,
        string? currentTaskId = null,
        string? theme = null,
        Action<string>? feedVaultPrepared = null,
        Action<MainWindowViewModel>? beforeViewModelInitialized = null,
        Func<Func<MainWindowViewModel>, MainWindowViewModel>? viewModelFactoryDispatcher = null,
        Action<Window>? headlessWindowCleanup = null)
    {
        var launchData = UnlimotionAutomationLaunchData.Create(scenario, language, currentTaskId, theme);
        if (scenario == UnlimotionAutomationScenario.Feed)
        {
            feedVaultPrepared?.Invoke(launchData.VaultPath);
        }

        var previousDefaultIsExpanded = TaskWrapperViewModel.DefaultIsExpanded;
        var lifetime = new HeadlessSessionLifetime(launchData, previousDefaultIsExpanded);
        MainWindowViewModel? vm = null;
        Window? window = null;
        var disposeState = 0;

        return new HeadlessAppLaunchOptions
        {
            BeforeLaunchAsync = async cancellationToken =>
            {
                async Task PrepareViewModelAsync()
                {
                    if (launchData.ExpandAllTaskTrees)
                    {
                        TaskWrapperViewModel.DefaultIsExpanded = true;
                    }

                    vm = viewModelFactoryDispatcher is null
                        ? CreateHeadlessViewModel(launchData, lifetime)
                        : viewModelFactoryDispatcher(() => CreateHeadlessViewModel(launchData, lifetime));
                    beforeViewModelInitialized?.Invoke(vm);
                    await vm.Connect();

                    if (scenario == UnlimotionAutomationScenario.Feed)
                    {
                        vm.Feed.IsExternalVaultSupported = true;
                        vm.Feed.TaskOwner = vm;
                        vm.Feed.TaskResolver = taskId => FindTaskById(vm, taskId);
                        await vm.Feed.InitializeVaultAsync(launchData.VaultPath);
                    }

                    if (!IsTaskSpaceRecoveryScenario(scenario))
                    {
                        SelectAutomationTask(vm, launchData);
                    }
                    ApplyAutomationWindowTitle(vm, launchData);
                    afterViewModelPrepared?.Invoke(vm);
                }

                if (scenario == UnlimotionAutomationScenario.StatusContract)
                {
                    // Status commands capture their cache context on first use. Prepare this
                    // synthetic ViewModel without inheriting TUnit's context so the real UI
                    // command can capture the Headless dispatcher context deterministically.
                    await Task.Run(PrepareViewModelAsync, cancellationToken);
                }
                else
                {
                    await PrepareViewModelAsync();
                }
            },
            CreateMainWindow = () =>
            {
                ApplyAutomationTheme(theme);
                window = new MainWindow
                {
                    Width = scenario == UnlimotionAutomationScenario.StatusContract ? 1280 : 1200,
                    Height = 800,
                    DataContext = vm ?? throw new InvalidOperationException("Headless ViewModel was not initialized.")
                };
                window.Opened += (_, __) => ApplyAutomationTreeExpansion(vm, launchData);
                return window;
            },
            DisposeCallback = () =>
            {
                if (Interlocked.Exchange(ref disposeState, 1) != 0)
                {
                    return;
                }

                try
                {
                    if (window is not null)
                    {
                        headlessWindowCleanup?.Invoke(window);
                    }
                }
                finally
                {
                    lifetime.Dispose();
                }
            }
        };
    }

    private static void ApplyAutomationTheme(string? theme)
    {
        if (Application.Current is not { } application || string.IsNullOrWhiteSpace(theme))
        {
            return;
        }

        application.RequestedThemeVariant = string.Equals(
            theme,
            AppearanceSettings.DarkTheme,
            StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
    }

    public static string GetCurrentTaskId(UnlimotionAutomationScenario scenario = UnlimotionAutomationScenario.Smoke)
    {
        return UnlimotionAutomationScenarioData.GetCurrentTaskId(scenario);
    }

    public static string GetCurrentTaskId(
        UnlimotionAutomationScenario scenario,
        string? language)
    {
        return UnlimotionAutomationScenarioData.GetCurrentTaskId(scenario, language);
    }

    public static string GetCurrentTaskTitle(UnlimotionAutomationScenario scenario = UnlimotionAutomationScenario.Smoke)
    {
        return UnlimotionAutomationScenarioData.GetCurrentTaskTitle(scenario);
    }

    public static string GetCurrentTaskTitle(
        UnlimotionAutomationScenario scenario,
        string? language)
    {
        return UnlimotionAutomationScenarioData.GetCurrentTaskTitle(scenario, language);
    }

    public static string? GetWindowTitle(
        UnlimotionAutomationScenario scenario,
        string? language)
    {
        return UnlimotionAutomationScenarioData.GetWindowTitle(scenario, language);
    }

    private static DesktopAppLaunchOptions WithCleanup(
        DesktopAppLaunchOptions launchOptions,
        IDisposable launchData)
    {
        return new DesktopAppLaunchOptions
        {
            ExecutablePath = launchOptions.ExecutablePath,
            WorkingDirectory = launchOptions.WorkingDirectory,
            Arguments = launchOptions.Arguments,
            EnvironmentVariables = launchOptions.EnvironmentVariables,
            MainWindowTimeout = launchOptions.MainWindowTimeout,
            PollInterval = launchOptions.PollInterval,
            WindowPlacement = launchOptions.WindowPlacement,
            DisposeCallback = () =>
            {
                try
                {
                    launchOptions.DisposeCallback?.Invoke();
                }
                finally
                {
                    launchData.Dispose();
                }
            }
        };
    }

    private static MainWindowViewModel CreateHeadlessViewModel(
        UnlimotionAutomationLaunchData launchData,
        HeadlessSessionLifetime lifetime)
    {
        EnsureReactiveUiInitialized();

        var configuration = lifetime.RegisterConfiguration(
            WritableJsonConfigurationFabric.Create(launchData.ConfigPath, reloadOnChange: false));
        var mapper = AppModelMapping.ConfigureMapping();
        var notificationManager = new AutomationNotificationManager();
        TaskStorageFactory storageFactory;
        try
        {
            storageFactory = new TaskStorageFactory(
                configuration,
                mapper,
                notificationManager,
                () => launchData.TasksPath);
        }
        catch (TaskSpaceCatalogException error)
        {
            var recoverySettings = new SettingsViewModel(configuration);
            recoverySettings.IsTaskSpaceRecoveryRequired = true;
            recoverySettings.TaskSpaceRecoveryMessage =
                Unlimotion.ViewModel.Localization.Localization.Format(
                "TaskSpaceStartupRecoveryRequired",
                string.Join(", ", error.ProblemSourceIds));
            recoverySettings.SetStorageConnectionState(SettingsConnectionState.Error);
            return lifetime.RegisterViewModel(
                new MainWindowViewModel(
                    new AppNameDefinitionService(),
                    notificationManager,
                    configuration,
                    () => null,
                    recoverySettings,
                    new GraphViewModel(),
                    TaskTreeExpansionStateStore.GetDefaultPath(launchData.ConfigPath)));
        }

        lifetime.RegisterStorage(storageFactory.CreateConfiguredStorage());

        var backupService = new BackupViaGitService(configuration, notificationManager, storageFactory);
        var settingsViewModel = new SettingsViewModel(configuration, backupService);
        settingsViewModel.RefreshGitMetadataCommand = ReactiveCommand.Create(settingsViewModel.ReloadGitMetadata);
        SettingsRemoteConnectionTypeCommands.Configure(settingsViewModel, backupService, notificationManager);
        var vm = lifetime.RegisterViewModel(
            new MainWindowViewModel(
                new AppNameDefinitionService(),
                notificationManager,
                configuration,
                () => storageFactory.SourceManager.ActiveStorage,
                settingsViewModel,
                new GraphViewModel(),
                TaskTreeExpansionStateStore.GetDefaultPath(launchData.ConfigPath)));

        var activeSource = storageFactory.SourceManager.ActiveSource;
        if (activeSource != null)
        {
            activeSource.TaskContext.MainWindow = vm;
        }

        ConfigureHeadlessTaskSpaces(
            vm,
            settingsViewModel,
            storageFactory.SourceManager,
            configuration,
            lifetime);

        return vm;
    }

    private static bool IsTaskSpaceRecoveryScenario(UnlimotionAutomationScenario scenario) =>
        scenario is UnlimotionAutomationScenario.TaskSpacesDuplicateCatalogRecovery or
            UnlimotionAutomationScenario.TaskSpacesOrphanCatalogRecovery;

    private static void ConfigureHeadlessTaskSpaces(
        MainWindowViewModel vm,
        SettingsViewModel settings,
        ITaskSourceManager sourceManager,
        IConfiguration configuration,
        HeadlessSessionLifetime lifetime)
    {
        var runner = lifetime.RegisterOperationRunner(new TaskSpaceOperationRunner());
        var activeConfiguration = new ActiveTaskSpaceConfiguration(configuration, sourceManager, runner);
        var queue = new TaskSpaceSettingsPersistenceQueue(activeConfiguration, runner);

        async Task RunOnUiThread(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(action);
        }

        async Task BindRuntime(TaskSourceRuntime runtime)
        {
            runtime.TaskContext.MainWindow = vm;
            await Dispatcher.UIThread.InvokeAsync(
                () => vm.BindInitializedStorage(runtime.Storage));
        }

        var coordinator = new TaskSpaceCoordinator(
            sourceManager,
            runner,
            queue,
            BindRuntime,
            () => RunOnUiThread(vm.ClearTaskSpaceSurface),
            () => Task.CompletedTask,
            _ => Task.CompletedTask);

        void Refresh()
        {
            settings.ReloadTaskSpaces(
                sourceManager.ConfiguredSources,
                sourceManager.ActiveSource?.Descriptor.Id ?? string.Empty);
        }

        async Task Activate(TaskSpaceOptionViewModel target)
        {
            await RunOnUiThread(() => settings.IsTaskSpaceSwitching = true);
            try
            {
                await coordinator.SwitchAsync(target.SourceId);
                await RunOnUiThread(() =>
                {
                    settings.ReloadActiveTaskSpaceSettings();
                    Refresh();
                });
            }
            finally
            {
                await RunOnUiThread(() => settings.IsTaskSpaceSwitching = false);
            }
        }

        settings.UseDeferredTaskSpaceSettingsPersistence();
        settings.SwitchTaskSpaceCommand = new AutomationAsyncCommand<TaskSpaceOptionViewModel>(Activate);
        settings.AddTaskSpaceCommand = new AutomationAsyncCommand(async () =>
        {
            await RunOnUiThread(() => settings.IsTaskSpaceSwitching = true);
            try
            {
                await coordinator.AddLocalAsync(settings.NewTaskSpaceName);
                await RunOnUiThread(() =>
                {
                    settings.ReloadActiveTaskSpaceSettings();
                    Refresh();
                });
            }
            finally
            {
                await RunOnUiThread(() => settings.IsTaskSpaceSwitching = false);
            }
        });
        settings.RenameTaskSpaceCommand = new AutomationAsyncCommand(async () =>
        {
            var selected = settings.SelectedTaskSpace;
            if (selected == null)
            {
                return;
            }

            await queue.DrainAsync();
            await runner.RunExclusiveAsync(
                "RenameTaskSpace",
                selected.SourceId,
                _ =>
                {
                    sourceManager.RenameConfiguredSource(selected.SourceId, selected.DisplayName);
                    return Task.CompletedTask;
                });
            await RunOnUiThread(Refresh);
        });
        settings.RemoveTaskSpaceCommand = new AutomationAsyncCommand(async () =>
        {
            var selected = settings.SelectedTaskSpace;
            if (selected == null || selected.IsActive)
            {
                return;
            }

            await queue.DrainAsync();
            await runner.RunExclusiveAsync(
                "RemoveTaskSpace",
                selected.SourceId,
                _ =>
                {
                    sourceManager.RemoveConfiguredSource(selected.SourceId);
                    return Task.CompletedTask;
                });
            await RunOnUiThread(Refresh);
        });
        Refresh();
    }

    private static void EnsureReactiveUiInitialized()
    {
        if (Interlocked.Exchange(ref _reactiveUiInitialized, 1) == 1)
        {
            return;
        }

        var builder = RxAppBuilder.CreateReactiveUIBuilder();
        builder.WithCoreServices();
        builder.WithMainThreadScheduler(AvaloniaScheduler.Instance);
        App.ConfigureReactiveUIBuilder(builder);
        builder.BuildApp();
    }

    private static void SelectAutomationTask(
        MainWindowViewModel vm,
        UnlimotionAutomationLaunchData launchData)
    {
        var currentTaskId = launchData.CurrentTaskId;
        vm.AllTasksMode = true;
        vm.DetailsAreOpen = true;

        if (launchData.OpenedTaskIds.Contains(currentTaskId))
        {
            PreloadReadmeDemoLastOpened(vm, launchData.OpenedTaskIds);
            SelectTaskById(vm, currentTaskId);
            return;
        }

        SelectTaskById(vm, currentTaskId);
    }

    private static void ApplyAutomationWindowTitle(
        MainWindowViewModel vm,
        UnlimotionAutomationLaunchData launchData)
    {
        if (!string.IsNullOrWhiteSpace(launchData.WindowTitle))
        {
            vm.Title = launchData.WindowTitle;
        }
    }

    private static void ApplyAutomationTreeExpansion(
        MainWindowViewModel? vm,
        UnlimotionAutomationLaunchData launchData)
    {
        if (launchData.ExpandAllTaskTrees)
        {
            ExpandAllTaskTrees(vm ?? throw new InvalidOperationException("Headless ViewModel was not initialized."));
        }
    }

    private static Dictionary<string, string?> CreateEnvironmentVariables(
        UnlimotionAutomationLaunchData launchData)
    {
        var environmentVariables = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [AutomationCurrentTaskIdEnvironmentVariable] = launchData.CurrentTaskId,
            [AutomationOpenDetailsEnvironmentVariable] = bool.TrueString,
            [AutomationOpenedTaskIdsEnvironmentVariable] = string.Join(';', launchData.OpenedTaskIds)
        };

        if (!string.IsNullOrWhiteSpace(launchData.WindowTitle))
        {
            environmentVariables[AutomationWindowTitleEnvironmentVariable] = launchData.WindowTitle;
        }

        if (launchData.ExpandAllTaskTrees)
        {
            environmentVariables[AutomationExpandAllTaskTreesEnvironmentVariable] = bool.TrueString;
        }

        var configuredMonitor = Environment.GetEnvironmentVariable(
            AutomationDesktopMonitorEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredMonitor))
        {
            environmentVariables[AutomationDesktopMonitorEnvironmentVariable] = configuredMonitor;
            environmentVariables[AutomationWindowWidthEnvironmentVariable] = "800";
            environmentVariables[AutomationWindowHeightEnvironmentVariable] = "400";
        }

        return environmentVariables;
    }

    private static DesktopWindowPlacement? ResolveDesktopWindowPlacement(
        UnlimotionAutomationScenario scenario,
        DesktopWindowPlacement? requestedPlacement)
    {
        if (requestedPlacement is not null)
        {
            return requestedPlacement;
        }

        var configuredMonitor = Environment.GetEnvironmentVariable(
            AutomationDesktopMonitorEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configuredMonitor))
        {
            return scenario == UnlimotionAutomationScenario.StatusContract
                ? DesktopWindowPlacement.Centered(1280, 800)
                : null;
        }

        var selector = configuredMonitor.Trim() switch
        {
            var value when string.Equals(value, "primary", StringComparison.OrdinalIgnoreCase) =>
                DesktopMonitorSelector.Primary,
            var value when string.Equals(value, "right", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(value, "last", StringComparison.OrdinalIgnoreCase) =>
                DesktopMonitorSelector.LastAvailable,
            var value when int.TryParse(value, out var index) =>
                DesktopMonitorSelector.FromIndex(index),
            var value => DesktopMonitorSelector.FromDeviceName(value)
        };

        return new DesktopWindowPlacement
        {
            Monitor = selector,
            Anchor = DesktopWindowAnchor.Center
        };
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

    private static void PreloadReadmeDemoLastOpened(
        MainWindowViewModel vm,
        IReadOnlyList<string> openedTaskIds)
    {
        foreach (var taskId in openedTaskIds)
        {
            SelectTaskById(vm, taskId);
        }
    }

    private static void SelectTaskById(MainWindowViewModel vm, string taskId)
    {
        var lookup = vm.taskRepository?.Tasks.Lookup(taskId);
        if (lookup?.HasValue != true)
        {
            return;
        }

        vm.CurrentTaskItem = lookup.Value.Value;
        vm.SelectCurrentTask();
    }

    private static TaskItemViewModel? FindTaskById(MainWindowViewModel vm, string taskId)
    {
        var lookup = vm.taskRepository?.Tasks.Lookup(taskId);
        return lookup?.HasValue == true ? lookup.Value.Value : null;
    }

    private sealed class HeadlessSessionLifetime : IDisposable
    {
        private readonly bool _previousDefaultIsExpanded;
        private IConfigurationRoot? _configuration;
        private ITaskStorage? _storage;
        private MainWindowViewModel? _viewModel;
        private TaskSpaceOperationRunner? _operationRunner;
        private UnlimotionAutomationLaunchData? _launchData;
        private int _disposed;

        public HeadlessSessionLifetime(
            UnlimotionAutomationLaunchData launchData,
            bool previousDefaultIsExpanded)
        {
            _launchData = launchData;
            _previousDefaultIsExpanded = previousDefaultIsExpanded;
        }

        public IConfigurationRoot RegisterConfiguration(IConfigurationRoot configuration)
        {
            _configuration = configuration;
            return configuration;
        }

        public ITaskStorage RegisterStorage(ITaskStorage storage)
        {
            _storage = storage;
            return storage;
        }

        public MainWindowViewModel RegisterViewModel(MainWindowViewModel viewModel)
        {
            _viewModel = viewModel;
            return viewModel;
        }

        public TaskSpaceOperationRunner RegisterOperationRunner(TaskSpaceOperationRunner operationRunner)
        {
            _operationRunner = operationRunner;
            return operationRunner;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            var failures = new List<Exception>();

            CaptureDispose(failures, _viewModel);
            _viewModel = null;

            CaptureDispose(failures, _storage);
            _storage = null;

            CaptureDispose(failures, _operationRunner);
            _operationRunner = null;

            CaptureDispose(failures, _configuration);
            _configuration = null;

            TaskWrapperViewModel.DefaultIsExpanded = _previousDefaultIsExpanded;

            CaptureDispose(failures, _launchData);
            _launchData = null;

            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }

            if (failures.Count > 1)
            {
                throw new AggregateException("Headless session cleanup failed.", failures);
            }
        }

        private static void CaptureDispose(List<Exception> failures, object? resource)
        {
            if (resource is not IDisposable disposable)
            {
                return;
            }

            try
            {
                disposable.Dispose();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
    }

    private sealed class UnlimotionAutomationLaunchData : IDisposable
    {
        private UnlimotionAutomationLaunchData(
            string repositoryRoot,
            string rootPath,
            string tasksPath,
            string vaultPath,
            string configPath,
            string currentTaskId,
            string currentTaskTitle,
            IReadOnlyList<string> openedTaskIds,
            string? windowTitle,
            bool expandAllTaskTrees)
        {
            RepositoryRoot = repositoryRoot;
            RootPath = rootPath;
            TasksPath = tasksPath;
            VaultPath = vaultPath;
            ConfigPath = configPath;
            CurrentTaskId = currentTaskId;
            CurrentTaskTitle = currentTaskTitle;
            OpenedTaskIds = openedTaskIds;
            WindowTitle = windowTitle;
            ExpandAllTaskTrees = expandAllTaskTrees;
        }

        public string RepositoryRoot { get; }

        public string RootPath { get; }

        public string TasksPath { get; }

        public string VaultPath { get; }

        public string ConfigPath { get; }

        public string CurrentTaskId { get; }

        public string CurrentTaskTitle { get; }

        public IReadOnlyList<string> OpenedTaskIds { get; }

        public string? WindowTitle { get; }

        public bool ExpandAllTaskTrees { get; }

        public static UnlimotionAutomationLaunchData Create(
            UnlimotionAutomationScenario scenario,
            string? language = null,
            string? currentTaskIdOverride = null,
            string? theme = null)
        {
            var repositoryRoot = FindRepositoryRoot();
            var rootPath = Path.Combine(Path.GetTempPath(), "Unlimotion.AppAutomation", Guid.NewGuid().ToString("N"));
            var tasksPath = Path.Combine(rootPath, "Tasks");
            var vaultPath = Path.Combine(rootPath, "Vault");
            var configPath = Path.Combine(rootPath, "Settings.json");
            var currentTaskId = string.IsNullOrWhiteSpace(currentTaskIdOverride)
                ? UnlimotionAutomationScenarioData.GetCurrentTaskId(scenario, language)
                : currentTaskIdOverride;
            var currentTaskTitle = UnlimotionAutomationScenarioData.GetTaskTitle(scenario, currentTaskId, language);
            var openedTaskIds = scenario == UnlimotionAutomationScenario.ReadmeDemo
                ? UnlimotionAutomationScenarioData.GetReadmeDemoLastOpenedTaskIds(language)
                    .Where(taskId => !string.Equals(taskId, currentTaskId, StringComparison.Ordinal))
                    .Append(currentTaskId)
                    .ToArray()
                : [];
            var environmentWindowTitle = Environment.GetEnvironmentVariable(
                AutomationWindowTitleEnvironmentVariable);
            var windowTitle = string.IsNullOrWhiteSpace(environmentWindowTitle)
                ? UnlimotionAutomationScenarioData.GetWindowTitle(scenario, language)
                : environmentWindowTitle;
            var expandAllTaskTrees = scenario == UnlimotionAutomationScenario.ReadmeDemo;

            Directory.CreateDirectory(tasksPath);
            UnlimotionAutomationScenarioData.SeedTasks(scenario, repositoryRoot, tasksPath, language);
            if (scenario == UnlimotionAutomationScenario.Feed)
            {
                Directory.CreateDirectory(vaultPath);
                UnlimotionAutomationScenarioData.SeedFeedVault(vaultPath);
            }

            UnlimotionAutomationScenarioData.WriteConfig(
                scenario,
                configPath,
                tasksPath,
                language,
                theme,
                vaultPath);

            return new UnlimotionAutomationLaunchData(
                repositoryRoot,
                rootPath,
                tasksPath,
                vaultPath,
                configPath,
                currentTaskId,
                currentTaskTitle,
                openedTaskIds,
                windowTitle,
                expandAllTaskTrees);
        }

        public void Dispose()
        {
            if (!Directory.Exists(RootPath))
            {
                return;
            }

            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch
            {
                // Temp test data cleanup is best-effort because file watchers can release handles late.
            }
        }

        private static string FindRepositoryRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "src", "Unlimotion.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Unable to locate Unlimotion repository root.");
        }
    }

    private sealed class AutomationNotificationManager : INotificationManagerWrapper
    {
        public void Ask(string header, string message, Action yesAction, Action? noAction = null)
        {
            noAction?.Invoke();
        }

        public Task<bool> ConfirmAsync(string header, string message)
        {
            return Task.FromResult(false);
        }

        public Task<bool> ConfirmTaskOutlinePasteAsync(TaskOutlinePastePreview preview)
        {
            return Task.FromResult(false);
        }

        public void ErrorToast(string message)
        {
        }

        public void SuccessToast(string message)
        {
        }
    }

}
