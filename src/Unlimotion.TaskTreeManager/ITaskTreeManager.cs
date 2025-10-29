using Unlimotion.Domain;

namespace Unlimotion.TaskTree;

public interface ITaskTreeManager
{
    public Task<List<TaskItem>> AddTask(TaskItem change, TaskItem? currentTask = null,
                                        bool isBlocked = false);

    public Task<List<TaskItem>> AddChildTask(TaskItem change, TaskItem currentTask);

    public Task<List<TaskItem>> DeleteTask(TaskItem change, bool deleteInStorage = true);

    public Task<List<TaskItem>> UpdateTask(TaskItem change);

    public Task<List<TaskItem>> CloneTask(TaskItem change, List<TaskItem> stepParents);

    public Task<List<TaskItem>> AddNewParentToTask(TaskItem change, TaskItem additionalParent);

    public Task<List<TaskItem>> MoveTaskToNewParent(TaskItem change, TaskItem newParent, TaskItem? prevParent);

    public Task<List<TaskItem>> UnblockTask(TaskItem taskToUnblock, TaskItem blockingTask);

    public Task<List<TaskItem>> BlockTask(TaskItem taskToBlock, TaskItem blockingTask);

    public Task<TaskItem> LoadTask(string taskId);

    public Task<List<TaskItem>> DeleteParentChildRelation(TaskItem parent, TaskItem child);

    public Task<List<TaskItem>> CalculateAndUpdateAvailability(TaskItem task);
}

