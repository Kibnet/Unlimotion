using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unlimotion.Domain;

namespace Unlimotion.TaskTree;

public interface IStorage
{
    Task<TaskItem> Save(TaskItem item);
    Task<bool> Remove(string itemId);
    Task<TaskItem?> Load(string itemId);
    IAsyncEnumerable<TaskItem> GetAll();
    Task BulkInsert(IEnumerable<TaskItem> taskItems);
    public event EventHandler<TaskStorageUpdateEventArgs> Updating;
    Task<bool> Connect();
    Task Disconnect();
    public event Action<Exception?>? OnConnectionError;
}