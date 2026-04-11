using System.Collections.Generic;

namespace Unlimotion.ViewModel;

public interface IRemoteBackupService
{
    public List<string> Remotes();
    public string? GetRemoteAuthType(string remoteName);
    public List<string> Refs();
    public List<string> GetSshPublicKeys();
    public string GenerateSshKey(string keyName);
    public string? ReadPublicKey(string publicKeyPath);
    public void Push(string msg);
    public void Pull();
    public void CloneOrUpdateRepo();
}
