using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Unlimotion.Domain;
using Unlimotion.Services;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion.Test;

public sealed class GitStatusIndicatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "git-indicator-" + Guid.NewGuid().ToString("N"));
    private static readonly Signature Author = new("Test", "test@example.com", DateTimeOffset.Now);
    public GitStatusIndicatorTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }
    private string CreateRepository(bool commit = true)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Repository.Init(path);
        if (commit)
        {
            using var repo = new Repository(path);
            File.WriteAllText(Path.Combine(path, "task"), "base");
            Commands.Stage(repo, "task");
            repo.Commit("base", Author, Author);
        }
        return path;
    }
    private static GitObservationRequest Request(string path, long generation = 1) => new("A", path, generation);
    private static Task<GitWorktreeObservation> Read(string path) => new GitWorktreeObserver().ObserveAsync(Request(path), default);
    private static string Manifest(string path) => string.Join("\n", Directory.GetFiles(path, "*", SearchOption.AllDirectories)
        .OrderBy(file => file, StringComparer.Ordinal).Select(file => Path.GetRelativePath(path, file) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))));

    [Test]
    [Arguments("clean", 0)]
    [Arguments("modified", 1)]
    [Arguments("untracked", 1)]
    [Arguments("deleted", 1)]
    [Arguments("staged", 1)]
    [Arguments("MM", 1)]
    [Arguments("rename", 1)]
    [Arguments("ignored", 0)]
    public async Task Observer_CountsDistinctPathsAndPreservesEveryByte(string scenario, int expected)
    {
        var path = CreateRepository();
        using (var repo = new Repository(path))
        {
            if (scenario == "ignored")
            {
                File.WriteAllText(Path.Combine(path, ".gitignore"), "ignored\n");
                Commands.Stage(repo, ".gitignore");
                repo.Commit("ignore", Author, Author);
                File.WriteAllText(Path.Combine(path, "ignored"), "data");
            }
            if (scenario is "modified" or "staged" or "MM") File.WriteAllText(Path.Combine(path, "task"), "edit");
            if (scenario is "staged" or "MM") Commands.Stage(repo, "task");
            if (scenario == "MM") File.WriteAllText(Path.Combine(path, "task"), "edited again");
            if (scenario == "untracked") File.WriteAllText(Path.Combine(path, "new"), "new");
            if (scenario == "deleted") File.Delete(Path.Combine(path, "task"));
            if (scenario == "rename")
            {
                File.Move(Path.Combine(path, "task"), Path.Combine(path, "renamed"));
                Commands.Stage(repo, "*");
            }
        }
        var before = Manifest(path);
        var observation = await Read(path);
        await Assert.That(observation.Kind).IsEqualTo(GitObservationKind.Known);
        await Assert.That(observation.DirtyFileCount).IsEqualTo(expected);
        await Assert.That(observation.Ahead).IsNull();
        await Assert.That(Manifest(path)).IsEqualTo(before);
        var artifacts = Path.GetFullPath(Path.Combine("artifacts", "git-indicator", "preservation"));
        Directory.CreateDirectory(artifacts);
        await File.WriteAllTextAsync(Path.Combine(artifacts, scenario + "-before.txt"), before);
        await File.WriteAllTextAsync(Path.Combine(artifacts, scenario + "-after.txt"), Manifest(path));
    }

    [Test]
    public async Task Observer_UnbornDetachedMissingAndNotApplicableRemainHonest()
    {
        var path = CreateRepository(false);
        var unborn = await Read(path);
        await Assert.That(unborn.Kind).IsEqualTo(GitObservationKind.Known);
        await Assert.That(unborn.Ahead).IsNull();
        var detachedPath = CreateRepository();
        using (var repo = new Repository(detachedPath)) Commands.Checkout(repo, repo.Head.Tip);
        var detached = await Read(detachedPath);
        await Assert.That(detached.Behind).IsNull();
        var missing = await Read(Path.Combine(_root, "missing"));
        await Assert.That(missing.Kind).IsEqualTo(GitObservationKind.Unknown);
        await Assert.That(missing.DirtyFileCount).IsNull();
        var notApplicable = await new GitWorktreeObserver().ObserveAsync(new("server", "", 1, false), default);
        await Assert.That(notApplicable.Kind).IsEqualTo(GitObservationKind.NotApplicable);
    }

    [Test]
    public async Task Observer_ConflictAndCommitDivergenceAreSeparate()
    {
        var path = CreateRepository();
        using (var repo = new Repository(path))
        {
            var baseCommit = repo.Head.Tip;
            var incoming = repo.CreateBranch("incoming", baseCommit);
            repo.Branches.Update(repo.Head, branch => branch.TrackedBranch = incoming.CanonicalName);
            File.WriteAllText(Path.Combine(path, "task"), "ours");
            Commands.Stage(repo, "task");
            repo.Commit("ours", Author, Author);
            var ahead = await Read(path);
            await Assert.That(ahead.Ahead).IsEqualTo(1);
            await Assert.That(ahead.Behind).IsEqualTo(0);
            await Assert.That(ahead.DirtyFileCount).IsEqualTo(0);
            var ours = repo.Head;
            Commands.Checkout(repo, incoming);
            File.WriteAllText(Path.Combine(path, "task"), "theirs");
            Commands.Stage(repo, "task");
            repo.Commit("theirs", Author, Author);
            Commands.Checkout(repo, ours);
            var divergent = await Read(path);
            await Assert.That(divergent.Ahead).IsEqualTo(1);
            await Assert.That(divergent.Behind).IsEqualTo(1);
            repo.Merge(repo.Branches["incoming"], Author, new MergeOptions());
        }
        var before = Manifest(path);
        var conflict = await Read(path);
        await Assert.That(conflict.HasConflicts).IsTrue();
        await Assert.That(conflict.DirtyFileCount).IsEqualTo(1);
        await Assert.That(Manifest(path)).IsEqualTo(before);
    }

    [Test]
    public async Task Observer_LockTimeoutAndCancellationDoNotStealLock()
    {
        var path = CreateRepository();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = Task.Run(() => { lock (BackupViaGitService.GitLock) { entered.SetResult(); release.Task.GetAwaiter().GetResult(); } });
        await entered.Task;
        try
        {
            var timedOut = await Read(path);
            await Assert.That(timedOut.ErrorCode).IsEqualTo("LockTimeout");
            await Assert.That(timedOut.DirtyFileCount).IsNull();
            using var cancellation = new CancellationTokenSource(100);
            var cancelled = false;
            try { await new GitWorktreeObserver().ObserveAsync(Request(path), cancellation.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            await Assert.That(cancelled).IsTrue();
            await Assert.That(owner.IsCompleted).IsFalse();
        }
        finally { release.TrySetResult(); await owner; }
        await Assert.That((await Read(path)).Kind).IsEqualTo(GitObservationKind.Known);
    }

    [Test]
    public async Task Coordinator_SaveAndRemoveRemeasureRealRepository()
    {
        var path = CreateRepository();
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        using var shutdown = new CancellationTokenSource();
        listener.Start();
        var accepted = listener.AcceptTcpClientAsync(shutdown.Token).AsTask();
        var endpoint = (System.Net.IPEndPoint)listener.LocalEndpoint;
        var remoteUrl = $"http://127.0.0.1:{endpoint.Port}/fixture.git";
        try
        {
            using (var repo = new Repository(path))
            {
                repo.Network.Remotes.Add("origin", remoteUrl);
                repo.Refs.Add("refs/remotes/origin/master", repo.Head.Tip.Id);
                repo.Branches.Update(repo.Head, b => b.Remote = "origin", b => b.UpstreamBranch = "refs/heads/master");
            }
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TaskStorage:Path"] = path, ["Git:RemoteName"] = "origin", ["Git:RemoteUrl"] = remoteUrl,
                ["Git:Branch"] = "master", ["Git:PushRefSpec"] = "refs/heads/master"
            }).Build();
            using var configLifetime = config as IDisposable;
            var service = new BackupViaGitService(config, activeSourceIdProvider: () => "A");
            var commandEvents = 0;
            service.OperationDiagnostic += _ => Interlocked.Increment(ref commandEvents);
            var storage = new global::Unlimotion.Storage.FileTaskStorage(new() { Path = path });
            using var vm = new GitStatusViewModel(new GitWorktreeObserver(), diagnostics: service);
            vm.SetEligibility(true, true);
            var before = Manifest(path);
            vm.SetSource("A", path, true, storage);
            await Until(() => vm.State == GitIndicatorState.Idle);
            await Assert.That(Manifest(path)).IsEqualTo(before);
            await Assert.That(vm.LastKnown!.Ahead).IsEqualTo(0);
            await storage.Save(new TaskItem { Id = "saved", Title = "after save" });
            await Until(() => vm.HasDirty && !vm.IsStale);
            await Assert.That(vm.LastKnown!.DirtyFileCount).IsEqualTo(1);
            await storage.Remove("saved");
            await Until(() => vm.State == GitIndicatorState.Idle);
            await Assert.That(commandEvents).IsEqualTo(0);
            await Assert.That(accepted.IsCompleted).IsFalse();
            var evidence = Path.GetFullPath(Path.Combine("artifacts", "git-indicator", "preservation"));
            Directory.CreateDirectory(evidence);
            await File.WriteAllTextAsync(Path.Combine(evidence, "command-network-spy.txt"),
                "Real coordinator observer + subscribed backup service: Started/Terminal events=0\n" +
                "Configured loopback HTTP remote with local tracking ref: accepted connections=0\n" +
                "Initial observation preserves all file bytes/index/refs; save/remove remeasure Dirty/Idle\n" +
                "Spy bound: configured backup service and configured remote; static observer call review complements byte manifests\n");
        }
        finally
        {
            shutdown.Cancel();
            listener.Stop();
            try { using var client = await accepted; }
            catch (OperationCanceledException) { }
            catch (System.Net.Sockets.SocketException) { }
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(4)]
    public async Task Coordinator_ReadErrorsNeverPresentHistoricalCleanAsIdle(int dirty)
    {
        var observer = new ControlledObserver();
        var now = DateTimeOffset.UtcNow;
        using var vm = new GitStatusViewModel(observer, now: () => now);
        vm.SetEligibility(true, true);
        vm.SetSource("A", _root, true);
        await observer.Reply(dirty);
        await Until(() => vm.LastKnown != null && !vm.IsStale);
        now = now.AddSeconds(31);
        await Assert.That(vm.IsStale).IsTrue();
        await Assert.That(vm.State).IsEqualTo(dirty > 0 ? GitIndicatorState.Dirty : GitIndicatorState.Unknown);
        for (var i = 0; i < 2; i++)
        {
            vm.RequestRefresh();
            await observer.Reply(null);
            await Until(() => observer.Completed == i + 2 && vm.IsStale);
            await Assert.That(vm.State).IsEqualTo(dirty > 0 ? GitIndicatorState.Dirty : GitIndicatorState.Unknown);
            await Assert.That(vm.LastKnown!.DirtyFileCount).IsEqualTo(dirty);
        }
    }

    [Test]
    public async Task Coordinator_SyncTimeoutErrorRetryAndConfirmedConflictHaveCorrectPriority()
    {
        var observer = new ControlledObserver();
        using var vm = new GitStatusViewModel(observer);
        vm.SetEligibility(true, true);
        vm.SetSource("A", _root, true);
        await observer.Reply(4);
        await Until(() => vm.LastKnown != null && !vm.IsStale);
        var id = Guid.NewGuid();
        vm.ApplyDiagnostic(new("A", _root, id, true));
        vm.RequestRefresh();
        await observer.Reply(null);
        await Until(() => observer.Completed == 2);
        await Assert.That(vm.State).IsEqualTo(GitIndicatorState.Syncing);
        await Assert.That(vm.HasDirty).IsTrue();
        await Assert.That(vm.IsStale).IsTrue();
        vm.ApplyDiagnostic(new("A", _root, id, false, BackupOperationResult.Failed));
        await Assert.That(vm.State).IsEqualTo(GitIndicatorState.Attention);
        var retry = Guid.NewGuid();
        vm.ApplyDiagnostic(new("A", _root, retry, true));
        await Assert.That(vm.State).IsEqualTo(GitIndicatorState.Syncing);
        await observer.Reply(4, conflicts: true);
        await Until(() => vm.LastKnown?.HasConflicts == true);
        await Assert.That(vm.State).IsEqualTo(GitIndicatorState.Attention);
        vm.ApplyDiagnostic(new("A", _root, retry, false, BackupOperationResult.Success));
        await Assert.That(vm.State).IsEqualTo(GitIndicatorState.Attention);
        await observer.Reply(0);
        await Until(() => vm.State == GitIndicatorState.Idle);
    }

    [Test]
    public async Task Coordinator_SourcePathGenerationAndDisposeRejectLateReplies()
    {
        var observer = new ControlledObserver();
        using var vm = new GitStatusViewModel(observer);
        vm.SetEligibility(true, true);
        vm.SetSource("A", _root, true);
        var readA = await observer.Next();
        var pathB = Path.Combine(_root, "B");
        vm.SetSource("B", pathB, true);
        readA.Completion.SetResult(new(readA.Request, DateTimeOffset.UtcNow, GitObservationKind.Known, 99, true));
        await Task.Delay(50);
        await Assert.That(vm.LastKnown).IsNull();
        await observer.Reply(2);
        await Until(() => vm.LastKnown?.DirtyFileCount == 2);
        vm.ApplyDiagnostic(new("A", _root, Guid.NewGuid(), true));
        await Assert.That(vm.IsBusy).IsFalse();
        vm.SetSource("B", _root, true);
        await Assert.That(vm.LastKnown).IsNull();
        var late = await observer.Next();
        vm.Dispose();
        late.Completion.SetResult(new(late.Request, DateTimeOffset.UtcNow, GitObservationKind.Known, 99));
        await Task.Delay(50);
        await Assert.That(vm.LastKnown).IsNull();
        vm.SetSource("server", "", false);
    }

    [Test]
    public async Task Coordinator_DebouncesBurstAndDisposesSubscriptions()
    {
        var observer = new ControlledObserver();
        var storage = new MutationHint();
        var vm = new GitStatusViewModel(observer);
        vm.SetEligibility(true, true);
        vm.SetSource("A", _root, true, storage);
        for (var i = 0; i < 20; i++) storage.Raise();
        await observer.Reply(1);
        await observer.Reply(2);
        await Until(() => vm.LastKnown?.DirtyFileCount == 2);
        await Task.Delay(350);
        await Assert.That(observer.Completed).IsEqualTo(2);
        vm.Dispose();
        storage.Raise();
        await Task.Delay(350);
        await Assert.That(observer.Completed).IsEqualTo(2);
    }

    [Test]
    [Arguments("missing-repository")]
    [Arguments("missing-remote")]
    [Arguments("missing-branch")]
    [Arguments("swallowed-push")]
    [Arguments("nested-connect-pull")]
    [Arguments("nested-clone-connect")]
    [Arguments("exception")]
    public async Task Diagnostics_RealServiceOutcomesAreStickyAndExceptionIsolated(string scenario)
    {
        var path = scenario == "missing-repository" ? Path.Combine(_root, "missing") : CreateRepository();
        var remote = Path.Combine(_root, "remote.git");
        Repository.Init(remote, true);
        using (var repo = new Repository(path == Path.Combine(_root, "missing") ? remote : path))
        {
            if (scenario != "missing-remote" && scenario != "missing-repository")
            {
                repo.Network.Remotes.Add("origin", scenario == "swallowed-push" ? new Uri(Path.Combine(_root, "absent.git")).AbsoluteUri : new Uri(remote).AbsoluteUri);
                if (scenario is "nested-connect-pull" or "nested-clone-connect")
                    repo.Network.Push(repo.Network.Remotes["origin"], repo.Head.CanonicalName + ":" + repo.Head.CanonicalName, new PushOptions());
            }
        }
        if (scenario == "nested-connect-pull")
        {
            var clonePath = Path.Combine(_root, "remote-writer");
            Repository.Clone(new Uri(remote).AbsoluteUri, clonePath);
            using (var writer = new Repository(clonePath))
            {
                File.WriteAllText(Path.Combine(clonePath, "task"), "theirs");
                Commands.Stage(writer, "task");
                writer.Commit("theirs", Author, Author);
                writer.Network.Push(writer.Head, new PushOptions());
            }
            using var local = new Repository(path);
            File.WriteAllText(Path.Combine(path, "task"), "ours");
            Commands.Stage(local, "task");
            local.Commit("ours", Author, Author);
        }
        var branch = scenario is "missing-branch" or "nested-connect-pull" ? "refs/heads/missing" : "refs/heads/master";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TaskStorage:Path"] = path, ["Git:RemoteName"] = "origin", ["Git:RemoteUrl"] = new Uri(remote).AbsoluteUri,
            ["Git:PushRefSpec"] = branch, ["Git:Branch"] = "master",
            ["Git:CommitterName"] = "Test", ["Git:CommitterEmail"] = "test@example.com"
        }).Build();
        using var configLifetime = config as IDisposable;
        var service = new BackupViaGitService(config, activeSourceIdProvider: () => "A");
        var events = new List<BackupOperationDiagnostic>();
        service.OperationDiagnostic += _ => throw new InvalidOperationException("subscriber error");
        service.OperationDiagnostic += events.Add;
        var threw = false;
        try
        {
            if (scenario == "nested-connect-pull") service.ConnectRepository(false);
            else if (scenario == "nested-clone-connect") service.CloneOrUpdateRepo();
            else if (scenario == "exception") service.ResolveConflict("", BackupConflictResolution.UseCurrent);
            else service.Push("test");
        }
        catch (Exception) { threw = true; }
        await Assert.That(threw).IsEqualTo(scenario == "exception");
        await Assert.That(events.Count).IsEqualTo(2);
        await Assert.That(events[0].Started).IsTrue();
        await Assert.That(events[1].Started).IsFalse();
        await Assert.That(events[0].OperationId).IsEqualTo(events[1].OperationId);
        var expected = scenario == "missing-repository" ? BackupOperationResult.SkippedUnknown :
            scenario == "nested-clone-connect" ? BackupOperationResult.Success : BackupOperationResult.Failed;
        await Assert.That(events[1].Result).IsEqualTo(expected);
        await Assert.That(events[1].SourceId).IsEqualTo("A");
    }

    [Test]
    public async Task Coordinator_DisableReenableRejectsLateReadAndRetainsOperationLifecycle()
    {
        var observer = new ControlledObserver();
        var storage = new MutationHint();
        using var vm = new GitStatusViewModel(observer);
        vm.SetSource("A", _root, true, storage);
        await Assert.That(vm.IsIndicatorVisible).IsFalse();
        vm.SetEligibility(true, true);
        var old = await observer.Next();
        var operation = Guid.NewGuid();
        vm.ApplyDiagnostic(new("A", _root, operation, true));
        vm.SetEligibility(false, true);
        for (var i = 0; i < 20; i++) { storage.Raise(); vm.RequestRefresh(); }
        await Task.Delay(350);
        await Assert.That(observer.Started).IsEqualTo(1);
        await Assert.That(vm.IsIndicatorVisible).IsFalse();
        await Assert.That(vm.IsBusy).IsTrue();
        await Assert.That(vm.SettingsInformation).IsEqualTo(Unlimotion.ViewModel.Localization.Localization.Get("GitIndicatorDisabled"));
        vm.SetEligibility(true, true);
        var fresh = await observer.Next();
        old.Completion.SetResult(new(old.Request, DateTimeOffset.UtcNow, GitObservationKind.Known, 99));
        await Until(() => observer.Completed == 1);
        await Assert.That(vm.LastKnown).IsNull();
        await Assert.That(vm.State).IsEqualTo(GitIndicatorState.Syncing);
        fresh.Completion.SetResult(new(fresh.Request, DateTimeOffset.UtcNow, GitObservationKind.Known, 2));
        await Until(() => vm.LastKnown?.DirtyFileCount == 2);
        vm.SetEligibility(false, true);
        vm.ApplyDiagnostic(new("A", _root, operation, false, BackupOperationResult.Success));
        await Assert.That(vm.IsBusy).IsFalse();
        await Task.Delay(350);
        await Assert.That(observer.Started).IsEqualTo(2);
        vm.SetEligibility(true, true);
        await observer.Reply(0);
        await Until(() => vm.State == GitIndicatorState.Idle);
    }

    [Test]
    public async Task Coordinator_TargetChangeClearsDivergenceRejectsReadAndKeepsBusyUntilTerminal()
    {
        var observer = new ControlledObserver();
        using var vm = new GitStatusViewModel(observer);
        vm.SetEligibility(true, true);
        vm.SetSource("A", _root, true, remoteName: "origin", branchName: "main");
        var initial = await observer.Next();
        initial.Completion.SetResult(new(initial.Request, DateTimeOffset.UtcNow, GitObservationKind.Known, 4, false, 3, 1));
        await Until(() => vm.LastKnown?.Ahead == 3);
        var operation = Guid.NewGuid();
        vm.ApplyDiagnostic(new("A", _root, operation, true));
        vm.RequestRefresh();
        var old = await observer.Next();
        vm.UpdateTargetContext("upstream", "other");
        await Assert.That(vm.LastKnown!.Ahead).IsNull();
        await Assert.That(vm.LastKnown.Behind).IsNull();
        await Assert.That(vm.LastKnown.DirtyFileCount).IsEqualTo(4);
        await Assert.That(vm.IsBusy).IsTrue();
        var fresh = await observer.Next();
        await Assert.That(fresh.Request.RemoteName).IsEqualTo("upstream");
        await Assert.That(fresh.Request.BranchName).IsEqualTo("other");
        old.Completion.SetResult(new(old.Request, DateTimeOffset.UtcNow, GitObservationKind.Known, 99, true, 99, 99));
        await Until(() => observer.Completed == 2);
        await Assert.That(vm.LastKnown.Ahead).IsNull();
        fresh.Completion.SetResult(new(fresh.Request, DateTimeOffset.UtcNow, GitObservationKind.Known, 4, false, 0, 2));
        await Until(() => vm.LastKnown?.Behind == 2);
        await Assert.That(vm.State).IsEqualTo(GitIndicatorState.Syncing);
        vm.ApplyDiagnostic(new("A", _root, operation, false, BackupOperationResult.Success));
        await Assert.That(vm.IsBusy).IsFalse();
    }

    [Test]
    public async Task Coordinator_QueuedNewRuntimeDiagnosticsSurviveBindingAndRejectRecycledSource()
    {
        var deliveries = new Queue<Action>();
        var diagnostics = new DiagnosticEvents();
        using var vm = new GitStatusViewModel(dispatch: action => deliveries.Enqueue(action), diagnostics: diagnostics);
        var tokenA = new object();
        var tokenB = new object();
        vm.SetSource("A", _root, true, sourceVersion: tokenA);
        deliveries.Enqueue(() => vm.SetSource("B", _root, true, sourceVersion: tokenB));
        var operation = Guid.NewGuid();
        diagnostics.Send(new("B", _root, operation, true, SourceVersion: tokenB));
        while (deliveries.TryDequeue(out var action)) action();
        await Assert.That(vm.IsBusy).IsTrue();
        vm.SetEligibility(true, true);
        await Assert.That(vm.State).IsEqualTo(GitIndicatorState.Syncing);
        diagnostics.Send(new("B", _root, operation, false, BackupOperationResult.Success, SourceVersion: tokenB));
        while (deliveries.TryDequeue(out var action)) action();
        await Assert.That(vm.IsBusy).IsFalse();
        vm.SetSource("B", _root, true, sourceVersion: new object());
        diagnostics.Send(new("B", _root, Guid.NewGuid(), true, SourceVersion: tokenB));
        while (deliveries.TryDequeue(out var action)) action();
        await Assert.That(vm.IsBusy).IsFalse();
    }

    private sealed class DiagnosticEvents : IBackupOperationDiagnostics
    {
        public event Action<BackupOperationDiagnostic>? OperationDiagnostic;
        public void Send(BackupOperationDiagnostic diagnostic) => OperationDiagnostic?.Invoke(diagnostic);
    }

    public static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Git indicator did not reach expected state.");
            await Task.Delay(20);
        }
    }
    private sealed class MutationHint : IStorageMutationObservation
    {
        public event EventHandler? StorageBytesMayHaveChanged;
        public void Raise() => StorageBytesMayHaveChanged?.Invoke(this, EventArgs.Empty);
    }
    private sealed class ControlledObserver : IGitWorktreeObserver
    {
        private readonly System.Threading.Channels.Channel<PendingRead> _reads = System.Threading.Channels.Channel.CreateUnbounded<PendingRead>();
        public int Completed;
        public int Started;
        public async Task<GitWorktreeObservation> ObserveAsync(GitObservationRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Started);
            var pending = new PendingRead(request, new(TaskCreationOptions.RunContinuationsAsynchronously));
            await _reads.Writer.WriteAsync(pending);
            // Intentionally ignore cancellation, to exercise a late native/backend reply.
            var value = await pending.Completion.Task;
            Interlocked.Increment(ref Completed);
            return value;
        }
        public async Task<PendingRead> Next() => await _reads.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8));
        public async Task Reply(int? dirty, bool conflicts = false)
        {
            var pending = await Next();
            pending.Completion.SetResult(new(pending.Request, DateTimeOffset.UtcNow,
                dirty == null ? GitObservationKind.Unknown : GitObservationKind.Known, dirty, conflicts,
                ErrorCode: dirty == null ? "ReadFailed" : null));
        }
    }
    private sealed record PendingRead(GitObservationRequest Request, TaskCompletionSource<GitWorktreeObservation> Completion);
}
