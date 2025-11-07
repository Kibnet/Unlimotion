using System;
using Unlimotion.TaskTree;

namespace Unlimotion.ViewModel;
public interface IDatabaseWatcher
{
    public event EventHandler<DbUpdatedEventArgs> OnUpdated;
    public void AddIgnoredTask(string taskId);
    public void SetEnable(bool enable);
    public void ForceUpdateFile(string filename, UpdateType type);
}