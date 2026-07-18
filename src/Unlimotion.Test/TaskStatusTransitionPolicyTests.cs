using System.Threading.Tasks;
using Unlimotion.Domain;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public sealed class TaskStatusTransitionPolicyTests
{
    [Test]
    public async Task DenialReasonNumericContract_IsStable()
    {
        await Assert.That((int)TaskStatusTransitionDenialReason.None).IsEqualTo(0);
        await Assert.That((int)TaskStatusTransitionDenialReason.TerminalCannotStart).IsEqualTo(1);
        await Assert.That((int)TaskStatusTransitionDenialReason.GraphUnavailableForStart).IsEqualTo(2);
        await Assert.That((int)TaskStatusTransitionDenialReason.FutureDatePreventsStart).IsEqualTo(3);
        await Assert.That((int)TaskStatusTransitionDenialReason.TerminalCannotComplete).IsEqualTo(4);
        await Assert.That((int)TaskStatusTransitionDenialReason.GraphUnavailableForCompletion).IsEqualTo(5);
        await Assert.That((int)TaskStatusTransitionDenialReason.CompletionCriteriaIncomplete).IsEqualTo(6);
        await Assert.That((int)TaskStatusTransitionDenialReason.CompletedCannotArchive).IsEqualTo(7);
        await Assert.That((int)TaskStatusTransitionDenialReason.InvalidTargetStatus).IsEqualTo(8);
    }

    [Test]
    [Arguments(DomainTaskStatus.NotReady, DomainTaskStatus.NotReady, true, TaskStatusTransitionDenialReason.None)]
    [Arguments(DomainTaskStatus.NotReady, DomainTaskStatus.Prepared, true, TaskStatusTransitionDenialReason.None)]
    [Arguments(DomainTaskStatus.NotReady, DomainTaskStatus.InProgress, true, TaskStatusTransitionDenialReason.None)]
    [Arguments(DomainTaskStatus.NotReady, DomainTaskStatus.Completed, true, TaskStatusTransitionDenialReason.None)]
    [Arguments(DomainTaskStatus.NotReady, DomainTaskStatus.Archived, true, TaskStatusTransitionDenialReason.None)]
    [Arguments(DomainTaskStatus.Prepared, DomainTaskStatus.NotReady, true, TaskStatusTransitionDenialReason.None)]
    [Arguments(DomainTaskStatus.Prepared, DomainTaskStatus.Prepared, true, TaskStatusTransitionDenialReason.None)]
    [Arguments(DomainTaskStatus.Prepared, DomainTaskStatus.InProgress, true, TaskStatusTransitionDenialReason.None)]
    [Arguments(DomainTaskStatus.Prepared, DomainTaskStatus.Completed, true, TaskStatusTransitionDenialReason.None)]
    [Arguments(DomainTaskStatus.Prepared, DomainTaskStatus.Archived, true, TaskStatusTransitionDenialReason.None)]
    [Arguments(DomainTaskStatus.InProgress, DomainTaskStatus.NotReady, true, TaskStatusTransitionDenialReason.None)]
    [Arguments(DomainTaskStatus.InProgress, DomainTaskStatus.Prepared, true, TaskStatusTransitionDenialReason.None)]
    [Arguments(DomainTaskStatus.InProgress, DomainTaskStatus.InProgress, true, TaskStatusTransitionDenialReason.None)]
    [Arguments(DomainTaskStatus.InProgress, DomainTaskStatus.Completed, true, TaskStatusTransitionDenialReason.None)]
    [Arguments(DomainTaskStatus.InProgress, DomainTaskStatus.Archived, true, TaskStatusTransitionDenialReason.None)]
    [Arguments(DomainTaskStatus.Completed, DomainTaskStatus.NotReady, true, TaskStatusTransitionDenialReason.None)]
    [Arguments(DomainTaskStatus.Completed, DomainTaskStatus.Prepared, true, TaskStatusTransitionDenialReason.None)]
    [Arguments(DomainTaskStatus.Completed, DomainTaskStatus.InProgress, false, TaskStatusTransitionDenialReason.TerminalCannotStart)]
    [Arguments(DomainTaskStatus.Completed, DomainTaskStatus.Completed, false, TaskStatusTransitionDenialReason.TerminalCannotComplete)]
    [Arguments(DomainTaskStatus.Completed, DomainTaskStatus.Archived, false, TaskStatusTransitionDenialReason.CompletedCannotArchive)]
    [Arguments(DomainTaskStatus.Archived, DomainTaskStatus.NotReady, true, TaskStatusTransitionDenialReason.None)]
    [Arguments(DomainTaskStatus.Archived, DomainTaskStatus.Prepared, true, TaskStatusTransitionDenialReason.None)]
    [Arguments(DomainTaskStatus.Archived, DomainTaskStatus.InProgress, false, TaskStatusTransitionDenialReason.TerminalCannotStart)]
    [Arguments(DomainTaskStatus.Archived, DomainTaskStatus.Completed, false, TaskStatusTransitionDenialReason.TerminalCannotComplete)]
    [Arguments(DomainTaskStatus.Archived, DomainTaskStatus.Archived, true, TaskStatusTransitionDenialReason.None)]
    public async Task Evaluate_PermissiveFacts_PreservesRawFiveByFiveMatrix(
        DomainTaskStatus currentStatus,
        DomainTaskStatus requestedStatus,
        bool expectedAllowed,
        TaskStatusTransitionDenialReason expectedReason)
    {
        var evaluation = TaskStatusTransitionPolicy.Evaluate(
            requestedStatus,
            PermissiveFacts(currentStatus));

        using (Assert.Multiple())
        {
            await Assert.That(evaluation.IsAllowed).IsEqualTo(expectedAllowed);
            await Assert.That(evaluation.Reason).IsEqualTo(expectedReason);
        }
    }

    [Test]
    [Arguments(DomainTaskStatus.Completed, DomainTaskStatus.InProgress, false, true, false, TaskStatusTransitionDenialReason.TerminalCannotStart)]
    [Arguments(DomainTaskStatus.Prepared, DomainTaskStatus.InProgress, false, true, false, TaskStatusTransitionDenialReason.GraphUnavailableForStart)]
    [Arguments(DomainTaskStatus.Prepared, DomainTaskStatus.InProgress, true, true, false, TaskStatusTransitionDenialReason.FutureDatePreventsStart)]
    [Arguments(DomainTaskStatus.Archived, DomainTaskStatus.Completed, false, false, false, TaskStatusTransitionDenialReason.TerminalCannotComplete)]
    [Arguments(DomainTaskStatus.Prepared, DomainTaskStatus.Completed, false, false, false, TaskStatusTransitionDenialReason.GraphUnavailableForCompletion)]
    [Arguments(DomainTaskStatus.Prepared, DomainTaskStatus.Completed, true, false, false, TaskStatusTransitionDenialReason.CompletionCriteriaIncomplete)]
    public async Task Evaluate_DenialConditions_UseDeterministicPriority(
        DomainTaskStatus currentStatus,
        DomainTaskStatus requestedStatus,
        bool isGraphAvailable,
        bool plannedBeginIsFuture,
        bool completionCriteriaSatisfied,
        TaskStatusTransitionDenialReason expectedReason)
    {
        var evaluation = TaskStatusTransitionPolicy.Evaluate(
            requestedStatus,
            new TaskStatusTransitionFacts(
                currentStatus,
                isGraphAvailable,
                plannedBeginIsFuture,
                completionCriteriaSatisfied));

        using (Assert.Multiple())
        {
            await Assert.That(evaluation.IsAllowed).IsFalse();
            await Assert.That(evaluation.Reason).IsEqualTo(expectedReason);
        }
    }

    [Test]
    public async Task Evaluate_UndefinedRequestedTarget_IsDeniedBeforeOtherRules()
    {
        var undefined = (DomainTaskStatus)int.MaxValue;
        var evaluation = TaskStatusTransitionPolicy.Evaluate(
            undefined,
            new TaskStatusTransitionFacts(
                undefined,
                IsGraphAvailable: false,
                PlannedBeginIsFuture: true,
                CompletionCriteriaSatisfied: false));

        using (Assert.Multiple())
        {
            await Assert.That(evaluation.IsAllowed).IsFalse();
            await Assert.That(evaluation.Reason)
                .IsEqualTo(TaskStatusTransitionDenialReason.InvalidTargetStatus);
        }
    }

    [Test]
    [Arguments(DomainTaskStatus.NotReady)]
    [Arguments(DomainTaskStatus.Prepared)]
    [Arguments(DomainTaskStatus.InProgress)]
    [Arguments(DomainTaskStatus.Completed)]
    [Arguments(DomainTaskStatus.Archived)]
    public async Task Evaluate_UndefinedSource_PreservesRecoveryRules(DomainTaskStatus requestedStatus)
    {
        var evaluation = TaskStatusTransitionPolicy.Evaluate(
            requestedStatus,
            PermissiveFacts((DomainTaskStatus)int.MaxValue));

        using (Assert.Multiple())
        {
            await Assert.That(evaluation.IsAllowed).IsTrue();
            await Assert.That(evaluation.Reason).IsEqualTo(TaskStatusTransitionDenialReason.None);
        }
    }

    [Test]
    [Arguments(DomainTaskStatus.InProgress, false, false, true, TaskStatusTransitionDenialReason.GraphUnavailableForStart)]
    [Arguments(DomainTaskStatus.InProgress, true, true, true, TaskStatusTransitionDenialReason.FutureDatePreventsStart)]
    [Arguments(DomainTaskStatus.Completed, false, false, false, TaskStatusTransitionDenialReason.GraphUnavailableForCompletion)]
    [Arguments(DomainTaskStatus.Completed, true, false, false, TaskStatusTransitionDenialReason.CompletionCriteriaIncomplete)]
    public async Task Evaluate_UndefinedSource_StillUsesOrdinaryFactGuards(
        DomainTaskStatus requestedStatus,
        bool isGraphAvailable,
        bool plannedBeginIsFuture,
        bool completionCriteriaSatisfied,
        TaskStatusTransitionDenialReason expectedReason)
    {
        var evaluation = TaskStatusTransitionPolicy.Evaluate(
            requestedStatus,
            new TaskStatusTransitionFacts(
                (DomainTaskStatus)int.MaxValue,
                isGraphAvailable,
                plannedBeginIsFuture,
                completionCriteriaSatisfied));

        using (Assert.Multiple())
        {
            await Assert.That(evaluation.IsAllowed).IsFalse();
            await Assert.That(evaluation.Reason).IsEqualTo(expectedReason);
        }
    }

    private static TaskStatusTransitionFacts PermissiveFacts(DomainTaskStatus currentStatus) => new(
        currentStatus,
        IsGraphAvailable: true,
        PlannedBeginIsFuture: false,
        CompletionCriteriaSatisfied: true);
}
