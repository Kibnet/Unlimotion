using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        public string ChildProp;

        public Func<TaskItem, List<string>> GetChild =>
            item => item.GetType().GetProperty(ChildProp).GetValue(item) as List<string>;

        public Action<TaskItem, List<string>> SetChild =>
            (item, list) => item.GetType().GetProperty(ChildProp).SetValue(item, list);

        public string ParentProp;

        public Func<TaskItem, List<string>> GetParent =>
            item => item.GetType().GetProperty(ParentProp).GetValue(item) as List<string>;

        public Action<TaskItem, List<string>> SetParent =>
            (item, list) => item.GetType().GetProperty(ParentProp).SetValue(item, list);

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
            ChildProp = pair.Value.getChild,
            ParentProp = pair.Value.getParent,
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
        var updates = new List<(string id, int oldParents, int newParents, int oldChild, int newChild)>();
        var updatedItems = 0;
        var anyRelationChanges = false;

        foreach (var (id, taskItem) in idToPath.OrderBy(k => k.Key))
        {
            try
            {
                var changed = false;
                var relationChanged = false;
                foreach (var linkInfo in linkDict)
                {
                    // Нормализация Child (оставляем только валидные и существующие — по политике)
                    var childSet = linkInfo.Children.TryGetValue(id, out var cs) ? cs : new HashSet<string>();
                    var childList = childSet.Where(g => idToPath.ContainsKey(g)).Distinct().OrderBy(g => g)
                        .ToList();

                    var existingChild = (linkInfo.GetChild(taskItem))?.ToList() ?? new List<string>();
                    if (!existingChild.SequenceEqual(childList))
                    {
                        changed = true;
                        relationChanged = true;
                        linkInfo.SetChild(taskItem, childList.Select(g => g.ToString()).ToList());
                    }

                    var oldChildCount = existingChild.Count;

                    // Установка Parents
                    var pset = linkInfo.Parents[id];
                    var plist = pset.OrderBy(g => g).ToList();

                    var existingParents = (linkInfo.GetParent(taskItem))?.ToList() ?? new List<string>();
                    if (!existingParents.SequenceEqual(plist))
                    {
                        changed = true;
                        relationChanged = true;
                        linkInfo.SetParent(taskItem, plist.Select(g => g.ToString()).ToList());
                    }
                    var oldParentsCount = existingParents.Count;

                    updates.Add((id, oldParentsCount, plist.Count, oldChildCount, childList.Count));
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
                ParentsAdded = updates.Sum(u => Math.Max(0, u.newParents - u.oldParents)),
                ChildNormalized = updates.Count(u => u.oldChild != u.newChild)
            },
            Issues = issues.OrderBy(x => x).ToArray()
        };

        await File.WriteAllTextAsync(reportPath,
            JsonConvert.SerializeObject(report, Formatting.Indented));

        Console.WriteLine(dryRun
            ? $"[DRY-RUN] Готово. Отчёт: {reportPath}"
            : $"Готово. Отчёт: {reportPath}");

        return new MigrationResult(SkippedByReport: false, AnyChanges: anyRelationChanges, UpdatedItems: updatedItems);
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
