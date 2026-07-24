using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Unlimotion.Domain;
using Unlimotion.Services;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;

namespace Unlimotion.Test;

public sealed class TaskSpaceTransactionTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(),
        $"TaskSpaceTransactionTests_{Guid.NewGuid():N}");
    private readonly List<IDisposable> _disposables = [];

    public TaskSpaceTransactionTests()
    {
        Directory.CreateDirectory(_rootPath);
    }

    [Test]
    public async Task OperationRunner_SerializesTopLevelOperations()
    {
        using var runner = new TaskSpaceOperationRunner();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = false;

        var first = runner.RunExclusiveAsync("first", "a", async _ =>
        {
            firstEntered.SetResult();
            await releaseFirst.Task;
        });
        await firstEntered.Task;
        var second = runner.RunExclusiveAsync("second", "b", _ =>
        {
            secondEntered = true;
            return Task.CompletedTask;
        });

        await Task.Delay(50);
        await Assert.That(secondEntered).IsFalse();
        releaseFirst.SetResult();
        await Task.WhenAll(first, second);
        await Assert.That(secondEntered).IsTrue();
    }

    [Test]
    public async Task OperationRunner_RejectsNestedLeaseInsteadOfDeadlocking()
    {
        using var runner = new TaskSpaceOperationRunner();

        await Assert.That(async () =>
                await runner.RunExclusiveAsync(
                    "outer",
                    "a",
                    _ => runner.RunExclusiveAsync("inner", "a", __ => Task.CompletedTask)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SettingsQueue_CoalescesLatestDraftForCapturedSource()
    {
        using var runner = new TaskSpaceOperationRunner();
        var writer = new RecordingTaskSpaceConfiguration(runner);
        var queue = new TaskSpaceSettingsPersistenceQueue(writer, runner);
        var blockerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBlocker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = runner.RunExclusiveAsync("block", "other", async _ =>
        {
            blockerEntered.SetResult();
            await releaseBlocker.Task;
        });
        await blockerEntered.Task;

        queue.Enqueue(CreateDraft("a", "one"));
        queue.Enqueue(CreateDraft("a", "two"));
        queue.Enqueue(CreateDraft("a", "latest"));
        releaseBlocker.SetResult();
        await blocker;
        await queue.DrainAsync();

        await Assert.That(writer.Persisted.Last().Git.Branch).IsEqualTo("latest");
        await Assert.That(writer.Persisted.All(draft => draft.SourceId == "a")).IsTrue();
        await Assert.That(writer.Persisted.Count).IsLessThanOrEqualTo(2);
    }

    [Test]
    public async Task SettingsQueue_RunsPostPersistCallbackAfterCanonicalWriteInsideSameLease()
    {
        using var runner = new TaskSpaceOperationRunner();
        var writer = new RecordingTaskSpaceConfiguration(runner);
        var callbackObservedPersistedDraft = false;
        var callbackObservedHeldLease = false;
        var queue = new TaskSpaceSettingsPersistenceQueue(
            writer,
            runner,
            draft =>
            {
                callbackObservedPersistedDraft =
                    writer.Persisted.Any(persisted =>
                        string.Equals(persisted.SourceId, draft.SourceId, StringComparison.Ordinal) &&
                        string.Equals(persisted.Git.Branch, draft.Git.Branch, StringComparison.Ordinal));
                callbackObservedHeldLease = runner.IsBusy;
                return Task.CompletedTask;
            });

        queue.Enqueue(CreateDraft("source-a", "persisted-branch"));
        await queue.DrainAsync();

        await Assert.That(callbackObservedPersistedDraft).IsTrue();
        await Assert.That(callbackObservedHeldLease).IsTrue();
        await Assert.That(runner.IsBusy).IsFalse();
    }

    [Test]
    public async Task SettingsQueue_RestartsWorkerAfterEveryDrainedBatch()
    {
        using var runner = new TaskSpaceOperationRunner();
        var writer = new RecordingTaskSpaceConfiguration(runner);
        var queue = new TaskSpaceSettingsPersistenceQueue(writer, runner);

        for (var batch = 0; batch < 25; batch++)
        {
            queue.Enqueue(CreateDraft("source-a", $"branch-{batch}"));
            await queue.DrainAsync();
        }

        await Assert.That(writer.Persisted).Count().IsEqualTo(25);
        await Assert.That(writer.Persisted.Last().Git.Branch).IsEqualTo("branch-24");
        await Assert.That(runner.IsBusy).IsFalse();
    }

    [Test]
    public async Task BackupService_UsesCapturedSourceSnapshotAndPersistsNormalizationToThatSource()
    {
        var configuration = CreateConfiguration(out _);
        var sourceAPath = Path.Combine(_rootPath, "backup-source-a");
        var legacyOtherPath = Path.Combine(_rootPath, "legacy-other-source");
        Directory.CreateDirectory(sourceAPath);
        Directory.CreateDirectory(legacyOtherPath);
        Repository.Init(sourceAPath);
        using (var repository = new Repository(sourceAPath))
        {
            repository.Network.Remotes.Add("origin", "https://github.com/example/source-a.git");
        }

        configuration.GetSection("TaskStorage")
            .GetSection(nameof(TaskStorageSettings.Path))
            .Set(legacyOtherPath);
        configuration.GetSection("Git")
            .GetSection(nameof(GitSettings.RemoteName))
            .Set("legacy-other");
        configuration.GetSection("Git")
            .GetSection(nameof(GitSettings.RemoteUrl))
            .Set("https://github.com/example/other.git");

        using var runner = new TaskSpaceOperationRunner();
        var sourceConfiguration = new RecordingTaskSpaceConfiguration(runner);
        sourceConfiguration.Drafts["source-a"] = new TaskSpaceSettingsDraft
        {
            SourceId = "source-a",
            Storage = new TaskStorageSettings { Path = sourceAPath },
            Git = new GitSettings
            {
                RemoteName = "origin",
                RemoteUrl = "https://github.com/example/source-a.git",
                Branch = "main",
                PushRefSpec = "refs/heads/main"
            }
        };
        sourceConfiguration.Drafts["source-b"] = CreateDraft("source-b", "source-b");
        var activeSourceId = "source-a";
        var service = new BackupViaGitService(
            configuration,
            operationRunner: runner,
            activeSourceIdProvider: () => activeSourceId,
            activeTaskSpaceConfiguration: sourceConfiguration);

        var result = service.SwitchRemoteConnectionType("origin", BackupAuthMode.Ssh);
        activeSourceId = "source-b";

        await Assert.That(result.RemoteUrl).IsEqualTo("git@github.com:example/source-a.git");
        await Assert.That(sourceConfiguration.Persisted).HasCount().EqualTo(2);
        await Assert.That(sourceConfiguration.Persisted.All(draft => draft.SourceId == "source-a")).IsTrue();
        await Assert.That(sourceConfiguration.Drafts["source-a"].Git.RemoteName).IsEqualTo("origin-ssh");
        await Assert.That(sourceConfiguration.Drafts["source-a"].Git.RemoteUrl)
            .IsEqualTo("git@github.com:example/source-a.git");
        await Assert.That(sourceConfiguration.Drafts["source-b"].Git.Branch).IsEqualTo("source-b");
        await Assert.That(configuration.GetSection("Git")
                .GetSection(nameof(GitSettings.RemoteName))
                .Get<string>())
            .IsEqualTo("legacy-other");
        await Assert.That(runner.IsBusy).IsFalse();
    }

    [Test]
    public async Task Coordinator_WaitsForExistingOperationLeaseBeforePreparingCandidate()
    {
        var fixture = CreateCoordinatorFixture();
        var created = fixture.Manager.AddConfiguredLocalSource(
            "Work",
            Path.Combine(_rootPath, "held-lease"));
        var leaseEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var heldLease = fixture.Runner.RunExclusiveAsync("ManualSync", "default", async _ =>
        {
            leaseEntered.SetResult();
            await releaseLease.Task;
        });
        await leaseEntered.Task;

        var switching = fixture.Coordinator.SwitchAsync(created.Id);
        await Task.Delay(50);

        await Assert.That(fixture.Manager.ActiveSource?.Descriptor.Id).IsEqualTo("default");
        await Assert.That(fixture.Builder.Builds.Any(build => build.Descriptor.Id == created.Id)).IsFalse();

        releaseLease.SetResult();
        await Task.WhenAll(heldLease, switching);
        await Assert.That(fixture.Manager.ActiveSource?.Descriptor.Id).IsEqualTo(created.Id);
    }

    [Test]
    public async Task Coordinator_FailedSettingsDrainLeavesOldRuntimeAndRestoresScheduler()
    {
        var fixture = CreateCoordinatorFixture(failSettingsDrain: true);
        var previous = fixture.Manager.ActiveSource;
        var created = fixture.Manager.AddConfiguredLocalSource(
            "Work",
            Path.Combine(_rootPath, "failed-drain"));

        await Assert.That(async () => await fixture.Coordinator.SwitchAsync(created.Id))
            .Throws<InvalidOperationException>();

        await Assert.That(fixture.Manager.ActiveSource).IsSameReferenceAs(previous);
        await Assert.That(fixture.Builder.Builds.Any(build => build.Descriptor.Id == created.Id)).IsFalse();
        await Assert.That(fixture.PauseSchedulerCalls).IsEqualTo(1);
        await Assert.That(fixture.RestoreSchedulerCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Coordinator_PublishesOnlyAfterCandidateWasInitializedAndBound()
    {
        var fixture = CreateCoordinatorFixture();
        var created = fixture.Manager.AddConfiguredLocalSource("Work", Path.Combine(_rootPath, "work"));

        var runtime = await fixture.Coordinator.SwitchAsync(created.Id);

        var candidate = fixture.Builder.Builds.Single(build => build.Descriptor.Id == created.Id);
        await Assert.That(candidate.Storage.ConnectCalls).IsEqualTo(1);
        await Assert.That(fixture.BoundSourceIds).IsEquivalentTo([created.Id]);
        await Assert.That(fixture.Manager.ActiveSource).IsSameReferenceAs(runtime);
        await Assert.That(fixture.Manager.ActiveSource?.Descriptor.Id).IsEqualTo(created.Id);
        await Assert.That(fixture.PauseSchedulerCalls).IsEqualTo(2);
        await Assert.That(fixture.RestoreSchedulerCalls).IsEqualTo(1);
        await Assert.That(fixture.Builder.Builds.Single(build => build.Descriptor.Id == "default").Storage.DisconnectCalls)
            .IsEqualTo(1);
    }

    [Test]
    public async Task Coordinator_AddPublishesCatalogOnlyAfterCandidateWasInitializedAndBound()
    {
        var fixture = CreateCoordinatorFixture();

        var runtime = await fixture.Coordinator.AddLocalAsync(
            "Work",
            Path.Combine(_rootPath, "atomic-add"));

        await Assert.That(fixture.Manager.ConfiguredSources.Select(source => source.Id))
            .Contains(runtime.Descriptor.Id);
        await Assert.That(fixture.Manager.ActiveSource).IsSameReferenceAs(runtime);
        await Assert.That(fixture.BoundSourceIds).IsEquivalentTo([runtime.Descriptor.Id]);
        await Assert.That(
                fixture.Builder.Builds.Single(build => build.Descriptor.Id == runtime.Descriptor.Id).Storage.ConnectCalls)
            .IsEqualTo(1);
    }

    [Test]
    public async Task Coordinator_RemoveActiveSpaceSwitchesToFallbackBeforeCatalogRemoval()
    {
        var fixture = CreateCoordinatorFixture();
        var created = fixture.Manager.AddConfiguredLocalSource(
            "Work",
            Path.Combine(_rootPath, "active-removal"));
        await fixture.Coordinator.SwitchAsync(created.Id);

        await fixture.Coordinator.RemoveAsync(created.Id);

        await Assert.That(fixture.Manager.ActiveSource?.Descriptor.Id)
            .IsEqualTo(TaskSourceDescriptor.DefaultSourceId);
        await Assert.That(fixture.Manager.ConfiguredSources.Select(source => source.Id))
            .DoesNotContain(created.Id);
        await Assert.That(fixture.Manager.ConfiguredSources).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Coordinator_FailedAddLeavesCandidateUnlistedAndPreviousRuntimeActive()
    {
        var fixture = CreateCoordinatorFixture(failTargetBind: true);
        var previous = fixture.Manager.ActiveSource;

        await Assert.That(async () => await fixture.Coordinator.AddLocalAsync(
                "Work",
                Path.Combine(_rootPath, "failed-add")))
            .Throws<InvalidOperationException>();

        var candidate = fixture.Builder.Builds.Last(build => build.Descriptor.Id != "default");
        await Assert.That(fixture.Manager.ConfiguredSources.Select(source => source.Id))
            .IsEquivalentTo(["default"]);
        await Assert.That(fixture.Manager.ActiveSource).IsSameReferenceAs(previous);
        await Assert.That(candidate.Storage.DisconnectCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Coordinator_AddDirectoryCreationFailureLeavesPreviousRuntimeAndSchedulerActive()
    {
        var fixture = CreateCoordinatorFixture();
        var previous = fixture.Manager.ActiveSource;
        var blockingFile = Path.Combine(_rootPath, "not-a-directory");
        File.WriteAllText(blockingFile, "blocks directory creation");

        await Assert.That(async () => await fixture.Coordinator.AddLocalAsync(
                "Work",
                Path.Combine(blockingFile, "Tasks")))
            .Throws<IOException>();

        await Assert.That(fixture.Manager.ConfiguredSources.Select(source => source.Id))
            .IsEquivalentTo(["default"]);
        await Assert.That(fixture.Manager.ActiveSource).IsSameReferenceAs(previous);
        await Assert.That(fixture.RestoreSchedulerCalls > 0).IsTrue();
    }

    [Test]
    public async Task Coordinator_AddStorageBuildFailureRetainsCreatedDirectoryAndPreviousRuntime()
    {
        var fixture = CreateCoordinatorFixture();
        var previous = fixture.Manager.ActiveSource;
        var candidatePath = Path.Combine(_rootPath, "storage-build-failed-add");
        fixture.Builder.NextBuildException = new UnauthorizedAccessException(
            "Injected storage build failure.");

        await Assert.That(async () => await fixture.Coordinator.AddLocalAsync(
                "Work",
                candidatePath))
            .Throws<UnauthorizedAccessException>();

        await Assert.That(fixture.Manager.ConfiguredSources.Select(source => source.Id))
            .IsEquivalentTo(["default"]);
        await Assert.That(fixture.Manager.ActiveSource).IsSameReferenceAs(previous);
        await Assert.That(Directory.Exists(candidatePath)).IsTrue();
        await Assert.That(fixture.RestoreSchedulerCalls > 0).IsTrue();
    }

    [Test]
    public async Task Coordinator_AddConnectFailureLeavesCandidateUnlistedAndPreviousRuntimeActive()
    {
        var fixture = CreateCoordinatorFixture();
        var previous = fixture.Manager.ActiveSource;
        fixture.Builder.NextConnectResult = false;

        await Assert.That(async () => await fixture.Coordinator.AddLocalAsync(
                "Work",
                Path.Combine(_rootPath, "connect-failed-add")))
            .Throws<InvalidOperationException>();

        var candidate = fixture.Builder.Builds.Last(build => build.Descriptor.Id != "default");
        await Assert.That(fixture.Manager.ConfiguredSources.Select(source => source.Id))
            .IsEquivalentTo(["default"]);
        await Assert.That(fixture.Manager.ActiveSource).IsSameReferenceAs(previous);
        await Assert.That(candidate.Storage.DisconnectCalls).IsEqualTo(1);
        await Assert.That(fixture.RestoreSchedulerCalls > 0).IsTrue();
    }

    [Test]
    public async Task Coordinator_AddInitialLoadFailureLeavesCandidateUnlistedAndPreviousRuntimeActive()
    {
        var fixture = CreateCoordinatorFixture();
        var previous = fixture.Manager.ActiveSource;
        fixture.Builder.NextInitialLoadException = new InvalidOperationException(
            "Injected initial load failure.");

        await Assert.That(async () => await fixture.Coordinator.AddLocalAsync(
                "Work",
                Path.Combine(_rootPath, "initial-load-failed-add")))
            .Throws<InvalidOperationException>();

        var candidate = fixture.Builder.Builds.Last(build => build.Descriptor.Id != "default");
        await Assert.That(fixture.Manager.ConfiguredSources.Select(source => source.Id))
            .IsEquivalentTo(["default"]);
        await Assert.That(fixture.Manager.ActiveSource).IsSameReferenceAs(previous);
        await Assert.That(candidate.Storage.DisconnectCalls).IsEqualTo(1);
        await Assert.That(fixture.RestoreSchedulerCalls > 0).IsTrue();
    }

    [Test]
    public async Task Coordinator_AddPersistenceFailuresKeepDirectoryAndRestorePreviousRuntime()
    {
        var failurePaths = new[]
        {
            "TaskSourceMutationJournal:State",
            $"{TaskSourcesSettings.SectionName}:SourcesCount"
        };

        foreach (var failurePath in failurePaths)
        {
            var innerConfiguration = CreateConfiguration(out _);
            var faultProvider = new FaultInjectingConfigurationProvider();
            var configuration = CreateFaultInjectingConfiguration(innerConfiguration, faultProvider);
            var fixture = CreateCoordinatorFixture(configuration: configuration);
            var previous = fixture.Manager.ActiveSource;
            var candidatePath = Path.Combine(
                _rootPath,
                $"persist-failed-add-{Guid.NewGuid():N}");
            Directory.CreateDirectory(candidatePath);
            var markerPath = Path.Combine(candidatePath, "user-marker.txt");
            File.WriteAllText(markerPath, "must remain");
            faultProvider.FailNextWrite(failurePath);

            await Assert.That(async () => await fixture.Coordinator.AddLocalAsync(
                    "Work",
                    candidatePath))
                .Throws<InvalidOperationException>();

            await Assert.That(fixture.Manager.ConfiguredSources.Select(source => source.Id))
                .IsEquivalentTo(["default"]);
            await Assert.That(fixture.Manager.ActiveSource).IsSameReferenceAs(previous);
            await Assert.That(File.Exists(markerPath)).IsTrue();
            await Assert.That(fixture.RestoreSchedulerCalls > 0).IsTrue();
            await Assert.That(configuration.GetSection("TaskSourceMutationJournal")
                    .GetSection("State").Get<string>() ?? string.Empty)
                .IsEmpty();
        }
    }

    [Test]
    public async Task Coordinator_ConnectFalseKeepsPreviousRuntimePublished()
    {
        var fixture = CreateCoordinatorFixture();
        var previous = fixture.Manager.ActiveSource;
        var created = fixture.Manager.AddConfiguredLocalSource("Work", Path.Combine(_rootPath, "work-false"));
        fixture.Builder.ConnectResultBySourceId[created.Id] = false;

        await Assert.That(async () => await fixture.Coordinator.SwitchAsync(created.Id))
            .Throws<InvalidOperationException>();

        var candidate = fixture.Builder.Builds.Single(build => build.Descriptor.Id == created.Id);
        await Assert.That(fixture.Manager.ActiveSource).IsSameReferenceAs(previous);
        await Assert.That(candidate.Storage.DisconnectCalls).IsEqualTo(1);
        await Assert.That(fixture.BoundSourceIds).IsEmpty();
    }

    [Test]
    public async Task Coordinator_BindFailureAbortsCandidateAndRebindsPreviousRuntime()
    {
        var fixture = CreateCoordinatorFixture(failTargetBind: true);
        var previous = fixture.Manager.ActiveSource;
        var created = fixture.Manager.AddConfiguredLocalSource("Work", Path.Combine(_rootPath, "work-bind"));
        fixture.TargetSourceId = created.Id;

        await Assert.That(async () => await fixture.Coordinator.SwitchAsync(created.Id))
            .Throws<InvalidOperationException>();

        var candidate = fixture.Builder.Builds.Single(build => build.Descriptor.Id == created.Id);
        await Assert.That(fixture.Manager.ActiveSource).IsSameReferenceAs(previous);
        await Assert.That(candidate.Storage.DisconnectCalls).IsEqualTo(1);
        await Assert.That(fixture.BoundSourceIds.Last()).IsEqualTo(previous?.Descriptor.Id);
    }

    [Test]
    public async Task Coordinator_InitFailureDisposesCandidateWithoutPublishingIt()
    {
        var fixture = CreateCoordinatorFixture();
        var previous = fixture.Manager.ActiveSource;
        var created = fixture.Manager.AddConfiguredLocalSource("Work", Path.Combine(_rootPath, "work-init"));
        fixture.Builder.InitialLoadExceptionBySourceId[created.Id] =
            new InvalidOperationException("Init failed.");

        await Assert.That(async () => await fixture.Coordinator.SwitchAsync(created.Id))
            .Throws<InvalidOperationException>();

        var candidate = fixture.Builder.Builds.Single(build => build.Descriptor.Id == created.Id);
        await Assert.That(fixture.Manager.ActiveSource).IsSameReferenceAs(previous);
        await Assert.That(candidate.Storage.DisconnectCalls).IsEqualTo(1);
        await Assert.That(fixture.BoundSourceIds).IsEmpty();
    }

    [Test]
    public async Task Coordinator_PreviousDisconnectFailureRollsBackPublishedCandidate()
    {
        var fixture = CreateCoordinatorFixture();
        var previous = fixture.Manager.ActiveSource;
        fixture.Builder.Builds.Single(build => build.Descriptor.Id == "default").Storage.DisconnectException =
            new InvalidOperationException("Disconnect failed.");
        var created = fixture.Manager.AddConfiguredLocalSource("Work", Path.Combine(_rootPath, "work-disconnect"));

        await Assert.That(async () => await fixture.Coordinator.SwitchAsync(created.Id))
            .Throws<InvalidOperationException>();

        var candidate = fixture.Builder.Builds.Single(build => build.Descriptor.Id == created.Id);
        await Assert.That(fixture.Manager.ActiveSource).IsSameReferenceAs(previous);
        await Assert.That(candidate.Storage.DisconnectCalls).IsEqualTo(1);
        await Assert.That(fixture.BoundSourceIds).IsEquivalentTo([created.Id, "default"]);
    }

    [Test]
    public async Task Coordinator_DoubleFailureClearsSurfaceAndKeepsSchedulerStopped()
    {
        var fixture = CreateCoordinatorFixture(failTargetBind: true, failPreviousBind: true);
        var created = fixture.Manager.AddConfiguredLocalSource("Work", Path.Combine(_rootPath, "work-recovery"));
        fixture.TargetSourceId = created.Id;

        await Assert.That(async () => await fixture.Coordinator.SwitchAsync(created.Id))
            .Throws<TaskSpaceRecoveryException>();

        await Assert.That(fixture.SurfaceCleared).IsTrue();
        await Assert.That(fixture.PauseSchedulerCalls).IsEqualTo(2);
        await Assert.That(fixture.RestoreSchedulerCalls).IsEqualTo(0);
    }

    [Test]
    public async Task BindInitializedStorage_ClearsOldSelectionAndShowsOnlyCandidateWithSameTaskId()
    {
        var configuration = CreateConfiguration(out _);
        var firstRaw = new ConfigurableStorage();
        firstRaw.Items.Add(new TaskItem { Id = "same-id", Title = "Space A" });
        var secondRaw = new ConfigurableStorage();
        secondRaw.Items.Add(new TaskItem { Id = "same-id", Title = "Space B" });
        var first = new UnifiedTaskStorage(
            new TaskTreeManager(firstRaw),
            new TaskItemViewModelContext { SourceId = "a" });
        var second = new UnifiedTaskStorage(
            new TaskTreeManager(secondRaw),
            new TaskItemViewModelContext { SourceId = "b" });
        await first.Init();
        await second.Init();
        using var vm = new MainWindowViewModel(
            appNameService: null,
            new NotificationManagerWrapper(null),
            configuration);
        await vm.BindInitializedStorage(first);
        vm.CurrentTaskItem = first.Tasks.Items.Single();
        vm.DetailsAreOpen = true;
        vm.Search.SearchText = "old search";

        await vm.BindInitializedStorage(second);

        await Assert.That(vm.CurrentTaskItem).IsNull();
        await Assert.That(vm.DetailsAreOpen).IsFalse();
        await Assert.That(vm.Search.SearchText).IsEmpty();
        await Assert.That(vm.taskRepository).IsSameReferenceAs(second);
        await Assert.That(vm.taskRepository?.Tasks.Items.Single().Title).IsEqualTo("Space B");
    }

    [Test]
    public async Task PreparedProjection_IsRepairedFromCanonicalProfileOnStartup()
    {
        var configuration = CreateConfiguration(out _);
        var settings = TaskSourceSettingsAdapter.LoadOrCreate(configuration, Path.Combine(_rootPath, "default"));
        settings.SyncSettings.Single().Git.Branch = "canonical";
        TaskSourceSettingsAdapter.Save(configuration, settings);
        TaskSourceSettingsAdapter.SyncLegacy(configuration, settings, settings.Sources.Single());

        configuration.GetSection("TaskSourceLegacyProjection")
            .GetSection(nameof(TaskSourceLegacyProjectionState.ProjectionState))
            .Set("Prepared");
        configuration.GetSection("Git").GetSection(nameof(GitSettings.Branch)).Set("partial-write");

        var recovered = TaskSourceSettingsAdapter.LoadOrCreate(configuration, Path.Combine(_rootPath, "default"));

        await Assert.That(configuration.Get<GitSettings>("Git")?.Branch).IsEqualTo("canonical");
        await Assert.That(recovered.LegacyProjection.ProjectionState).IsEqualTo("Committed");
    }

    [Test]
    public async Task CommittedProjection_DivergenceImportsOnlyCommittedActiveProfile()
    {
        var configuration = CreateConfiguration(out _);
        var settings = TaskSourceSettingsAdapter.LoadOrCreate(configuration, Path.Combine(_rootPath, "default"));
        settings.Sources.Add(new TaskSourceDescriptor
        {
            Id = "inactive",
            DisplayName = "Inactive",
            Kind = TaskSourceKind.File,
            Path = Path.Combine(_rootPath, "inactive")
        });
        settings.SyncSettings.Add(new TaskSourceSyncSettings
        {
            SourceId = "inactive",
            Git = new GitSettings { Branch = "inactive-branch" }
        });
        TaskSourceSettingsAdapter.Save(configuration, settings);
        TaskSourceSettingsAdapter.SyncLegacy(configuration, settings, settings.Sources[0]);
        configuration.GetSection("Git").GetSection(nameof(GitSettings.Branch)).Set("legacy-edit");

        var recovered = TaskSourceSettingsAdapter.LoadOrCreate(configuration, Path.Combine(_rootPath, "default"));

        await Assert.That(recovered.SyncSettings.Single(profile => profile.SourceId == "default").Git.Branch)
            .IsEqualTo("legacy-edit");
        await Assert.That(recovered.SyncSettings.Single(profile => profile.SourceId == "inactive").Git.Branch)
            .IsEqualTo("inactive-branch");
    }

    [Test]
    public async Task PersistedCatalog_RoundTripsCompleteProfilesAfterProviderRestart()
    {
        var path = Path.Combine(_rootPath, $"{Guid.NewGuid():N}-restart.json");
        File.WriteAllText(path, "{}");
        var serverGit = new GitSettings
        {
            BackupEnabled = true,
            ShowStatusToasts = false,
            RemoteUrl = "ssh://git.example/work.git",
            Branch = "work",
            UserName = "git-user",
            Password = "git-token",
            SshPrivateKeyPath = "keys/private",
            SshPublicKeyPath = "keys/public",
            SshKeyStoragePath = "keys",
            PullIntervalSeconds = 17,
            PushIntervalSeconds = 29,
            RemoteName = "upstream",
            PushRefSpec = "refs/heads/work",
            CommitterName = "Task Spaces",
            CommitterEmail = "spaces@example.test"
        };
        var serverSettings = new TaskSourceServerSettings
        {
            SourceId = "server",
            Login = "server-user",
            Password = "server-password",
            AccessToken = "server-access",
            RefreshToken = "server-refresh",
            ExpireTime = DateTimeOffset.Parse("2030-01-02T03:04:05+00:00"),
            UserId = "server-user-id"
        };

        var first = WritableJsonConfigurationFabric.Create(path, reloadOnChange: false);
        try
        {
            var settings = TaskSourceSettingsAdapter.LoadOrCreate(
                first,
                Path.Combine(_rootPath, "restart-default"));
            settings.Sources.Add(new TaskSourceDescriptor
            {
                Id = "server",
                DisplayName = "Server workspace",
                Kind = TaskSourceKind.Server,
                Url = "https://tasks.example.test",
                IsEnabled = true
            });
            settings.ServerSettings.Add(serverSettings);
            settings.SyncSettings.Add(new TaskSourceSyncSettings
            {
                SourceId = "server",
                Git = serverGit
            });
            settings.ActiveSourceId = "server";
            TaskSourceSettingsAdapter.Save(first, settings);
            TaskSourceSettingsAdapter.SyncLegacy(
                first,
                settings,
                settings.Sources.Single(source => source.Id == "server"));
        }
        finally
        {
            (first as IDisposable)?.Dispose();
        }

        var reopened = WritableJsonConfigurationFabric.Create(path, reloadOnChange: false);
        try
        {
            var recovered = TaskSourceSettingsAdapter.LoadOrCreate(
                reopened,
                Path.Combine(_rootPath, "unused-default"));
            var recoveredSource = recovered.Sources.Single(source => source.Id == "server");
            var recoveredServer = recovered.ServerSettings.Single(server => server.SourceId == "server");
            var recoveredGit = recovered.SyncSettings.Single(profile => profile.SourceId == "server").Git;

            await Assert.That(recovered.ActiveSourceId).IsEqualTo("server");
            await Assert.That(recoveredSource.DisplayName).IsEqualTo("Server workspace");
            await Assert.That(recoveredSource.Url).IsEqualTo("https://tasks.example.test");
            await Assert.That(JsonSerializer.Serialize(recoveredServer))
                .IsEqualTo(JsonSerializer.Serialize(serverSettings));
            await Assert.That(JsonSerializer.Serialize(recoveredGit))
                .IsEqualTo(JsonSerializer.Serialize(serverGit));
        }
        finally
        {
            (reopened as IDisposable)?.Dispose();
        }
    }

    [Test]
    public async Task RemovalJournal_SanitizesRemovedSecretsFromRawConfiguration()
    {
        var configuration = CreateConfiguration(out var path);
        var before = TaskSourceSettingsAdapter.LoadOrCreate(configuration, Path.Combine(_rootPath, "default"));
        before.Sources.Add(new TaskSourceDescriptor
        {
            Id = "server",
            DisplayName = "Server",
            Kind = TaskSourceKind.Server,
            Url = "https://tasks.example"
        });
        before.ServerSettings.Add(new TaskSourceServerSettings
        {
            SourceId = "server",
            Login = "removed-login-sentinel",
            Password = "removed-password-sentinel",
            AccessToken = "removed-access-sentinel",
            RefreshToken = "removed-refresh-sentinel"
        });
        before.SyncSettings.Add(new TaskSourceSyncSettings
        {
            SourceId = "server",
            Git = new GitSettings { Password = "removed-git-sentinel" }
        });
        TaskSourceSettingsAdapter.Save(configuration, before);
        var after = JsonSerializer.Deserialize<TaskSourcesSettings>(JsonSerializer.Serialize(before))!;
        after.Sources.RemoveAll(source => source.Id == "server");
        after.ServerSettings.RemoveAll(server => server.SourceId == "server");
        after.SyncSettings.RemoveAll(profile => profile.SourceId == "server");

        TaskSourceSettingsAdapter.ApplyCatalogMutation(configuration, before, after, "Remove");
        var raw = await File.ReadAllTextAsync(path);

        await Assert.That(raw).DoesNotContain("removed-password-sentinel");
        await Assert.That(raw).DoesNotContain("removed-access-sentinel");
        await Assert.That(raw).DoesNotContain("removed-refresh-sentinel");
        await Assert.That(raw).DoesNotContain("removed-git-sentinel");
        await Assert.That(configuration.GetSection("TaskSourceMutationJournal").GetSection("State").Get<string>() ?? "")
            .IsEmpty();
    }

    [Test]
    public async Task PreparedRemovalJournal_RollsBackBeforeStartupActivation()
    {
        var configuration = CreateConfiguration(out _);
        var before = TaskSourceSettingsAdapter.LoadOrCreate(configuration, Path.Combine(_rootPath, "default"));
        var after = JsonSerializer.Deserialize<TaskSourcesSettings>(JsonSerializer.Serialize(before))!;
        after.Sources.Add(new TaskSourceDescriptor
        {
            Id = "partial",
            DisplayName = "Partial",
            Kind = TaskSourceKind.File,
            Path = Path.Combine(_rootPath, "partial")
        });
        after.SyncSettings.Add(new TaskSourceSyncSettings { SourceId = "partial" });
        TaskSourceSettingsAdapter.Save(configuration, after);
        var journal = configuration.GetSection("TaskSourceMutationJournal");
        journal.GetSection("MutationId").Set("fault");
        journal.GetSection("Operation").Set("Add");
        journal.GetSection("BeforeSnapshot").Set(JsonSerializer.Serialize(before));
        journal.GetSection("AfterSnapshot").Set(JsonSerializer.Serialize(after));
        journal.GetSection("State").Set("Prepared");

        var recovered = TaskSourceSettingsAdapter.LoadOrCreate(configuration, Path.Combine(_rootPath, "default"));

        await Assert.That(recovered.Sources.Select(source => source.Id)).DoesNotContain("partial");
        await Assert.That(journal.GetSection("State").Get<string>() ?? "").IsEmpty();
    }

    [Test]
    public async Task ProjectionWriteFault_RollsBackCatalogRuntimeAndCommittedProjection()
    {
        var innerConfiguration = CreateConfiguration(out _);
        var faultProvider = new FaultInjectingConfigurationProvider();
        var configuration = CreateFaultInjectingConfiguration(innerConfiguration, faultProvider);
        var fixture = CreateCoordinatorFixture(configuration: configuration);
        var previous = fixture.Manager.ActiveSource;
        var created = fixture.Manager.AddConfiguredLocalSource(
            "Work",
            Path.Combine(_rootPath, "projection-fault"));
        faultProvider.FailNextWrite(
            $"TaskSourceLegacyProjection:{nameof(TaskSourceLegacyProjectionState.ProjectionState)}");

        await Assert.That(async () => await fixture.Coordinator.SwitchAsync(created.Id))
            .Throws<InvalidOperationException>();

        await Assert.That(fixture.Manager.ActiveSource).IsSameReferenceAs(previous);
        await Assert.That(configuration.GetSection(TaskSourcesSettings.SectionName)
                .GetSection(nameof(TaskSourcesSettings.ActiveSourceId)).Get<string>())
            .IsEqualTo("default");
        await Assert.That(configuration.GetSection("TaskSourceLegacyProjection")
                .GetSection(nameof(TaskSourceLegacyProjectionState.ProjectionState)).Get<string>())
            .IsEqualTo("Committed");
        await Assert.That(configuration.GetSection("TaskSourceLegacyProjection")
                .GetSection(nameof(TaskSourceLegacyProjectionState.CommittedSourceId)).Get<string>())
            .IsEqualTo("default");
    }

    [Test]
    public async Task CatalogWriteFault_RollsBackAllPhysicalSlotsAndClearsJournal()
    {
        var innerConfiguration = CreateConfiguration(out var path);
        var faultProvider = new FaultInjectingConfigurationProvider();
        var configuration = CreateFaultInjectingConfiguration(innerConfiguration, faultProvider);
        var before = TaskSourceSettingsAdapter.LoadOrCreate(
            configuration,
            Path.Combine(_rootPath, "default"));
        var after = JsonSerializer.Deserialize<TaskSourcesSettings>(JsonSerializer.Serialize(before))!;
        after.Sources.Add(new TaskSourceDescriptor
        {
            Id = "faulted-add",
            DisplayName = "faulted-display-sentinel",
            Kind = TaskSourceKind.File,
            Path = Path.Combine(_rootPath, "faulted-path-sentinel")
        });
        after.SyncSettings.Add(new TaskSourceSyncSettings
        {
            SourceId = "faulted-add",
            Git = new GitSettings { Password = "faulted-secret-sentinel" }
        });
        faultProvider.FailNextWrite($"{TaskSourcesSettings.SectionName}:SourcesCount");

        await Assert.That(() => TaskSourceSettingsAdapter.ApplyCatalogMutation(
                configuration,
                before,
                after,
                "Add"))
            .Throws<InvalidOperationException>();

        var recovered = TaskSourceSettingsAdapter.LoadOrCreate(
            configuration,
            Path.Combine(_rootPath, "default"));
        var raw = await File.ReadAllTextAsync(path);
        await Assert.That(recovered.Sources.Select(source => source.Id)).IsEquivalentTo(["default"]);
        await Assert.That(raw).DoesNotContain("faulted-display-sentinel");
        await Assert.That(raw).DoesNotContain("faulted-path-sentinel");
        await Assert.That(raw).DoesNotContain("faulted-secret-sentinel");
        await Assert.That(configuration.GetSection("TaskSourceMutationJournal")
                .GetSection("State").Get<string>() ?? string.Empty)
            .IsEmpty();
    }

    [Test]
    public async Task MutationJournalPreparationFault_RestoresCatalogAndSanitizesSnapshots()
    {
        var innerConfiguration = CreateConfiguration(out var path);
        var faultProvider = new FaultInjectingConfigurationProvider();
        var configuration = CreateFaultInjectingConfiguration(innerConfiguration, faultProvider);
        var before = TaskSourceSettingsAdapter.LoadOrCreate(
            configuration,
            Path.Combine(_rootPath, "default"));
        var after = JsonSerializer.Deserialize<TaskSourcesSettings>(JsonSerializer.Serialize(before))!;
        after.Sources.Add(new TaskSourceDescriptor
        {
            Id = "journal-fault",
            DisplayName = "journal-display-sentinel",
            Kind = TaskSourceKind.File,
            Path = Path.Combine(_rootPath, "journal-path-sentinel")
        });
        after.SyncSettings.Add(new TaskSourceSyncSettings
        {
            SourceId = "journal-fault",
            Git = new GitSettings { Password = "journal-secret-sentinel" }
        });
        faultProvider.FailNextWrite("TaskSourceMutationJournal:AfterSnapshot");

        await Assert.That(() => TaskSourceSettingsAdapter.ApplyCatalogMutation(
                configuration,
                before,
                after,
                "Add"))
            .Throws<InvalidOperationException>();

        var recovered = TaskSourceSettingsAdapter.LoadOrCreate(
            configuration,
            Path.Combine(_rootPath, "default"));
        var raw = await File.ReadAllTextAsync(path);
        await Assert.That(recovered.Sources.Select(source => source.Id)).IsEquivalentTo(["default"]);
        await Assert.That(raw).DoesNotContain("journal-display-sentinel");
        await Assert.That(raw).DoesNotContain("journal-path-sentinel");
        await Assert.That(raw).DoesNotContain("journal-secret-sentinel");
        await Assert.That(configuration.GetSection("TaskSourceMutationJournal")
                .GetSection("State").Get<string>() ?? string.Empty)
            .IsEmpty();
    }

    [Test]
    public async Task CatalogMutation_EveryPersistedWriteFault_ReopensAsCompleteBeforeState()
    {
        var baselineFaultProvider = new FaultInjectingConfigurationProvider();
        var baselineConfiguration = CreateFaultInjectingConfiguration(
            CreateConfiguration(out _),
            baselineFaultProvider);
        var baselineBefore = CreateRemovalBeforeState(baselineConfiguration);
        var baselineAfter = CreateRemovalAfterState(baselineBefore);
        baselineFaultProvider.BeginRecording();
        TaskSourceSettingsAdapter.ApplyCatalogMutation(
            baselineConfiguration,
            baselineBefore,
            baselineAfter,
            "Remove");
        var writePaths = baselineFaultProvider.RecordedWrites.ToArray();
        await Assert.That(writePaths.Length).IsGreaterThan(0);

        for (var writeNumber = 1; writeNumber <= writePaths.Length; writeNumber++)
        {
            var faultProvider = new FaultInjectingConfigurationProvider();
            var configuration = CreateFaultInjectingConfiguration(
                CreateConfiguration(out _),
                faultProvider);
            var before = CreateRemovalBeforeState(configuration);
            var after = CreateRemovalAfterState(before);
            faultProvider.FailWriteNumber(writeNumber);
            var failed = false;
            try
            {
                TaskSourceSettingsAdapter.ApplyCatalogMutation(
                    configuration,
                    before,
                    after,
                    "Remove");
            }
            catch (InvalidOperationException)
            {
                failed = true;
            }
            catch (AggregateException)
            {
                failed = true;
            }

            if (!failed)
            {
                throw new InvalidOperationException(
                    $"Catalog mutation did not surface injected write #{writeNumber} ({writePaths[writeNumber - 1]}).");
            }

            var recovered = TaskSourceSettingsAdapter.LoadOrCreate(
                configuration,
                Path.Combine(_rootPath, $"matrix-default-{writeNumber}"));
            if (!recovered.Sources.Any(source => source.Id == "server") ||
                recovered.ServerSettings.Single(server => server.SourceId == "server").Password !=
                "matrix-password-sentinel" ||
                recovered.SyncSettings.Single(profile => profile.SourceId == "server").Git.Password !=
                "matrix-git-sentinel")
            {
                throw new InvalidOperationException(
                    $"Catalog mutation write #{writeNumber} ({writePaths[writeNumber - 1]}) did not recover the complete before state.");
            }

            if (!string.IsNullOrEmpty(configuration.GetSection("TaskSourceMutationJournal")
                    .GetSection("State")
                    .Get<string>()))
            {
                throw new InvalidOperationException(
                    $"Catalog mutation write #{writeNumber} ({writePaths[writeNumber - 1]}) left a prepared journal.");
            }
        }

        await Assert.That(writePaths.Distinct(StringComparer.Ordinal).Count()).IsGreaterThan(10);
    }

    [Test]
    public async Task ActivationProjection_EveryPersistedWriteFault_RestoresPreviousRuntimeAndProjection()
    {
        var baselineFaultProvider = new FaultInjectingConfigurationProvider();
        var baselineConfiguration = CreateFaultInjectingConfiguration(
            CreateConfiguration(out _),
            baselineFaultProvider);
        var baselineFixture = CreateCoordinatorFixture(configuration: baselineConfiguration);
        var baselineTarget = baselineFixture.Manager.AddConfiguredLocalSource(
            "Matrix target",
            Path.Combine(_rootPath, "projection-matrix-baseline"));
        baselineFaultProvider.BeginRecording();
        await baselineFixture.Coordinator.SwitchAsync(baselineTarget.Id);
        var writePaths = baselineFaultProvider.RecordedWrites.ToArray();
        await Assert.That(writePaths.Length).IsGreaterThan(0);

        for (var writeNumber = 1; writeNumber <= writePaths.Length; writeNumber++)
        {
            var faultProvider = new FaultInjectingConfigurationProvider();
            var configuration = CreateFaultInjectingConfiguration(
                CreateConfiguration(out _),
                faultProvider);
            var fixture = CreateCoordinatorFixture(configuration: configuration);
            var previousRuntime = fixture.Manager.ActiveSource;
            var target = fixture.Manager.AddConfiguredLocalSource(
                "Matrix target",
                Path.Combine(_rootPath, $"projection-matrix-{writeNumber}"));
            faultProvider.FailWriteNumber(writeNumber);
            var failed = false;
            try
            {
                await fixture.Coordinator.SwitchAsync(target.Id);
            }
            catch (Exception)
            {
                failed = true;
            }

            if (!failed)
            {
                throw new InvalidOperationException(
                    $"Activation did not surface injected write #{writeNumber} ({writePaths[writeNumber - 1]}).");
            }

            if (!ReferenceEquals(fixture.Manager.ActiveSource, previousRuntime) ||
                fixture.Manager.ActiveSource?.Descriptor.Id != TaskSourceDescriptor.DefaultSourceId)
            {
                throw new InvalidOperationException(
                    $"Activation write #{writeNumber} ({writePaths[writeNumber - 1]}) did not restore the previous runtime.");
            }

            var recovered = TaskSourceSettingsAdapter.LoadOrCreate(
                configuration,
                Path.Combine(_rootPath, $"projection-default-{writeNumber}"));
            if (recovered.ActiveSourceId != TaskSourceDescriptor.DefaultSourceId ||
                recovered.LegacyProjection.ProjectionState != "Committed" ||
                recovered.LegacyProjection.CommittedSourceId != TaskSourceDescriptor.DefaultSourceId)
            {
                throw new InvalidOperationException(
                    $"Activation write #{writeNumber} ({writePaths[writeNumber - 1]}) did not restore the committed default projection.");
            }
        }

        await Assert.That(writePaths.Distinct(StringComparer.Ordinal).Count()).IsGreaterThan(10);
    }

    [Test]
    public async Task FirstMigration_EveryPersistedWriteFault_RestartsWithCompleteLegacyProfile()
    {
        var baseline = CreateLegacyMigrationConfiguration("baseline");
        baseline.FaultProvider.BeginRecording();
        TaskSourceSettingsAdapter.LoadOrCreate(
            baseline.Configuration,
            Path.Combine(_rootPath, "migration-default-baseline"));
        var writePaths = baseline.FaultProvider.RecordedWrites.ToArray();
        await Assert.That(writePaths.Length).IsGreaterThan(0);

        for (var writeNumber = 1; writeNumber <= writePaths.Length; writeNumber++)
        {
            var setup = CreateLegacyMigrationConfiguration(writeNumber.ToString());
            setup.FaultProvider.FailWriteNumber(writeNumber);
            var failed = false;
            try
            {
                TaskSourceSettingsAdapter.LoadOrCreate(
                    setup.Configuration,
                    Path.Combine(_rootPath, $"migration-default-{writeNumber}"));
            }
            catch (Exception)
            {
                failed = true;
            }

            if (!failed)
            {
                throw new InvalidOperationException(
                    $"First migration did not surface injected write #{writeNumber} ({writePaths[writeNumber - 1]}).");
            }

            TaskSourcesSettings recovered;
            try
            {
                recovered = TaskSourceSettingsAdapter.LoadOrCreate(
                    setup.Configuration,
                    Path.Combine(_rootPath, $"migration-default-{writeNumber}"));
            }
            catch (Exception recoveryError)
            {
                throw new InvalidOperationException(
                    $"First migration write #{writeNumber} ({writePaths[writeNumber - 1]}) could not restart.",
                    recoveryError);
            }
            var source = recovered.Sources.Single();
            var server = recovered.ServerSettings.Single();
            var git = recovered.SyncSettings.Single().Git;
            if (recovered.ActiveSourceId != TaskSourceDescriptor.DefaultSourceId ||
                source.Kind != TaskSourceKind.Server ||
                source.Url != setup.Storage.URL ||
                server.Login != setup.Storage.Login ||
                server.Password != setup.Storage.Password ||
                server.AccessToken != "migration-access-sentinel" ||
                server.RefreshToken != "migration-refresh-sentinel" ||
                server.UserId != "migration-user-sentinel" ||
                JsonSerializer.Serialize(git) != JsonSerializer.Serialize(setup.Git) ||
                recovered.LegacyProjection.ProjectionState != "Committed")
            {
                throw new InvalidOperationException(
                    $"First migration write #{writeNumber} ({writePaths[writeNumber - 1]}) did not recover the complete legacy profile.");
            }
        }

        await Assert.That(writePaths.Distinct(StringComparer.Ordinal).Count()).IsGreaterThan(10);
    }

    [Test]
    public async Task CorruptCatalog_WithDuplicateSourceId_FailsBeforeActivation()
    {
        var configuration = CreateConfiguration(out _);
        var settings = TaskSourceSettingsAdapter.LoadOrCreate(
            configuration,
            Path.Combine(_rootPath, "default"));
        settings.Sources.Add(new TaskSourceDescriptor
        {
            Id = "default",
            DisplayName = "Duplicate",
            Kind = TaskSourceKind.File,
            Path = Path.Combine(_rootPath, "duplicate")
        });
        TaskSourceSettingsAdapter.Save(configuration, settings);

        await Assert.That(() => TaskSourceSettingsAdapter.LoadOrCreate(
                configuration,
                Path.Combine(_rootPath, "default")))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CorruptCatalog_WithOrphanCredentials_FailsBeforeActivation()
    {
        var configuration = CreateConfiguration(out _);
        var settings = TaskSourceSettingsAdapter.LoadOrCreate(
            configuration,
            Path.Combine(_rootPath, "default"));
        settings.ServerSettings.Add(new TaskSourceServerSettings
        {
            SourceId = "missing",
            Password = "orphan-secret"
        });
        TaskSourceSettingsAdapter.Save(configuration, settings);

        await Assert.That(() => TaskSourceSettingsAdapter.LoadOrCreate(
                configuration,
                Path.Combine(_rootPath, "default")))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CatalogRead_IncludesPhysicalSlotWrittenBeforeLaggingCount()
    {
        var configuration = CreateConfiguration(out _);
        var settings = TaskSourceSettingsAdapter.LoadOrCreate(
            configuration,
            Path.Combine(_rootPath, "default"));
        settings.Sources.Add(new TaskSourceDescriptor
        {
            Id = "new-space",
            DisplayName = "New space",
            Kind = TaskSourceKind.File,
            Path = Path.Combine(_rootPath, "new-space")
        });
        settings.SyncSettings.Add(new TaskSourceSyncSettings
        {
            SourceId = "new-space",
            Git = new GitSettings { Branch = "new-space" }
        });
        TaskSourceSettingsAdapter.Save(configuration, settings);
        configuration
            .GetSection(TaskSourcesSettings.SectionName)
            .GetSection("SourcesCount")
            .Set(1);

        var recovered = TaskSourceSettingsAdapter.LoadOrCreate(
            configuration,
            Path.Combine(_rootPath, "default"));

        await Assert.That(recovered.Sources.Select(source => source.Id))
            .IsEquivalentTo(["default", "new-space"]);
        await Assert.That(recovered.SyncSettings.Select(sync => sync.SourceId))
            .IsEquivalentTo(["default", "new-space"]);
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }

        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private CoordinatorFixture CreateCoordinatorFixture(
        bool failTargetBind = false,
        bool failPreviousBind = false,
        bool failSettingsDrain = false,
        IConfiguration? configuration = null)
    {
        configuration ??= CreateConfiguration(out _);
        var builder = new ConfigurableTaskStorageBuilder();
        var runner = new TaskSpaceOperationRunner();
        _disposables.Add(runner);
        var manager = new TaskSourceManager(
            configuration,
            builder,
            defaultStoragePathProvider: () => Path.Combine(_rootPath, "default"),
            operationRunner: runner);
        manager.ActivateConfiguredSource();
        var sourceConfiguration = new ActiveTaskSpaceConfiguration(configuration, manager, runner);
        ITaskSpaceSettingsPersistenceQueue queue = failSettingsDrain
            ? new FailingSettingsQueue()
            : new TaskSpaceSettingsPersistenceQueue(sourceConfiguration, runner);
        var fixture = new CoordinatorFixture(manager, builder, runner);
        fixture.Coordinator = new TaskSpaceCoordinator(
            manager,
            runner,
            queue,
            runtime =>
            {
                fixture.BoundSourceIds.Add(runtime.Descriptor.Id);
                var isTarget = fixture.TargetSourceId == null
                    ? !string.Equals(runtime.Descriptor.Id, "default", StringComparison.Ordinal)
                    : string.Equals(runtime.Descriptor.Id, fixture.TargetSourceId, StringComparison.Ordinal);
                if (failTargetBind && isTarget)
                {
                    throw new InvalidOperationException("Bind failed.");
                }

                if (failPreviousBind &&
                    string.Equals(runtime.Descriptor.Id, manager.ActiveSource?.Descriptor.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Restore bind failed.");
                }

                return Task.CompletedTask;
            },
            () =>
            {
                fixture.SurfaceCleared = true;
                return Task.CompletedTask;
            },
            () =>
            {
                fixture.PauseSchedulerCalls++;
                return Task.CompletedTask;
            },
            _ =>
            {
                fixture.RestoreSchedulerCalls++;
                return Task.CompletedTask;
            });
        return fixture;
    }

    private IConfigurationRoot CreateConfiguration(out string path)
    {
        path = Path.Combine(_rootPath, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{}");
        var configuration = WritableJsonConfigurationFabric.Create(path, reloadOnChange: false);
        if (configuration is IDisposable disposable)
        {
            _disposables.Add(disposable);
        }

        return configuration;
    }

    private TaskSourcesSettings CreateRemovalBeforeState(IConfiguration configuration)
    {
        var before = TaskSourceSettingsAdapter.LoadOrCreate(
            configuration,
            Path.Combine(_rootPath, "matrix-default"));
        before.Sources.Add(new TaskSourceDescriptor
        {
            Id = "server",
            DisplayName = "Matrix server",
            Kind = TaskSourceKind.Server,
            Url = "https://matrix.example"
        });
        before.ServerSettings.Add(new TaskSourceServerSettings
        {
            SourceId = "server",
            Login = "matrix-login-sentinel",
            Password = "matrix-password-sentinel",
            AccessToken = "matrix-access-sentinel",
            RefreshToken = "matrix-refresh-sentinel"
        });
        before.SyncSettings.Add(new TaskSourceSyncSettings
        {
            SourceId = "server",
            Git = new GitSettings
            {
                RemoteUrl = "https://matrix.example/repository.git",
                Password = "matrix-git-sentinel"
            }
        });
        TaskSourceSettingsAdapter.Save(configuration, before);
        return before;
    }

    private static TaskSourcesSettings CreateRemovalAfterState(TaskSourcesSettings before)
    {
        var after = JsonSerializer.Deserialize<TaskSourcesSettings>(JsonSerializer.Serialize(before))!;
        after.Sources.RemoveAll(source => source.Id == "server");
        after.ServerSettings.RemoveAll(server => server.SourceId == "server");
        after.SyncSettings.RemoveAll(profile => profile.SourceId == "server");
        return after;
    }

    private LegacyMigrationSetup CreateLegacyMigrationConfiguration(string suffix)
    {
        var inner = CreateConfiguration(out _);
        var storage = new TaskStorageSettings
        {
            IsServerMode = true,
            URL = $"https://migration-{suffix}.example",
            Login = "migration-login-sentinel",
            Password = "migration-password-sentinel",
            IsFuzzySearch = true
        };
        var git = new GitSettings
        {
            BackupEnabled = true,
            ShowStatusToasts = false,
            RemoteUrl = $"https://migration-{suffix}.example/repository.git",
            Branch = "migration-branch",
            UserName = "migration-git-user",
            Password = "migration-git-password-sentinel",
            SshPrivateKeyPath = "migration-private-key",
            SshPublicKeyPath = "migration-public-key",
            SshKeyStoragePath = "migration-key-storage",
            PullIntervalSeconds = 17,
            PushIntervalSeconds = 29,
            RemoteName = "migration-origin",
            PushRefSpec = "refs/heads/migration-branch",
            CommitterName = "Migration User",
            CommitterEmail = "migration@example.test"
        };
        inner.Set("TaskStorage", storage);
        inner.Set("Git", git);
        inner.Set("ClientSettings", new ClientSettings
        {
            AccessToken = "migration-access-sentinel",
            RefreshToken = "migration-refresh-sentinel",
            UserId = "migration-user-sentinel",
            Login = storage.Login,
            ExpireTime = DateTimeOffset.UtcNow.AddHours(1)
        });
        var faultProvider = new FaultInjectingConfigurationProvider();
        return new LegacyMigrationSetup(
            CreateFaultInjectingConfiguration(inner, faultProvider),
            faultProvider,
            storage,
            git);
    }

    private sealed record LegacyMigrationSetup(
        IConfigurationRoot Configuration,
        FaultInjectingConfigurationProvider FaultProvider,
        TaskStorageSettings Storage,
        GitSettings Git);

    private static IConfigurationRoot CreateFaultInjectingConfiguration(
        IConfigurationRoot inner,
        FaultInjectingConfigurationProvider faultProvider) =>
        new ConfigurationRoot([faultProvider, .. inner.Providers]);

    private static TaskSpaceSettingsDraft CreateDraft(string sourceId, string branch) =>
        new()
        {
            SourceId = sourceId,
            Storage = new TaskStorageSettings { Path = sourceId },
            Git = new GitSettings { Branch = branch }
        };

    private sealed class RecordingTaskSpaceConfiguration(ITaskSpaceOperationRunner runner)
        : IActiveTaskSpaceConfiguration
    {
        public Dictionary<string, TaskSpaceSettingsDraft> Drafts { get; } =
            new(StringComparer.Ordinal);

        public List<TaskSpaceSettingsDraft> Persisted { get; } = [];

        public TaskSpaceSettingsDraft Read(string sourceId) =>
            Drafts.TryGetValue(sourceId, out var draft)
                ? ActiveTaskSpaceConfiguration.CloneDraft(draft)
                : CreateDraft(sourceId, string.Empty);

        public TaskSpaceSettingsDraft CaptureActiveProjection(string sourceId) =>
            CreateDraft(sourceId, string.Empty);

        public void PersistCore(TaskSpaceOperationContext context, TaskSpaceSettingsDraft draft)
        {
            runner.Validate(context);
            var clone = ActiveTaskSpaceConfiguration.CloneDraft(draft);
            Persisted.Add(clone);
            Drafts[draft.SourceId] = ActiveTaskSpaceConfiguration.CloneDraft(draft);
        }
    }

    private sealed class CoordinatorFixture(
        TaskSourceManager manager,
        ConfigurableTaskStorageBuilder builder,
        TaskSpaceOperationRunner runner)
    {
        public TaskSourceManager Manager { get; } = manager;
        public ConfigurableTaskStorageBuilder Builder { get; } = builder;
        public TaskSpaceOperationRunner Runner { get; } = runner;
        public List<string> BoundSourceIds { get; } = [];
        public string? TargetSourceId { get; set; }
        public bool SurfaceCleared { get; set; }
        public int PauseSchedulerCalls { get; set; }
        public int RestoreSchedulerCalls { get; set; }
        public TaskSpaceCoordinator Coordinator { get; set; } = null!;
    }

    private sealed class FailingSettingsQueue : ITaskSpaceSettingsPersistenceQueue
    {
        public Exception? LastError { get; } = new InvalidOperationException("Injected drain failure.");

        public void Enqueue(TaskSpaceSettingsDraft draft) =>
            throw new NotSupportedException();

        public Task DrainAsync() =>
            Task.FromException(new InvalidOperationException(
                "Pending task-space settings could not be persisted.",
                LastError));
    }

    private sealed class ConfigurableTaskStorageBuilder : ITaskStorageBuilder
    {
        public List<(TaskSourceDescriptor Descriptor, ConfigurableStorage Storage)> Builds { get; } = [];
        public Dictionary<string, bool> ConnectResultBySourceId { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Exception> InitialLoadExceptionBySourceId { get; } = new(StringComparer.Ordinal);
        public Exception? NextBuildException { get; set; }
        public bool? NextConnectResult { get; set; }
        public Exception? NextInitialLoadException { get; set; }

        public TaskStorageBuildResult Build(TaskStorageBuildRequest request)
        {
            if (NextBuildException != null)
            {
                var exception = NextBuildException;
                NextBuildException = null;
                throw exception;
            }

            var storage = new ConfigurableStorage
            {
                ConnectResult = NextConnectResult ??
                                (!ConnectResultBySourceId.TryGetValue(request.Descriptor.Id, out var result) || result),
                InitialLoadException = NextInitialLoadException ??
                                       InitialLoadExceptionBySourceId.GetValueOrDefault(request.Descriptor.Id)
            };
            NextConnectResult = null;
            NextInitialLoadException = null;
            Builds.Add((request.Descriptor, storage));
            return new TaskStorageBuildResult(
                new UnifiedTaskStorage(new TaskTreeManager(storage), request.TaskContext),
                watcher: null);
        }
    }

    private sealed class ConfigurableStorage : IStorage
    {
        public List<TaskItem> Items { get; } = [];
        public bool ConnectResult { get; init; } = true;
        public int ConnectCalls { get; private set; }
        public int DisconnectCalls { get; private set; }
        public Exception? InitialLoadException { get; init; }
        public Exception? DisconnectException { get; set; }

        public event EventHandler<TaskStorageUpdateEventArgs>? Updating
        {
            add { }
            remove { }
        }

        public event Action<Exception?>? OnConnectionError
        {
            add { }
            remove { }
        }

        public Task<TaskItem> Save(TaskItem item) => Task.FromResult(item);
        public Task<bool> Remove(string itemId) => Task.FromResult(true);
        public Task<TaskItem?> Load(string itemId) => Task.FromResult<TaskItem?>(null);
        public async IAsyncEnumerable<TaskItem> GetAll()
        {
            if (InitialLoadException != null)
            {
                throw InitialLoadException;
            }

            foreach (var item in Items)
            {
                yield return item;
            }
        }
        public Task BulkInsert(IEnumerable<TaskItem> taskItems) => Task.CompletedTask;

        public Task<bool> Connect()
        {
            ConnectCalls++;
            return Task.FromResult(ConnectResult);
        }

        public Task Disconnect()
        {
            DisconnectCalls++;
            if (DisconnectException != null)
            {
                throw DisconnectException;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FaultInjectingConfigurationProvider : ConfigurationProvider
    {
        private string? _pathToFail;
        private int? _writeNumberToFail;
        private int _writeNumber;
        private bool _recordWrites;

        public List<string> RecordedWrites { get; } = [];

        public void FailNextWrite(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            _pathToFail = path;
        }

        public void FailWriteNumber(int writeNumber)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(writeNumber, 1);
            _writeNumber = 0;
            _writeNumberToFail = writeNumber;
        }

        public void BeginRecording()
        {
            RecordedWrites.Clear();
            _writeNumber = 0;
            _recordWrites = true;
        }

        public override void Set(string key, string? value)
        {
            _writeNumber++;
            if (_recordWrites)
            {
                RecordedWrites.Add(key);
            }

            var shouldFailByPath = string.Equals(_pathToFail, key, StringComparison.Ordinal);
            var shouldFailByNumber = _writeNumberToFail == _writeNumber;
            if (!shouldFailByPath && !shouldFailByNumber)
            {
                base.Set(key, value);
                return;
            }

            _pathToFail = null;
            _writeNumberToFail = null;
            throw new InvalidOperationException($"Injected configuration write fault at '{key}'.");
        }
    }
}
