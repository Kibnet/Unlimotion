using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public sealed class TaskAvailabilityParityTests
{
    [Test]
    public async Task AnalyzerAndEngineTransitionRulesUseSameAvailabilityDecisions()
    {
        foreach (var scenario in CreateScenarios())
        {
            var analyzer = new TaskAvailabilityAnalyzer(scenario.Tasks);
            var service = new TaskAvailabilityService(scenario.Tasks);

            var analyzerAnalysis = analyzer.Analyze(scenario.SubjectId);
            var serviceAnalysis = service.Analyze(scenario.SubjectId);
            var subject = scenario.Tasks.Single(task => task.Id == scenario.SubjectId);
            var startDecision = service.EvaluateStatusTransition(subject, DomainTaskStatus.InProgress);
            var completeDecision = service.EvaluateStatusTransition(subject, DomainTaskStatus.Completed);

            await Assert.That(serviceAnalysis.IsCanBeCompleted)
                .IsEqualTo(analyzerAnalysis.IsCanBeCompleted)
                .Because(scenario.Name);
            await Assert.That(serviceAnalysis.CanStart)
                .IsEqualTo(analyzerAnalysis.CanStart)
                .Because(scenario.Name);
            await Assert.That(serviceAnalysis.CanComplete)
                .IsEqualTo(analyzerAnalysis.CanComplete)
                .Because(scenario.Name);
            await Assert.That(startDecision.Allowed)
                .IsEqualTo(analyzerAnalysis.CanStart)
                .Because(scenario.Name);
            await Assert.That(completeDecision.Allowed)
                .IsEqualTo(analyzerAnalysis.CanComplete)
                .Because(scenario.Name);
        }
    }

    private static IReadOnlyList<ParityScenario> CreateScenarios()
    {
        var available = CreateTask("available", DomainTaskStatus.Prepared);

        var parent = CreateTask("parent", DomainTaskStatus.Prepared);
        parent.ContainsTasks.Add("child");
        var child = CreateTask("child", DomainTaskStatus.Prepared);
        child.ParentTasks.Add("parent");

        var blocker = CreateTask("blocker", DomainTaskStatus.Prepared);
        blocker.BlocksTasks.Add("blocked");
        var blocked = CreateTask("blocked", DomainTaskStatus.Prepared);
        blocked.BlockedByTasks.Add("blocker");

        var inheritedBlocker = CreateTask("inherited-blocker", DomainTaskStatus.Prepared);
        inheritedBlocker.BlocksTasks.Add("blocked-parent");
        var blockedParent = CreateTask("blocked-parent", DomainTaskStatus.Prepared);
        blockedParent.BlockedByTasks.Add("inherited-blocker");
        blockedParent.ContainsTasks.Add("descendant");
        var descendant = CreateTask("descendant", DomainTaskStatus.Prepared);
        descendant.ParentTasks.Add("blocked-parent");

        var criteria = CreateTask("criteria", DomainTaskStatus.Prepared);
        criteria.CompletionCriteria.Add(new TaskCompletionCriterion
        {
            Id = "criterion",
            Text = "Check result",
            IsSatisfied = false
        });

        var future = CreateTask("future", DomainTaskStatus.Prepared);
        future.PlannedBeginDateTime = DateTimeOffset.UtcNow.AddDays(1);

        var completed = CreateTask("completed", DomainTaskStatus.Completed);
        var archived = CreateTask("archived", DomainTaskStatus.Archived);

        return
        [
            new ParityScenario("available task", [available], available.Id),
            new ParityScenario("contained incomplete task", [parent, child], parent.Id),
            new ParityScenario("direct blocker", [blocker, blocked], blocked.Id),
            new ParityScenario("inherited blocker", [inheritedBlocker, blockedParent, descendant], descendant.Id),
            new ParityScenario("unsatisfied criteria", [criteria], criteria.Id),
            new ParityScenario("future planned begin", [future], future.Id),
            new ParityScenario("completed terminal status", [completed], completed.Id),
            new ParityScenario("archived terminal status", [archived], archived.Id)
        ];
    }

    private static TaskItem CreateTask(string id, DomainTaskStatus status) => new()
    {
        Id = id,
        Title = id,
        Description = string.Empty,
        UserId = "test-user",
        Status = status,
        IsCanBeCompleted = true,
        ContainsTasks = new List<string>(),
        ParentTasks = new List<string>(),
        BlocksTasks = new List<string>(),
        BlockedByTasks = new List<string>(),
        CompletionCriteria = new List<TaskCompletionCriterion>()
    };

    private sealed record ParityScenario(
        string Name,
        IReadOnlyList<TaskItem> Tasks,
        string SubjectId);
}
