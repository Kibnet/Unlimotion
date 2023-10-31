using System;
using System.Threading.Tasks;

namespace Unlimotion.ViewModel;
public interface IDatabaseWatcher {
    event EventHandler<DbUpdatedEventArgs>? OnDatabaseUpdated;
    bool IsEnabled { get; set; }
    Task Start();
    void Stop();
}