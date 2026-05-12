using System.Collections.Generic;

namespace Unlimotion.ViewModel;

public interface IRemoteBackupService
{
    public List<string> Remotes();
    public string? GetRemoteAuthType(string remoteName);
    public string? GetRemoteUrl(string remoteName);
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
    public BackupRepositoryConnectPreview PreviewConnectRepository();
    public void ConnectRepository(bool allowMergeWithNonEmptyRemote);
    public void CloneOrUpdateRepo();
}
