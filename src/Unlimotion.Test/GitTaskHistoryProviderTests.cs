using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Newtonsoft.Json.Linq;
using Unlimotion.Services;

namespace Unlimotion.Test;

[NotInParallel("GitTaskHistory")]
public sealed class GitTaskHistoryProviderTests
{
    [Test]
    public async Task GetPageAsync_ReturnsWorkingTreeAndCommitChanges_WithStablePaging()
    {
        using var fixture = new GitHistoryFixture();
        fixture.WriteTask("Original", "NotReady", "first");
        fixture.Commit("create task");
        fixture.WriteTask("Renamed", "InProgress", "first");
        fixture.Commit("rename and start");
        fixture.WriteTask("Renamed", "InProgress", "working copy");

        var provider = new GitTaskHistoryProvider();
        var first = await provider.GetPageAsync(
            new TaskHistoryRequest(fixture.StoragePath, "local", fixture.TaskId, null, PageSize: 1),
            CancellationToken.None);

        await Assert.That(first.Entries.Count).IsEqualTo(2);
        await Assert.That(first.Entries[0].IsWorkingTree).IsTrue();
        await Assert.That(first.Entries[0].Changes.Any(change =>
            change.DisplayName.Contains("Description", StringComparison.OrdinalIgnoreCase) ||
            change.FieldPath == "Description")).IsTrue();
        await Assert.That(first.Entries[1].Message).IsEqualTo("rename and start");
        await Assert.That(first.Entries[1].Changes.Any(change => change.FieldPath == "Title")).IsTrue();
        await Assert.That(first.Entries[1].Changes.Any(change => change.FieldPath == "Status")).IsTrue();
        await Assert.That(first.NextCursor is not null).IsTrue();

        var second = await provider.GetPageAsync(
            new TaskHistoryRequest(fixture.StoragePath, "local", fixture.TaskId, first.NextCursor, PageSize: 1),
            CancellationToken.None);

        await Assert.That(second.Entries.Count).IsEqualTo(1);
        await Assert.That(second.Entries[0].Message).IsEqualTo("create task");
        await Assert.That(second.NextCursor is null).IsTrue();
    }

    [Test]
    public async Task GetPageAsync_UsesTaskIdWhenFileWasRenamed()
    {
        using var fixture = new GitHistoryFixture();
        fixture.WriteTask("Original", "NotReady", "first", "task-a.json");
        fixture.Commit("create task");
        File.Move(
            Path.Combine(fixture.StoragePath, "task-a.json"),
            Path.Combine(fixture.StoragePath, "renamed.json"));
        fixture.WriteTask("Renamed", "NotReady", "first", "renamed.json");
        fixture.Commit("rename file");

        var page = await new GitTaskHistoryProvider().GetPageAsync(
            new TaskHistoryRequest(fixture.StoragePath, "local", fixture.TaskId, null),
            CancellationToken.None);

        var rename = page.Entries.Single(entry => entry.Message == "rename file");
        await Assert.That(rename.Changes.Any(change => change.FieldPath == "@path")).IsTrue();
        await Assert.That(rename.Changes.Any(change => change.FieldPath == "Title")).IsTrue();
    }


    [Test]
    public async Task ReadValueAsync_ReturnsFullLongValueLazily()
    {
        using var fixture = new GitHistoryFixture();
        var oldDescription = new string('a', 420);
        var newDescription = new string('b', 460);
        fixture.WriteTask("Task", "NotReady", oldDescription);
        fixture.Commit("create task");
        fixture.WriteTask("Task", "NotReady", newDescription);
        fixture.Commit("replace long description");

        var provider = new GitTaskHistoryProvider();
        var page = await provider.GetPageAsync(
            new TaskHistoryRequest(fixture.StoragePath, "local", fixture.TaskId, null),
            CancellationToken.None);
        var description = page.Entries
            .Single(entry => entry.Message == "replace long description")
            .Changes.Single(change => change.FieldPath == "Description");

        await Assert.That(description.HasDetails).IsTrue();
        await Assert.That(description.OldValueReference).IsNotNull();
        await Assert.That(description.NewValueReference).IsNotNull();
        await Assert.That(await provider.ReadValueAsync(
            description.OldValueReference!, CancellationToken.None)).IsEqualTo(oldDescription);
        await Assert.That(await provider.ReadValueAsync(
            description.NewValueReference!, CancellationToken.None)).IsEqualTo(newDescription);
    }

