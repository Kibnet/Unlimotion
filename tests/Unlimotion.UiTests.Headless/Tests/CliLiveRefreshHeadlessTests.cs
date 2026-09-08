using System.Diagnostics;
using System.Reflection;
using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using DynamicData;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.Domain;
using Unlimotion.Storage;
using Unlimotion.UiTests.Authoring.Pages;
using Unlimotion.ViewModel;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.UiTests.Headless.Tests;

[NotInParallel("DesktopUi")]
public sealed class CliLiveRefreshHeadlessTests
{
    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(15);

    [Test]
    public async Task CliChanges_RefreshOpenDesktopProjection()
    {
        MainWindowViewModel? capturedViewModel = null;
        using var session = DesktopAppSession.Launch(
            UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
                UnlimotionAutomationScenario.CliLiveRefresh,
                language: "en",
                afterViewModelPrepared: viewModel => capturedViewModel = viewModel));

        var viewModel = capturedViewModel
            ?? throw new InvalidOperationException("CLI live-refresh view model was not captured.");
        var storage = viewModel.taskRepository as UnifiedTaskStorage
            ?? throw new InvalidOperationException("CLI live-refresh scenario did not use UnifiedTaskStorage.");
        BindHeadlessSynchronizationContext(storage);
        var fileStorage = storage.TaskTreeManager.Storage as FileStorage
            ?? throw new InvalidOperationException("CLI live-refresh scenario did not use FileStorage.");
        var persistedReader = CreateExternalStorage(fileStorage.Path);
        var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow));
        var statusPicker = GetNativeControl<TaskStatusPicker>(page.CurrentTaskStatusButton);
        var allTasksTree = GetNativeControl<TreeView>(page.AllTasksTree);
        var completionCriteriaItems = GetNativeControl<ItemsControl>(page.CompletionCriteriaItems);

        await RunCliAsync("set-status", fileStorage.Path, "--id", UnlimotionAutomationScenarioData.CliLiveRefreshTaskId, "--status", "InProgress");
        await AssertPersistedTaskAsync(
            persistedReader,
            task => task.Status == DomainTaskStatus.InProgress,
            "set-status must persist InProgress before the desktop projection refreshes.");
        await WaitForUiAsync(
            () => CurrentTask(viewModel).Status == DomainTaskStatus.InProgress &&
                  statusPicker.Task?.Status == DomainTaskStatus.InProgress &&
                  RenderedTaskHasStatus(
                      allTasksTree,
                      UnlimotionAutomationScenarioData.CliLiveRefreshTaskId,
                      DomainTaskStatus.InProgress),
            () => DescribeRefreshState(fileStorage, storage, viewModel));

        await RunCliAsync("set-criterion", fileStorage.Path, "--id", UnlimotionAutomationScenarioData.CliLiveRefreshTaskId, "--criterion", "criterion-one", "--satisfied", "true");
        await AssertPersistedTaskAsync(
            persistedReader,
            task => task.CompletionCriteria.Single(criterion => criterion.Id == "criterion-one").IsSatisfied,
            "set-criterion must persist criterion-one before the desktop projection refreshes.");
        await WaitForUiAsync(
            () => CurrentTask(viewModel).CompletionCriteria.Single(criterion => criterion.Id == "criterion-one").IsSatisfied &&
                  RenderedCriterionIsSatisfied(completionCriteriaItems, "criterion-one") &&
                  !RenderedCriterionIsSatisfied(completionCriteriaItems, "criterion-two"),
            () => DescribeRefreshState(fileStorage, storage, viewModel));
        await Assert.That(GetStatusOptionEnabled(statusPicker, "TaskStatusOptionCompleted"))
            .IsFalse()
            .Because("Completion must remain disabled while one rendered criterion is unsatisfied.");

        await RunCliAsync("satisfy-criterion", fileStorage.Path, "--id", UnlimotionAutomationScenarioData.CliLiveRefreshTaskId, "--criterion", "criterion-two");
        await AssertPersistedTaskAsync(
            persistedReader,
            task => task.CompletionCriteria.All(criterion => criterion.IsSatisfied),
            "satisfy-criterion must persist both satisfied criteria before the desktop projection refreshes.");
        await WaitForUiAsync(
            () => CurrentTask(viewModel).CompletionCriteria.All(criterion => criterion.IsSatisfied) &&
                  RenderedCriterionIsSatisfied(completionCriteriaItems, "criterion-one") &&
                  RenderedCriterionIsSatisfied(completionCriteriaItems, "criterion-two"),
            () => DescribeRefreshState(fileStorage, storage, viewModel));
        await Assert.That(GetStatusOptionEnabled(statusPicker, "TaskStatusOptionCompleted"))
            .IsTrue()
            .Because("Completion must become enabled after both rendered criteria are satisfied.");

        await RunCliAsync("complete", fileStorage.Path, "--id", UnlimotionAutomationScenarioData.CliLiveRefreshTaskId);
        await AssertPersistedTaskAsync(
            persistedReader,
            task => task.Status == DomainTaskStatus.Completed,
            "complete must persist Completed before the desktop projection refreshes.");
        await WaitForUiAsync(
            () => CurrentTask(viewModel).Status == DomainTaskStatus.Completed &&
                  statusPicker.Task?.Status == DomainTaskStatus.Completed &&
                  RenderedTaskHasStatus(
                      allTasksTree,
                      UnlimotionAutomationScenarioData.CliLiveRefreshTaskId,
                      DomainTaskStatus.Completed),
            () => DescribeRefreshState(fileStorage, storage, viewModel));

    }

    [Test]
    public async Task ExternalWriterCreateRelationAndDelete_RefreshOpenDesktopProjection()
    {
        MainWindowViewModel? capturedViewModel = null;
        using var session = DesktopAppSession.Launch(
            UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
                UnlimotionAutomationScenario.CliLiveRefresh,
                language: "en",
                afterViewModelPrepared: viewModel => capturedViewModel = viewModel));

        var viewModel = capturedViewModel
            ?? throw new InvalidOperationException("External graph view model was not captured.");
        var storage = viewModel.taskRepository as UnifiedTaskStorage
            ?? throw new InvalidOperationException("External graph scenario did not use UnifiedTaskStorage.");
        BindHeadlessSynchronizationContext(storage);
        var fileStorage = storage.TaskTreeManager.Storage as FileStorage
            ?? throw new InvalidOperationException("External graph scenario did not use FileStorage.");
        var writer = CreateExternalStorage(fileStorage.Path);
        var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow));
        var titleTextBox = GetNativeControl<TextBox>(page.CurrentTaskTitleTextBox);
        var containsTree = GetNativeControl<TreeView>(page.CurrentItemContainsTree);
        var parentsTree = GetNativeControl<TreeView>(page.CurrentItemParentsTree);
        var child = CreateTask(UnlimotionAutomationScenarioData.CliLiveRefreshChildTaskId, "External graph child");
        await writer.Save(child);
        await Assert.That((await RequireTaskAsync(writer, child.Id)).Id).IsEqualTo(child.Id);
        await WaitForUiAsync(() => TryGetTask(storage, child.Id, out _));
        SelectCurrentTask(viewModel, RequireTask(storage, child.Id));
        await WaitForUiAsync(() => string.Equals(
            titleTextBox.Text,
            child.Title,
            StringComparison.Ordinal));

        await writer.WithDirectoryLockAsync(async () =>
        {
            var parent = await RequireTaskAsync(writer, UnlimotionAutomationScenarioData.CliLiveRefreshParentTaskId);
            parent.ContainsTasks = [child.Id];
            child.ParentTasks = [parent.Id];
            await writer.Save(parent);
            await writer.Save(child);
        }).WaitAsync(RefreshTimeout);
        var persistedParent = await RequireTaskAsync(writer, UnlimotionAutomationScenarioData.CliLiveRefreshParentTaskId);
        var persistedChild = await RequireTaskAsync(writer, child.Id);
        await Assert.That(persistedParent.ContainsTasks).Contains(child.Id);
        await Assert.That(persistedChild.ParentTasks)
            .Contains(UnlimotionAutomationScenarioData.CliLiveRefreshParentTaskId);
        await WaitForUiAsync(() =>
        {
            var parent = RequireTask(storage, UnlimotionAutomationScenarioData.CliLiveRefreshParentTaskId);
            var currentChild = RequireTask(storage, child.Id);
            return parent.ContainsTasks.Any(item => item.Id == child.Id) &&
                   currentChild.ParentsTasks.Any(item => item.Id == parent.Id);
        });
        SelectCurrentTask(viewModel, RequireTask(storage, UnlimotionAutomationScenarioData.CliLiveRefreshParentTaskId));
        await WaitForUiAsync(() => ContainsRenderedTask(containsTree, child.Id));
        SelectCurrentTask(viewModel, RequireTask(storage, child.Id));
        await WaitForUiAsync(() => ContainsRenderedTask(
            parentsTree,
            UnlimotionAutomationScenarioData.CliLiveRefreshParentTaskId));
        SelectCurrentTask(viewModel, RequireTask(storage, UnlimotionAutomationScenarioData.CliLiveRefreshParentTaskId));

        await writer.WithDirectoryLockAsync(async () =>
        {
            var parent = await RequireTaskAsync(writer, UnlimotionAutomationScenarioData.CliLiveRefreshParentTaskId);
            parent.ContainsTasks = [];
            await writer.Save(parent);
            await writer.Remove(child.Id);
        }).WaitAsync(RefreshTimeout);
        persistedParent = await RequireTaskAsync(writer, UnlimotionAutomationScenarioData.CliLiveRefreshParentTaskId);
        await Assert.That(persistedParent.ContainsTasks).DoesNotContain(child.Id);
        await WaitForUiAsync(() =>
        {
            var parent = RequireTask(storage, UnlimotionAutomationScenarioData.CliLiveRefreshParentTaskId);
            return !TryGetTask(storage, child.Id, out _) &&
                   parent.ContainsTasks.All(item => item.Id != child.Id) &&
                   !ContainsRenderedTask(containsTree, child.Id);
        });

        await Assert.That(File.Exists(Path.Combine(fileStorage.Path, child.Id))).IsFalse();
    }

    private static TaskItemViewModel CurrentTask(MainWindowViewModel viewModel) =>
        viewModel.CurrentTaskItem ?? throw new InvalidOperationException("Current task disappeared during CLI refresh test.");

    private static TControl GetNativeControl<TControl>(IUiControl wrappedControl)
        where TControl : Control
    {
        var automationElement = wrappedControl.GetType()
            .GetProperty("Inner", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(wrappedControl)
            ?? throw new InvalidOperationException($"Headless wrapper '{wrappedControl.AutomationId}' did not expose its native element.");
        return automationElement.GetType()
            .GetProperty("Control", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(automationElement) as TControl
            ?? throw new InvalidOperationException($"Headless control '{wrappedControl.AutomationId}' was not a {typeof(TControl).Name}.");
    }

    private static async Task RunCliAsync(string command, string tasksPath, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(typeof(Unlimotion.Cli.Program).Assembly.Location);
        startInfo.ArgumentList.Add(command);
        startInfo.ArgumentList.Add("--tasks");
        startInfo.ArgumentList.Add(tasksPath);
        startInfo.ArgumentList.AddRange(arguments);
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("json");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start CLI command '{command}'.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var output = await standardOutput;
        var error = await standardError;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"CLI command '{command}' failed with exit code {process.ExitCode}. stdout={output}; stderr={error}");
        }
    }

    private static async Task WaitForUiAsync(Func<bool> condition, Func<string>? diagnostic = null)
    {
        var deadline = DateTimeOffset.UtcNow + RefreshTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (HeadlessRuntime.Dispatch(() =>
                {
                    Dispatcher.UIThread.RunJobs();
                    return condition();
                }))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException(
            "Desktop UI did not converge to the externally persisted task state. " +
            (diagnostic?.Invoke() ?? string.Empty));
    }

    private static void BindHeadlessSynchronizationContext(UnifiedTaskStorage storage)
    {
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new HeadlessSynchronizationContext());
            storage.BindToCurrentSynchronizationContext();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static void SelectCurrentTask(MainWindowViewModel viewModel, TaskItemViewModel task) =>
        HeadlessRuntime.Dispatch(() => viewModel.CurrentTaskItem = task);

    private static bool ContainsRenderedTask(TreeView tree, string taskId) =>
        tree.ItemsSource?.OfType<TaskWrapperViewModel>().Any(item =>
            string.Equals(item.Id, taskId, StringComparison.Ordinal)) == true;

    private static bool RenderedTaskHasStatus(TreeView tree, string taskId, DomainTaskStatus status) =>
        tree.ItemsSource?.OfType<TaskWrapperViewModel>().Any(item =>
            string.Equals(item.Id, taskId, StringComparison.Ordinal) &&
            item.TaskItem.Status == status) == true;

    private static bool RenderedCriterionIsSatisfied(ItemsControl items, string criterionId) =>
        items.ItemsSource?.OfType<TaskCompletionCriterion>().SingleOrDefault(criterion =>
            string.Equals(criterion.Id, criterionId, StringComparison.Ordinal))?.IsSatisfied == true;

    private static bool GetStatusOptionEnabled(TaskStatusPicker statusPicker, string automationId)
    {
        return HeadlessRuntime.Dispatch(() =>
        {
            var onClick = FindMethod(statusPicker.GetType(), "OnClick")
                ?? throw new InvalidOperationException("TaskStatusPicker did not expose OnClick.");
            onClick.Invoke(statusPicker, []);
            Dispatcher.UIThread.RunJobs();
            try
            {
                var flyout = statusPicker.Flyout as MenuFlyout
                    ?? throw new InvalidOperationException("Task status flyout was not created.");
                var option = flyout.Items.OfType<MenuItem>().Single(item => string.Equals(
                    AutomationProperties.GetAutomationId(item),
                    automationId,
                    StringComparison.Ordinal));
                return option.IsEnabled;
            }
            finally
            {
                statusPicker.Flyout?.Hide();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    private static MethodInfo? FindMethod(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (method is not null)
            {
                return method;
            }
        }

        return null;
    }

    private static string DescribeRefreshState(
        FileStorage fileStorage,
        UnifiedTaskStorage storage,
        MainWindowViewModel viewModel)
    {
        var current = CurrentTask(viewModel);
        var cached = RequireTask(storage, UnlimotionAutomationScenarioData.CliLiveRefreshTaskId);
        var path = Path.Combine(fileStorage.Path, UnlimotionAutomationScenarioData.CliLiveRefreshTaskId);
        return $"file={File.ReadAllText(path)}; " +
               $"liveRevision={fileStorage.LiveGraphRevision}; " +
               $"current={current.Status};criteria={string.Join(',', current.CompletionCriteria.Select(item => $"{item.Id}:{item.IsSatisfied}"))}; " +
               $"cached={cached.Status};criteria={string.Join(',', cached.CompletionCriteria.Select(item => $"{item.Id}:{item.IsSatisfied}"))}";
    }

    private static async Task<TaskItem> RequireTaskAsync(FileTaskStorage storage, string id) =>
        await storage.Load(id, forced: true)
        ?? throw new InvalidOperationException($"Persisted task '{id}' was not found.");

    private static FileTaskStorage CreateExternalStorage(string path) =>
        new(new FileTaskStorageOptions
        {
            Path = path,
            UseDirectoryLock = true,
            PreserveUnknownJson = true
        });

    private static async Task AssertPersistedTaskAsync(
        FileTaskStorage storage,
        Func<TaskItem, bool> condition,
        string because)
    {
        var task = await RequireTaskAsync(storage, UnlimotionAutomationScenarioData.CliLiveRefreshTaskId);
        await Assert.That(condition(task)).IsTrue().Because(because);
    }

    private static TaskItemViewModel RequireTask(UnifiedTaskStorage storage, string id) =>
        TryGetTask(storage, id, out var task)
            ? task
            : throw new InvalidOperationException($"Desktop task '{id}' was not found.");

    private static bool TryGetTask(UnifiedTaskStorage storage, string id, out TaskItemViewModel task)
    {
        var lookup = storage.Tasks.Lookup(id);
        task = lookup.HasValue ? lookup.Value : null!;
        return lookup.HasValue;
    }

    private static TaskItem CreateTask(string id, string title)
    {
        var now = DateTimeOffset.UtcNow;
        return new TaskItem
        {
            Id = id,
            Title = title,
            Description = "Task created by external writer.",
            Status = DomainTaskStatus.Prepared,
            StatusHistory =
            [
                new TaskStatusHistoryEntry
                {
                    Status = DomainTaskStatus.Prepared,
                    ChangedAt = now,
                    Author = "test"
                }
            ],
            IsCanBeCompleted = true,
            CreatedDateTime = now,
            UpdatedDateTime = now,
            Version = 1
        };
    }

    private sealed class HeadlessSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) =>
            HeadlessRuntime.Dispatch(() => callback(state));
    }
}
