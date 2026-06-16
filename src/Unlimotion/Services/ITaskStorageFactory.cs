using Microsoft.Extensions.Configuration;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public interface ITaskStorageFactory
{
    ITaskSourceManager SourceManager { get; }
    ITaskStorage CreateConfiguredStorage();
    ITaskStorage CreateFileStorage(string? path);
    ITaskStorage CreateDetachedFileStorage(string? path);
    ITaskStorage CreateServerStorage(string? url);
    void SwitchStorage(bool isServerMode, IConfiguration configuration);
}
