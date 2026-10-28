using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Configuration;
using Unlimotion;
using Unlimotion.Domain;
using Unlimotion.Services;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using Unlimotion.Views;
using WritableJsonConfiguration;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class MainScreenLoadingUiTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SourceCommands_RejectDirectExecutionWhileLoadingOrSwitching(bool switching)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var context = TestMainWindowContext.Create(TimeSpan.Zero);
            var settings = context.MainWindowViewModel.Settings;
            var events = new ConcurrentQueue<string>();
            using var factory = new SettingsViewModelTests.RecordingTaskStorageFactory(
                Path.Combine(Path.GetTempPath(), $"source-guard-{Guid.NewGuid():N}"), events);
            using var commands = SettingsViewModelTests.ConfigureAppSettingsCommands(settings,
                context.Configuration, new SettingsViewModelTests.FakeRemoteBackupService(), factory);
            if (switching) settings.IsTaskSpaceSwitching = true;
            else SetTasksLoading(context.MainWindowViewModel, true);
            try
            {
                foreach (var command in new ICommand?[]
                {
                    settings.AddTaskSpaceCommand, settings.SwitchTaskSpaceCommand,
                    settings.RenameTaskSpaceCommand, settings.RemoveTaskSpaceCommand,
                    settings.ConnectCommand, settings.SignOutCommand, settings.CloneCommand,
                    settings.PullCommand, settings.PushCommand, settings.SyncNowCommand,
                    settings.MigrateCommand, settings.BackupCommand, settings.ResaveCommand,
                    settings.BrowseTaskStoragePathCommand, settings.BrowseSshKeyStoragePathCommand,
                    settings.ResetSshKeyStoragePathCommand, settings.GenerateSshKeyCommand,
                    settings.RefreshGitMetadataCommand, settings.RefreshSshKeysCommand,
                    settings.SwitchRemoteToHttpCommand, settings.SwitchRemoteToSshCommand
                })
                {
                    await Assert.That(command).IsNotNull();
                    await Assert.That(command!.CanExecute(null)).IsFalse();
                    command.Execute(null);
                }
                Dispatcher.UIThread.RunJobs();
                await Assert.That(events.Count).IsEqualTo(0);
            }
            finally
            {
                settings.IsTaskSpaceSwitching = false;
                SetTasksLoading(context.MainWindowViewModel, false);
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task SettingsButton_OpensSeparateScreenDuringLoading_AndCloseReturnsToLoading()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var context = TestMainWindowContext.Create(TimeSpan.Zero);
            var vm = context.MainWindowViewModel;
            var view = new MainScreen { DataContext = vm };
            var window = CreateWindow(view);
            window.Show();
            try
            {
                SetTasksLoading(vm, true);
                Dispatcher.UIThread.RunJobs();
                var settingsButton = FindControlByAutomationId<Button>(view, "GlobalSettingsButton");
                Click(window, settingsButton);
                var settingsOverlay = FindControlByAutomationId<Grid>(view, "GlobalSettingsOverlay");
                await Assert.That(settingsOverlay.IsEffectivelyVisible).IsTrue();
                await Assert.That(FindControlByAutomationId<Border>(view, "SettingsBusyStatus").IsEffectivelyVisible).IsTrue();
                await Assert.That(FindControlByAutomationId<Border>(view, "SettingsStorageSection").IsEffectivelyEnabled).IsFalse();
                await Assert.That(FindControlByAutomationId<Border>(view, "SettingsAppearanceSection").IsEffectivelyEnabled).IsTrue();
                window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
                await Assert.That(FindControlByAutomationId<Grid>(view, "SettingsHotkeyHelpOverlayHost").IsVisible).IsTrue();
                vm.Settings.IsTaskSpaceRecoveryRequired = true;
                Dispatcher.UIThread.RunJobs();
                await Assert.That(settingsOverlay.IsEffectivelyVisible).IsFalse();
                await Assert.That(FindControlByAutomationId<Grid>(view, "SettingsHotkeyHelpOverlayHost").IsVisible).IsFalse();
                window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
                await Assert.That(FindControlByAutomationId<Grid>(view, "HotkeyHelpOverlayHost").IsVisible).IsFalse();
                vm.Settings.IsTaskSpaceRecoveryRequired = false;
                Click(window, settingsButton);
                window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await Assert.That(settingsOverlay.IsEffectivelyVisible).IsTrue();
                await Assert.That(FindControlByAutomationId<Grid>(view, "SettingsHotkeyHelpOverlayHost").IsVisible).IsFalse();
                Click(window, FindControlByAutomationId<Button>(view, "GlobalSettingsCloseButton"));
                await Assert.That(settingsOverlay.IsEffectivelyVisible).IsFalse();
                await Assert.That(FindControlByAutomationId<Grid>(view, "TasksLoadingOverlay").IsEffectivelyVisible).IsTrue();
            }
            finally
            {
                vm.Settings.IsTaskSpaceRecoveryRequired = false;
                SetTasksLoading(vm, false);
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("Control is not in the window.");
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [Test]
    public async Task ExistingHelp_IsDismissedWhenRecoveryOrPickerTakesInput()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var context = TestMainWindowContext.Create(TimeSpan.Zero);
            var vm = context.MainWindowViewModel;
            var screen = new MainScreen { DataContext = vm };
            var window = CreateWindow(screen);
            window.Show();
            try
            {
                FindControlByAutomationId<Button>(screen, "GlobalSettingsButton").Focus();
                window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
                var help = FindControlByAutomationId<Grid>(screen, "HotkeyHelpOverlayHost");
                await Assert.That(help.IsVisible).IsTrue();
                vm.Settings.IsTaskSpaceRecoveryRequired = true;
                Dispatcher.UIThread.RunJobs();
                await Assert.That(help.IsVisible).IsFalse();
                vm.Settings.IsTaskSpaceRecoveryRequired = false;
                FindControlByAutomationId<Button>(screen, "GlobalSettingsButton").Focus();
                window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
                await Assert.That(help.IsVisible).IsTrue();
                using (vm.BeginSettingsPicker())
                {
                    Dispatcher.UIThread.RunJobs();
                    await Assert.That(help.IsVisible).IsFalse();
                    vm.OpenSettings();
                    await Assert.That(vm.IsSettingsOpen).IsFalse();
                }
            }
            finally { vm.Settings.IsTaskSpaceRecoveryRequired = false; window.Close(); }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments(320, false, 1d)]
    [Arguments(680, true, 1.5d)]
    [Arguments(1440, true, 1d)]
    public async Task SettingsDuringLoading_FitsWindowAndKeepsCloseReachable(int width, bool dark, double scale)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var context = TestMainWindowContext.Create(TimeSpan.Zero);
            var vm = context.MainWindowViewModel;
            var previousTheme = Application.Current!.RequestedThemeVariant;
            var previousFontSize = Application.Current.Resources["AppFontSize"];
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            Application.Current.Resources["AppFontSize"] = 12d * scale;
            var screen = new MainScreen { DataContext = vm };
            var window = CreateWindow(screen);
            window.Width = width;
            window.Show();
            try
            {
                SetTasksLoading(vm, true);
                Dispatcher.UIThread.RunJobs();
                var settingsButton = FindControlByAutomationId<Button>(screen, "GlobalSettingsButton");
                var buttonPoint = settingsButton.TranslatePoint(default, window)!.Value;
                await Assert.That(buttonPoint.X >= 0 && buttonPoint.X + settingsButton.Bounds.Width <= width).IsTrue();
                SaveConnectFrame(window, $"settings-{width}-before-click");
                Click(window, settingsButton);
                SaveConnectFrame(window, $"settings-{width}-after-click");
                await Assert.That(vm.IsSettingsOpen).IsTrue();
                var close = FindControlByAutomationId<Button>(screen, "GlobalSettingsCloseButton");
                var closePoint = close.TranslatePoint(default, window)!.Value;
                await Assert.That(closePoint.X >= 0 && closePoint.X + close.Bounds.Width <= width).IsTrue();
                await Assert.That(FindControlByAutomationId<Border>(screen, "SettingsBusyStatus").IsEffectivelyVisible).IsTrue();
                SaveConnectFrame(window, $"settings-{width}-{(dark ? "dark" : "light")}-{scale * 100:0}");
                Click(window, close);
                await Assert.That(FindControlByAutomationId<Grid>(screen, "TasksLoadingOverlay").IsEffectivelyVisible).IsTrue();
            }
            finally
            {
                SetTasksLoading(vm, false);
                window.Close();
                Application.Current.RequestedThemeVariant = previousTheme;
                Application.Current.Resources["AppFontSize"] = previousFontSize;
            }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SettingsConnectCommand_ShowsOverlayUntilCompletion(bool fail)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var context = TestMainWindowContext.Create(TimeSpan.Zero);
            var vm = context.MainWindowViewModel;
            var settings = vm.Settings;
            var path = Path.Combine(Path.GetTempPath(), $"connect-overlay-{Guid.NewGuid():N}");
            using var factory = new SettingsViewModelTests.RecordingTaskStorageFactory(
                path, new System.Collections.Concurrent.ConcurrentQueue<string>());
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = false;
            factory.BeforeSwitchAsync = async () =>
            {
                entered = true;
                await release.Task;
                if (fail) throw new InvalidOperationException("Simulated connection failure");
            };
            settings.TaskStoragePath = path;
            using var commands = SettingsViewModelTests.ConfigureAppSettingsCommands(
                settings, context.Configuration, new SettingsViewModelTests.FakeRemoteBackupService(), factory);
            var view = new MainScreen { DataContext = vm };
            var window = CreateWindow(view);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Click(window, FindControlByAutomationId<Button>(view, "GlobalSettingsButton"));
            Dispatcher.UIThread.RunJobs();
            var connectButton = FindControlByAutomationId<Button>(view, "ConnectLocalStorageButton");
            var overlay = FindControlByAutomationId<Grid>(view, "TaskSpaceSwitchOverlay");
            var spinner = FindControlByAutomationId<SeamlessLoadingIndicator>(view, "TaskSpaceSwitchProgress");
            try
            {
                settings.IsTaskSpaceSwitching = true;
                settings.ConnectCommand!.Execute(null);
                Dispatcher.UIThread.RunJobs();
                await Assert.That(entered).IsFalse();
                await Assert.That(settings.IsTaskSpaceSwitching).IsTrue();
                settings.IsTaskSpaceSwitching = false;
                Dispatcher.UIThread.RunJobs();
                await Assert.That(connectButton.Command == settings.ConnectCommand).IsTrue();
                connectButton.Command!.Execute(connectButton.CommandParameter);
                await Assert.That(WaitFor(() => entered)).IsTrue();
                await Assert.That(overlay.IsEffectivelyVisible).IsTrue();
                await Assert.That(spinner.IsEffectivelyVisible).IsTrue();
                SaveConnectFrame(window, fail ? "error-pending" : "loading");
                var callbackRan = false;
                Dispatcher.UIThread.Post(() => callbackRan = true);
                await Assert.That(WaitFor(() => callbackRan)).IsTrue();
                release.TrySetResult();
                await Assert.That(WaitFor(() => settings.StorageConnectionState != SettingsConnectionState.Connecting &&
                    !overlay.IsVisible)).IsTrue();
                await Assert.That(settings.StorageConnectionState).IsEqualTo(
                    fail ? SettingsConnectionState.Error : SettingsConnectionState.Connected);
                await Assert.That(spinner.IsEffectivelyVisible).IsFalse();
                SaveConnectFrame(window, fail ? "error" : "connected");
            }
            finally
            {
                release.TrySetResult();
                WaitFor(() => settings.StorageConnectionState != SettingsConnectionState.Connecting);
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static void SaveConnectFrame(Window window, string state)
    {
        var directory = Environment.GetEnvironmentVariable("UNLIMOTION_CONNECT_EVIDENCE");
        if (string.IsNullOrEmpty(directory)) return;
        using var frame = window.CaptureRenderedFrame();
        if (frame == null) { Console.WriteLine("Connect screenshot unavailable in headless renderer."); return; }
        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, $"connect-{state}.png"));
    }

    [Test]
    public async Task MainScreen_TogglesTasksLoadingOverlay_WithLoadingState()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                var view = new MainScreen { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var overlay = FindControlByAutomationId<Grid>(view, "TasksLoadingOverlay");
                var spinner = FindControlByAutomationId<SeamlessLoadingIndicator>(
                    view,
                    "TasksLoadingSpinner");

                await Assert.That(overlay.IsVisible).IsFalse();
                await Assert.That(spinner.GetType().Name).IsEqualTo("SeamlessLoadingIndicator");

                SetTasksLoading(vm, true);
                var becameVisible = WaitFor(() => overlay.IsVisible && spinner.IsVisible);
                await Assert.That(becameVisible).IsTrue();

                SetTasksLoading(vm, false);
                var becameHidden = WaitFor(() => !overlay.IsVisible);
                await Assert.That(becameHidden).IsTrue();
                Dispatcher.UIThread.RunJobs();
                await Assert.That(spinner.CurrentPhase).IsEqualTo(0d);
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task MainScreen_Connect_KeepsUiResponsive_DuringBlockingInitialLoad()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var context = TestMainWindowContext.Create(TimeSpan.FromSeconds(5));
            Window? window = null;

            try
            {
                var vm = context.MainWindowViewModel;
                var view = new MainScreen { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var overlay = FindControlByAutomationId<Grid>(view, "TasksLoadingOverlay");
                var spinner = FindControlByAutomationId<SeamlessLoadingIndicator>(
                    view,
                    "TasksLoadingSpinner");
                var connectTask = vm.Connect();

                var loadStarted = WaitFor(
                    () => overlay.IsVisible &&
                          spinner.IsVisible &&
                          !connectTask.IsCompleted,
                    timeoutMilliseconds: 2000);
                await Assert.That(loadStarted).IsTrue();

                var postedCallbackRan = false;
                Dispatcher.UIThread.Post(() => postedCallbackRan = true);

                var stayedResponsive = WaitFor(
                    () => postedCallbackRan &&
                          overlay.IsVisible &&
                          !connectTask.IsCompleted,
                    timeoutMilliseconds: 2000);
                await Assert.That(stayedResponsive).IsTrue();

                Click(window, FindControlByAutomationId<Button>(view, "GlobalSettingsButton"));
                await Assert.That(FindControlByAutomationId<Grid>(view, "GlobalSettingsOverlay").IsEffectivelyVisible).IsTrue();
                await Assert.That(connectTask.IsCompleted).IsFalse();
                Click(window, FindControlByAutomationId<Button>(view, "GlobalSettingsCloseButton"));
                await Assert.That(overlay.IsEffectivelyVisible).IsTrue();

                await connectTask;

                var finishedLoading = WaitFor(
                    () => !overlay.IsVisible && vm.taskRepository?.Tasks.Count == 1,
                    timeoutMilliseconds: 2000);
                await Assert.That(finishedLoading).IsTrue();
            }
            finally
            {
                window?.Close();
            }
        }, CancellationToken.None);
    }

    private static Window CreateWindow(Control content)
    {
        return new Window
        {
            Width = 1200,
            Height = 800,
            Content = content
        };
    }

    private static T FindControlByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        var control = root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(candidate =>
                string.Equals(
                    AutomationProperties.GetAutomationId(candidate),
                    automationId,
                    StringComparison.Ordinal));

        return control ?? throw new InvalidOperationException(
            $"Control with AutomationId '{automationId}' was not found.");
    }

    private static void SetTasksLoading(MainWindowViewModel vm, bool value)
    {
        var property = typeof(MainWindowViewModel).GetProperty(
            nameof(MainWindowViewModel.IsTasksLoading),
            BindingFlags.Instance | BindingFlags.Public);
        var setter = property?.GetSetMethod(nonPublic: true);

        if (setter == null)
        {
            throw new InvalidOperationException("Could not access MainWindowViewModel.IsTasksLoading setter.");
        }

        setter.Invoke(vm, [value]);
    }

    private static bool WaitFor(Func<bool> predicate, int timeoutMilliseconds = 2000)
    {
        return SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return predicate();
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));
    }

    private sealed class TestMainWindowContext : IDisposable
    {
        private readonly string _configPath;
        private readonly IDisposable? _configurationDisposable;

        private TestMainWindowContext(
            string configPath,
            MainWindowViewModel mainWindowViewModel,
            IDisposable? configurationDisposable)
        {
            _configPath = configPath;
            MainWindowViewModel = mainWindowViewModel;
            _configurationDisposable = configurationDisposable;
        }

        public MainWindowViewModel MainWindowViewModel { get; }
        public IConfiguration Configuration { get; private init; } = null!;

        public static TestMainWindowContext Create(TimeSpan loadDelay)
        {
            var configPath = Path.Combine(
                Environment.CurrentDirectory,
                $"LoadingUiTests_{Guid.NewGuid():N}.json");
            File.WriteAllText(configPath, "{}");

            IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(configPath, reloadOnChange: false);
            var notificationManager = new NotificationManagerWrapperMock();
            var taskContext = new TaskItemViewModelContext
            {
                NotificationManager = notificationManager
            };
            var storage = new UnifiedTaskStorage(new TaskTreeManager(new BlockingInitialLoadStorage(loadDelay)), taskContext);
            var settings = new SettingsViewModel(configuration);
            var mainWindowViewModel = new MainWindowViewModel(
                new AppNameDefinitionService(),
                notificationManager,
                configuration,
                () => storage,
                settings);

            taskContext.MainWindow = mainWindowViewModel;

            return new TestMainWindowContext(
                configPath,
                mainWindowViewModel,
                configuration as IDisposable) { Configuration = configuration };
        }

        public void Dispose()
        {
            _configurationDisposable?.Dispose();

            if (File.Exists(_configPath))
            {
                File.Delete(_configPath);
            }
        }
    }

    private sealed class BlockingInitialLoadStorage : IStorage
    {
        private readonly Dictionary<string, TaskItem> _tasks;
        private readonly TimeSpan _loadDelay;

        public BlockingInitialLoadStorage(TimeSpan loadDelay)
        {
            _loadDelay = loadDelay;
            _tasks = new Dictionary<string, TaskItem>(StringComparer.Ordinal)
            {
                ["task-1"] = CreateTask("task-1", "Loaded task")
            };
        }

        public event EventHandler<TaskStorageUpdateEventArgs> Updating
        {
            add { }
            remove { }
        }

        public event Action<Exception?>? OnConnectionError
        {
            add { }
            remove { }
        }

        public Task<bool> Connect()
        {
            return Task.FromResult(true);
        }

        public Task Disconnect()
        {
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<TaskItem> GetAll()
        {
            Thread.Sleep(_loadDelay);

            foreach (var task in _tasks.Values)
            {
                yield return Clone(task);
            }
        }

        public Task BulkInsert(IEnumerable<TaskItem> taskItems)
        {
            foreach (var taskItem in taskItems)
            {
                _tasks[taskItem.Id ?? Guid.NewGuid().ToString("N")] = Clone(taskItem);
            }

            return Task.CompletedTask;
        }

        public Task<TaskItem?> Load(string itemId)
        {
            return Task.FromResult(
                _tasks.TryGetValue(itemId, out var task)
                    ? Clone(task)
                    : null);
        }

        public Task<bool> Remove(string itemId)
        {
            return Task.FromResult(_tasks.Remove(itemId));
        }

        public Task<TaskItem> Save(TaskItem item)
        {
            var clone = Clone(item);
            clone.Id ??= Guid.NewGuid().ToString("N");
            item.Id = clone.Id;
            _tasks[clone.Id] = clone;
            return Task.FromResult(item);
        }

        private static TaskItem CreateTask(string id, string title)
        {
            return new TaskItem
            {
                Id = id,
                Title = title,
                CreatedDateTime = DateTimeOffset.UtcNow,
                UpdatedDateTime = DateTimeOffset.UtcNow,
                ContainsTasks = new List<string>(),
                ParentTasks = new List<string>(),
                BlocksTasks = new List<string>(),
                BlockedByTasks = new List<string>(),
                IsCompleted = false,
                IsCanBeCompleted = true,
            };
        }

        private static TaskItem Clone(TaskItem task)
        {
            return new TaskItem
            {
                Id = task.Id,
                Title = task.Title,
                Description = task.Description,
                CreatedDateTime = task.CreatedDateTime,
                UpdatedDateTime = task.UpdatedDateTime,
                CompletedDateTime = task.CompletedDateTime,
                ArchiveDateTime = task.ArchiveDateTime,
                PlannedBeginDateTime = task.PlannedBeginDateTime,
                PlannedEndDateTime = task.PlannedEndDateTime,
                PlannedDuration = task.PlannedDuration,
                UnlockedDateTime = task.UnlockedDateTime,
                Repeater = task.Repeater,
                Importance = task.Importance,
                Wanted = task.Wanted,
                Version = task.Version,
                IsCompleted = task.IsCompleted,
                IsCanBeCompleted = task.IsCanBeCompleted,
                ContainsTasks = task.ContainsTasks?.ToList() ?? new List<string>(),
                ParentTasks = task.ParentTasks?.ToList() ?? new List<string>(),
                BlocksTasks = task.BlocksTasks?.ToList() ?? new List<string>(),
                BlockedByTasks = task.BlockedByTasks?.ToList() ?? new List<string>(),
            };
        }
    }
}
