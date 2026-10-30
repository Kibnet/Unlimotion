using System;
using System.Windows.Input;
using PropertyChanged;

namespace Unlimotion.ViewModel;

public partial class SettingsViewModel
{
    private long sourceAdmissionRevision;

    [AlsoNotifyFor(nameof(IsSourceMutationBlocked), nameof(CanEditSourceSettings), nameof(SourceBusyStatusText))]
    public bool IsTaskDataLoading { get; set; }

    [AlsoNotifyFor(nameof(IsSourceMutationBlocked), nameof(CanEditSourceSettings), nameof(SourceBusyStatusText))]
    public bool IsTaskOutlinePasteInProgress { get; set; }

    public bool IsSourceMutationBlocked => IsTaskDataLoading || IsTaskSpaceSwitching ||
        IsStorageBusy || IsBackupBusy || IsTaskSpaceRecoveryRequired || IsTaskOutlinePasteInProgress;
    public bool CanEditSourceSettings => !IsSourceMutationBlocked;
    [AlsoNotifyFor(nameof(SourceBusyStatusText))]
    public string? SourceOperationStatusText { get; set; }
    public string SourceBusyStatusText => SourceOperationStatusText ?? _localization.Get(IsTaskSpaceSwitching
        ? "TaskSpaceSwitching" : IsBackupBusy ? "SyncingRepository" :
        IsTaskDataLoading ? "LoadingTasks" : "SettingsSourceOperationInProgress");

    public SourceAdmissionContext CaptureSourceAdmission() => new(sourceAdmissionRevision,
        TaskStoragePath, ServerStorageUrl, GitRemoteName, GitRemoteUrl, GitBranch,
        _configuration["TaskSources:ActiveSourceId"]);
    public bool IsSourceAdmissionCurrent(SourceAdmissionContext context) =>
        context == CaptureSourceAdmission() && !IsSourceMutationBlocked;

    public bool IsPasteAdmissionCurrent(SourceAdmissionContext context) =>
        context == CaptureSourceAdmission() && IsTaskOutlinePasteInProgress &&
        !IsTaskDataLoading && !IsTaskSpaceSwitching && !IsStorageBusy &&
        !IsBackupBusy && !IsTaskSpaceRecoveryRequired;

    public ICommand GuardSourceCommand(ICommand command, Func<bool>? availability = null) =>
        new AdmissionGuardedCommand(command,
            () => !IsSourceMutationBlocked && (availability?.Invoke() ?? true), this);

    private void InvalidateSourceAdmission()
    {
        sourceAdmissionRevision++;
        RefreshStorageSelectionState();
        RefreshBackupActionAvailability();
        _resetSshKeyStoragePathCommand.NotifyAvailabilityChanged();
    }

    private void OnIsTaskDataLoadingChanged() => InvalidateSourceAdmission();
    private void OnIsTaskSpaceSwitchingChanged() => InvalidateSourceAdmission();
    private void OnIsTaskSpaceRecoveryRequiredChanged() => InvalidateSourceAdmission();
    private void OnIsStorageBusyChanged() => InvalidateSourceAdmission();
    private void OnIsBackupBusyChanged() => InvalidateSourceAdmission();
    private void OnIsTaskOutlinePasteInProgressChanged() => InvalidateSourceAdmission();
}

public readonly record struct SourceAdmissionContext(long Revision, string? TaskPath, string? TaskUrl,
    string? RemoteName, string? RemoteUrl, string? Branch, string? SourceId);
