using System;
using System.Linq;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using Unlimotion.ViewModel;

namespace Unlimotion;

public static class TaskStorageExtensions
{
    public static IObservable<IChangeSet<TaskItemViewModel, string>> GetRoots(this ITaskStorage storage)
    {
        var rootFilter = storage.Tasks.Connect()
            .AutoRefreshOnObservable(t => t.Contains.ToObservableChangeSet())
            .TransformMany(item =>
            {
                var many = item.Contains.Where(s => !string.IsNullOrEmpty(s)).Select(id => id);
                return many;
            }, s => s)

            .Distinct(k => k)
            .ToCollection()
            .Select(items =>
            {
                bool Predicate(TaskItemViewModel task) => items.Count == 0 || items.All(t => t != task.Id);
                return (Func<TaskItemViewModel, bool>)Predicate;
            });

        IObservable<IChangeSet<TaskItemViewModel, string>> roots;
        roots = storage.Tasks.Connect()
            .Filter(rootFilter);
        return roots;
    }
}