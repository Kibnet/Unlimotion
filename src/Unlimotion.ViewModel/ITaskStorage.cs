using System.Collections.Generic;

namespace Unlimotion.ViewModel;

public interface ITaskStorage
{
    IEnumerable<TaskItem> GetAll();

    bool Save(TaskItem item);
    bool Remove(string itemId);
}