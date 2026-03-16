using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unlimotion.Domain;

namespace Unlimotion.Test;

public class MigrateTests : IDisposable
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
        if (path is null)
        {
            throw new InvalidOperationException("Report file not found");
        }

        return path;
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

    [Test]
    public async Task Migrate_BuildsParentsAndNormalizesChildren()
    {
        // A -> {B, C, B} (дубликат B), B и C без детей
        var a = new TaskItem { Id = "A", ContainsTasks = new List<string> { "B", "C", "B" } };
        var b = new TaskItem { Id = "B" };
        var c = new TaskItem { Id = "C" };

        var saveCount = 0;
        Task<TaskItem> Save(TaskItem t) { Interlocked.Increment(ref saveCount); return Task.FromResult(t); }

        await FileTaskMigrator.Migrate(AsAsync(a, b, c), props, Save, _tempDir, dryRun: false, CancellationToken.None);

        // A: children нормализованы и отсортированы
        await Assert.That(a.ContainsTasks.SequenceEqual(new[] { "B", "C" })).IsTrue();

        // Parents на B и C расставлены из Children у A
        await Assert.That(b.ParentTasks.SequenceEqual(new[] { "A" })).IsTrue();
        await Assert.That(c.ParentTasks.SequenceEqual(new[] { "A" })).IsTrue();

        // save вызван для каждого элемента
        await Assert.That(saveCount).IsEqualTo(3);

        // summary отражает 2 добавленных родителя (B<-A, C<-A) и факт нормализации children у A
        var report = GetReportPath();
        var (parentsAdded, childNormalized) = ReadSummary(report);
        await Assert.That(parentsAdded).IsEqualTo(2);
        await Assert.That(childNormalized).IsEqualTo(1);

        // Issues пуст
        var issues = ReadIssues(report);
        await Assert.That(issues).IsEmpty();
    }

    [Test]
    public async Task Migrate_BuildsBlockAndNormalizesBlock()
    {
        // A -> {B, C, B} (дубликат B), B и C без детей
        var a = new TaskItem { Id = "A", BlocksTasks = new List<string> { "B", "C", "B" } };
        var b = new TaskItem { Id = "B" };
        var c = new TaskItem { Id = "C" };

        var saveCount = 0;
        Task<TaskItem> Save(TaskItem t) { Interlocked.Increment(ref saveCount); return Task.FromResult(t); }

        await FileTaskMigrator.Migrate(AsAsync(a, b, c), props, Save, _tempDir, dryRun: false, CancellationToken.None);

        // A: children нормализованы и отсортированы
        await Assert.That(a.BlocksTasks.SequenceEqual(new[] { "B", "C" })).IsTrue();

        // Parents на B и C расставлены из Children у A
        await Assert.That(b.BlockedByTasks.SequenceEqual(new[] { "A" })).IsTrue();
        await Assert.That(c.BlockedByTasks.SequenceEqual(new[] { "A" })).IsTrue();

        // save вызван для каждого элемента
        await Assert.That(saveCount).IsEqualTo(3);

        // summary отражает 2 добавленных родителя (B<-A, C<-A) и факт нормализации children у A
        var report = GetReportPath();
        var (parentsAdded, childNormalized) = ReadSummary(report);
        await Assert.That(parentsAdded).IsEqualTo(2);
        await Assert.That(childNormalized).IsEqualTo(1);

        // Issues пуст
        var issues = ReadIssues(report);
        await Assert.That(issues).IsEmpty();
    }

    [Test]
    public async Task Migrate_RemovesDanglingChild_AndReportsIssue()
    {
        // A -> {X, B}, где X отсутствует
        var a = new TaskItem { Id = "A", ContainsTasks = new List<string> { "X", "B" } };
        var b = new TaskItem { Id = "B" };

        var saveCount = 0;
        Task<TaskItem> Save(TaskItem t) { Interlocked.Increment(ref saveCount); return Task.FromResult(t); }

        await FileTaskMigrator.Migrate(AsAsync(a, b), props, Save, _tempDir, dryRun: false, CancellationToken.None);

        // Висячая ссылка X удалена, остаётся только существующий B
        await Assert.That(a.ContainsTasks.SequenceEqual(new[] { "B" })).IsTrue();

        // Родитель у B добавлен
        await Assert.That(b.ParentTasks.SequenceEqual(new[] { "A" })).IsTrue();

        // Есть Issue про dangling
        var issues = ReadIssues(GetReportPath());
        await Assert.That(issues.Any(s => s == "DanglingChildRef: Contain parent=A child=X")).IsTrue();
    }

    [Test]
    public async Task Migrate_RemovesSelfLink_AndReportsIssue()
    {
        // A -> {A} (самоссылка)
        var a = new TaskItem { Id = "A", ContainsTasks = new List<string> { "A" } };

        var calls = 0;
        Task<TaskItem> Save(TaskItem t) { Interlocked.Increment(ref calls); return Task.FromResult(t); }

        await FileTaskMigrator.Migrate(AsAsync(a), props, Save, _tempDir, dryRun: false, CancellationToken.None);

        // Самоссылка удалена
        await Assert.That(a.ContainsTasks).IsEmpty();
        await Assert.That(a.ParentTasks).IsEmpty(); // у себя родителем не становится

        // Issue про самоссылку
        var issues = ReadIssues(GetReportPath());
        await Assert.That(issues.Any(s => s == "SelfLinkRemoved: Contain A")).IsTrue();
    }

    [Test]
    public async Task Migrate_DryRun_DoesNotCallSave_ButWritesReport()
    {
        var a = new TaskItem { Id = "A" };
        var b = new TaskItem { Id = "B", ContainsTasks = new List<string> { "A" } };

        var saveCount = 0;
        Task<TaskItem> Save(TaskItem t) { Interlocked.Increment(ref saveCount); return Task.FromResult(t); }

        await FileTaskMigrator.Migrate(AsAsync(a, b), props, Save, _tempDir, dryRun: true, CancellationToken.None);

        // данные в памяти всё равно обновлены
        await Assert.That(a.ParentTasks.SequenceEqual(new[] { "B" })).IsTrue();
        await Assert.That(b.ContainsTasks.SequenceEqual(new[] { "A" })).IsTrue();

        // но сохранения не было
        await Assert.That(saveCount).IsEqualTo(0);

        // отчёт существует
        await Assert.That(File.Exists(GetReportPath())).IsTrue();
    }

    [Test]
    public async Task Migrate_SaveError_IsReported_AndOtherItemsProceed()
    {
        var a = new TaskItem { Id = "A" };
        var b = new TaskItem { Id = "B" };

        var saved = new List<string>();
        Task<TaskItem> Save(TaskItem t)
        {
            if (t.Id == "B") throw new InvalidOperationException("boom");
            saved.Add(t.Id);
            return Task.FromResult(t);
        }

        await FileTaskMigrator.Migrate(AsAsync(a, b), props, Save, _tempDir, dryRun: false, CancellationToken.None);

        // A сохранился, B упал
        await Assert.That(saved).Contains("A");
        await Assert.That(saved).DoesNotContain("B");

        var issues = ReadIssues(GetReportPath());
        await Assert.That(issues.Any(s => s.StartsWith("WriteError: "))).IsTrue(); // конкретный текст включает ToString объекта
    }

    [Test]
    public async Task FileTaskMigrator_Migrate_ShouldSkip_WhenReportIsCurrent_AndForceDisabled()
    {
        var a = new TaskItem
        {
            Id = "A",
            Version = 0,
            ContainsTasks = new List<string> { "B" }
        };
        var b = new TaskItem
        {
            Id = "B",
            Version = 0,
            ParentTasks = new List<string>()
        };

        var reportPath = Path.Combine(_tempDir, "migration.report");
        await File.WriteAllTextAsync(reportPath, "{\"Version\":1}");

        var saveCount = 0;
        Task<TaskItem> Save(TaskItem t)
        {
            Interlocked.Increment(ref saveCount);
            return Task.FromResult(t);
        }

        var result = await FileTaskMigrator.Migrate(
            AsAsync(a, b),
            props,
            Save,
            _tempDir,
            dryRun: false,
            ct: CancellationToken.None,
            forceRecheck: false);

        await Assert.That(result.SkippedByReport).IsTrue();
        await Assert.That(result.AnyChanges).IsFalse();
        await Assert.That(result.UpdatedItems).IsEqualTo(0);
        await Assert.That(saveCount).IsEqualTo(0);
        await Assert.That(b.ParentTasks).IsEmpty();
        await Assert.That(a.Version).IsEqualTo(0);
        await Assert.That(b.Version).IsEqualTo(0);
    }

    [Test]
    public async Task FileTaskMigrator_Migrate_ShouldRecheck_WhenForceEnabled_EvenIfReportExists()
    {
        var a = new TaskItem
        {
            Id = "A",
            Version = 0,
            ContainsTasks = new List<string> { "B" }
        };
        var b = new TaskItem
        {
            Id = "B",
            Version = 0,
            ParentTasks = new List<string>()
        };

        var reportPath = Path.Combine(_tempDir, "migration.report");
        await File.WriteAllTextAsync(reportPath, "{\"Version\":1}");

        var saveCount = 0;
        Task<TaskItem> Save(TaskItem t)
        {
            Interlocked.Increment(ref saveCount);
            return Task.FromResult(t);
        }

        var result = await FileTaskMigrator.Migrate(
            AsAsync(a, b),
            props,
            Save,
            _tempDir,
            dryRun: false,
            ct: CancellationToken.None,
            forceRecheck: true);

        await Assert.That(result.SkippedByReport).IsFalse();
        await Assert.That(result.AnyChanges).IsTrue();
        await Assert.That(result.UpdatedItems >= 2).IsTrue();
        await Assert.That(b.ParentTasks).Contains("A");
        await Assert.That(a.Version).IsEqualTo(1);
        await Assert.That(b.Version).IsEqualTo(1);
        await Assert.That(saveCount >= 2).IsTrue();
    }
}
