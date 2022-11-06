using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.ViewModel;

public interface ITaskStorage
{
    IEnumerable<TaskItem> GetAll();
    IEnumerable<ComputedTaskInfo> GetTasksComputedInfo();
    Task<bool> Save(TaskItem item);
    Task<bool> SaveComputedTaskInfo(ComputedTaskInfo computedTaskInfo);
    Task<bool> Remove(string itemId);
    Task<bool> Connect();
    Task Disconnect();
}