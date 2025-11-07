using System;
using Unlimotion.Domain;

namespace Unlimotion.TaskTree;

public interface IStorage
{
    Task<bool> Save(TaskItem item);
    Task<bool> Remove(string itemId);
    Task<TaskItem?> Load(string itemId);
    IAsyncEnumerable<TaskItem> GetAll();
    Task BulkInsert(IEnumerable<TaskItem> taskItems);
    public event EventHandler<TaskStorageUpdateEventArgs> Updating;
    Task<bool> Connect();
    Task Disconnect();
    public event Action<Exception?>? OnConnectionError;
}