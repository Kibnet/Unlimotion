using System;
using System.Threading.Tasks;
using DynamicData;

namespace Unlimotion.ViewModel;

public interface ITaskRepository
{
    SourceCache<TaskItemViewModel, string> Tasks { get; }
    void Init();
    event EventHandler<EventArgs> Initiated;
    Task Remove(string itemId, bool deleteFile);
    Task Save(TaskItem item);
    Task<TaskItem> Load(string itemId);
    IObservable<IChangeSet<TaskItemViewModel, string>> GetRoots();
    TaskItemViewModel Clone(TaskItem clone, params TaskItemViewModel[] destinations);
}