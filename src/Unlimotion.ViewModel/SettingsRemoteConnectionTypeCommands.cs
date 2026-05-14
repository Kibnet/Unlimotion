using System;
using System.Threading.Tasks;
using ReactiveUI;
using Unlimotion.ViewModel.Localization;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel;

public static class SettingsRemoteConnectionTypeCommands
{
    public static void Configure(
        SettingsViewModel settings,
        IRemoteBackupService? backupService,
        INotificationManagerWrapper? notificationManager)
    {
        async Task SwitchRemoteConnectionTypeAsync(BackupAuthMode targetMode)
        {
            if (backupService == null || string.IsNullOrWhiteSpace(settings.GitRemoteName))
            {
                return;
            }

            var remoteName = settings.GitRemoteName;
            settings.SetBackupConnectionState(BackupStatusState.Connecting, L10n.Get("SwitchingRemoteConnectionType"));
            try
            {
                var result = await Task.Run(() =>
                    backupService.SwitchRemoteConnectionType(remoteName, targetMode));
                settings.ApplyRemoteConnectionTypeSwitch(result);

                var successText = L10n.Get(result.CreatedRemote
                    ? "RemoteConnectionTypeCopyCreated"
                    : "RemoteConnectionTypeSwitched");
                if (settings.BackupConnectionState == BackupStatusState.Connected)
                {
                    settings.SetBackupConnectionState(BackupStatusState.Connected, successText);
                }
                else if (settings.GitShowStatusToasts)
                {
                    notificationManager?.SuccessToast(successText);
                }
            }
            catch (Exception ex)
            {
                settings.ReloadGitMetadata();
                settings.SetBackupConnectionState(
                    BackupStatusState.Error,
                    L10n.Format("RemoteConnectionTypeSwitchFailed", ex.Message));
                notificationManager?.ErrorToast(L10n.Format("RemoteConnectionTypeSwitchFailed", ex.Message));
            }
        }

        settings.SwitchRemoteConnectionTypeCommand = ReactiveCommand.CreateFromTask<string?>(targetType =>
            SwitchRemoteConnectionTypeAsync(
                string.Equals(targetType, "SSH", StringComparison.OrdinalIgnoreCase)
                    ? BackupAuthMode.Ssh
                    : BackupAuthMode.Token));
        settings.SwitchRemoteToHttpCommand = ReactiveCommand.CreateFromTask(() =>
            SwitchRemoteConnectionTypeAsync(BackupAuthMode.Token));
        settings.SwitchRemoteToSshCommand = ReactiveCommand.CreateFromTask(() =>
            SwitchRemoteConnectionTypeAsync(BackupAuthMode.Ssh));
    }
}
