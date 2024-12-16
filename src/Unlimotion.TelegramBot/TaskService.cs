using DynamicData;
using DynamicData.Binding;
using Serilog;
using Unlimotion.Domain;
using Unlimotion.ViewModel;

namespace Unlimotion.TelegramBot
{
    public class TaskService
    {
        ITaskStorage storage;

        public TaskService(string repoPath)
        {
            storage = TaskStorages.RegisterFileTaskStorage(repoPath);
            storage.Init();
        }

        public TaskItemViewModel GetTask(string id)
        {
            try
            {
                return storage.Tasks.Lookup(id).Value;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при получении задачи {TaskId}", id);
                return null;
            }
        }

        public void SaveTask(TaskItem task)
        {
            try
            {
                storage.Update(task);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при сохранении задачи {TaskId}", task.Id);
            }
        }

        public void DeleteTask(string id)
        {
            try
            {
                var task = storage.Tasks.Lookup(id).Value;
                storage.Delete(task);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при удалении задачи {TaskId}", id);
            }
        }

        public List<TaskItemViewModel> SearchTasks(string query)
        {
            var tasks = new List<TaskItemViewModel>();
            if (string.IsNullOrWhiteSpace(query))
            {
                return tasks;
            }
            try
            {
                tasks = storage.Tasks.Items
                    .Where(m => (m.Title != null && m.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                                (m.Description != null && m.Description.Contains(query, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при поиске задач по запросу {Query}", query);
            }
            return tasks;
        }

        public IEnumerable<TaskItemViewModel> RootTasks()
        {
            var TaskList = new ObservableCollectionExtended<TaskItemViewModel>();
            try
            {
                // Привязываем изменения к ObservableCollection
                storage.GetRoots()
                    .Bind(TaskList)
                    .Subscribe();

            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при получении корневых задач");
            }
            return TaskList;
        }
    }
}
