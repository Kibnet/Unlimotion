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

    public static async Task Migrate(IAsyncEnumerable<TaskItem> tasks,
        Dictionary<string, (string getChild, string getParent)> links, Func<TaskItem, Task<TaskItem>> saveFunc,
        string storagePath, bool dryRun = false, CancellationToken ct = default)
    {
        var reportPath = Path.Combine(storagePath, "migration.report");
        if (File.Exists(reportPath))
        {
            //Открываем файл отчёта и получаем версию
            var reportJson = JsonConvert.DeserializeObject<dynamic>(File.ReadAllText(reportPath));
            var version = reportJson?.Version ?? 0;
            var valueVersion = ((JValue)version).Value;
            if (valueVersion is long _version && version >= Version)
                return;
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
        await Parallel.ForEachAsync(files, ct, async (taskItem, token) =>
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

        foreach (var (id, taskItem) in idToPath.OrderBy(k => k.Key))
        {
            try
            {
                foreach (var linkInfo in linkDict)
                {
                    // Нормализация Child (оставляем только валидные и существующие — по политике)
                    var childSet = linkInfo.Children.TryGetValue(id, out var cs) ? cs : new HashSet<string>();
                    var childList = childSet.Where(g => idToPath.ContainsKey(g)).Distinct().OrderBy(g => g)
                        .ToList();


                    var oldChildCount = (linkInfo.GetChild(taskItem))?.Count ?? 0;
                    linkInfo.SetChild(taskItem, childList.Select(g => g.ToString()).ToList());

                    // Установка Parents
                    var pset = linkInfo.Parents[id];
                    var plist = pset.OrderBy(g => g).ToList();

                    var oldParentsCount = (linkInfo.GetParent(taskItem))?.Count ?? 0;
                    linkInfo.SetParent(taskItem, plist.Select(g => g.ToString()).ToList());

                    updates.Add((id, oldParentsCount, plist.Count, oldChildCount, childList.Count));
                }

                if (dryRun) continue;

                var tmp = taskItem + ".__new";
                var bak = taskItem + ".__bak";
                taskItem.Version = 1;
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
            FilesTotal = idToPath.Count,
            Updated = updates.Count,
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
    }
}