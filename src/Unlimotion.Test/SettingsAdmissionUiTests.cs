using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
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
