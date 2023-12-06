using System.Collections.Generic;

namespace Unlimotion.Services;

public interface IRemoteBackupService
{
    public List<string> Remotes();
    public List<string> Refs();
    public void Push(string msg);
    public void Pull();
}