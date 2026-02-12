using System.Collections.Generic;

namespace Unlimotion.ViewModel;

public interface ITaskRelationsIndex
{
    void Rebuild(IEnumerable<TaskItemViewModel> tasks);
}