    [Test]
    public async Task GetPageAsync_StopsAtCreationBeforeUnrelatedUnreadableFiles()
    {
        using var fixture = new GitHistoryFixture();
        File.WriteAllText(Path.Combine(fixture.StoragePath, "unrelated.json"), "");
        fixture.Commit("before task existed");
        fixture.WriteTask("Created", "NotReady", "first");
        fixture.Commit("create task");

        var page = await new GitTaskHistoryProvider().GetPageAsync(
            new TaskHistoryRequest(fixture.StoragePath, "local", fixture.TaskId, null, PageSize: 1), CancellationToken.None);

        await Assert.That(page.Entries.Select(entry => entry.Message).ToArray()).IsEquivalentTo(new[] { "create task" });
        await Assert.That(page.NextCursor).IsNull();
        await Assert.That(page.IsPartial).IsFalse();
    }

    [Test]
    public async Task GetPageAsync_MergeRetainsBothBranchesOfCurrentIncarnation()
    {
        using var fixture = new GitHistoryFixture();
        fixture.WriteTask("Initial", "NotReady", "initial");
        fixture.Commit("create task");
        var repository = fixture.Repository;
        var side = repository.CreateBranch("side");
        fixture.WriteTask("Main edit", "NotReady", "initial");
        fixture.Commit("main edit");
        var mainTip = repository.Head.Tip;
        Commands.Checkout(repository, side);
        fixture.WriteTask("Initial", "NotReady", "side edit");
        fixture.Commit("side edit");
        var signature = new Signature("Test", "test@unlimotion.local", DateTimeOffset.UtcNow);
        var merge = repository.ObjectDatabase.CreateCommit(signature, signature, "merge", repository.Head.Tip.Tree,
            [repository.Head.Tip, mainTip], false);
        repository.Reset(ResetMode.Hard, merge);
        var page = await new GitTaskHistoryProvider().GetPageAsync(
            new TaskHistoryRequest(fixture.StoragePath, "local", fixture.TaskId, null), CancellationToken.None);
        await Assert.That(page.Entries.Any(entry => entry.Message == "main edit")).IsTrue();
        await Assert.That(page.Entries.Any(entry => entry.Message == "side edit")).IsTrue();
        await Assert.That(page.Entries.Last().Message).IsEqualTo("create task");
    }

    [Test]
    public async Task GetPageAsync_RecreatedTaskDoesNotIncludePreviousIncarnation()
    {
        using var fixture = new GitHistoryFixture();
        fixture.WriteTask("Old task", "NotReady", "old");
        fixture.Commit("old incarnation");
        File.Delete(Path.Combine(fixture.StoragePath, "task.json"));
        fixture.Commit("delete task");
        fixture.WriteTask("New task", "NotReady", "new");
        fixture.Commit("recreate task");
        fixture.WriteTask("New task edited", "NotReady", "new");
        fixture.Commit("edit current task");
        var provider = new GitTaskHistoryProvider();
        var first = await provider.GetPageAsync(
            new TaskHistoryRequest(fixture.StoragePath, "local", fixture.TaskId, null, PageSize: 1), CancellationToken.None);
        var last = await provider.GetPageAsync(
            new TaskHistoryRequest(fixture.StoragePath, "local", fixture.TaskId, first.NextCursor, PageSize: 1), CancellationToken.None);

        await Assert.That(first.Entries.Single().Message).IsEqualTo("edit current task");
        await Assert.That(last.Entries.Single().Message).IsEqualTo("recreate task");
        await Assert.That(last.NextCursor).IsNull();
        await Assert.That(last.IsPartial).IsFalse();
    }

    [Test]
    public async Task GetPageAsync_UncommittedRecreatedTaskHasOnlyWorkingCopy()
    {
        using var fixture = new GitHistoryFixture();
        fixture.WriteTask("Old task", "NotReady", "old");
        fixture.Commit("old incarnation");
        File.Delete(Path.Combine(fixture.StoragePath, "task.json"));
        fixture.Commit("delete task");
        fixture.WriteTask("New task", "NotReady", "new");

        var page = await new GitTaskHistoryProvider().GetPageAsync(
            new TaskHistoryRequest(fixture.StoragePath, "local", fixture.TaskId, null), CancellationToken.None);

        await Assert.That(page.Entries.Count).IsEqualTo(1);
        await Assert.That(page.Entries.Single().IsWorkingTree).IsTrue();
        await Assert.That(page.NextCursor).IsNull();
        await Assert.That(page.IsPartial).IsFalse();
    }

