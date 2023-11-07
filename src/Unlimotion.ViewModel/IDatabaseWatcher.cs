using System;

namespace Unlimotion.ViewModel;
public interface IDatabaseWatcher
{
    public event EventHandler<DbUpdatedEventArgs> OnUpdated;
    public void AddIgnoredTask(string taskId);
}