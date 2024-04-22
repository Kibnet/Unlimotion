using DynamicData;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unlimotion.ViewModel.BLL;

public static class TaskTreeManager 
{
    public static async Task<List<TaskItemViewModel>> ProcessTaskChangeAsync(TaskChange taskChange, ITaskRepository repository)
    {
        var result = new List<TaskItemViewModel>();

        TaskItem parent = await repository.Load(taskChange.Parent.Id);
        switch (taskChange.Action)
        {
            case Action.AddChild:
                parent.ContainsTasks.Add(taskChange.Id);
                break;
             _: throw new NotImplementedException("Передан неизвестный тип действия над таском");
        }
        result.Add(new TaskItemViewModel(parent, repository));

        TaskItem child = await repository.Load(taskChange.Id);
        var childVm = new TaskItemViewModel(child, repository);
        switch (taskChange.Action)
        {
            case Action.AddChild:
                childVm.Parents.Add(parent.Id);
                break;
            _: throw new NotImplementedException("Передан неизвестный тип действия над таском");
        }

        result.Add(childVm);

        return result;
    }
}


