using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public class TaskAvailabilityAnalyzerTests
{
    [Test]
    public async Task Analyze_DeepAncestorChain_UsesAllParentsWithoutCallStackRecursion()
    {
        var tasks = Enumerable.Range(0, 20_000).Select(index => CreateTask("depth-" + index, DomainTaskStatus.Completed)).ToArray();
        for (var index = 1; index < tasks.Length; index++) tasks[index].ParentTasks.Add(tasks[index - 1].Id);
        var blocker = CreateTask("blocker", DomainTaskStatus.Prepared);
        tasks[0].BlockedByTasks.Add(blocker.Id);
        var result = new TaskAvailabilityAnalyzer(tasks.Append(blocker)).Analyze(tasks[^1]);
        await Assert.That(result.Reasons.Any(reason => reason.SubjectId == "blocker" && reason.SourceTaskId == "depth-0")).IsTrue();
        await Assert.That(result.IsCanBeCompleted).IsFalse();
    }

    [Test]
    public async Task Validate_DeepDependencyCycle_IsDiagnosedWithoutCallStackRecursion()
    {
        var tasks = Enumerable.Range(0, 20_000).Select(index => CreateTask("block-" + index, DomainTaskStatus.Completed)).ToArray();
        for (var index = 0; index < tasks.Length; index++)
        {
            var next = tasks[(index + 1) % tasks.Length];
            tasks[index].BlocksTasks.Add(next.Id);
            next.BlockedByTasks.Add(tasks[index].Id);
        }
        var result = new TaskAvailabilityAnalyzer(tasks).Validate();
        await Assert.That(result.ReferenceIssues.Any(issue => issue.Kind == TaskGraphReferenceIssueKind.DependencyCycle)).IsTrue();
    }

    [Test]
    public async Task Analyze_FixedEvaluationClock_RespectsExactPlannedBeginBoundary()
    {
        var at = System.DateTimeOffset.Parse("2026-10-04T12:00:00Z");
        var task = CreateTask("clock", DomainTaskStatus.Prepared);
        task.PlannedBeginDateTime = at;
        await Assert.That(new TaskAvailabilityAnalyzer(new[] { task }, at.AddTicks(-1)).Analyze(task).CanStart).IsFalse();
        await Assert.That(new TaskAvailabilityAnalyzer(new[] { task }, at).Analyze(task).CanStart).IsTrue();
        using var cancelled = new System.Threading.CancellationTokenSource();
        cancelled.Cancel();
        await Assert.That(() => new TaskAvailabilityAnalyzer(new[] { task }, at, cancelled.Token).Analyze(task)).Throws<System.OperationCanceledException>();
    }

    [Test]
    public async Task Analyze_ParentWithIncompleteContainedTask_IsNotStartable()
    {
        var parent = CreateTask(
            "parent",
            DomainTaskStatus.Prepared,
            containsTasks: new List<string> { "child" });
        var child = CreateTask(
            "child",
            DomainTaskStatus.InProgress,
            parentTasks: new List<string> { "parent" });

        var analysis = new TaskAvailabilityAnalyzer(new[] { parent, child }).Analyze("parent");

        await Assert.That(analysis.IsCanBeCompleted).IsFalse();
        await Assert.That(analysis.CanStart).IsFalse();
        await Assert.That(analysis.Reasons.Any(static reason => reason.Kind == TaskAvailabilityReasonKind.IncompleteContainedTask)).IsTrue();
    }

    [Test]
    public async Task Analyze_ChildWithParentBlockedByIncompleteTask_InheritsBlocker()
    {
        var blocker = CreateTask("blocker", DomainTaskStatus.NotReady);
        var parent = CreateTask(
            "parent",
            DomainTaskStatus.Prepared,
            blockedByTasks: new List<string> { "blocker" });
        var child = CreateTask(
            "child",
            DomainTaskStatus.Prepared,
            parentTasks: new List<string> { "parent" });

        var analysis = new TaskAvailabilityAnalyzer(new[] { blocker, parent, child }).Analyze("child");

        await Assert.That(analysis.IsCanBeCompleted).IsFalse();
        await Assert.That(analysis.CanStart).IsFalse();
        await Assert.That(analysis.Reasons.Any(static reason =>
            reason.Kind == TaskAvailabilityReasonKind.IncompleteInheritedBlocker &&
            reason.SubjectId == "blocker" &&
            reason.SourceTaskId == "parent")).IsTrue();
    }

    [Test]
    public async Task Analyze_UnsatisfiedCriteria_BlockCompletionButNotStartOrAvailability()
    {
        var task = CreateTask("task", DomainTaskStatus.Prepared);
        task.CompletionCriteria = new List<TaskCompletionCriterion>
        {
            new()
            {
                Id = "criterion",
                Text = "Check real outcome",
                IsSatisfied = false
            }
        };

        var analysis = new TaskAvailabilityAnalyzer(new[] { task }).Analyze("task");

        await Assert.That(analysis.IsCanBeCompleted).IsTrue();
        await Assert.That(analysis.CanStart).IsTrue();
        await Assert.That(analysis.CanComplete).IsFalse();
        await Assert.That(analysis.Reasons.Any(static reason => reason.Kind == TaskAvailabilityReasonKind.UnsatisfiedCriterion)).IsTrue();
    }

    [Test]
    public async Task Validate_ReportsMissingReverseLinkAndAvailabilityMismatch()
    {
        var parent = CreateTask(
            "parent",
            DomainTaskStatus.Prepared,
            isCanBeCompleted: true,
            containsTasks: new List<string> { "child" });
        var child = CreateTask("child", DomainTaskStatus.NotReady);

        var validation = new TaskAvailabilityAnalyzer(new[] { parent, child }).Validate();

        await Assert.That(validation.IsValid).IsFalse();
        await Assert.That(validation.ReferenceIssues.Any(static issue =>
            issue.Kind == TaskGraphReferenceIssueKind.MissingReverseLink &&
            issue.SourceTaskId == "parent" &&
            issue.TargetTaskId == "child" &&
            issue.InverseRelation == nameof(TaskItem.ParentTasks))).IsTrue();
        await Assert.That(validation.AvailabilityMismatches.Any(static mismatch =>
            mismatch.TaskId == "parent" &&
            mismatch.StoredIsCanBeCompleted &&
            !mismatch.ComputedIsCanBeCompleted)).IsTrue();
    }

    private static TaskItem CreateTask(
        string id,
        DomainTaskStatus status,
        bool isCanBeCompleted = true,
        List<string>? containsTasks = null,
        List<string>? parentTasks = null,
        List<string>? blocksTasks = null,
        List<string>? blockedByTasks = null) =>
        new()
        {
            Id = id,
            Title = id,
            Description = string.Empty,
            UserId = "test-user",
            Status = status,
            IsCanBeCompleted = isCanBeCompleted,
            ContainsTasks = containsTasks ?? new List<string>(),
            ParentTasks = parentTasks ?? new List<string>(),
            BlocksTasks = blocksTasks ?? new List<string>(),
            BlockedByTasks = blockedByTasks ?? new List<string>(),
            CompletionCriteria = new List<TaskCompletionCriterion>()
        };
}