    [Test]
    public async Task GetPageAsync_KnownEmptyRevisionHasAccurateNotice()
    {
        using var fixture = new GitHistoryFixture();
        fixture.WriteTask("Task", "NotReady", "first");
        fixture.Commit("create task");
        File.WriteAllText(Path.Combine(fixture.StoragePath, "task.json"), "");
        fixture.Commit("empty task file");
        fixture.WriteTask("Task", "NotReady", "repaired");
        fixture.Commit("repair task");

        var page = await new GitTaskHistoryProvider().GetPageAsync(
            new TaskHistoryRequest(fixture.StoragePath, "local", fixture.TaskId, null), CancellationToken.None);
        var empty = page.Entries.Single(entry => entry.Message == "empty task file");
        await Assert.That(empty.Notice).IsEqualTo(Unlimotion.ViewModel.Localization.Localization.Get("TaskHistoryEmptyRevision"));
        await Assert.That(page.IsPartial).IsTrue();
    }

    [Test]
    public async Task DiffBuilder_InitialEmptyValuesAreMetadataButRealClearingIsVisible()
    {
        var missing = Snapshot("""{"Id":"1"}""");
        var initialized = Snapshot("""{"Id":"1","Description":"","Repeater":null,"PlannedBeginDateTime":null,"PlannedEndDateTime":null,"PlannedDuration":null,"Extension":[]}""");
        foreach (var changes in new[]
                 {
                     TaskHistoryDiffBuilder.Build(missing, initialized, "repo", "root", "old", "new"),
                     TaskHistoryDiffBuilder.Build(initialized, missing, "repo", "root", "old", "new")
                 })
        {
            await Assert.That(changes.Count).IsGreaterThan(0);
            await Assert.That(changes.All(change => change.IsMetadata)).IsTrue();
        }
        var cleared = TaskHistoryDiffBuilder.Build(
            Snapshot("""{"Id":"1","Description":"Text","PlannedBeginDateTime":"2026-10-01"}"""),
            Snapshot("""{"Id":"1","Description":"","PlannedBeginDateTime":null}"""), "repo", "root", "old", "new");
        await Assert.That(cleared.Count).IsEqualTo(2);
        await Assert.That(cleared.Any(change => change.IsMetadata)).IsFalse();
        var archived = TaskHistoryDiffBuilder.Build(missing, Snapshot("""{"Id":"1","IsCompleted":null}"""), "repo", "root", "old", "new");
        await Assert.That(archived.Single().IsMetadata).IsFalse();
    }

    [Test]
    public async Task DiffBuilder_StructuredValuesAreReadableWithoutJsonPunctuation()
    {
        var changes = TaskHistoryDiffBuilder.Build(
            Snapshot("""{"Id":"1","StatusHistory":[]}"""),
            Snapshot("""{"Id":"1","StatusHistory":[{"Author":"Иван","Status":"InProgress"}]}"""), "repo", "root", "old", "new");
        var change = changes.Single();
        await Assert.That(change.IsMetadata).IsTrue();
        await Assert.That(change.OldValueDisplay).IsEqualTo(Unlimotion.ViewModel.Localization.Localization.Get("TaskHistoryEmptyList"));
        await Assert.That(change.NewValueDisplay.Contains("Иван", StringComparison.Ordinal)).IsTrue();
        await Assert.That(change.NewValueDisplay.Contains('[') || change.NewValueDisplay.Contains('{')).IsFalse();
        var full = TaskHistoryDiffBuilder.FormatFullValue(JToken.Parse("""[{"Author":"Иван","Nested":{"Count":2}}]"""));
        await Assert.That(full.Contains("Иван", StringComparison.Ordinal)).IsTrue();
        await Assert.That(full.Contains('[') || full.Contains('{')).IsFalse();
    }

