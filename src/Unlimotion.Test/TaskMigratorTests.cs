using Newtonsoft.Json.Linq;
using ServiceStack.OrmLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion;
using Unlimotion.Domain;
using Xunit;

namespace Unlimotion.Test;

public class MigrateTests
{
    private readonly string _tempDir;
    Dictionary<string, (string getChild, string getParent)> props;

    public MigrateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "migrate-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        props = new Dictionary<string, (string getChild, string getParent)>
        {
            { "Contain", (nameof(TaskItem.ContainsTasks), nameof(TaskItem.ParentTasks)) },
            { "Block", (nameof(TaskItem.BlocksTasks), nameof(TaskItem.BlockedByTasks)) },
        };
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
    }

    // --------- Helpers ---------

    private static async IAsyncEnumerable<TaskItem> AsAsync(params TaskItem[] items)
    {
        foreach (var i in items)
        {
            // имитируем асинхронный источник
            await Task.Yield();
            yield return i;
        }
    }

    private string GetReportPath()
    {
        // выбираем самый свежий отчёт
        var path = Directory.GetFiles(_tempDir, "migration.report")
                            .OrderByDescending(f => f)
                            .FirstOrDefault();
        Assert.False(path is null, "Report file not found");
        return path!;
    }

    private static List<string> ReadIssues(string reportPath)
    {
        var j = JObject.Parse(File.ReadAllText(reportPath));
        return j["Issues"]?.Values<string>().ToList() ?? new List<string>();
    }

    private static (int parentsAdded, int childNormalized) ReadSummary(string reportPath)
    {
        var j = JObject.Parse(File.ReadAllText(reportPath));
        var pa = j["Summary"]?["ParentsAdded"]?.Value<int>() ?? -1;
        var cn = j["Summary"]?["ChildNormalized"]?.Value<int>() ?? -1;
        return (pa, cn);
    }

    // --------- Tests ---------

    [Fact]
    public async Task Migrate_BuildsParentsAndNormalizesChildren()
    {
        // A -> {B, C, B} (дубликат B), B и C без детей
        var a = new TaskItem { Id = "A", ContainsTasks = new List<string> { "B", "C", "B" } };
        var b = new TaskItem { Id = "B" };
        var c = new TaskItem { Id = "C" };

        var saveCount = 0;
        Task<bool> Save(TaskItem t) { Interlocked.Increment(ref saveCount); return Task.FromResult(true); }

        await FileTaskMigrator.Migrate(AsAsync(a, b, c), props, Save, _tempDir, dryRun: false, CancellationToken.None);

        // A: children нормализованы и отсортированы
        Assert.Equal(new[] { "B", "C" }, a.ContainsTasks);

        // Parents на B и C расставлены из Children у A
        Assert.Equal(new[] { "A" }, b.ParentTasks);
        Assert.Equal(new[] { "A" }, c.ParentTasks);

        // save вызван для каждого элемента
        Assert.Equal(3, saveCount);

        // summary отражает 2 добавленных родителя (B<-A, C<-A) и факт нормализации children у A
        var report = GetReportPath();
        var (parentsAdded, childNormalized) = ReadSummary(report);
        Assert.Equal(2, parentsAdded);
        Assert.Equal(1, childNormalized);

        // Issues пуст
        var issues = ReadIssues(report);
        Assert.Empty(issues);
    }

    [Fact]
    public async Task Migrate_BuildsBlockAndNormalizesBlock()
    {
        // A -> {B, C, B} (дубликат B), B и C без детей
        var a = new TaskItem { Id = "A", BlocksTasks = new List<string> { "B", "C", "B" } };
        var b = new TaskItem { Id = "B" };
        var c = new TaskItem { Id = "C" };

        var saveCount = 0;
        Task<bool> Save(TaskItem t) { Interlocked.Increment(ref saveCount); return Task.FromResult(true); }

        await FileTaskMigrator.Migrate(AsAsync(a, b, c), props, Save, _tempDir, dryRun: false, CancellationToken.None);

        // A: children нормализованы и отсортированы
        Assert.Equal(new[] { "B", "C" }, a.BlocksTasks);

        // Parents на B и C расставлены из Children у A
        Assert.Equal(new[] { "A" }, b.BlockedByTasks);
        Assert.Equal(new[] { "A" }, c.BlockedByTasks);

        // save вызван для каждого элемента
        Assert.Equal(3, saveCount);

        // summary отражает 2 добавленных родителя (B<-A, C<-A) и факт нормализации children у A
        var report = GetReportPath();
        var (parentsAdded, childNormalized) = ReadSummary(report);
        Assert.Equal(2, parentsAdded);
        Assert.Equal(1, childNormalized);

        // Issues пуст
        var issues = ReadIssues(report);
        Assert.Empty(issues);
    }

    [Fact]
    public async Task Migrate_RemovesDanglingChild_AndReportsIssue()
    {
        // A -> {X, B}, где X отсутствует
        var a = new TaskItem { Id = "A", ContainsTasks = new List<string> { "X", "B" } };
        var b = new TaskItem { Id = "B" };

        var saveCount = 0;
        Task<bool> Save(TaskItem t) { Interlocked.Increment(ref saveCount); return Task.FromResult(true); }

        await FileTaskMigrator.Migrate(AsAsync(a, b), props, Save, _tempDir, dryRun: false, CancellationToken.None);

        // Висячая ссылка X удалена, остаётся только существующий B
        Assert.Equal(new[] { "B" }, a.ContainsTasks);

        // Родитель у B добавлен
        Assert.Equal(new[] { "A" }, b.ParentTasks);

        // Есть Issue про dangling
        var issues = ReadIssues(GetReportPath());
        Assert.Contains(issues, s => s == "DanglingChildRef: Contain parent=A child=X");
    }

    [Fact]
    public async Task Migrate_RemovesSelfLink_AndReportsIssue()
    {
        // A -> {A} (самоссылка)
        var a = new TaskItem { Id = "A", ContainsTasks = new List<string> { "A" } };

        var calls = 0;
        Task<bool> Save(TaskItem t) { Interlocked.Increment(ref calls); return Task.FromResult(true); }

        await FileTaskMigrator.Migrate(AsAsync(a), props, Save, _tempDir, dryRun: false, CancellationToken.None);

        // Самоссылка удалена
        Assert.Empty(a.ContainsTasks);
        Assert.Empty(a.ParentTasks); // у себя родителем не становится

        // Issue про самоссылку
        var issues = ReadIssues(GetReportPath());
        Assert.Contains(issues, s => s == "SelfLinkRemoved: Contain A");
    }

    [Fact]
    public async Task Migrate_DryRun_DoesNotCallSave_ButWritesReport()
    {
        var a = new TaskItem { Id = "A" };
        var b = new TaskItem { Id = "B", ContainsTasks = new List<string> { "A" } };

        var saveCount = 0;
        Task<bool> Save(TaskItem t) { Interlocked.Increment(ref saveCount); return Task.FromResult(true); }

        await FileTaskMigrator.Migrate(AsAsync(a, b), props, Save, _tempDir, dryRun: true, CancellationToken.None);

        // данные в памяти всё равно обновлены
        Assert.Equal(new[] { "B" }, a.ParentTasks);
        Assert.Equal(new[] { "A" }, b.ContainsTasks);

        // но сохранения не было
        Assert.Equal(0, saveCount);

        // отчёт существует
        Assert.True(File.Exists(GetReportPath()));
    }

    [Fact]
    public async Task Migrate_SaveError_IsReported_AndOtherItemsProceed()
    {
        var a = new TaskItem { Id = "A" };
        var b = new TaskItem { Id = "B" };

        var saved = new List<string>();
        Task<bool> Save(TaskItem t)
        {
            if (t.Id == "B") throw new InvalidOperationException("boom");
            saved.Add(t.Id);
            return Task.FromResult(true);
        }

        await FileTaskMigrator.Migrate(AsAsync(a, b), props, Save, _tempDir, dryRun: false, CancellationToken.None);

        // A сохранился, B упал
        Assert.Contains("A", saved);
        Assert.DoesNotContain("B", saved);

        var issues = ReadIssues(GetReportPath());
        Assert.Contains(issues, s => s.StartsWith("WriteError: ")); // конкретный текст включает ToString объекта
    }
}
