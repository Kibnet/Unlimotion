using System;
using Unlimotion.Server.Domain;

namespace TaskTreeManager;

public interface IStorage
{
    Task<bool> Save(TaskItem item);
    Task<bool> Remove(string itemId);
    Task<TaskItem> Load(string itemId);

}


