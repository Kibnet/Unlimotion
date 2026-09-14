using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.ViewModel;
using System.Windows.Input;
using Unlimotion.TaskTree;
using System.Reactive.Linq;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class StartupProjectionAndRelationsTests : BaseModelTests
{
    [Test]
    public async Task DeferredCardCommands_ConcurrentFirstAccessPublishesOneInstance()
    {
        using var task = new TaskItemViewModel(new TaskItem { Id = "commands", Title = "Task" }, taskRepository, () => false);
        var commands = new ICommand[32];
        Parallel.For(0, commands.Length, index => commands[index] = task.ArchiveCommand);
        await Assert.That(commands.All(command => ReferenceEquals(command, commands[0]))).IsTrue();
    }

    [Test]
    public async Task DeferredCardCommands_FirstAccessUsesCurrentStatusAndPreservesOverrides()
    {
        using var task = new TaskItemViewModel(new TaskItem { Id = "commands", Title = "Task" }, taskRepository, () => false);
        task.Status = Unlimotion.Domain.TaskStatus.Completed;
        await Assert.That(task.ArchiveCommand.CanExecute(null)).IsFalse();
        await Assert.That(task.AddCompletionCriterionCommand.CanExecute(null)).IsFalse();
        task.Status = Unlimotion.Domain.TaskStatus.Prepared;
        await Assert.That(task.ArchiveCommand.CanExecute(null)).IsTrue();
        await Assert.That(task.AddCompletionCriterionCommand.CanExecute(null)).IsTrue();
        var replacement = new ExternalCommand();
        task.UnblockCommand = replacement;
        await Assert.That(task.UnblockCommand).IsSameReferenceAs(replacement);
        task.Dispose();
        await Assert.That(replacement.Disposed).IsFalse();
    }

    [Test]
    public async Task DeferredCardCommands_AccessAfterDisposeDoesNotLeaveActiveSubscriptions()
    {
        var task = new TaskItemViewModel(new TaskItem { Id = "commands", Title = "Task" }, taskRepository, () => false);
        task.Dispose();
        var command = task.ArchiveCommand;
        var notifications = 0;
        command.CanExecuteChanged += (_, _) => notifications++;
        task.Status = Unlimotion.Domain.TaskStatus.Completed;
        task.Status = Unlimotion.Domain.TaskStatus.Prepared;
        await Assert.That(notifications).IsEqualTo(0);
    }

    private sealed class ExternalCommand : ICommand, IDisposable
    {
        public bool Disposed { get; private set; }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
        public void Dispose() => Disposed = true;
    }

    [Test]
    public async Task UnopenedTaskCards_ShouldStayWithinAllocationBudget()
    {
        using var warmup = new TaskItemViewModel(new TaskItem { Id = "warmup", Title = "Task" }, taskRepository, () => false);
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var tasks = Enumerable.Range(0, 128)
            .Select(index => new TaskItemViewModel(new TaskItem { Id = $"allocation-{index}", Title = "Task" }, taskRepository, () => false))
            .ToArray();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        foreach (var task in tasks) task.Dispose();
        Console.WriteLine($"Unopened task cards allocated {allocated} bytes.");
        await Assert.That(allocated).IsLessThan(24L * 1024 * 1024);
    }

    [Test]
    public async Task DurationMenu_ShouldReuseCommandsAndUpdateTheTaskOnFirstUse()
    {
        using var task = new TaskItemViewModel(new TaskItem { Id = "duration", Title = "Task" }, taskRepository, () => false);
        var commands = task.SetDurationCommands;
        await Assert.That(task.SetDurationCommands).IsSameReferenceAs(commands);
        await Assert.That(commands.OneHourCommand.CanExecute(null)).IsTrue();
        commands.OneHourCommand.Execute(null);
        await Assert.That(task.PlannedDuration).IsEqualTo(TimeSpan.FromHours(1));
        await Assert.That(commands.NoneCommand.CanExecute(null)).IsTrue();
        commands.NoneCommand.Execute(null);
        await Assert.That(task.PlannedDuration).IsNull();
    }

    [Test]
    public async Task TaskRelationsIndex_ShouldSynchronizeRelationCollectionsWithIds()
    {
        foreach (var task in taskRepository.Tasks.Items)
        {
            var containsIds = task.ContainsTasks.Select(item => item.Id).ToHashSet();
            var parentIds = task.ParentsTasks.Select(item => item.Id).ToHashSet();
            var blocksIds = task.BlocksTasks.Select(item => item.Id).ToHashSet();
            var blockedByIds = task.BlockedByTasks.Select(item => item.Id).ToHashSet();

            await Assert.That(containsIds.SetEquals(task.Contains)).IsTrue();
            await Assert.That(parentIds.SetEquals(task.Parents)).IsTrue();
            await Assert.That(blocksIds.SetEquals(task.Blocks)).IsTrue();
            await Assert.That(blockedByIds.SetEquals(task.BlockedBy)).IsTrue();
        }
    }

    [Test]
    public async Task HeavyProjections_ShouldBeLoadedLazilyAfterTabActivation()
    {
        await Assert.That(mainWindowVM.CompletedItems).IsEmpty();
        await Assert.That(mainWindowVM.Graph.Tasks).IsEmpty();
        await Assert.That(mainWindowVM.Graph.UnlockedTasks).IsEmpty();
        mainWindowVM.GraphMode = true;

        var graphReady = SpinWait.SpinUntil(() => mainWindowVM.Graph.Tasks.Count > 0, TimeSpan.FromSeconds(3));
        await Assert.That(graphReady).IsTrue();
    }
}
