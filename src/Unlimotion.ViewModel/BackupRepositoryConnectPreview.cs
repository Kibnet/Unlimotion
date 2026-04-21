namespace Unlimotion.ViewModel;

public enum BackupRepositoryConnectAction
{
    PullExistingRepository,
    InitializeLocalAndPush,
    FetchIntoEmptyLocalFolder,
    MergeNonEmptyLocalWithRemote
}

public sealed record BackupRepositoryConnectPreview(
    BackupRepositoryConnectAction Action,
    bool RequiresConfirmation,
    bool LocalFolderHasContent,
    bool RemoteHasContent,
    string RepositoryPath,
    string RemoteUrl);
