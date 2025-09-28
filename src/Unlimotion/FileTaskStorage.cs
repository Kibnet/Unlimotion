using AutoMapper;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DynamicData;
using DynamicData.Binding;
using LibGit2Sharp;
using Microsoft.Msagl.Core.Geometry.Curves;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Splat;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Models;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;

namespace Unlimotion
{
    public sealed class FileEntry
    {
        public string Name { get; init; } = default!;
        public string? LocalPath { get; init; } // есть на десктопе, на Android чаще null
        public long Size { get; init; } // длина файла
        public DateTimeOffset? Created { get; init; } // есть на десктопе, на Android обычно null
        public IStorageFile? StorageFile { get; init; } // пригодится для дальнейшей работы через потоки
    }

    public static class CrossPlatformFileEnumerator
    {
        /// <summary>
        /// Возвращает список файлов из указанной папки (только верхний уровень),
        /// отфильтровывая размер == 0. 
        /// На десктопе сортирует по CreationTime, на Android (SAF) – по имени.
        /// </summary>
        public static async Task<IReadOnlyList<FileEntry>> EnumerateTopFilesAsync(IStorageFolder folder)
        {
            // Пытаемся получить локальный путь — на Windows/Linux/macOS обычно есть,
            // на Android (SAF) почти всегда null.
            var baseLocalPath = folder.TryGetLocalPath();

            if (!string.IsNullOrEmpty(baseLocalPath))
            {
                // Десктопный путь доступен — работаем через DirectoryInfo, как в вашем коде
                var dir = new DirectoryInfo(baseLocalPath!);
                var files = dir
                    .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                    .Where(fi => fi.Length > 0)
                    .OrderBy(fi => fi.CreationTimeUtc) // строго по CreationTime, как просили
                    .Select(fi => new FileEntry
                    {
                        Name = fi.Name,
                        LocalPath = fi.FullName,
                        Size = fi.Length,
                        Created = fi.CreationTimeUtc
                    })
                    .ToArray();

                return files;
            }
            else
            {
                // Android / SAF — работаем через IStorageFolder
                var items = folder.GetItemsAsync();

                // Берём только файлы верхнего уровня
                var files = new List<FileEntry>();
                await foreach (var item in items)
                {
                    if (item is IStorageFile f)
                    {
                        // Размер можно получить из потока
                        await using var s = await f.OpenReadAsync();
                        if (s.Length <= 0) continue;

                        files.Add(new FileEntry
                        {
                            Name = f.Name,
                            LocalPath = f.TryGetLocalPath(), // скорее всего null на Android, но пусть будет
                            Size = s.Length,
                            Created = null, // SAF обычно не даёт Created
                            StorageFile = f
                        });
                    }
                }

                // На Android времени создания нет — сортируем стабильно по имени
                return files
                    .OrderBy(x => x.Created ?? DateTimeOffset.MinValue) // если вдруг Created появится — учтём
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    public partial class FileTaskStorage : ITaskStorage, IStorage
    {
        public SourceCache<TaskItemViewModel, string> Tasks { get; private set; }

        public ITaskTreeManager TaskTreeManager => taskTreeManager ??= new TaskTreeManager((IStorage)this);

        // Абстракция над хранилищем: либо локальный путь (десктоп), либо IStorageFolder (Android/везде)
        private readonly string? _localPath;
        private readonly IStorageFolder? _folder;

        private IDatabaseWatcher? dbWatcher;
        public bool isPause;
        private IObservable<Func<TaskItemViewModel, bool>> rootFilter;

        public event EventHandler<TaskStorageUpdateEventArgs> Updating;
        public event Action<Exception?>? OnConnectionError;
        public event EventHandler<EventArgs> Initiated;
        private readonly IMapper mapper;
        private ITaskTreeManager taskTreeManager;

        // ---- КОНСТРУКТОРЫ ----

        // Универсально: предпочтительный — передаём IStorageFolder (работает везде)
        public FileTaskStorage(IStorageFolder folder)
        {
            _folder = folder ?? throw new ArgumentNullException(nameof(folder));
            _localPath = folder.TryGetLocalPath(); // на десктопах будет путь; на Android чаще null
            mapper = Locator.Current.GetService<IMapper>();
        }

        // Для старого кода/десктопов можно передать явный путь
        public FileTaskStorage(string desktopPath)
        {
            _localPath = desktopPath ?? throw new ArgumentNullException(nameof(desktopPath));
            _folder = null;
            var sp = Dialogs.GetStorageProvider();
            if (sp is not null)
            {
                if (Uri.TryCreate(desktopPath, UriKind.Absolute, out var uri))
                {
                    // file://… → десктоп; content://… → Android/SAF
                    var folder = sp.TryGetFolderFromPathAsync(uri).Result;
                    if (folder is not null)
                    {
                        _folder = folder;
                        _localPath = null;

                    }
                }
            }
            mapper = Locator.Current.GetService<IMapper>();
        }

        // ---- ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ДЛЯ ФАЙЛОВОЙ СИСТЕМЫ ----

        private async Task<IReadOnlyList<(string Name, long Size, DateTimeOffset? Created)>> EnumerateTopFilesAsync()
        {
            // Если у нас есть локальный путь — используем DirectoryInfo (быстрее и с датой создания)
            if (!string.IsNullOrEmpty(_localPath))
            {
                var dir = new DirectoryInfo(_localPath!);
                return dir
                    .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                    .Where(fi => fi.Length > 0)
                    .OrderBy(fi => fi.CreationTimeUtc)
                    .Select(fi => (fi.Name, fi.Length, (DateTimeOffset?)fi.CreationTimeUtc))
                    .ToArray();
            }

            // Иначе — через IStorageFolder (Android/универсально)
            if (_folder is null)
                return Array.Empty<(string, long, DateTimeOffset?)>();

            var items = _folder.GetItemsAsync();
            var list = new List<(string Name, long Size, DateTimeOffset? Created)>();
            await foreach (var it in items)
            {
                if (it is IStorageFile f)
                {
                    await using var s = await f.OpenReadAsync();
                    if (s.Length <= 0) continue;
                    // SAF обычно не даёт Created
                    list.Add((f.Name, s.Length, null));
                }
            }

            // Стабильная сортировка: по дате если есть, затем по имени
            return list
                .OrderBy(t => t.Created ?? DateTimeOffset.MinValue)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private async Task<IStorageFile?> GetFileByNameAsync(string name, bool createIfMissing = false)
        {
            if (!string.IsNullOrEmpty(_localPath))
            {
                // Десктоп: просто путь
                var full = System.IO.Path.Combine(_localPath!, name);
                if (createIfMissing)
                {
                    // создадим пустой файл, если его ещё нет
                    if (!File.Exists(full))
                    {
                        Directory.CreateDirectory(_localPath!);
                        await using var _ = File.Create(full);
                    }
                }

                // Оборачиваем в «виртуальный» файл только для единообразия? Не обязательно.
                // На десктопе работаем через локальные API, поэтому вернём null — признак "используй локальный путь".
                return null;
            }

            if (_folder is null)
                return null;

            // Avalonia 11+: TryGetItemAsync может отсутствовать в конкретной версии — тогда перечислим каталог
            // (в большинстве случаев достаточно перечислить и найти по имени)
            var items = _folder.GetItemsAsync();
            await foreach (var item in items)
            {
                if (item is IStorageFile file && string.Equals(file.Name, name, StringComparison.Ordinal))
                    return file;
            }

            if (createIfMissing)
            {
                // Создаём (перезапись/создание)
                return await _folder.CreateFileAsync(name);
            }

            return null;
        }

        // ---- ПУБЛИЧНЫЙ API ----

        public async IAsyncEnumerable<TaskItem> GetAll()
        {
            var files = await EnumerateTopFilesAsync();
            foreach (var (name, _, _) in files)
            {
                var task = await Load(name);
                if (task is null)
                {
                    // не удалось прочитать — удаляем (как было в вашем коде)
                    await SafeDeleteByName(name);
                    continue;
                }

                if (string.IsNullOrEmpty(task.Id))
                    continue;

                yield return task;
            }
        }

        public async Task Init()
        {
            Tasks = new(item => item.Id);

            // Миграции — передаём "источник" как есть (он внутри использует GetAll/Save/Path)
            // !!! ВАЖНО: если ваш FileTaskMigrator ожидает "Path" как string, передайте _localPath или
            // добавьте перегрузку в мигратор для IStorageFolder. Здесь я оставляю _localPath??"" — для Android
            // нужно будет сделать мигратор, который умеет работать через IStorageFolder.
            await FileTaskMigrator.Migrate(
                GetAll(),
                new Dictionary<string, (string getChild, string getParent)>
                {
                    { "Contain", (nameof(TaskItem.ContainsTasks), nameof(TaskItem.ParentTasks)) },
                    { "Block", (nameof(TaskItem.BlocksTasks), nameof(TaskItem.BlockedByTasks)) },
                },
                Save,
                _localPath ?? string.Empty // <- на Android пусто; доработайте мигратор для Storage API
            );

            await foreach (var task in GetAll())
            {
                var vm = new TaskItemViewModel(task, this);
                Tasks.AddOrUpdate(vm);
            }

            rootFilter = Tasks.Connect()
                .AutoRefreshOnObservable(t => t.Contains.ToObservableChangeSet())
                .TransformMany(item => item.Contains.Where(s => !string.IsNullOrEmpty(s)).Select(id => id), s => s)
                .Distinct(k => k)
                .ToCollection()
                .Select(items =>
                {
                    bool Predicate(TaskItemViewModel task) => items.Count == 0 || items.All(t => t != task.Id);
                    return (Func<TaskItemViewModel, bool>)Predicate;
                });

            dbWatcher = Locator.Current.GetService<IDatabaseWatcher>();
            Updating += TaskStorageOnUpdating;
            if (dbWatcher is not null)
                dbWatcher.OnUpdated += DbWatcherOnUpdated;

            OnInited();
        }

        private void TaskStorageOnUpdating(object sender, TaskStorageUpdateEventArgs e)
        {
            dbWatcher?.AddIgnoredTask(e.Id);
        }

        private async void DbWatcherOnUpdated(object sender, DbUpdatedEventArgs e)
        {
            switch (e.Type)
            {
                case UpdateType.Saved:
                    {
                        var taskItem = await Load(e.Id); // e.Id = itemId (имя файла)
                        if (taskItem != null)
                        {
                            var vml = Tasks.Lookup(taskItem.Id);
                            if (vml.HasValue)
                                vml.Value.Update(taskItem);
                            else
                                Tasks.AddOrUpdate(new TaskItemViewModel(taskItem, this));
                        }

                        break;
                    }
                case UpdateType.Removed:
                    {
                        // e.Id — это имя файла/Id задачи
                        var deletedItem = Tasks.Lookup(e.Id);
                        if (deletedItem.HasValue)
                            await Delete(deletedItem.Value, false);
                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public IObservable<IChangeSet<TaskItemViewModel, string>> GetRoots()
        {
            return Tasks.Connect().Filter(rootFilter);
        }

        protected virtual void OnInited() => Initiated?.Invoke(this, EventArgs.Empty);

        public async Task<bool> Save(TaskItem taskItem)
        {
            while (isPause)
                Thread.SpinWait(1);

            var item = taskItem;
            item.Id ??= Guid.NewGuid().ToString();

            var converter = new IsoDateTimeConverter
            {
                DateTimeFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffzzz",
                Culture = CultureInfo.InvariantCulture,
                DateTimeStyles = DateTimeStyles.None
            };

            try
            {
                // Унифицируем e.Id = item.Id (а не полный путь), чтобы кроссплатформенно
                Updating?.Invoke(this, new TaskStorageUpdateEventArgs
                {
                    Id = item.Id,
                    Type = UpdateType.Saved
                });

                var json = JsonConvert.SerializeObject(item, Formatting.Indented, converter);

                if (!string.IsNullOrEmpty(_localPath))
                {
                    Directory.CreateDirectory(_localPath!);
                    var full = System.IO.Path.Combine(_localPath!, item.Id);
                    await File.WriteAllTextAsync(full, json);
                    taskItem.Id = item.Id;
                    return true;
                }

                if (_folder is null) return false;

                // Android / SAF
                // Создаём/перезаписываем файл
                var file = await _folder.CreateFileAsync(item.Id);
                await using (var stream = await file.OpenWriteAsync())
                using (var writer = new StreamWriter(stream))
                {
                    await writer.WriteAsync(json);
                }

                taskItem.Id = item.Id;
                return true;
            }
            catch (Exception ex)
            {
                OnConnectionError?.Invoke(ex);
                return false;
            }
        }

        public async Task<bool> Remove(string itemId)
        {
            try
            {
                Updating?.Invoke(this, new TaskStorageUpdateEventArgs
                {
                    Id = itemId,
                    Type = UpdateType.Removed
                });

                if (!string.IsNullOrEmpty(_localPath))
                {
                    var full = System.IO.Path.Combine(_localPath!, itemId);
                    if (File.Exists(full))
                    {
                        File.Delete(full);
                        return true;
                    }
                    return false;
                }

                if (_folder is null) return false;

                var file = await _folder.GetFileAsync(itemId);

                if (file != null)
                {
                    await file.DeleteAsync();
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                OnConnectionError?.Invoke(ex);
                return false;
            }
        }

        public async Task<TaskItem?> Load(string itemId)
        {
            var jsonSerializer = new JsonSerializer();
            try
            {
                if (!string.IsNullOrEmpty(_localPath))
                {
                    var full = System.IO.Path.Combine(_localPath!, itemId);

                    // Ваш прежний «лечащий» парсер
                    var obj = JsonRepairingReader.DeserializeWithRepair<TaskItem>(full, jsonSerializer,
                        saveRepairedSidecar: false);
                    return obj;
                }

                if (_folder is null)
                    return null;

                var file = await _folder.GetFileAsync(itemId);

                if (file == null)
                    return null;

                await using var stream = await file.OpenReadAsync();
                var repaired = JsonRepairingReader.DeserializeWithRepair<TaskItem>(stream, jsonSerializer);
                return repaired;
            }
            catch (Exception ex)
            {
                OnConnectionError?.Invoke(ex);
                return null;
            }
        }

        public Task<bool> Connect() => Task.FromResult(true);

        public Task Disconnect() => Task.CompletedTask;

        public void SetPause(bool pause) => isPause = pause;

        public async Task<bool> Add(TaskItemViewModel change, TaskItemViewModel? currentTask = null,
            bool isBlocked = false)
        {
            var taskItemList =
                (await TaskTreeManager.AddTask(change.Model, currentTask?.Model, isBlocked)).OrderBy(t => t.SortOrder);
            var newTask = taskItemList.Last();
            change.Id = newTask.Id;
            change.Update(newTask);
            Tasks.AddOrUpdate(change);
            foreach (var task in taskItemList.SkipLast(1))
                UpdateCache(task);
            return true;
        }

        public async Task<bool> AddChild(TaskItemViewModel change, TaskItemViewModel currentTask)
        {
            var taskItemList =
                (await TaskTreeManager.AddChildTask(change.Model, currentTask.Model)).OrderBy(t => t.SortOrder);
            var newTask = taskItemList.Last();
            change.Id = newTask.Id;
            change.Update(newTask);
            Tasks.AddOrUpdate(change);
            foreach (var task in taskItemList.SkipLast(1))
                UpdateCache(task);
            return true;
        }

        public async Task<bool> Delete(TaskItemViewModel change, bool deleteInStorage = true)
        {
            var connectedItemList = await TaskTreeManager.DeleteTask(change.Model);
            foreach (var task in connectedItemList)
                UpdateCache(task);
            Tasks.Remove(change);

            if (deleteInStorage && !string.IsNullOrEmpty(change.Id))
                await Remove(change.Id);

            return true;
        }

        public async Task<bool> Delete(TaskItemViewModel change, TaskItemViewModel parent)
        {
            var connItemList = await TaskTreeManager.DeleteParentChildRelation(parent.Model, change.Model);
            foreach (var task in connItemList)
                UpdateCache(task);
            return true;
        }

        public async Task<bool> Update(TaskItemViewModel change)
        {
            await Update(change.Model);
            return true;
        }

        public async Task<bool> Update(TaskItem change)
        {
            await TaskTreeManager.UpdateTask(change);
            return true;
        }

        public async Task<TaskItemViewModel> Clone(TaskItemViewModel change,
            params TaskItemViewModel[]? additionalParents)
        {
            var additionalItemParents = new List<TaskItem>();
            if (additionalParents != null)
                additionalItemParents.AddRange(additionalParents.Select(p => p.Model));

            var taskItemList =
                (await TaskTreeManager.CloneTask(change.Model, additionalItemParents)).OrderBy(t => t.SortOrder);
            var newTask = taskItemList.Last();
            change.Id = newTask.Id;
            change.Update(newTask);
            Tasks.AddOrUpdate(change);
            foreach (var task in taskItemList.SkipLast(1))
                UpdateCache(task);
            return change;
        }

        public async Task<bool> CopyInto(TaskItemViewModel change, TaskItemViewModel[]? additionalParents)
        {
            var taskItemList = await TaskTreeManager.AddNewParentToTask(change.Model, additionalParents![0].Model);
            taskItemList.ForEach(UpdateCache);
            return true;
        }

        public async Task<bool> MoveInto(TaskItemViewModel change, TaskItemViewModel[] additionalParents,
            TaskItemViewModel? currentTask)
        {
            var taskItemList =
                await TaskTreeManager.MoveTaskToNewParent(change.Model, additionalParents[0].Model, currentTask?.Model);
            taskItemList.ForEach(UpdateCache);
            return true;
        }

        public async Task<bool> Unblock(TaskItemViewModel taskToUnblock, TaskItemViewModel blockingTask)
        {
            var taskItemList = await TaskTreeManager.UnblockTask(taskToUnblock.Model, blockingTask.Model);
            taskItemList.ForEach(UpdateCache);
            return true;
        }

        public async Task<bool> Block(TaskItemViewModel change, TaskItemViewModel currentTask)
        {
            var taskItemList = await TaskTreeManager.BlockTask(change.Model, currentTask.Model);
            taskItemList.ForEach(UpdateCache);
            return true;
        }

        public async Task RemoveParentChildConnection(TaskItemViewModel parent, TaskItemViewModel child)
        {
            var taskItemList = await TaskTreeManager.DeleteParentChildRelation(parent.Model, child.Model);
            taskItemList.ForEach(UpdateCache);
        }

        private async Task SafeDeleteByName(string name)
        {
            try
            {
                await Remove(name);
            }
            catch
            {
                /* ignore */
            }
        }

        private void UpdateCache(TaskItem task)
        {
            var vm = Tasks.Lookup(task.Id);
            if (vm.HasValue)
                vm.Value.Update(task);
            // иначе — пропускаем, как в исходнике (без NotFound)
        }
    }
}