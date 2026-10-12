using System.Collections.Generic;

namespace Unlimotion.ViewModel;

/// <summary>Optional read-only capability for settings not yet persisted to configuration.</summary>
public interface ISshPublicKeyDirectoryReader
{
    List<string> GetSshPublicKeysForDirectory(string? storagePath);
}

public interface IRemoteBackupService
{
    public List<string> Remotes();
    public string? GetRemoteAuthType(string remoteName);
    public string? GetRemoteUrl(string remoteName);
    public RemoteConnectionTypeSwitchResult SwitchRemoteConnectionType(string remoteName, BackupAuthMode targetMode);
    public List<string> Refs();
    public List<string> GetSshPublicKeys();
    public string GenerateSshKey(string keyName);
    public string? ReadPublicKey(string publicKeyPath);
    public BackupConflictStatus GetConflictStatus();
    public void ResolveConflict(string path, BackupConflictResolution resolution);
    public void ResolveConflictFields(string path, IReadOnlyList<BackupConflictFieldSelection> fieldSelections);
    public void CommitResolvedConflicts(string message);
    public void Push(string msg);
    public void Pull();
    public void PullExistingRepository();
    public BackupRepositoryConnectPreview PreviewConnectRepository();
    public void ConnectRepository(bool allowMergeWithNonEmptyRemote);
    public void CloneOrUpdateRepo();
}
