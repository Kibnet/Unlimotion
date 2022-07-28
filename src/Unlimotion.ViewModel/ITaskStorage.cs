using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.ViewModel;

public interface ITaskStorage
{
    IEnumerable<TaskItem> GetAll();
    Task<bool> Save(TaskItem item);
    Task<bool> Remove(string itemId);
    Task Connect();
}