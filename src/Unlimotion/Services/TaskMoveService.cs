using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public sealed class TaskMoveService(ITaskStorageFactory storageFactory)
{
    public async Task MoveTaskTreeToFileStorageAsync(
        TaskItemViewModel rootTask,
        ITaskStorage? sourceStorage,
        string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(rootTask);

        var destinationStorage = storageFactory.CreateDetachedFileStorage(destinationPath);
        try
        {
            var movedTaskIds = new HashSet<string>();
            var queue = new Queue<TaskItemViewModel>();
            queue.Enqueue(rootTask);

            while (queue.Count > 0)
            {
                var task = queue.Dequeue();
                if (!movedTaskIds.Add(task.Id))
                {
                    continue;
                }

                await destinationStorage.TaskTreeManager.Storage.Save(task.Model);
                foreach (var child in task.ContainsTasks)
                {
                    queue.Enqueue(child);
                }
            }

            var sourceRawStorage = sourceStorage?.TaskTreeManager.Storage;
            if (sourceRawStorage == null)
            {
                return;
            }

            foreach (var id in movedTaskIds)
            {
                await sourceRawStorage.Remove(id);
            }
        }
        finally
        {
            (destinationStorage as IDisposable)?.Dispose();
        }
    }
}
