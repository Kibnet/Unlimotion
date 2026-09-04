using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Unlimotion.Notes.Conflicts;
using Unlimotion.Notes.Daily;
using Unlimotion.Notes.Identity;
using Unlimotion.Notes.Vault;
using Unlimotion.Notes.Watching;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class FeedDailyNoteFileNameFormatTests
{
    [Test]
    public async Task ApplyingDottedFormatPersistsPortableSettingAndRebindsDailyTimeline()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var vault = new FileNoteVault(directory.Path);
            var today = new DateOnly(2026, 8, 25);
            await vault.CreateAsync("Ежедневные/2026-08-25.md", "legacy daily note\n");
            using var feed = new FeedViewModel(() => today);

            await feed.InitializeVaultAsync(directory.Path);
            var validation = feed.ValidateDailyNoteFileNameFormat("yyyy.MM.dd");
            var applied = await feed.ApplyDailyNoteFileNameFormatAsync("yyyy.MM.dd");

            using (Assert.Multiple())
            {
                await Assert.That(validation.IsValid).IsTrue();
                await Assert.That(validation.PreviewPath).IsEqualTo("Ежедневные/2026.08.25.md");
                await Assert.That(applied.Succeeded).IsTrue();
                await Assert.That(applied.AppliedState?.FileNameFormat).IsEqualTo("yyyy.MM.dd");
                await Assert.That(feed.Days).IsEmpty();
                await Assert.That((await vault.ReadAsync("Ежедневные/2026-08-25.md"))!.Text)
                    .IsEqualTo("legacy daily note\n");
            }

            await feed.FilesDrawer!.RefreshAsync();
            await Assert.That(feed.FilesDrawer.Files.Select(static file => file.RelativePath))
                .Contains("Ежедневные/2026-08-25.md");

            feed.QuickCaptureText = "captured after dotted layout";
            await feed.CaptureAsync();

            var sidecar = await vault.ReadAsync(DailyNoteSettingsStore.RelativePath);
            var dotted = await vault.ReadAsync("Ежедневные/2026.08.25.md");
            using (Assert.Multiple())
            {
                await Assert.That(sidecar).IsNotNull();
                await Assert.That(sidecar!.Text).Contains("\"dailyFileNameFormat\": \"yyyy.MM.dd\"");
                await Assert.That(dotted).IsNotNull();
                await Assert.That(dotted!.Text).Contains("captured after dotted layout");
                await Assert.That(feed.Days.Count).IsEqualTo(1);
                await Assert.That(feed.Days[0].RelativePath).IsEqualTo("Ежедневные/2026.08.25.md");
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ReloadingExternalFormatUsesNewLayoutWithoutRenamingLegacyFiles()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var vault = new FileNoteVault(directory.Path);
            var today = new DateOnly(2026, 8, 25);
            await vault.CreateAsync("Ежедневные/2026-08-25.md", "legacy daily note\n");
            await vault.CreateAsync("Ежедневные/2026.08.24.md", "external dotted daily note\n");
            using var feed = new FeedViewModel(() => today);

            await feed.InitializeVaultAsync(directory.Path);
            await vault.CreateAsync(
                DailyNoteSettingsStore.RelativePath,
                "{\"schemaVersion\":1,\"dailyFileNameFormat\":\"yyyy.MM.dd\"}\n");
            var reloaded = await feed.ReloadDailyNoteFileNameFormatAsync();

            using (Assert.Multiple())
            {
                await Assert.That(reloaded.FileNameFormat).IsEqualTo("yyyy.MM.dd");
                await Assert.That(reloaded.IsExternalChange).IsTrue();
                await Assert.That(feed.Days.Count).IsEqualTo(1);
                await Assert.That(feed.Days[0].RelativePath).IsEqualTo("Ежедневные/2026.08.24.md");
                await Assert.That(File.Exists(vault.ResolveSafePath("Ежедневные/2026-08-25.md"))).IsTrue();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ExternalSidecarWatcherRebindsWithoutDeadlockingItsOwnRuntime()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var vault = new FileNoteVault(directory.Path);
            var today = new DateOnly(2026, 8, 25);
            await vault.CreateAsync("Ежедневные/2026-08-25.md", "legacy daily note\n");
            await vault.CreateAsync("Ежедневные/2026.08.24.md", "external dotted daily note\n");
            using var feed = new FeedViewModel(() => today);
            var reconfigured = new TaskCompletionSource<NoteDailyFileNameFormatState>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            feed.DailyNoteFileNameFormatChanged += (_, state) =>
            {
                if (state.IsExternalChange)
                {
                    reconfigured.TrySetResult(state);
                }
            };

            await feed.InitializeVaultAsync(directory.Path);
            var sidecarPath = vault.ResolveSafePath(DailyNoteSettingsStore.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(sidecarPath)!);
            await File.WriteAllTextAsync(
                sidecarPath,
                "{\"schemaVersion\":1,\"dailyFileNameFormat\":\"yyyy.MM.dd\"}\n");

            var state = await reconfigured.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForFeedIdleAsync(feed);
            using (Assert.Multiple())
            {
                await Assert.That(state.FileNameFormat).IsEqualTo("yyyy.MM.dd");
                await Assert.That(feed.Days.Count).IsEqualTo(1);
                await Assert.That(feed.Days[0].RelativePath).IsEqualTo("Ежедневные/2026.08.24.md");
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task InvalidExternalSettingKeepsTheLastValidDailySession()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var fileVault = new FileNoteVault(directory.Path);
            var vault = new NonWatchingNoteVault(fileVault);
            var today = new DateOnly(2026, 8, 25);
            await fileVault.CreateAsync("Ежедневные/2026-08-25.md", "legacy daily note\n");
            using var feed = new FeedViewModel(() => today, _ => vault);

            await feed.InitializeVaultAsync(directory.Path);
            await fileVault.CreateAsync(
                DailyNoteSettingsStore.RelativePath,
                "{\"schemaVersion\":1,\"dailyFileNameFormat\":\"yyyy/MM/dd\"}\n");
            var reloaded = await feed.ReloadDailyNoteFileNameFormatAsync();

            using (Assert.Multiple())
            {
                await Assert.That(reloaded.FileNameFormat).IsEqualTo(DailyNoteNaming.DefaultFileNameFormat);
                await Assert.That(reloaded.IsExternalChange).IsTrue();
                await Assert.That(reloaded.RequiresReload).IsTrue();
                await Assert.That(feed.IsVaultInitialized).IsTrue();
                await Assert.That(feed.Days.Count).IsEqualTo(1);
                await Assert.That(feed.Days[0].RelativePath).IsEqualTo("Ежедневные/2026-08-25.md");
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task EmptyFirstConnectKeepsQuickCapturePendingAfterReinitialize()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var fileVault = new FileNoteVault(directory.Path);
            var today = new DateOnly(2026, 8, 25);
            var registry = new VaultRootRegistry();
            const string capture = "Мысль, записанная после пустого первого подключения";

            using (var firstFeed = new FeedViewModel(
                       () => today,
                       root => new NonWatchingNoteVault(new FileNoteVault(root)),
                       vaultRootRegistry: registry))
            {
                await firstFeed.InitializeVaultAsync(directory.Path);
                await Assert.That(firstFeed.Days).IsEmpty();

                firstFeed.QuickCaptureText = capture;
                await firstFeed.CaptureAsync();
                await Assert.That((await fileVault.ReadAsync("Ежедневные/2026-08-25.md"))!.Text)
                    .Contains(capture);
            }

            using var reloadedFeed = new FeedViewModel(
                () => today,
                root => new NonWatchingNoteVault(new FileNoteVault(root)),
                vaultRootRegistry: registry);
            await reloadedFeed.InitializeVaultAsync(directory.Path);
            reloadedFeed.StartReviewCommand.Execute(null);
            await WaitForReviewSelectionAsync(reloadedFeed);

            using (Assert.Multiple())
            {
                await Assert.That(reloadedFeed.PendingReviewBlocks).IsEqualTo(1);
                await Assert.That(reloadedFeed.CurrentReview!.SelectedMarkdown).Contains(capture);
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task EmptyDefaultLayoutDoesNotHideExistingDottedHistoryAfterReconfigure()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var fileVault = new FileNoteVault(directory.Path);
            var vault = new NonWatchingNoteVault(fileVault);
            var today = new DateOnly(2026, 8, 25);
            const string dottedPath = "Ежедневные/2026.08.25.md";
            using var feed = new FeedViewModel(() => today, _ => vault);

            await feed.InitializeVaultAsync(directory.Path);
            await Assert.That(feed.Days).IsEmpty();
            await fileVault.CreateAsync(dottedPath, "История до выбора точечного формата\n");

            var applied = await feed.ApplyDailyNoteFileNameFormatAsync("yyyy.MM.dd");

            using (Assert.Multiple())
            {
                await Assert.That(applied.Succeeded).IsTrue();
                await Assert.That(feed.BootstrapIndexedFiles).IsEqualTo(1);
                await Assert.That(feed.Days.Single().RelativePath).IsEqualTo(dottedPath);
                await Assert.That(feed.PendingReviewBlocks).IsZero();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task InvalidExternalSidecarWatcherPublishesRetryableReloadState()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var vault = new FileNoteVault(directory.Path);
            var today = new DateOnly(2026, 8, 25);
            await vault.CreateAsync("Ежедневные/2026-08-25.md", "last valid daily note\n");
            using var feed = new FeedViewModel(() => today);
            var failure = new TaskCompletionSource<NoteDailyFileNameFormatState>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            feed.DailyNoteFileNameFormatChanged += (_, state) =>
            {
                if (state.RequiresReload)
                {
                    failure.TrySetResult(state);
                }
            };

            await feed.InitializeVaultAsync(directory.Path);
            var sidecarPath = vault.ResolveSafePath(DailyNoteSettingsStore.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(sidecarPath)!);
            await File.WriteAllTextAsync(
                sidecarPath,
                "{\"schemaVersion\":1,\"dailyFileNameFormat\":\"yyyy/MM/dd\"}\n");

            var state = await failure.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForFeedIdleAsync(feed);
            using (Assert.Multiple())
            {
                await Assert.That(state.IsExternalChange).IsTrue();
                await Assert.That(state.RequiresReload).IsTrue();
                await Assert.That(state.StatusMessage).IsNotNull();
                await Assert.That(feed.IsVaultInitialized).IsTrue();
                await Assert.That(feed.Days.Single().RelativePath).IsEqualTo("Ежедневные/2026-08-25.md");
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task DirectoryRenameRescanWithUnchangedSidecarKeepsDirtyFormatDraftAndActiveSession()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            using var recovery = new TempNotesDirectory();
            var fileVault = new FileNoteVault(directory.Path);
            var vault = new NonWatchingNoteVault(fileVault);
            var today = new DateOnly(2026, 8, 25);
            await fileVault.CreateAsync("Ежедневные/2026-08-25.md", "daily note\n");
            await fileVault.CreateAsync(
                DailyNoteSettingsStore.RelativePath,
                "{\"schemaVersion\":1,\"dailyFileNameFormat\":\"yyyy-MM-dd\"}\n");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
            var settings = new SettingsViewModel(
                configuration,
                isExternalNoteVaultSupported: true)
            {
                NoteVaultRootPath = directory.Path
            };
            using var feed = new FeedViewModel(() => today, _ => vault);
            var watcherPublishedFormatStateCount = 0;
            EventHandler<NoteDailyFileNameFormatState> settingsBridge = (_, state) =>
            {
                Interlocked.Increment(ref watcherPublishedFormatStateCount);
                settings.ApplyNoteDailyFileNameFormatState(state);
            };
            feed.DailyNoteFileNameFormatChanged += settingsBridge;
            try
            {
                settings.ConfigureNoteDailyFileNameFormatBridge(
                    feed.ValidateDailyNoteFileNameFormat,
                    feed.ApplyDailyNoteFileNameFormatAsync,
                    feed.ReloadDailyNoteFileNameFormatAsync);
                await feed.InitializeVaultAsync(directory.Path);
                settings.SetNoteDailyFileNameFormatFeedAvailability(
                    feed.IsVaultInitialized,
                    feed.IsBusy || feed.IsIdentityFrozen,
                    feed.VaultRootPath);
                settings.NoteDailyFileNameFormatDraft = "yyyy.MM.dd";

                var filesDrawerBeforeRescan = feed.FilesDrawer;
                var areaManagementBeforeRescan = feed.AreaManagement;
                var sidecarBeforeRescan = await fileVault.ReadAsync(DailyNoteSettingsStore.RelativePath);
                var identity = await new VaultIdentityService(fileVault).GetOrCreateAsync();
                var sidecarSource = new RuntimeManualVaultWatchSource();
                var sink = new WatcherDailyNoteSettingsReloadSink(feed);
                await using var runtime = new FeedVaultWatchRuntime(
                    identity.VaultId,
                    fileVault,
                    new OwnWriteRegistry(),
                    new InMemoryDirtyDocumentRegistry(),
                    recovery.Path,
                    sink,
                    new RuntimeManualVaultWatchSource(),
                    sidecarSource,
                    TimeSpan.Zero);
                runtime.Start();
                await runtime.ActivateAsync();

                Interlocked.Exchange(ref watcherPublishedFormatStateCount, 0);
                var beforeRename = Path.Combine(directory.Path, "Before rename");
                var afterRename = Path.Combine(directory.Path, "After rename");
                Directory.CreateDirectory(beforeRename);
                Directory.Move(beforeRename, afterRename);
                sidecarSource.Emit(new VaultRawChange(
                    VaultRawChangeKind.Renamed,
                    afterRename,
                    beforeRename,
                    IsDirectory: true));
                var rescan = await sink.DailyNoteSettingsReloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var sidecarAfterRescan = await fileVault.ReadAsync(DailyNoteSettingsStore.RelativePath);

                using (Assert.Multiple())
                {
                    await Assert.That(rescan.Scope).IsEqualTo(VaultWatchScope.Sidecar);
                    await Assert.That(rescan.Kind).IsEqualTo(VaultWatchChangeKind.RescanRequired);
                    await Assert.That(watcherPublishedFormatStateCount).IsZero();
                    await Assert.That(settings.HasUnappliedNoteDailyFileNameFormatDraft).IsTrue();
                    await Assert.That(settings.NoteDailyFileNameFormatDraft).IsEqualTo("yyyy.MM.dd");
                    await Assert.That(settings.HasExternalNoteDailyFileNameFormatChange).IsFalse();
                    await Assert.That(settings.CanReloadExternalNoteDailyFileNameFormat).IsFalse();
                    await Assert.That(feed.IsVaultInitialized).IsTrue();
                    await Assert.That(feed.FilesDrawer).IsSameReferenceAs(filesDrawerBeforeRescan);
                    await Assert.That(feed.AreaManagement).IsSameReferenceAs(areaManagementBeforeRescan);
                    await Assert.That(sidecarAfterRescan!.Revision).IsEqualTo(sidecarBeforeRescan!.Revision);
                    await Assert.That(sidecarAfterRescan.Text).IsEqualTo(sidecarBeforeRescan.Text);
                }
            }
            finally
            {
                feed.DailyNoteFileNameFormatChanged -= settingsBridge;
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ConflictingApplyKeepsTheLastValidDailySession()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var fileVault = new FileNoteVault(directory.Path);
            var vault = new NonWatchingNoteVault(fileVault);
            var today = new DateOnly(2026, 8, 25);
            await fileVault.CreateAsync("Ежедневные/2026-08-25.md", "legacy daily note\n");
            using var feed = new FeedViewModel(() => today, _ => vault);

            await feed.InitializeVaultAsync(directory.Path);
            await fileVault.CreateAsync(
                DailyNoteSettingsStore.RelativePath,
                "{\"schemaVersion\":1,\"dailyFileNameFormat\":\"yyyy.MM.dd\"}\n");
            var applied = await feed.ApplyDailyNoteFileNameFormatAsync("yyyy_MM_dd");

            using (Assert.Multiple())
            {
                await Assert.That(applied.Succeeded).IsFalse();
                await Assert.That(feed.IsVaultInitialized).IsTrue();
                await Assert.That(feed.Days.Count).IsEqualTo(1);
                await Assert.That(feed.Days[0].RelativePath).IsEqualTo("Ежедневные/2026-08-25.md");
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task FailedRebindAfterSaveKeepsTheLastValidDailySessionInteractive()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var fileVault = new FileNoteVault(directory.Path);
            var vault = new FailingRebindNoteVault(fileVault);
            var today = new DateOnly(2026, 8, 25);
            await fileVault.CreateAsync("Ежедневные/2026-08-25.md", "legacy daily note\n");
            await fileVault.CreateAsync(
                DailyNoteSettingsStore.RelativePath,
                "{\"schemaVersion\":1,\"dailyFileNameFormat\":\"yyyy-MM-dd\",\"preserved\":\"keep\"}\n");
            using var feed = new FeedViewModel(() => today, _ => vault);

            await feed.InitializeVaultAsync(directory.Path);
            vault.FailReviewRead = true;
            var applied = await feed.ApplyDailyNoteFileNameFormatAsync("yyyy.MM.dd");

            using (Assert.Multiple())
            {
                await Assert.That(applied.Succeeded).IsFalse();
                await Assert.That(feed.IsVaultInitialized).IsTrue();
                await Assert.That(feed.Days).Count().IsEqualTo(1);
                await Assert.That(feed.Days[0].RelativePath).IsEqualTo("Ежедневные/2026-08-25.md");
                await Assert.That((await fileVault.ReadAsync(DailyNoteSettingsStore.RelativePath))!.Text)
                    .Contains("\"dailyFileNameFormat\": \"yyyy-MM-dd\"");
                await Assert.That((await fileVault.ReadAsync(DailyNoteSettingsStore.RelativePath))!.Text)
                    .Contains("\"preserved\": \"keep\"");
            }

            vault.FailReviewRead = false;
            feed.QuickCaptureText = "still uses the last valid layout";
            await feed.CaptureAsync();
            await Assert.That((await fileVault.ReadAsync("Ежедневные/2026-08-25.md"))!.Text)
                .Contains("still uses the last valid layout");

            var retried = await feed.ApplyDailyNoteFileNameFormatAsync("yyyy.MM.dd");
            using (Assert.Multiple())
            {
                await Assert.That(retried.Succeeded).IsTrue();
                await Assert.That(retried.AppliedState?.FileNameFormat).IsEqualTo("yyyy.MM.dd");
                await Assert.That((await fileVault.ReadAsync(DailyNoteSettingsStore.RelativePath))!.Text)
                    .Contains("\"dailyFileNameFormat\": \"yyyy.MM.dd\"");
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ExternalSidecarWriteAfterApplySaveWinsBeforeCandidateSessionUsesTheNewLayout()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var fileVault = new FileNoteVault(directory.Path);
            var race = new ApplySidecarRaceController();
            var today = new DateOnly(2026, 8, 25);
            const string capture = "capture follows the external sidecar";
            const string externalPath = "Ежедневные/25.08.2026.md";
            using var feed = new FeedViewModel(
                () => today,
                root => new ApplySidecarRaceNoteVault(new FileNoteVault(root), race));

            await feed.InitializeVaultAsync(directory.Path);
            race.RewriteAfterNextDailySettingsSave = true;

            var applied = await feed.ApplyDailyNoteFileNameFormatAsync("yyyy.MM.dd");
            feed.QuickCaptureText = capture;
            await feed.CaptureAsync();

            using (Assert.Multiple())
            {
                await Assert.That(race.ExternalRewritePerformed).IsTrue();
                await Assert.That(applied.Succeeded).IsTrue();
                await Assert.That(applied.AppliedState?.FileNameFormat).IsEqualTo("dd.MM.yyyy");
                await Assert.That(applied.AppliedState?.IsExternalChange).IsTrue();
                await Assert.That((await fileVault.ReadAsync(externalPath))!.Text).Contains(capture);
                await Assert.That(await fileVault.ReadAsync("Ежедневные/2026.08.25.md")).IsNull();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task AuxiliaryPreparationFailureDoesNotLeakCandidateRootReservation()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var parent = new TempNotesDirectory();
            var originalRoot = Path.Combine(parent.Path, "original");
            var relocatedRoot = Path.Combine(parent.Path, "relocated");
            Directory.CreateDirectory(originalRoot);
            var registry = new VaultRootRegistry();
            var failure = new AuxiliaryPreparationFailureController();
            var today = new DateOnly(2026, 8, 25);
            using var feed = new FeedViewModel(
                () => today,
                root => new AuxiliaryPreparationFailureNoteVault(new FileNoteVault(root), failure),
                vaultRootRegistry: registry);

            await feed.InitializeVaultAsync(originalRoot);
            failure.FailSecondAreaReadAfterArm = true;

            var applied = await feed.ApplyDailyNoteFileNameFormatAsync("yyyy.MM.dd");
            Directory.Move(originalRoot, relocatedRoot);
            await feed.InitializeVaultAsync(relocatedRoot);

            using (Assert.Multiple())
            {
                await Assert.That(applied.Succeeded).IsFalse();
                await Assert.That(failure.FailurePerformed).IsTrue();
                await Assert.That(feed.IsVaultInitialized).IsTrue();
                await Assert.That(feed.ErrorMessage).IsNull();
                await Assert.That(feed.VaultRootPath).IsEqualTo(relocatedRoot);
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RootRelocationWithTheSameVaultIdentityUsesAnExclusiveHandoff()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var parent = new TempNotesDirectory();
            var originalRoot = Path.Combine(parent.Path, "original");
            var relocatedRoot = Path.Combine(parent.Path, "relocated");
            Directory.CreateDirectory(originalRoot);
            var originalVault = new FileNoteVault(originalRoot);
            var today = new DateOnly(2026, 8, 25);
            var registry = new VaultRootRegistry();
            await originalVault.CreateAsync("Ежедневные/2026-08-25.md", "before relocation\n");
            var feed = new FeedViewModel(
                () => today,
                root => new NonWatchingNoteVault(new FileNoteVault(root)),
                vaultRootRegistry: registry);
            try
            {
                await feed.InitializeVaultAsync(originalRoot);
                Directory.Move(originalRoot, relocatedRoot);
                var identity = await new VaultIdentityService(new FileNoteVault(relocatedRoot)).GetOrCreateAsync();

                await feed.InitializeVaultAsync(relocatedRoot);
                feed.QuickCaptureText = "after relocation";
                await feed.CaptureAsync();

                var originalConflict = await NotesTestSupport.Capture<InvalidOperationException>(() =>
                    registry.Attach(identity.VaultId, originalRoot));
                using (Assert.Multiple())
                {
                    await Assert.That(feed.IsVaultInitialized).IsTrue();
                    await Assert.That(feed.VaultRootPath).IsEqualTo(relocatedRoot);
                    await Assert.That((await new FileNoteVault(relocatedRoot)
                            .ReadAsync("Ежедневные/2026-08-25.md"))!.Text)
                        .Contains("after relocation");
                    await Assert.That(originalConflict.Message).Contains("another local root");
                }

                feed.Dispose();
                registry.Attach(identity.VaultId, originalRoot);
            }
            finally
            {
                feed.Dispose();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TrailingSeparatorReinitializeDoesNotCreateRegistryConflictBeforeApplyingDailyFormat()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var fileVault = new FileNoteVault(directory.Path);
            var registry = new VaultRootRegistry();
            var today = new DateOnly(2026, 8, 25);
            await fileVault.CreateAsync("Ежедневные/2026-08-25.md", "daily note\n");
            using var feed = new FeedViewModel(
                () => today,
                root => new NonWatchingNoteVault(new FileNoteVault(root)),
                vaultRootRegistry: registry);

            await feed.InitializeVaultAsync(directory.Path);
            await feed.InitializeVaultAsync(directory.Path + Path.DirectorySeparatorChar);
            var applied = await feed.ApplyDailyNoteFileNameFormatAsync("yyyy.MM.dd");

            using (Assert.Multiple())
            {
                await Assert.That(applied.Succeeded).IsTrue();
                await Assert.That(applied.IsCancelled).IsFalse();
                await Assert.That(feed.ErrorMessage).IsNull();
                await Assert.That(feed.IsVaultInitialized).IsTrue();
                await Assert.That((await fileVault.ReadAsync(DailyNoteSettingsStore.RelativePath))!.Text)
                    .Contains("\"dailyFileNameFormat\": \"yyyy.MM.dd\"");
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RootSwitchCancelsAWaitingApplyBeforeItWritesThePreviousVault()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var firstDirectory = new TempNotesDirectory();
            using var secondDirectory = new TempNotesDirectory();
            var firstFileVault = new FileNoteVault(firstDirectory.Path);
            var firstVault = new BlockingSettingsReadNoteVault(firstFileVault);
            var secondVault = new NonWatchingNoteVault(new FileNoteVault(secondDirectory.Path));
            var vaults = new Dictionary<string, INoteVault>(StringComparer.OrdinalIgnoreCase)
            {
                [firstDirectory.Path] = firstVault,
                [secondDirectory.Path] = secondVault
            };
            var today = new DateOnly(2026, 8, 25);
            await firstFileVault.CreateAsync("Ежедневные/2026-08-25.md", "first vault\n");
            using var feed = new FeedViewModel(() => today, root => vaults[root]);

            await feed.InitializeVaultAsync(firstDirectory.Path);
            firstVault.BlockSettingsRead = true;
            var apply = feed.ApplyDailyNoteFileNameFormatAsync("yyyy.MM.dd");
            await firstVault.SettingsReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await feed.InitializeVaultAsync(secondDirectory.Path);
            var result = await apply;

            using (Assert.Multiple())
            {
                await Assert.That(result.IsCancelled).IsTrue();
                await Assert.That(feed.VaultRootPath).IsEqualTo(secondDirectory.Path);
                await Assert.That(await firstFileVault.ReadAsync(DailyNoteSettingsStore.RelativePath)).IsNull();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RootSwitchDuringBlockedOldSessionDisposeCancelsApplyAndRestoresFirstSidecar()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var firstDirectory = new TempNotesDirectory();
            using var secondDirectory = new TempNotesDirectory();
            using var recovery = new TempNotesDirectory();
            var firstFileVault = new FileNoteVault(firstDirectory.Path);
            var secondFileVault = new FileNoteVault(secondDirectory.Path);
            var firstVault = new NonWatchingNoteVault(firstFileVault);
            var secondVault = new NonWatchingNoteVault(secondFileVault);
            var vaults = new Dictionary<string, INoteVault>(StringComparer.OrdinalIgnoreCase)
            {
                [firstDirectory.Path] = firstVault,
                [secondDirectory.Path] = secondVault
            };
            var today = new DateOnly(2026, 8, 25);
            await firstFileVault.CreateAsync("Ежедневные/2026-08-25.md", "first vault\n");
            await secondFileVault.CreateAsync("Ежедневные/2026-08-25.md", "second vault\n");
            using var feed = new FeedViewModel(() => today, root => vaults[root]);

            await feed.InitializeVaultAsync(firstDirectory.Path);
            var identity = await new VaultIdentityService(firstFileVault).GetOrCreateAsync();
            var markdownSource = new RuntimeManualVaultWatchSource(blockDispose: true);
            var sidecarSource = new RuntimeManualVaultWatchSource(blockDispose: true);
            var oldRuntime = new FeedVaultWatchRuntime(
                identity.VaultId,
                firstFileVault,
                new OwnWriteRegistry(),
                new InMemoryDirtyDocumentRegistry(),
                recovery.Path,
                new RecordingFeedVaultWatchRuntimeSink(),
                markdownSource,
                sidecarSource,
                TimeSpan.Zero);
            oldRuntime.Start();
            await oldRuntime.ActivateAsync();
            SetWatchRuntime(feed, oldRuntime);

            var nonNullFilesDrawerInstalls = 0;
            PropertyChangedEventHandler filesDrawerObserver = (_, args) =>
            {
                if (args.PropertyName == nameof(FeedViewModel.FilesDrawer) && feed.FilesDrawer is not null)
                {
                    Interlocked.Increment(ref nonNullFilesDrawerInstalls);
                }
            };
            feed.PropertyChanged += filesDrawerObserver;
            try
            {
                var apply = feed.ApplyDailyNoteFileNameFormatAsync("yyyy.MM.dd");
                await Task.WhenAll(markdownSource.DisposeEntered.Task, sidecarSource.DisposeEntered.Task)
                    .WaitAsync(TimeSpan.FromSeconds(5));
                var persistedDuringApply = await firstFileVault.ReadAsync(DailyNoteSettingsStore.RelativePath);
                await Assert.That(persistedDuringApply!.Text)
                    .Contains("\"dailyFileNameFormat\": \"yyyy.MM.dd\"");

                var switchToSecondRoot = feed.InitializeVaultAsync(secondDirectory.Path);
                markdownSource.AllowDispose.TrySetResult();
                sidecarSource.AllowDispose.TrySetResult();

                var applyResult = await apply.WaitAsync(TimeSpan.FromSeconds(5));
                await switchToSecondRoot.WaitAsync(TimeSpan.FromSeconds(5));

                using (Assert.Multiple())
                {
                    await Assert.That(applyResult.IsCancelled).IsTrue();
                    await Assert.That(await firstFileVault.ReadAsync(DailyNoteSettingsStore.RelativePath)).IsNull();
                    await Assert.That(nonNullFilesDrawerInstalls).IsEqualTo(1);
                    await Assert.That(feed.IsVaultInitialized).IsTrue();
                    await Assert.That(feed.VaultRootPath).IsEqualTo(secondDirectory.Path);
                    await Assert.That(feed.Days.Single().RelativePath).IsEqualTo("Ежедневные/2026-08-25.md");
                    await Assert.That(feed.Days.Single().Text).Contains("second vault");
                }
            }
            finally
            {
                feed.PropertyChanged -= filesDrawerObserver;
                markdownSource.AllowDispose.TrySetResult();
                sidecarSource.AllowDispose.TrySetResult();
                await oldRuntime.DisposeAsync();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task DisposeAfterTransferBeforeVisibleInstallRollsBackApplyAndReleasesRegistryAttachment()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var fileVault = new FileNoteVault(directory.Path);
            var registry = new VaultRootRegistry();
            var today = new DateOnly(2026, 8, 25);
            await fileVault.CreateAsync("Ежедневные/2026-08-25.md", "daily note\n");
            using var feed = new FeedViewModel(
                () => today,
                root => new NonWatchingNoteVault(new FileNoteVault(root)),
                vaultRootRegistry: registry);
            await feed.InitializeVaultAsync(directory.Path);

            using var releaseResetVisibleState = new ManualResetEventSlim(false);
            using var disposeCancellation = new CancellationTokenSource();
            var resetVisibleStateReached = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var resetVisibleStateBlocked = 0;
            PropertyChangedEventHandler resetVisibleStateObserver = (_, args) =>
            {
                if (args.PropertyName != nameof(FeedViewModel.VaultRootPath) ||
                    feed.VaultRootPath is not null ||
                    Interlocked.CompareExchange(ref resetVisibleStateBlocked, 1, 0) != 0)
                {
                    return;
                }

                resetVisibleStateReached.TrySetResult();
                releaseResetVisibleState.Wait();
            };
            var disposeAfterTransfer = Task.Run(async () =>
            {
                await resetVisibleStateReached.Task.WaitAsync(disposeCancellation.Token);
                try
                {
                    feed.Dispose();
                }
                finally
                {
                    releaseResetVisibleState.Set();
                }
            });
            var observerAttached = false;
            try
            {
                var apply = feed.ApplyDailyNoteFileNameFormatAsync("yyyy.MM.dd");
                feed.PropertyChanged += resetVisibleStateObserver;
                observerAttached = true;

                await resetVisibleStateReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var applyResult = await apply.WaitAsync(TimeSpan.FromSeconds(5));
                await disposeAfterTransfer.WaitAsync(TimeSpan.FromSeconds(5));

                using var freshFeed = new FeedViewModel(
                    () => today,
                    root => new NonWatchingNoteVault(new FileNoteVault(root)),
                    vaultRootRegistry: registry);
                await freshFeed.InitializeVaultAsync(directory.Path);

                using (Assert.Multiple())
                {
                    await Assert.That(applyResult.IsCancelled).IsTrue();
                    await Assert.That(await fileVault.ReadAsync(DailyNoteSettingsStore.RelativePath)).IsNull();
                    await Assert.That(freshFeed.IsVaultInitialized).IsTrue();
                    await Assert.That(freshFeed.VaultRootPath).IsEqualTo(directory.Path);
                    await Assert.That(freshFeed.ErrorMessage).IsNull();
                }
            }
            finally
            {
                if (observerAttached)
                {
                    feed.PropertyChanged -= resetVisibleStateObserver;
                }

                releaseResetVisibleState.Set();
                disposeCancellation.Cancel();
                try
                {
                    await disposeAfterTransfer;
                }
                catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
                {
                }
            }
        }, CancellationToken.None);
    }

    private static Task ReloadDailyNoteSettingsFromWatcherAsync(
        FeedViewModel feed,
        CancellationToken cancellationToken)
    {
        var method = typeof(FeedViewModel).GetMethod(
            "ReloadDailyNoteSettingsFromWatcherAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The Feed watcher reload handler was not found.");
        return (Task)(method.Invoke(feed, [cancellationToken])
            ?? throw new InvalidOperationException("The Feed watcher reload handler did not return a task."));
    }

    private static void SetWatchRuntime(FeedViewModel feed, FeedVaultWatchRuntime runtime)
    {
        var field = typeof(FeedViewModel).GetField(
            "watchRuntime",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The Feed watcher runtime field was not found.");
        field.SetValue(feed, runtime);
    }

    private sealed class WatcherDailyNoteSettingsReloadSink(FeedViewModel feed) : IFeedVaultWatchRuntimeSink
    {
        public TaskCompletionSource<VaultWatchChange> DailyNoteSettingsReloaded { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask ReloadMarkdownAsync(DocumentReloadSignal signal, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask ShowMarkdownConflictAsync(
            DocumentConflictState conflict,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask RefreshAreasAsync(VaultWatchChange change, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask RefreshReviewAsync(VaultWatchChange change, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public async ValueTask ReloadDailyNoteSettingsAsync(
            VaultWatchChange change,
            CancellationToken cancellationToken)
        {
            try
            {
                await ReloadDailyNoteSettingsFromWatcherAsync(feed, cancellationToken);
                DailyNoteSettingsReloaded.TrySetResult(change);
            }
            catch (Exception exception)
            {
                DailyNoteSettingsReloaded.TrySetException(exception);
            }
        }

        public ValueTask FreezeForIdentityChangeAsync(
            FeedVaultIdentityFreezeSignal signal,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class NonWatchingNoteVault(INoteVault inner) : INoteVault
    {
        public string RootPath => inner.RootPath;

        public Task<VaultDocument?> ReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default) =>
            inner.ReadAsync(relativePath, cancellationToken);

        public Task<VaultWriteResult> WriteAsync(
            string relativePath,
            string text,
            string? expectedRevision,
            bool hasUtf8Bom = false,
            CancellationToken cancellationToken = default) =>
            inner.WriteAsync(relativePath, text, expectedRevision, hasUtf8Bom, cancellationToken);

        public Task<VaultWriteResult> CreateAsync(
            string relativePath,
            string text,
            bool hasUtf8Bom = false,
            CancellationToken cancellationToken = default) =>
            inner.CreateAsync(relativePath, text, hasUtf8Bom, cancellationToken);

        public Task<IReadOnlyList<string>> ListMarkdownFilesAsync(
            CancellationToken cancellationToken = default) =>
            inner.ListMarkdownFilesAsync(cancellationToken);

        public Task<IReadOnlyList<string>> ListFilesAsync(
            string relativeDirectory,
            string searchPattern,
            CancellationToken cancellationToken = default) =>
            inner.ListFilesAsync(relativeDirectory, searchPattern, cancellationToken);

        public Task<bool> DeleteAsync(
            string relativePath,
            string? expectedRevision,
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(relativePath, expectedRevision, cancellationToken);

        public string ResolveSafePath(string relativePath) => inner.ResolveSafePath(relativePath);
    }

    private sealed class FailingRebindNoteVault(INoteVault inner) : INoteVault
    {
        public bool FailReviewRead { get; set; }

        public string RootPath => inner.RootPath;

        public Task<VaultDocument?> ReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default) =>
            inner.ReadAsync(relativePath, cancellationToken);

        public Task<VaultWriteResult> WriteAsync(
            string relativePath,
            string text,
            string? expectedRevision,
            bool hasUtf8Bom = false,
            CancellationToken cancellationToken = default) =>
            inner.WriteAsync(relativePath, text, expectedRevision, hasUtf8Bom, cancellationToken);

        public Task<VaultWriteResult> CreateAsync(
            string relativePath,
            string text,
            bool hasUtf8Bom = false,
            CancellationToken cancellationToken = default) =>
            inner.CreateAsync(relativePath, text, hasUtf8Bom, cancellationToken);

        public Task<IReadOnlyList<string>> ListMarkdownFilesAsync(
            CancellationToken cancellationToken = default) =>
            inner.ListMarkdownFilesAsync(cancellationToken);

        public Task<IReadOnlyList<string>> ListFilesAsync(
            string relativeDirectory,
            string searchPattern,
            CancellationToken cancellationToken = default)
        {
            if (FailReviewRead
                && relativeDirectory.StartsWith(".unlimotion/review", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Simulated rebind failure.");
            }

            return inner.ListFilesAsync(relativeDirectory, searchPattern, cancellationToken);
        }

        public Task<bool> DeleteAsync(
            string relativePath,
            string? expectedRevision,
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(relativePath, expectedRevision, cancellationToken);

        public string ResolveSafePath(string relativePath) => inner.ResolveSafePath(relativePath);
    }

    private sealed class ApplySidecarRaceController
    {
        public bool RewriteAfterNextDailySettingsSave { get; set; }

        public bool ExternalRewritePerformed { get; set; }
    }

    private sealed class ApplySidecarRaceNoteVault(
        INoteVault inner,
        ApplySidecarRaceController race) : INoteVault
    {
        public string RootPath => inner.RootPath;

        public async Task<VaultDocument?> ReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            if (race.RewriteAfterNextDailySettingsSave
                && string.Equals(relativePath, VaultIdentityService.ManifestPath, StringComparison.Ordinal))
            {
                race.RewriteAfterNextDailySettingsSave = false;
                var current = await inner.ReadAsync(DailyNoteSettingsStore.RelativePath, cancellationToken);
                await inner.WriteAsync(
                    DailyNoteSettingsStore.RelativePath,
                    "{\"schemaVersion\":1,\"dailyFileNameFormat\":\"dd.MM.yyyy\"}\n",
                    current?.Revision,
                    cancellationToken: cancellationToken);
                race.ExternalRewritePerformed = true;
            }

            return await inner.ReadAsync(relativePath, cancellationToken);
        }

        public async Task<VaultWriteResult> WriteAsync(
            string relativePath,
            string text,
            string? expectedRevision,
            bool hasUtf8Bom = false,
            CancellationToken cancellationToken = default) =>
            await inner.WriteAsync(relativePath, text, expectedRevision, hasUtf8Bom, cancellationToken);

        public Task<VaultWriteResult> CreateAsync(
            string relativePath,
            string text,
            bool hasUtf8Bom = false,
            CancellationToken cancellationToken = default) =>
            inner.CreateAsync(relativePath, text, hasUtf8Bom, cancellationToken);

        public Task<IReadOnlyList<string>> ListMarkdownFilesAsync(
            CancellationToken cancellationToken = default) =>
            inner.ListMarkdownFilesAsync(cancellationToken);

        public Task<IReadOnlyList<string>> ListFilesAsync(
            string relativeDirectory,
            string searchPattern,
            CancellationToken cancellationToken = default) =>
            inner.ListFilesAsync(relativeDirectory, searchPattern, cancellationToken);

        public Task<bool> DeleteAsync(
            string relativePath,
            string? expectedRevision,
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(relativePath, expectedRevision, cancellationToken);

        public string ResolveSafePath(string relativePath) => inner.ResolveSafePath(relativePath);
    }

    private sealed class AuxiliaryPreparationFailureController
    {
        private int areaReadsAfterArm;

        public bool FailSecondAreaReadAfterArm { get; set; }

        public bool FailurePerformed { get; private set; }

        public bool ShouldFailAreaRead()
        {
            if (!FailSecondAreaReadAfterArm || ++areaReadsAfterArm != 2)
            {
                return false;
            }

            FailSecondAreaReadAfterArm = false;
            FailurePerformed = true;
            return true;
        }
    }

    private sealed class AuxiliaryPreparationFailureNoteVault(
        INoteVault inner,
        AuxiliaryPreparationFailureController failure) : INoteVault
    {
        public string RootPath => inner.RootPath;

        public Task<VaultDocument?> ReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(relativePath, ".unlimotion/areas.json", StringComparison.Ordinal)
                && failure.ShouldFailAreaRead())
            {
                throw new InvalidOperationException("Simulated auxiliary area loading failure.");
            }

            return inner.ReadAsync(relativePath, cancellationToken);
        }

        public Task<VaultWriteResult> WriteAsync(
            string relativePath,
            string text,
            string? expectedRevision,
            bool hasUtf8Bom = false,
            CancellationToken cancellationToken = default) =>
            inner.WriteAsync(relativePath, text, expectedRevision, hasUtf8Bom, cancellationToken);

        public Task<VaultWriteResult> CreateAsync(
            string relativePath,
            string text,
            bool hasUtf8Bom = false,
            CancellationToken cancellationToken = default) =>
            inner.CreateAsync(relativePath, text, hasUtf8Bom, cancellationToken);

        public Task<IReadOnlyList<string>> ListMarkdownFilesAsync(
            CancellationToken cancellationToken = default) =>
            inner.ListMarkdownFilesAsync(cancellationToken);

        public Task<IReadOnlyList<string>> ListFilesAsync(
            string relativeDirectory,
            string searchPattern,
            CancellationToken cancellationToken = default) =>
            inner.ListFilesAsync(relativeDirectory, searchPattern, cancellationToken);

        public Task<bool> DeleteAsync(
            string relativePath,
            string? expectedRevision,
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(relativePath, expectedRevision, cancellationToken);

        public string ResolveSafePath(string relativePath) => inner.ResolveSafePath(relativePath);
    }

    private sealed class BlockingSettingsReadNoteVault(INoteVault inner) : INoteVault
    {
        public TaskCompletionSource SettingsReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool BlockSettingsRead { get; set; }

        public string RootPath => inner.RootPath;

        public async Task<VaultDocument?> ReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            if (BlockSettingsRead
                && string.Equals(relativePath, DailyNoteSettingsStore.RelativePath, StringComparison.Ordinal))
            {
                SettingsReadStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return await inner.ReadAsync(relativePath, cancellationToken);
        }

        public Task<VaultWriteResult> WriteAsync(
            string relativePath,
            string text,
            string? expectedRevision,
            bool hasUtf8Bom = false,
            CancellationToken cancellationToken = default) =>
            inner.WriteAsync(relativePath, text, expectedRevision, hasUtf8Bom, cancellationToken);

        public Task<VaultWriteResult> CreateAsync(
            string relativePath,
            string text,
            bool hasUtf8Bom = false,
            CancellationToken cancellationToken = default) =>
            inner.CreateAsync(relativePath, text, hasUtf8Bom, cancellationToken);

        public Task<IReadOnlyList<string>> ListMarkdownFilesAsync(
            CancellationToken cancellationToken = default) =>
            inner.ListMarkdownFilesAsync(cancellationToken);

        public Task<IReadOnlyList<string>> ListFilesAsync(
            string relativeDirectory,
            string searchPattern,
            CancellationToken cancellationToken = default) =>
            inner.ListFilesAsync(relativeDirectory, searchPattern, cancellationToken);

        public Task<bool> DeleteAsync(
            string relativePath,
            string? expectedRevision,
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(relativePath, expectedRevision, cancellationToken);

        public string ResolveSafePath(string relativePath) => inner.ResolveSafePath(relativePath);
    }

    private static async Task WaitForFeedIdleAsync(FeedViewModel feed)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (feed.IsBusy && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        await Assert.That(feed.IsBusy).IsFalse();
    }

    private static async Task WaitForReviewSelectionAsync(FeedViewModel feed)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while ((feed.IsBusy || feed.CurrentReview is null) && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        await Assert.That(feed.IsBusy).IsFalse();
        await Assert.That(feed.CurrentReview).IsNotNull();
    }
}
