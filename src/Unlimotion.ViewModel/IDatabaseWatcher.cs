using System;
using System.Threading.Tasks;

namespace Unlimotion.ViewModel;
public interface IDatabaseWatcher
{
    event EventHandler<DbUpdatedEventArgs>? OnDatabaseUpdated;
    bool IsEnabled { get; }
    Task Start();
    void Stop();
    void AddIgnoredTask(string taskId);
    void RemoveIgnoredTask(string taskId);
}