using Microsoft.Extensions.Configuration;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public interface ITaskStorageFactory
{
    ITaskStorage? CurrentStorage { get; }
    IDatabaseWatcher? CurrentWatcher { get; }
    ITaskStorage CreateFileStorage(string? path);
    ITaskStorage CreateServerStorage(string? url);
    void SwitchStorage(bool isServerMode, IConfiguration configuration);
}
