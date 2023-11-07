using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.ViewModel;

public interface ITaskStorage
{
    IEnumerable<TaskItem> GetAll();
    Task<bool> Save(TaskItem item);
    Task<bool> Remove(string itemId);
    Task<TaskItem> Load(string itemId);
    Task<bool> Connect();
    Task Disconnect();

    public event EventHandler<TaskStorageUpdateEventArgs> Updating;
}