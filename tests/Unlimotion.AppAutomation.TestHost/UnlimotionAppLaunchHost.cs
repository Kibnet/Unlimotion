using AppAutomation.Session.Contracts;
using AppAutomation.TestHost.Avalonia;
using Microsoft.Extensions.Configuration;
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
    public const string CurrentTaskId = UnlimotionAutomationScenarioData.SmokeCurrentTaskId;
    public const string CurrentTaskTitle = UnlimotionAutomationScenarioData.SmokeCurrentTaskTitle;

    public static Type AvaloniaAppType => typeof(App);

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
        string? buildConfiguration = null,
        bool buildBeforeLaunch = true,
        bool buildOncePerProcess = true,
        TimeSpan? buildTimeout = null,
        TimeSpan? mainWindowTimeout = null,
        TimeSpan? pollInterval = null)
    {
        var launchData = UnlimotionAutomationLaunchData.Create(scenario, language);
        var environmentVariables = CreateEnvironmentVariables(launchData);

        try
        {
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
        Action<MainWindowViewModel>? afterViewModelPrepared = null)
    {
        var launchData = UnlimotionAutomationLaunchData.Create(scenario, language);
        MainWindowViewModel? vm = null;
        var previousDefaultIsExpanded = TaskWrapperViewModel.DefaultIsExpanded;

        return new HeadlessAppLaunchOptions
        {
            BeforeLaunchAsync = _ =>
            {
                if (launchData.ExpandAllTaskTrees)
                {
                    TaskWrapperViewModel.DefaultIsExpanded = true;
                }

                vm = CreateHeadlessViewModel(launchData);
                vm.Connect().GetAwaiter().GetResult();
                SelectAutomationTask(vm, launchData.CurrentTaskId);
                ApplyAutomationWindowTitle(vm, launchData);
                afterViewModelPrepared?.Invoke(vm);
                return ValueTask.CompletedTask;
            },
            CreateMainWindow = () =>
            {
                var window = new MainWindow
                {
                    Width = 1200,
                    Height = 800,
                    DataContext = vm ?? throw new InvalidOperationException("Headless ViewModel was not initialized.")
                };
                window.Opened += (_, __) => ApplyAutomationTreeExpansion(vm, launchData);
                return window;
            },
            DisposeCallback = () =>
            {
                (vm as IDisposable)?.Dispose();
                TaskWrapperViewModel.DefaultIsExpanded = previousDefaultIsExpanded;
                launchData.Dispose();
            }
        };
    }

    public static string GetCurrentTaskId(UnlimotionAutomationScenario scenario = UnlimotionAutomationScenario.Smoke)
    {
        return UnlimotionAutomationScenarioData.GetCurrentTaskId(scenario);
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

    private static MainWindowViewModel CreateHeadlessViewModel(UnlimotionAutomationLaunchData launchData)
    {
        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(launchData.ConfigPath);
        var mapper = AppModelMapping.ConfigureMapping();
        var notificationManager = new AutomationNotificationManager();
        var storageFactory = new TaskStorageFactory(configuration, mapper, notificationManager);
        TaskStorageFactory.DefaultStoragePath = launchData.TasksPath;
        storageFactory.CreateFileStorage(launchData.TasksPath);

        var settingsViewModel = new SettingsViewModel(configuration);
        var vm = new MainWindowViewModel(
            new AppNameDefinitionService(),
            notificationManager,
            configuration,
            () => storageFactory.CurrentStorage,
            settingsViewModel,
            new GraphViewModel());

        TaskItemViewModel.NotificationManagerInstance = notificationManager;
        TaskItemViewModel.MainWindowInstance = vm;

        return vm;
    }

    private static void SelectAutomationTask(MainWindowViewModel vm, string currentTaskId)
    {
        vm.AllTasksMode = true;
        vm.DetailsAreOpen = true;

        if (string.Equals(currentTaskId, UnlimotionAutomationScenarioData.ReadmeDemoCurrentTaskId, StringComparison.Ordinal))
        {
            PreloadReadmeDemoLastOpened(vm);
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

        return environmentVariables;
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

    private static void PreloadReadmeDemoLastOpened(MainWindowViewModel vm)
    {
        foreach (var taskId in UnlimotionAutomationScenarioData.ReadmeDemoLastOpenedTaskIds)
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

    private sealed class UnlimotionAutomationLaunchData : IDisposable
    {
        private UnlimotionAutomationLaunchData(
            string repositoryRoot,
            string rootPath,
            string tasksPath,
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

        public string ConfigPath { get; }

        public string CurrentTaskId { get; }

        public string CurrentTaskTitle { get; }

        public IReadOnlyList<string> OpenedTaskIds { get; }

        public string? WindowTitle { get; }

        public bool ExpandAllTaskTrees { get; }

        public static UnlimotionAutomationLaunchData Create(
            UnlimotionAutomationScenario scenario,
            string? language = null)
        {
            var repositoryRoot = FindRepositoryRoot();
            var rootPath = Path.Combine(Path.GetTempPath(), "Unlimotion.AppAutomation", Guid.NewGuid().ToString("N"));
            var tasksPath = Path.Combine(rootPath, "Tasks");
            var configPath = Path.Combine(rootPath, "Settings.json");
            var currentTaskId = UnlimotionAutomationScenarioData.GetCurrentTaskId(scenario);
            var currentTaskTitle = UnlimotionAutomationScenarioData.GetCurrentTaskTitle(scenario, language);
            var openedTaskIds = scenario == UnlimotionAutomationScenario.ReadmeDemo
                ? UnlimotionAutomationScenarioData.ReadmeDemoLastOpenedTaskIds
                : [];
            var windowTitle = UnlimotionAutomationScenarioData.GetWindowTitle(scenario, language);
            var expandAllTaskTrees = scenario == UnlimotionAutomationScenario.ReadmeDemo;

            Directory.CreateDirectory(tasksPath);
            UnlimotionAutomationScenarioData.SeedTasks(scenario, repositoryRoot, tasksPath, language);
            UnlimotionAutomationScenarioData.WriteConfig(scenario, configPath, tasksPath, language);

            return new UnlimotionAutomationLaunchData(
                repositoryRoot,
                rootPath,
                tasksPath,
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

        public void ErrorToast(string message)
        {
        }

        public void SuccessToast(string message)
        {
        }
    }
}
