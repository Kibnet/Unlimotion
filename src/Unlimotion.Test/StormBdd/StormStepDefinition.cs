using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.Test.StormBdd;

internal sealed record StormScenario(
    string Id,
    string Title,
    string RuleTitle,
    IReadOnlyList<string> Tags,
    IReadOnlyList<StormScenarioStep> Steps);

internal sealed record StormScenarioStep(string Keyword, string Text, int Line);

internal sealed record StormStepDefinition(
    string Id,
    string Keyword,
    string Text,
    IReadOnlySet<string> SupportsScenarios,
    Func<StormScenarioContext, Task> ExecuteAsync)
{
    public string MatchKey => StormScenarioRunner.CreateMatchKey(Keyword, Text);
}

internal sealed class StormScenarioContext
{
    public bool DesktopReleaseSupportedShellConfirmed { get; set; }

    public bool NonDesktopSharedUiModelRequired { get; set; }

    public bool PlatformProjectContractsChecked { get; set; }

    public bool RuntimeReleaseSupportClaimed { get; set; }

    public bool TelegramGitTimersEnabled { get; set; }

    public bool TelegramGitConflictResolutionInProgress { get; set; }

    public TelegramGitTimerExecutionResult? TelegramGitTimerResult { get; set; }

    public bool TelegramCommandTaskSetAvailable { get; set; }

    public bool TelegramCommandStoryBehaviorConfirmed { get; set; }

    public TelegramCommandAuthorizationScenarioResult? TelegramCommandAuthorizationResult { get; set; }

    public bool TelegramCallbackTaskSetAvailable { get; set; }

    public bool TelegramCallbackAllowedUserConfirmed { get; set; }

    public TelegramCallbackScenarioResult? TelegramCallbackResult { get; set; }

    public bool NotificationMainScreenAvailable { get; set; }

    public bool NotificationStoryBehaviorConfirmed { get; set; }

    public NotificationToastScenarioResult? NotificationToastResult { get; set; }

    public bool ServerStorageTaskSetAvailable { get; set; }

    public bool ServerStorageStoryBehaviorConfirmed { get; set; }

    public ServerStorageAuthScenarioResult? ServerStorageAuthResult { get; set; }

    public ServerStorageCrudRealtimeScenarioResult? ServerStorageCrudRealtimeResult { get; set; }

    public bool FilterResetTaskSetAvailable { get; set; }

    public bool FilterResetStoryBehaviorConfirmed { get; set; }

    public FilterResetScenarioResult? FilterResetResult { get; set; }

    public bool EmojiFilterTaskSetAvailable { get; set; }

    public bool EmojiFilterStoryBehaviorConfirmed { get; set; }

    public EmojiFilterScenarioResult? EmojiFilterResult { get; set; }

    public bool SearchBehaviorTaskSetAvailable { get; set; }

    public bool SearchBehaviorStoryBehaviorConfirmed { get; set; }

    public SearchBehaviorScenarioResult? SearchBehaviorResult { get; set; }

    public bool TaskCreationGraphTaskSetAvailable { get; set; }

    public bool TaskCreationGraphStoryBehaviorConfirmed { get; set; }

    public TaskCreationGraphScenarioResult? TaskCreationGraphResult { get; set; }

    public bool MultipleParentsRelationTaskSetAvailable { get; set; }

    public bool MultipleParentsRelationStoryBehaviorConfirmed { get; set; }

    public MultipleParentsRelationScenarioResult? MultipleParentsRelationResult { get; set; }

    public bool TaskGraphWorkspaceCommandTaskSetAvailable { get; set; }

    public bool TaskGraphWorkspaceCommandStoryBehaviorConfirmed { get; set; }

    public TaskGraphWorkspaceCommandScenarioResult? TaskGraphWorkspaceCommandResult { get; set; }

    public bool TaskStatusSupportTaskSetAvailable { get; set; }

    public bool TaskStatusSupportStoryBehaviorConfirmed { get; set; }

    public TaskStatusSupportScenarioResult? TaskStatusSupportResult { get; set; }

    public bool TaskStatusCompletionBlockTaskSetAvailable { get; set; }

    public bool TaskStatusCompletionBlockStoryBehaviorConfirmed { get; set; }

    public TaskStatusCompletionBlockScenarioResult? TaskStatusCompletionBlockResult { get; set; }

    public bool TaskStatusMigrationTaskSetAvailable { get; set; }

    public bool TaskStatusMigrationStoryBehaviorConfirmed { get; set; }

    public TaskStatusMigrationScenarioResult? TaskStatusMigrationResult { get; set; }

    public bool TaskAvailabilityBlockersTaskSetAvailable { get; set; }

    public bool TaskAvailabilityBlockersStoryBehaviorConfirmed { get; set; }

    public TaskAvailabilityBlockersScenarioResult? TaskAvailabilityBlockersResult { get; set; }

    public bool TaskAvailabilityUnlockedTimeTaskSetAvailable { get; set; }

    public bool TaskAvailabilityUnlockedTimeStoryBehaviorConfirmed { get; set; }

    public TaskAvailabilityUnlockedTimeScenarioResult? TaskAvailabilityUnlockedTimeResult { get; set; }

    public bool TaskAvailabilityInProgressRollbackTaskSetAvailable { get; set; }

    public bool TaskAvailabilityInProgressRollbackStoryBehaviorConfirmed { get; set; }

    public TaskAvailabilityInProgressRollbackScenarioResult? TaskAvailabilityInProgressRollbackResult { get; set; }

    public bool WorkspaceNavigationTabsTaskSetAvailable { get; set; }

    public bool WorkspaceNavigationTabsStoryBehaviorConfirmed { get; set; }

    public WorkspaceNavigationTabsScenarioResult? WorkspaceNavigationTabsResult { get; set; }

    public bool WorkspaceBreadcrumbsLastOpenedTaskSetAvailable { get; set; }

    public bool WorkspaceBreadcrumbsLastOpenedStoryBehaviorConfirmed { get; set; }

    public WorkspaceBreadcrumbsLastOpenedScenarioResult? WorkspaceBreadcrumbsLastOpenedResult { get; set; }

    public bool WorkspaceTreeCommandsTaskSetAvailable { get; set; }

    public bool WorkspaceTreeCommandsStoryBehaviorConfirmed { get; set; }

    public WorkspaceTreeCommandsScenarioResult? WorkspaceTreeCommandsResult { get; set; }

    public bool TaskPlanningDatesTaskSetAvailable { get; set; }

    public bool TaskPlanningDatesStoryBehaviorConfirmed { get; set; }

    public TaskPlanningDatesScenarioResult? TaskPlanningDatesResult { get; set; }

    public bool RepeaterPatternTaskSetAvailable { get; set; }

    public bool RepeaterPatternStoryBehaviorConfirmed { get; set; }

    public RepeaterPatternScenarioResult? RepeaterPatternResult { get; set; }

    public HashSet<string> ExecutedStepDefinitionIds { get; } = new(StringComparer.Ordinal);
}
