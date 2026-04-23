using System.Text.Json;
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
    public const string CurrentTaskId = "f41774af-38f6-486c-9c5d-e4ba3300438c";
    public const string CurrentTaskTitle = "Blocked task 7";

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
        string? buildConfiguration = null,
        bool buildBeforeLaunch = true,
        bool buildOncePerProcess = true,
        TimeSpan? buildTimeout = null,
        TimeSpan? mainWindowTimeout = null,
        TimeSpan? pollInterval = null)
    {
        var launchData = UnlimotionAutomationLaunchData.Create();

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
                    EnvironmentVariables = new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        [AutomationCurrentTaskIdEnvironmentVariable] = CurrentTaskId,
                        [AutomationOpenDetailsEnvironmentVariable] = bool.TrueString
                    }
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

    public static HeadlessAppLaunchOptions CreateHeadlessLaunchOptions()
    {
        var launchData = UnlimotionAutomationLaunchData.Create();
        MainWindowViewModel? vm = null;

        return new HeadlessAppLaunchOptions
        {
            BeforeLaunchAsync = _ =>
            {
                vm = CreateHeadlessViewModel(launchData);
                vm.Connect().GetAwaiter().GetResult();
                SelectAutomationTask(vm);
                return ValueTask.CompletedTask;
            },
            CreateMainWindow = () =>
            {
                return new MainWindow
                {
                    Width = 1200,
                    Height = 800,
                    DataContext = vm ?? throw new InvalidOperationException("Headless ViewModel was not initialized.")
                };
            },
            DisposeCallback = () =>
            {
                (vm as IDisposable)?.Dispose();
                launchData.Dispose();
            }
        };
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

    private static void SelectAutomationTask(MainWindowViewModel vm)
    {
        var lookup = vm.taskRepository?.Tasks.Lookup(CurrentTaskId);
        if (lookup?.HasValue == true)
        {
            vm.AllTasksMode = true;
            vm.CurrentTaskItem = lookup.Value.Value;
            vm.SelectCurrentTask();
        }

        vm.DetailsAreOpen = true;
    }

    private sealed class UnlimotionAutomationLaunchData : IDisposable
    {
        private UnlimotionAutomationLaunchData(string repositoryRoot, string rootPath, string tasksPath, string configPath)
        {
            RepositoryRoot = repositoryRoot;
            RootPath = rootPath;
            TasksPath = tasksPath;
            ConfigPath = configPath;
        }

        public string RepositoryRoot { get; }

        public string RootPath { get; }

        public string TasksPath { get; }

        public string ConfigPath { get; }

        public static UnlimotionAutomationLaunchData Create()
        {
            var repositoryRoot = FindRepositoryRoot();
            var rootPath = Path.Combine(Path.GetTempPath(), "Unlimotion.AppAutomation", Guid.NewGuid().ToString("N"));
            var tasksPath = Path.Combine(rootPath, "Tasks");
            var configPath = Path.Combine(rootPath, "Settings.json");

            Directory.CreateDirectory(tasksPath);
            CopySnapshots(repositoryRoot, tasksPath);
            WriteConfig(configPath, tasksPath);

            return new UnlimotionAutomationLaunchData(repositoryRoot, rootPath, tasksPath, configPath);
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

        private static void CopySnapshots(string repositoryRoot, string tasksPath)
        {
            var snapshotsPath = Path.Combine(repositoryRoot, "src", "Unlimotion.Test", "Snapshots");
            foreach (var sourcePath in Directory.EnumerateFiles(snapshotsPath))
            {
                var destinationPath = Path.Combine(tasksPath, Path.GetFileName(sourcePath));
                File.Copy(sourcePath, destinationPath, overwrite: true);
            }
        }

        private static void WriteConfig(string configPath, string tasksPath)
        {
            var config = new
            {
                TaskStorage = new
                {
                    Path = tasksPath,
                    URL = string.Empty,
                    Login = string.Empty,
                    Password = string.Empty,
                    IsServerMode = "False"
                },
                Git = new
                {
                    BackupEnabled = "False",
                    ShowStatusToasts = "False",
                    RemoteUrl = string.Empty,
                    Branch = "master",
                    UserName = "YourEmail",
                    Password = "YourToken",
                    PullIntervalSeconds = "30",
                    PushIntervalSeconds = "60",
                    RemoteName = "origin",
                    PushRefSpec = "refs/heads/master",
                    CommitterName = "Backuper",
                    CommitterEmail = "Backuper@unlimotion.ru"
                },
                AllTasks = new
                {
                    ShowCompleted = "False",
                    ShowArchived = "False",
                    ShowWanted = "False",
                    CurrentSortDefinition = "Comfort",
                    CurrentSortDefinitionForUnlocked = "Comfort"
                }
            };

            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(
                configPath,
                JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
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
