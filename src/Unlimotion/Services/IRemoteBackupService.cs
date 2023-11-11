namespace Unlimotion.Services;

public interface IRemoteBackupService
{
    public void Push(string msg);
    public void Pull();
}