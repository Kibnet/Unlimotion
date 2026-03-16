using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unlimotion.Domain;

namespace Unlimotion;

public class FileTaskMigrator
{
    public readonly record struct MigrationResult(bool SkippedByReport, bool AnyChanges, int UpdatedItems);

    private class LinkInfo
    {
        public string Id;
        public Func<TaskItem, List<string>> GetChild;
        public Action<TaskItem, List<string>> SetChild;

        public Func<TaskItem, List<string>> GetParent;
        public Action<TaskItem, List<string>> SetParent;

        public ConcurrentDictionary<string, HashSet<string>> Children = new();

        public Dictionary<string, HashSet<string>> Parents = new();
    }

    public static readonly int Version = 1;

    public static async Task<MigrationResult> Migrate(IAsyncEnumerable<TaskItem> tasks,
        Dictionary<string, (string getChild, string getParent)> links, Func<TaskItem, Task<TaskItem>> saveFunc,
        string storagePath, bool dryRun = false, CancellationToken ct = default, bool forceRecheck = false)
    {
        var reportPath = Path.Combine(storagePath, "migration.report");
        if (!forceRecheck && IsCurrentReport(reportPath))
        {
            return new MigrationResult(SkippedByReport: true, AnyChanges: false, UpdatedItems: 0);
        }

        var files = tasks;

        ConcurrentDictionary<string, TaskItem> idToPath = new();

        var linkDict = links.Select(pair => new LinkInfo
        {
            Id = pair.Key,
            GetChild = pair.Value.getChild switch
            {
                nameof(TaskItem.ContainsTasks) => task => task.ContainsTasks,
                nameof(TaskItem.BlocksTasks) => task => task.BlocksTasks,
                nameof(TaskItem.ParentTasks) => task => task.ParentTasks,
                nameof(TaskItem.BlockedByTasks) => task => task.BlockedByTasks,
                _ => _ => new List<string>()
            },
            SetChild = pair.Value.getChild switch
            {
                nameof(TaskItem.ContainsTasks) => (task, values) => task.ContainsTasks = values,
                nameof(TaskItem.BlocksTasks) => (task, values) => task.BlocksTasks = values,
                nameof(TaskItem.ParentTasks) => (task, values) => task.ParentTasks = values,
                nameof(TaskItem.BlockedByTasks) => (task, values) => task.BlockedByTasks = values,
                _ => (_, _) => { }
            },
            GetParent = pair.Value.getParent switch
            {
                nameof(TaskItem.ContainsTasks) => task => task.ContainsTasks,
                nameof(TaskItem.BlocksTasks) => task => task.BlocksTasks,
                nameof(TaskItem.ParentTasks) => task => task.ParentTasks,
                nameof(TaskItem.BlockedByTasks) => task => task.BlockedByTasks,
                _ => _ => []
            },
            SetParent = pair.Value.getParent switch
            {
                nameof(TaskItem.ContainsTasks) => (task, values) => task.ContainsTasks = values,
                nameof(TaskItem.BlocksTasks) => (task, values) => task.BlocksTasks = values,
                nameof(TaskItem.ParentTasks) => (task, values) => task.ParentTasks = values,
                nameof(TaskItem.BlockedByTasks) => (task, values) => task.BlockedByTasks = values,
                _ => (_, _) => { }
            }
        }).ToArray();
        var issues = new ConcurrentBag<string>();

        // ---------- ПАСС 1: Индексация ----------
        await Parallel.ForEachAsync(files, ct, (taskItem, token) =>
        {
            var fileId = taskItem.Id;
            idToPath[fileId] = taskItem;

            foreach (var linkInfo in linkDict)
            {
                try
                {
                    // Чтение Child
                    var set = new HashSet<string>();
                    var childProp = linkInfo.GetChild.Invoke(taskItem);
                    if (childProp != null)
                    {
                        foreach (var s in childProp)
                        {
                            if (s == fileId)
                            {
                                issues.Add($"SelfLinkRemoved: {linkInfo.Id} {fileId}");
                                continue;
                            }

                            set.Add(s);
                        }
                    }

                    linkInfo.Children[fileId] = set;
                }
                catch (Exception ex)
                {
                    issues.Add($"ReadError: {linkInfo.Id} {fileId} -> {ex.Message}");
                }
            }
            return ValueTask.CompletedTask;
        });

        foreach (var linkInfo in linkDict)
        {
            // Проверка «висящих» детей
            foreach (var (pid, kids) in linkInfo.Children)
            {
                foreach (var kid in kids)
                {
                    if (!idToPath.ContainsKey(kid))
                        issues.Add($"DanglingChildRef: {linkInfo.Id} parent={pid} child={kid}");
                }
            }

            // ---------- ПАСС 2: Расчёт Parents ----------
            foreach (var id in idToPath.Keys)
                linkInfo.Parents[id] = new HashSet<string>();

            foreach (var (p, kids) in linkInfo.Children)
            {
                foreach (var c in kids)
                {
                    if (linkInfo.Parents.TryGetValue(c, out var set))
                        set.Add(p);
                    // если ребёнка нет в каталоге — мы уже залогировали dangling, пропускаем
                }
            }
        }

        // ---------- ПАСС 3: Обновление ----------
        var updatedItems = 0;
        var anyRelationChanges = false;
        var newParentsTotal = 0;
        var childNormalizedTotal = 0;

        foreach (var (id, taskItem) in idToPath)
        {
            try
            {
                var changed = false;
                var relationChanged = false;
                foreach (var linkInfo in linkDict)
                {
                    // Нормализация Child (оставляем только валидные и существующие — по политике)
                    if (!linkInfo.Children.TryGetValue(id, out var childSet))
                        childSet = new();

                    var childList = new List<string>(childSet.Count);
                    foreach (var childId in childSet)
                    {
                        if (idToPath.ContainsKey(childId))
                        {
                            childList.Add(childId);
                        }
                    }

                    childList.Sort();

                    var existingChild = linkInfo.GetChild(taskItem);
                    if (!AreSameStrings(existingChild, childList))
                    {
                        changed = true;
                        relationChanged = true;
                        linkInfo.SetChild(taskItem, childList);
                        var existingChildCount = existingChild?.Count ?? 0;
                        childNormalizedTotal += Math.Abs(existingChildCount - childList.Count);
                    }

                    // Установка Parents
                    var pset = linkInfo.Parents[id];
                    var plist = pset.ToList();
                    plist.Sort();

                    var existingParents = linkInfo.GetParent(taskItem);
                    if (!AreSameStrings(existingParents, plist))
                    {
                        changed = true;
                        relationChanged = true;
                        linkInfo.SetParent(taskItem, plist);
                        var existingParentCount = existingParents?.Count ?? 0;
                        var diff = plist.Count - existingParentCount;
                        if (diff > 0)
                        {
                            newParentsTotal += diff;
                        }
                    }
                }

                if (taskItem.Version < Version)
                {
                    taskItem.Version = Version;
                    changed = true;
                }

                if (!changed) continue;

                if (relationChanged)
                    anyRelationChanges = true;
                updatedItems++;

                if (dryRun) continue;

                await saveFunc(taskItem);
            }
            catch (Exception ex)
            {
                issues.Add($"WriteError: {taskItem} -> {ex.Message}");
            }
        }

        // ---------- Отчёт ----------
        var report = new
        {
            Version = 1,
            Timestamp = DateTimeOffset.Now,
            DryRun = dryRun,
            ForceRecheck = forceRecheck,
            FilesTotal = idToPath.Count,
            Updated = updatedItems,
            Summary = new
            {
                ParentsAdded = newParentsTotal,
                ChildNormalized = childNormalizedTotal
            },
            Issues = issues.OrderBy(x => x).ToArray()
        };

        await File.WriteAllTextAsync(reportPath,
            JsonConvert.SerializeObject(report, Formatting.Indented));

        Debug.WriteLine(dryRun
            ? $"[DRY-RUN] Готово. Отчёт: {reportPath}"
            : $"Готово. Отчёт: {reportPath}");

        return new MigrationResult(SkippedByReport: false, AnyChanges: anyRelationChanges, UpdatedItems: updatedItems);
    }

    private static bool AreSameStrings(List<string>? existing, List<string> normalized)
    {
        if (existing == null || existing.Count == 0)
            return normalized.Count == 0;
        if (existing.Count != normalized.Count)
            return false;

        for (int i = 0; i < normalized.Count; i++)
        {
            if (!string.Equals(existing[i], normalized[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool IsCurrentReport(string reportPath)
    {
        if (!File.Exists(reportPath))
            return false;

        try
        {
            var reportJson = JObject.Parse(File.ReadAllText(reportPath));
            var reportVersion = reportJson["Version"]?.Value<int>() ?? 0;
            return reportVersion >= Version;
        }
        catch
        {
            return false;
        }
    }
}
