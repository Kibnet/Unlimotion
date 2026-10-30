using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reactive;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using ReactiveUI;
using Unlimotion;
using Unlimotion.ViewModel;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class SettingsAdmissionUiTests
{
    [Test]
    public async Task CurrentTaskDeleteConfirmation_FromOldSource_DoesNotDeleteOrClearSelection()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = new MainWindowViewModelFixture();
            var vm = fixture.MainWindowViewModelTest;
            await vm.Connect();
            var task = vm.taskRepository!.Tasks.Lookup(MainWindowViewModelFixture.RootTask1Id).Value;
            vm.CurrentTaskItem = task;
            var removed = 0;
            task.RemoveFunc = _ => { removed++; return Task.FromResult(true); };
            Action? confirm = null;
            ((NotificationManagerWrapperMock)vm.ManagerWrapper).AskHandler = yes => confirm = yes;
            vm.Remove.Execute(null);
            await Assert.That(SpinWait.SpinUntil(() => { Dispatcher.UIThread.RunJobs(); return confirm != null; }, 3000)).IsTrue();
            vm.Settings.IsTaskSpaceSwitching = true;
            vm.Settings.IsTaskSpaceSwitching = false;
            confirm!();
            Dispatcher.UIThread.RunJobs();
            await Assert.That(removed).IsEqualTo(0);
            await Assert.That(ReferenceEquals(vm.CurrentTaskItem, task)).IsTrue();
        }, CancellationToken.None);
    }

    [Test]
    public async Task ChangingLanguage_DuringSync_KeepsMutationBlockedUntilSyncCompletes()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = new MainWindowViewModelFixture();
            var settings = fixture.MainWindowViewModelTest.Settings;
            var language = settings.LanguageMode;
            try
            {
                settings.SetBackupConnectionState(BackupStatusState.Syncing);
                settings.LanguageMode = language == "ru" ? "en" : "ru";
                await Assert.That(settings.BackupConnectionState).IsEqualTo(BackupStatusState.Syncing);
                await Assert.That(settings.IsSourceMutationBlocked).IsTrue();
                await Assert.That(settings.CanEditSourceSettings).IsFalse();
                settings.SetBackupConnectionState(BackupStatusState.Connected);
                await Assert.That(settings.IsSourceMutationBlocked).IsFalse();
            }
            finally { settings.SetBackupConnectionState(BackupStatusState.NotConfigured); settings.LanguageMode = language; }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Paste_BusyDuringCreate_FinishesCreatedNodeAndBlocksSourceChanges()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = new MainWindowViewModelFixture();
            var vm = fixture.MainWindowViewModelTest;
            await vm.Connect();
            var inner = vm.taskRepository!;
            var proxy = DispatchProxy.Create<ITaskStorage, HeldAddProxy>();
            var held = (HeldAddProxy)(object)proxy;
            held.Inner = inner;
            vm.taskRepository = proxy;
            vm.CurrentTaskItem = null;
            vm.GetClipboardTextAsync = () => Task.FromResult<string?>("First node\nSecond node");
            ((NotificationManagerWrapperMock)vm.ManagerWrapper).ConfirmTaskOutlinePasteHandler = _ => Task.FromResult(true);
            var paste = vm.PasteTaskOutline();
            try
            {
                await held.Created.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await Assert.That(vm.Settings.IsTaskOutlinePasteInProgress).IsTrue();
                await Assert.That(vm.Settings.CanEditSourceSettings).IsFalse();
                vm.Settings.IsTaskDataLoading = true;
                held.Release.TrySetResult();
                await paste;
                var stored = await inner.TaskTreeManager.Storage.Load(held.CreatedId!);
                await Assert.That(stored?.Title).IsEqualTo("First node");
                await Assert.That(inner.Tasks.Items.Any(task => task.Title == "Second node")).IsFalse();
                await Assert.That(vm.Settings.IsTaskOutlinePasteInProgress).IsFalse();
            }
            finally
            {
                held.Release.TrySetResult();
                vm.Settings.IsTaskDataLoading = false;
                vm.taskRepository = inner;
            }
        }, CancellationToken.None);
    }

    public class HeldAddProxy : DispatchProxy
    {
        public ITaskStorage Inner { get; set; } = null!;
        public TaskCompletionSource Created { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? CreatedId { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var result = method!.Invoke(Inner, args);
            return method.Name == nameof(ITaskStorage.Add) ? HoldAsync((Task<TaskItemViewModel>)result!) : result;
        }
        private async Task<TaskItemViewModel> HoldAsync(Task<TaskItemViewModel> pending)
        {
            var task = await pending;
            CreatedId = task.Id;
            Created.TrySetResult();
            await Release.Task;
            return task;
        }
    }
    [Test]
    public async Task GlobalTaskCreationCommands_RejectDirectExecutionDuringLoading()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = new MainWindowViewModelFixture();
            var vm = fixture.MainWindowViewModelTest;
            await vm.Connect();
            vm.CurrentTaskItem = vm.taskRepository!.Tasks.Lookup(MainWindowViewModelFixture.RootTask1Id).Value;
            var count = vm.taskRepository.Tasks.Count;
            vm.Settings.IsTaskDataLoading = true;
            try
            {
                foreach (var (command, parameter) in new (ICommand Command, object? Parameter)[]
                {
                    (vm.Create, null), (vm.CreateSibling, false),
                    (vm.CreateBlockedSibling, null), (vm.CreateInner, null)
                })
                {
                    await Assert.That(command.CanExecute(parameter)).IsFalse();
                    command.Execute(parameter);
                }
                Dispatcher.UIThread.RunJobs();
                await Assert.That(vm.taskRepository.Tasks.Count).IsEqualTo(count);
            }
            finally { vm.Settings.IsTaskDataLoading = false; }
        }, CancellationToken.None);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DeleteConfirmationFromOldSource_DoesNotDelete(bool batch)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = new MainWindowViewModelFixture();
            var vm = fixture.MainWindowViewModelTest;
            await vm.Connect();
            Action? confirm = null;
            var notifications = (NotificationManagerWrapperMock)vm.ManagerWrapper;
            notifications.AskHandler = yes => confirm = yes;
            var count = vm.taskRepository!.Tasks.Count;
            var roots = vm.CurrentAllTasksItems.Where(wrapper => wrapper.Parent == null)
                .Take(batch ? 2 : 1).ToArray();
            await Assert.That(roots.Length).IsEqualTo(batch ? 2 : 1);
            vm.RemoveSelectedWrappers(roots);
            await Assert.That((object?)confirm).IsNotNull();
            vm.Settings.IsTaskSpaceSwitching = true;
            vm.Settings.IsTaskSpaceSwitching = false;
            confirm!();
            Dispatcher.UIThread.RunJobs();
            await Assert.That(vm.taskRepository.Tasks.Count).IsEqualTo(count);
        }, CancellationToken.None);
    }

    [Test]
    public async Task PasteConfirmationFromOldSource_DoesNotCreateTasks()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = new MainWindowViewModelFixture();
            var vm = fixture.MainWindowViewModelTest;
            await vm.Connect();
            vm.CurrentTaskItem = null;
            var count = vm.taskRepository!.Tasks.Count;
            var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ((NotificationManagerWrapperMock)vm.ManagerWrapper).ConfirmTaskOutlinePasteHandler = _ => confirmation.Task;
            vm.GetClipboardTextAsync = () => Task.FromResult<string?>("Must not be pasted");
            var paste = vm.PasteTaskOutline();
            vm.Settings.IsTaskSpaceSwitching = true;
            vm.Settings.IsTaskSpaceSwitching = false;
            confirmation.SetResult(true);
            await paste;
            await Assert.That(vm.taskRepository.Tasks.Count).IsEqualTo(count);
        }, CancellationToken.None);
    }

    [Test]
    public async Task OverlappingBackupAndLoading_RestoreStorageAvailability()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = new MainWindowViewModelFixture();
            var settings = fixture.MainWindowViewModelTest.Settings;
            settings.IsTaskDataLoading = true;
            settings.SetBackupConnectionState(BackupStatusState.Syncing);
            settings.IsTaskDataLoading = false;
            await Assert.That(settings.CanConnectStorage).IsFalse();
            settings.SetBackupConnectionState(BackupStatusState.NotConfigured);
            await Assert.That(settings.CanConnectStorage).IsTrue();
        }, CancellationToken.None);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FolderPickerFromOldSource_DoesNotApplyItsResult(bool fail)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = new MainWindowViewModelFixture();
            var settings = fixture.MainWindowViewModelTest.Settings;
            var initialPath = settings.TaskStoragePath;
            var initialState = settings.StorageConnectionState;
            var chosenPath = Path.Combine(fixture.FixtureDirectoryPath, "stale-picker");
            var dialogs = new HeldFolderPicker();
            using var factory = new SettingsViewModelTests.RecordingTaskStorageFactory(
                fixture.DefaultTasksFolderPath, new ConcurrentQueue<string>());
            var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
            using var commands = SettingsViewModelTests.ConfigureAppSettingsCommands(settings, configuration,
                new SettingsViewModelTests.FakeRemoteBackupService(), factory, dialogs);
            var command = settings.BrowseTaskStoragePathCommand!;
            command.Execute(null);
            await Assert.That(dialogs.Entered).IsTrue();
            settings.IsTaskSpaceSwitching = true;
            settings.IsTaskSpaceSwitching = false;
            if (fail) dialogs.Result.SetException(new IOException("Stale picker failure"));
            else dialogs.Result.SetResult(chosenPath);
            await Assert.That(SpinWait.SpinUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return command.CanExecute(null);
            }, 3000)).IsTrue();
            await Assert.That(settings.TaskStoragePath).IsEqualTo(initialPath);
            await Assert.That(settings.StorageConnectionState).IsEqualTo(initialState);
            await Assert.That(Directory.Exists(chosenPath)).IsFalse();
        }, CancellationToken.None);
    }

    [Test]
    [Arguments("complete")]
    [Arguments("archive")]
    [Arguments("criteria")]
    [Arguments("unblock")]
    [Arguments("relation")]
    [Arguments("save")]
    public async Task ConcreteTaskCommands_RejectWritesWithLateOwner(string action)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = new MainWindowViewModelFixture();
            var vm = fixture.MainWindowViewModelTest;
            await vm.Connect();
            var task = vm.taskRepository!.Tasks.Lookup(MainWindowViewModelFixture.RootTask1Id).Value;
            var other = vm.taskRepository.Tasks.Lookup(MainWindowViewModelFixture.RootTask2Id).Value;
            task.MainWindow = null;
            vm.CurrentTaskItem = task;
            ICommand command = action switch
            {
                "complete" => vm.CompleteCurrentTaskCommand,
                "archive" => task.ArchiveCommand,
                "criteria" => task.AddCompletionCriterionCommand,
                "unblock" => task.UnblockCommand,
                "relation" => task.DeleteParentChildRelationCommand,
                _ => task.SaveItemCommand
            };
            task.MainWindow = vm;
            await task.WaitForPendingSavesAsync();
            var before = Newtonsoft.Json.JsonConvert.SerializeObject(task.Model);
            var filePath = Path.Combine(fixture.DefaultTasksFolderPath, task.Id);
            var storedBefore = File.ReadAllText(filePath);
            vm.Settings.IsTaskDataLoading = true;
            Dispatcher.UIThread.RunJobs();
            var parameter = action is "unblock" or "relation" ? other : null;
            try
            {
                await Assert.That(command.CanExecute(parameter)).IsFalse();
                if (command is ReactiveCommand<Unit, Unit> unit) await unit.Execute().ToTask();
                else await ((ReactiveCommand<TaskItemViewModel, Unit>)command).Execute(other).ToTask();
                await Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(task.Model)).IsEqualTo(before);
                await Assert.That(File.ReadAllText(filePath)).IsEqualTo(storedBefore);
            }
            finally { vm.Settings.IsTaskDataLoading = false; Dispatcher.UIThread.RunJobs(); }
            await Assert.That(command.CanExecute(parameter)).IsTrue();
        }, CancellationToken.None);
    }

    [Test]
    public async Task AutosaveDeferredByBackup_PersistsAfterBusyWithoutAnotherEdit()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = new MainWindowViewModelFixture();
            var vm = fixture.MainWindowViewModelTest;
            await vm.Connect();
            var task = vm.taskRepository!.Tasks.Items.First();
            task.MainWindow = vm;
            task.PropertyChangedThrottleTimeSpanDefault = TimeSpan.FromMilliseconds(200);
            var title = "Deferred editor " + Guid.NewGuid().ToString("N");
            task.Title = title;
            vm.Settings.SetBackupConnectionState(BackupStatusState.Syncing);
            await Task.Delay(900);
            vm.Settings.SetBackupConnectionState(BackupStatusState.NotConfigured);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            string? storedTitle;
            do
            {
                await Task.Delay(25);
                storedTitle = (await vm.taskRepository.TaskTreeManager.Storage.Load(task.Id))?.Title;
            } while (storedTitle != title && DateTime.UtcNow < deadline);
            await Assert.That(storedTitle).IsEqualTo(title);
        }, CancellationToken.None);
    }

    private sealed class HeldFolderPicker : IDialogs
    {
        public bool Entered { get; private set; }
        public TaskCompletionSource<string> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> ShowOpenFolderDialogAsync(string? title = null, string? directory = null)
        {
            Entered = true;
            return Result.Task;
        }
    }
}
