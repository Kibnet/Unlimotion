using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unlimotion.Domain;
using Unlimotion.Storage;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

public sealed class TaskReloadStorageTests
{
    [Test]
    [Arguments("{broken")]
    [Arguments("")]
    [Arguments("null")]
    [Arguments("{\"Id\":\"another-task\"}")]
    public async Task Reload_CorruptOrWrongTaskIsFailedAndKeepsCachedSnapshot(string contents)
    {
        using var fixture = new Fixture();
        await fixture.Storage.Save(fixture.Task);
        await File.WriteAllTextAsync(fixture.File, contents);
        var result = await fixture.Storage.ReloadTaskAsync(fixture.Task.Id);
        await Assert.That(result.Outcome).IsEqualTo(TaskReloadOutcome.Failed);
        await Assert.That((await fixture.Storage.Load(fixture.Task.Id))!.Title).IsEqualTo("original");
        await Assert.That(await File.ReadAllTextAsync(fixture.File)).IsEqualTo(contents);
    }

    [Test]
    public async Task Reload_ForcedReadUpdatesStatusAndKeepsSourceBytesAndHistory()
    {
        using var fixture = new Fixture();
        await fixture.Storage.Save(fixture.Task);
        var updated = fixture.Task with { Title = "external", Status = DomainTaskStatus.Completed };
        var contents = JsonConvert.SerializeObject(updated);
        await File.WriteAllTextAsync(fixture.File, contents);
        var result = await fixture.Storage.ReloadTaskAsync(fixture.Task.Id);
        await Assert.That(result.Outcome).IsEqualTo(TaskReloadOutcome.Loaded);
        await Assert.That(result.Snapshot!.Status).IsEqualTo(DomainTaskStatus.Completed);
        await Assert.That((await fixture.Storage.Load(fixture.Task.Id))!.Title).IsEqualTo("external");
        await Assert.That(await File.ReadAllTextAsync(fixture.File)).IsEqualTo(contents);
        await Assert.That(result.Snapshot.StatusHistory.Count).IsEqualTo(updated.StatusHistory.Count);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Reload_RetriesDetachedReadWhenNewerUpdateOrDeleteArrives(bool delete)
    {
        using var fixture = new Fixture();
        await fixture.Storage.Save(fixture.Task);
        await fixture.Storage.EnableLiveGraphAsync();
        fixture.Storage.AfterRead = async () =>
        {
            if (delete) File.Delete(fixture.File);
            else await File.WriteAllTextAsync(fixture.File,
                JsonConvert.SerializeObject(fixture.Task with { Title = "newest", Status = DomainTaskStatus.Completed }));
            fixture.Storage.InvalidateLiveGraph();
        };
        var result = await fixture.Storage.ReloadTaskAsync(fixture.Task.Id);
        await Assert.That(result.Outcome).IsEqualTo(delete ? TaskReloadOutcome.Missing : TaskReloadOutcome.Loaded);
        var published = fixture.Storage.ReadLastPublishedGraph()!;
        if (delete) await Assert.That(published.Tasks).IsEmpty();
        else
        {
            await Assert.That(result.Snapshot!.Title).IsEqualTo("newest");
            await Assert.That(published.Tasks.Single().Status).IsEqualTo(DomainTaskStatus.Completed);
        }
    }

    [Test]
    public async Task Reload_WithoutWatcherDetectsChangedBytesBeforePublishing()
    {
        using var fixture = new Fixture();
        await fixture.Storage.Save(fixture.Task);
        fixture.Storage.AfterRead = () => File.WriteAllTextAsync(fixture.File,
            JsonConvert.SerializeObject(fixture.Task with { Title = "changed without watcher" }));
        var result = await fixture.Storage.ReloadTaskAsync(fixture.Task.Id);
        await Assert.That(result.Snapshot!.Title).IsEqualTo("changed without watcher");
    }

    [Test]
    public async Task Reload_ExhaustedGenerationRetriesReturnFailedWithoutPublication()
    {
        using var fixture = new Fixture();
        await fixture.Storage.Save(fixture.Task);
        fixture.Storage.AlwaysInvalidate = true;
        var result = await fixture.Storage.ReloadTaskAsync(fixture.Task.Id);
        await Assert.That(result.Failure).IsEqualTo(TaskReloadFailure.ChangedDuringRead);
        await Assert.That(fixture.Storage.ReadCount).IsEqualTo(3);
        await Assert.That(await File.ReadAllTextAsync(fixture.File)).Contains("original");
    }

    private sealed class ReloadRaceStorage(string path) : FileTaskStorage(new FileTaskStorageOptions { Path = path })
    {
        public Func<Task>? AfterRead { get; set; }
        public bool AlwaysInvalidate { get; set; }
        public int ReadCount { get; private set; }
        protected override async Task OnTaskReloadSnapshotReadAsync(string taskId)
        {
            ReadCount++;
            var action = AfterRead;
            AfterRead = null;
            if (action != null) await action();
            if (AlwaysInvalidate) InvalidateLiveGraph();
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "task-reload-" + Guid.NewGuid().ToString("N"));
        public TaskItem Task { get; } = new() { Id = "task", Title = "original", Status = DomainTaskStatus.Prepared };
        public ReloadRaceStorage Storage { get; }
        public string File => Path.Combine(directory, Task.Id);
        public Fixture() { Directory.CreateDirectory(directory); Storage = new ReloadRaceStorage(directory); }
        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
