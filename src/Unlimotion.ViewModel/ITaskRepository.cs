using System;
using System.Threading.Tasks;
using DynamicData;

namespace Unlimotion.ViewModel;

public interface ITaskRepository
{
    SourceCache<TaskItemViewModel, string> Tasks { get; }
    void Init();
    Task Remove(string itemId);
    Task Save(TaskItem item);
    Task<TaskItem> Load(string itemId);
    IObservable<IChangeSet<TaskItemViewModel, string>> GetRoots();
    TaskItemViewModel Clone(TaskItem clone, params TaskItemViewModel[] destinations);
}