    [Test]
    public async Task ReadValueAsync_RejectsWorkingCopyChangedAfterPreview()
    {
        using var fixture = new GitHistoryFixture();
        fixture.WriteTask("Task", "NotReady", new string('a', 420));
        fixture.Commit("create task");
        fixture.WriteTask("Task", "NotReady", new string('b', 460));

        var provider = new GitTaskHistoryProvider();
        var page = await provider.GetPageAsync(
            new TaskHistoryRequest(fixture.StoragePath, "local", fixture.TaskId, null),
            CancellationToken.None);
        var description = page.Entries.Single(entry => entry.IsWorkingTree)
            .Changes.Single(change => change.FieldPath == "Description");

        fixture.WriteTask("Task", "NotReady", new string('c', 480));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            provider.ReadValueAsync(description.NewValueReference!, CancellationToken.None));
        await Assert.That(string.IsNullOrWhiteSpace(error!.Message)).IsFalse();
    }

    [Test]
    public async Task GetPageAsync_ResetSessionRejectsOldCursor()
    {
        using var fixture = new GitHistoryFixture();
        fixture.WriteTask("One", "NotReady", "first");
        fixture.Commit("create task");
        fixture.WriteTask("Two", "NotReady", "second");
        fixture.Commit("second revision");

        var provider = new GitTaskHistoryProvider();
        var first = await provider.GetPageAsync(
            new TaskHistoryRequest(fixture.StoragePath, "local", fixture.TaskId, null, PageSize: 1),
            CancellationToken.None);
        provider.ResetSession();

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetPageAsync(
            new TaskHistoryRequest(fixture.StoragePath, "local", fixture.TaskId, first.NextCursor, PageSize: 1),
            CancellationToken.None));
    }

    [Test]
    public async Task GetPageAsync_ContinuesFrozenHeadAfterRepositoryAdvances()
    {
        using var fixture = new GitHistoryFixture();
        fixture.WriteTask("One", "NotReady", "first");
        fixture.Commit("create task");
        fixture.WriteTask("Two", "NotReady", "second");
        fixture.Commit("second revision");

        var provider = new GitTaskHistoryProvider();
        var first = await provider.GetPageAsync(
            new TaskHistoryRequest(fixture.StoragePath, "local", fixture.TaskId, null, PageSize: 1),
            CancellationToken.None);
        fixture.WriteTask("Three", "NotReady", "third");
        fixture.Commit("new head after paging started");

        var second = await provider.GetPageAsync(
            new TaskHistoryRequest(fixture.StoragePath, "local", fixture.TaskId, first.NextCursor, PageSize: 1),
            CancellationToken.None);

        await Assert.That(second.Entries.Single().Message).IsEqualTo("create task");
    }

    [Test]
    public async Task GetPageAsync_MarksCorruptKnownRevisionAsPartial()
    {
        using var fixture = new GitHistoryFixture();
        fixture.WriteTask("One", "NotReady", "first");
        fixture.Commit("create task");
        File.WriteAllText(Path.Combine(fixture.StoragePath, "task.json"), "[");
        fixture.Commit("corrupt task");
        fixture.WriteTask("Two", "InProgress", "recovered");
        fixture.Commit("recover task");

        var page = await new GitTaskHistoryProvider().GetPageAsync(
            new TaskHistoryRequest(fixture.StoragePath, "local", fixture.TaskId, null),
            CancellationToken.None);

        await Assert.That(page.IsPartial).IsTrue();
        await Assert.That(page.Entries.Any(entry => entry.IsPartial)).IsTrue();
    }

    [Test]
    public async Task GetPageAsync_ContinuesPastCorruptHeadWhenWorkingTreeWasRepaired()
    {
        using var fixture = new GitHistoryFixture();
        fixture.WriteTask("One", "NotReady", "first");
        fixture.Commit("create task");
        File.WriteAllText(Path.Combine(fixture.StoragePath, "task.json"), "[");
        fixture.Commit("corrupt head");
        fixture.WriteTask("Two", "InProgress", "repaired working tree");

        var page = await new GitTaskHistoryProvider().GetPageAsync(
            new TaskHistoryRequest(fixture.StoragePath, "local", fixture.TaskId, null),
            CancellationToken.None);

        await Assert.That(page.IsPartial).IsTrue();
        await Assert.That(page.Entries.Any(entry => entry.IsWorkingTree && entry.IsPartial)).IsTrue();
        await Assert.That(page.Entries.Any(entry => entry.Message == "create task")).IsTrue();
    }

    [Test]
    public async Task GetPageAsync_MarksCorruptRevisionAsPartialAcrossRenameAndRepair()
    {
        using var fixture = new GitHistoryFixture();
        fixture.WriteTask("One", "NotReady", "first", "a.json");
        File.WriteAllText(Path.Combine(fixture.StoragePath, "c.json"), """{"Id":"other","Title":"Other 1"}""");
        fixture.Commit("create task");
        File.WriteAllText(Path.Combine(fixture.StoragePath, "a.json"), "[");
        fixture.Commit("corrupt old path");
        File.Delete(Path.Combine(fixture.StoragePath, "a.json"));
        fixture.WriteTask("Two", "InProgress", "recovered", "b.json");
        File.WriteAllText(Path.Combine(fixture.StoragePath, "c.json"), """{"Id":"other","Title":"Other 2"}""");
        fixture.Commit("rename and repair");

        var page = await new GitTaskHistoryProvider().GetPageAsync(
            new TaskHistoryRequest(fixture.StoragePath, "local", fixture.TaskId, null),
            CancellationToken.None);

        await Assert.That(page.IsPartial).IsTrue();
        var repair = page.Entries.Single(entry => entry.Message == "rename and repair");
        await Assert.That(repair.IsPartial).IsTrue();
        await Assert.That(repair.Changes.Any(change =>
            change.ChangeType is TaskHistoryChangeType.Added or TaskHistoryChangeType.Removed)).IsFalse();
    }

    [Test]
    public async Task DiffBuilder_PreservesObjectToNullAndScalarTypeChanges()
    {
        var objectToNull = TaskHistoryDiffBuilder.Build(
            Snapshot("""{"Id":"1","X":{}}"""),
            Snapshot("""{"Id":"1","X":null}"""),
            "repo", "root", "old", "new");
        var objectToScalar = TaskHistoryDiffBuilder.Build(
            Snapshot("""{"Id":"1","X":{"a":1}}"""),
            Snapshot("""{"Id":"1","X":"new"}"""),
            "repo", "root", "old", "new");

        await Assert.That(objectToNull.Count).IsEqualTo(1);
        await Assert.That(objectToNull[0].FieldPath).IsEqualTo("X");
        await Assert.That(objectToScalar.Count).IsEqualTo(1);
        await Assert.That(objectToScalar[0].NewValueDisplay).IsEqualTo("new");
    }

    [Test]
    public async Task DiffBuilder_ReportsCriteriaOrderAndExtensionChanges()
    {
        var reordered = TaskHistoryDiffBuilder.Build(
            Snapshot("""{"Id":"1","CompletionCriteria":[{"Id":"a","Text":"A"},{"Id":"b","Text":"B"}]}"""),
            Snapshot("""{"Id":"1","CompletionCriteria":[{"Id":"b","Text":"B"},{"Id":"a","Text":"A"}]}"""),
            "repo", "root", "old", "new");
        var extension = TaskHistoryDiffBuilder.Build(
            Snapshot("""{"Id":"1","CompletionCriteria":[{"Id":"a","Text":"A","Extra":1}]}"""),
            Snapshot("""{"Id":"1","CompletionCriteria":[{"Id":"a","Text":"A","Extra":2}]}"""),
            "repo", "root", "old", "new");

        await Assert.That(reordered.Any(change => change.FieldPath.EndsWith("@order", StringComparison.Ordinal))).IsTrue();
        await Assert.That(extension.Single().OldValueDisplay == extension.Single().NewValueDisplay).IsFalse();
    }

    [Test]
    public async Task GetPageAsync_ReportsUnavailableOutsideGit()
    {
        var path = Path.Combine(Path.GetTempPath(), "unlimotion-history-no-git", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            var page = await new GitTaskHistoryProvider().GetPageAsync(
                new TaskHistoryRequest(path, "local", Guid.NewGuid().ToString(), null),
                CancellationToken.None);

            await Assert.That(page.IsUnavailable).IsTrue();
            await Assert.That(string.IsNullOrWhiteSpace(page.StatusMessage)).IsFalse();
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Test]
    public async Task DiffBuilder_HidesEquivalentLegacyStatusMigration_ButShowsRealLegacyTransition()
    {
        var oldEquivalent = Snapshot("""
            {"Id":"1","Title":"Task","IsCompleted":false}
            """);
        var modernEquivalent = Snapshot("""
            {"Id":"1","Title":"Task","Status":"NotReady"}
            """);
        var equivalent = TaskHistoryDiffBuilder.Build(
            oldEquivalent, modernEquivalent, "repo", "root", "old", "new");

        await Assert.That(equivalent.Count).IsEqualTo(2);
        await Assert.That(equivalent.All(change => change.IsMetadata)).IsTrue();

        var oldStatus = Snapshot("""
            {"Id":"1","Title":"Task","IsCompleted":false}
            """);
        var completed = Snapshot("""
            {"Id":"1","Title":"Task","IsCompleted":true}
            """);
        var transition = TaskHistoryDiffBuilder.Build(
            oldStatus, completed, "repo", "root", "old", "new");

        await Assert.That(transition.Count).IsEqualTo(1);
        await Assert.That(transition[0].FieldPath).IsEqualTo("IsCompleted");
        await Assert.That(transition[0].IsMetadata).IsFalse();
    }

    [Test]
    public async Task PaneViewModel_IgnoresLatePageFromPreviouslySelectedTask()
    {
        var provider = new DelayedProvider();
        using var viewModel = new TaskHistoryPaneViewModel(provider);
        viewModel.SelectTask("storage", "source", "old");
        var oldRefresh = viewModel.RefreshAsync();
        await provider.Started.Task;

        viewModel.SelectTask("storage", "source", "new");
        provider.Release.SetResult();
        await oldRefresh;

        await Assert.That(viewModel.Entries.Count).IsEqualTo(0);
        await Assert.That(viewModel.HasMore).IsFalse();
    }

    [Test]
    public async Task DiffBuilder_TreatsAbsentAndNullDatesAsMetadata_ButShowsRealDateChange()
    {
        var emptyDates = TaskHistoryDiffBuilder.Build(
            Snapshot("""{"Id":"1"}"""),
            Snapshot("""{"Id":"1","CompletedDateTime":null,"ArchiveDateTime":null}"""),
            "repo", "root", "old", "new");
        var completed = TaskHistoryDiffBuilder.Build(
            Snapshot("""{"Id":"1","CompletedDateTime":null}"""),
            Snapshot("""{"Id":"1","CompletedDateTime":"2026-09-27T10:00:00Z"}"""),
            "repo", "root", "old", "new");

        await Assert.That(emptyDates.Count).IsEqualTo(2);
        await Assert.That(emptyDates.All(change => change.IsMetadata)).IsTrue();
        await Assert.That(completed.Single().IsMetadata).IsFalse();
    }

    [Test]
    public async Task PaneViewModel_KeepsOldRowsVisibleUntilRefreshCompletes()
    {
        var provider = new RefreshProvider();
        using var viewModel = new TaskHistoryPaneViewModel(provider);
        viewModel.SelectTask("storage", "source", "task");
        await viewModel.RefreshAsync();

        var refresh = viewModel.RefreshAsync();
        await provider.SecondRequestStarted.Task;
        await Assert.That(viewModel.Entries.Count).IsEqualTo(1);
        await Assert.That(viewModel.Entries[0].Message).IsEqualTo("first");

        provider.ReleaseSecond.SetResult();
        await refresh;
        await Assert.That(viewModel.Entries.Count).IsEqualTo(1);
        await Assert.That(viewModel.Entries[0].Message).IsEqualTo("second");
    }

    [Test]
    public async Task PaneViewModel_CancelsDelayedDetailsWithoutPublishingOrThrowing()
    {
        var provider = new DelayedDetailsProvider();
        using var viewModel = new TaskHistoryPaneViewModel(provider);
        viewModel.SelectTask("storage", "source", "old");
        var reference = new TaskHistoryValueReference("repo", "sha", "task.json", "Description", false);
        var change = new TaskHistoryFieldChange(
            "Description", "Description", "old…", "new…", TaskHistoryChangeType.Modified, false, reference, reference);

        var details = viewModel.ShowDetailsAsync(change);
        await provider.Started.Task;
        viewModel.SelectTask("storage", "source", "new");
        provider.Release.SetResult("late value");
        await details;

        await Assert.That(viewModel.HasDetails).IsFalse();
        await Assert.That(viewModel.DetailOldValue).IsEmpty();
        await Assert.That(viewModel.DetailNewValue).IsEmpty();
    }

    [Test]
    public async Task PaneViewModel_ClosingDetailsCancelsPendingValueRead()
    {
        var provider = new DelayedDetailsProvider();
        using var viewModel = new TaskHistoryPaneViewModel(provider);
        viewModel.SelectTask("storage", "source", "task");
        var reference = new TaskHistoryValueReference("repo", "sha", "task.json", "Description", false);
        var change = new TaskHistoryFieldChange("Description", "Description", "old…", "new…",
            TaskHistoryChangeType.Modified, false, reference, reference);
        var details = viewModel.ShowDetailsAsync(change);
        await provider.Started.Task;
        viewModel.ClearDetails();
        provider.Release.SetResult("late full text");
        await details;
        await Assert.That(viewModel.HasDetails).IsFalse();
        await Assert.That(viewModel.DetailOldValue).IsEmpty();
        await Assert.That(viewModel.DetailNewValue).IsEmpty();
    }

    private static GitTaskHistoryProvider.TaskFileSnapshot Snapshot(string json) =>
        new(JObject.Parse(json), "task.json", "task.json", IsWorkingTree: false);

    private sealed class DelayedProvider : ITaskHistoryProvider
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TaskHistoryPage> GetPageAsync(
            TaskHistoryRequest request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task;
            return new TaskHistoryPage(
                [new TaskHistoryEntry(
                    "1234567", "author", DateTimeOffset.UtcNow, "git", request.TaskId,
                    [new TaskHistoryFieldChange(
                        "Title", "Title", "old", "new", TaskHistoryChangeType.Modified, false)])],
                "next",
                string.Empty);
        }

        public Task<string> ReadValueAsync(
            TaskHistoryValueReference reference,
            CancellationToken cancellationToken) => Task.FromResult(string.Empty);
    }

    private sealed class DelayedDetailsProvider : ITaskHistoryProvider
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TaskHistoryPage> GetPageAsync(
            TaskHistoryRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new TaskHistoryPage([], null, string.Empty));

        public async Task<string> ReadValueAsync(
            TaskHistoryValueReference reference,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return await Release.Task;
        }
    }

    private sealed class RefreshProvider : ITaskHistoryProvider
    {
        private int _requests;
        public TaskCompletionSource SecondRequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSecond { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TaskHistoryPage> GetPageAsync(
            TaskHistoryRequest request, CancellationToken cancellationToken)
        {
            var requestNumber = Interlocked.Increment(ref _requests);
            if (requestNumber == 2)
            {
                SecondRequestStarted.SetResult();
                await ReleaseSecond.Task;
            }

            return new TaskHistoryPage(
                [new TaskHistoryEntry(
                    "sha", "author", DateTimeOffset.UtcNow, "git", requestNumber == 1 ? "first" : "second",
                    [new TaskHistoryFieldChange("Title", "Title", "old", "new", TaskHistoryChangeType.Modified, false)])],
                null,
                string.Empty);
        }

        public Task<string> ReadValueAsync(
            TaskHistoryValueReference reference,
            CancellationToken cancellationToken) => Task.FromResult(string.Empty);
    }

    private sealed class GitHistoryFixture : IDisposable
    {
        private readonly Repository _repository;
        private readonly Signature _signature =
            new("Unlimotion Test", "test@unlimotion.local", DateTimeOffset.UtcNow);

        public GitHistoryFixture()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "unlimotion-task-history",
                Guid.NewGuid().ToString("N"));
            StoragePath = Path.Combine(RootPath, "tasks");
            Directory.CreateDirectory(StoragePath);
            Repository.Init(RootPath);
            _repository = new Repository(RootPath);
        }

        public string RootPath { get; }
        public string StoragePath { get; }
        public string TaskId { get; } = Guid.NewGuid().ToString();
        public Repository Repository => _repository;

        public void WriteTask(
            string title,
            string status,
            string description,
            string fileName = "task.json")
        {
            var json = new JObject
            {
                ["Id"] = TaskId,
                ["Title"] = title,
                ["Description"] = description,
                ["Status"] = status,
                ["UpdatedDateTime"] = DateTimeOffset.UtcNow
            };
            File.WriteAllText(Path.Combine(StoragePath, fileName), json.ToString());
        }

        public void Commit(string message)
        {
            Commands.Stage(_repository, "*");
            _repository.Commit(message, _signature, _signature);
        }

        public void Dispose()
        {
            _repository.Dispose();
            foreach (var file in Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(RootPath, recursive: true);
        }
    }
}
