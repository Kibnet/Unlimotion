using DynamicData;
using DynamicData.Binding;
using Serilog;
using Unlimotion.ViewModel;

namespace Unlimotion.TelegramBot
{
    public class TaskService
    {
        ITaskRepository repository;

        public TaskService(string repoPath)
        {
            TaskStorages.RegisterFileTaskStorage(repoPath);
            repository = TaskStorages.RegisterTaskRepository();
            repository.Init();
        }

        public TaskItemViewModel GetTask(string id)
        {
            try
            {
                return repository.Tasks.Lookup(id).Value;
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
                repository.Save(task);
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
                repository.Remove(id, true);
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
                tasks = repository.Tasks.Items
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
                repository.GetRoots()
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
