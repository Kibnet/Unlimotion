using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Notes.Identity;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Review;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Test;

public class VaultIdentityAndBootstrapSyncTests
{
    [Test]
    public async Task IdentityCreateIsStableAcrossReconnectAndRootRelocation()
    {
        using var parent = new TempNotesDirectory();
        var original = Path.Combine(parent.Path, "original");
        Directory.CreateDirectory(original);
        var first = await new VaultIdentityService(new FileNoteVault(original)).GetOrCreateAsync();
        var relocated = Path.Combine(parent.Path, "relocated");
        Directory.Move(original, relocated);

        var second = await new VaultIdentityService(new FileNoteVault(relocated)).GetOrCreateAsync();

        await Assert.That(first.SchemaVersion).IsEqualTo(1);
        await Assert.That(second.VaultId).IsEqualTo(first.VaultId);
    }

    [Test]
    public async Task SameVaultIdentityCannotAttachAtTwoLocalRoots()
    {
        using var first = new TempNotesDirectory();
        using var second = new TempNotesDirectory();
        var registry = new VaultRootRegistry();
        registry.Attach("vault1", first.Path);

        var conflict = await NotesTestSupport.Capture<InvalidOperationException>(() => registry.Attach("vault1", second.Path));

        await Assert.That(conflict.Message.Contains("another local root", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task SameRootReferenceCountPreventsPrematureDuplicateRootRelease()
    {
        using var first = new TempNotesDirectory();
        using var second = new TempNotesDirectory();
        var registry = new VaultRootRegistry();
        registry.Attach("vault1", first.Path);
        registry.Attach("vault1", first.Path);
        registry.Detach("vault1", first.Path);

        _ = await NotesTestSupport.Capture<InvalidOperationException>(() => registry.Attach("vault1", second.Path));
        registry.Detach("vault1", first.Path);
        registry.Attach("vault1", second.Path);
        var attachedAfterFinalDetach = true;

        await Assert.That(attachedAfterFinalDetach).IsTrue();
    }

    [Test]
    public async Task AnotherDeviceCanFindAndReuseValidatedCompleteManifest()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        await vault.CreateAsync("Ежедневные/2026-08-20.md", "- [ ] Pending\nOld text\n");
        var firstDevice = new FirstConnectBootstrapService(vault, new MarkdownDocumentParser());
        await firstDevice.CreateOrResumeAsync("vault1", "deviceAop", ["Ежедневные/2026-08-20.md"]);

        var secondDevice = new FirstConnectBootstrapService(new FileNoteVault(directory.Path), new MarkdownDocumentParser());
        var reused = await secondDevice.FindValidCompleteAsync("vault1", ["incomplete-op", "deviceAop"]);

        await Assert.That(reused).IsNotNull();
        await Assert.That(reused!.ReusedExisting).IsTrue();
        await Assert.That(reused.PendingCheckboxes).IsEqualTo(1);
        await Assert.That(reused.Fingerprints.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ConcurrentCompleteManifestsReuseOnlyTheirSafeIntersection()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string commonPath = "Ежедневные/2026-08-19.md";
        const string firstOnlyPath = "Ежедневные/2026-08-20.md";
        const string secondOnlyPath = "Ежедневные/2026-08-21.md";
        await vault.CreateAsync(commonPath, "Общая история\n");
        await vault.CreateAsync(firstOnlyPath, "Только первая snapshot\n");
        var service = new FirstConnectBootstrapService(vault, new MarkdownDocumentParser());
        await service.CreateOrResumeAsync("vault1", "device-a", [commonPath, firstOnlyPath]);
        await vault.CreateAsync(secondOnlyPath, "Только вторая snapshot\n");
        await service.CreateOrResumeAsync("vault1", "device-b", [commonPath, secondOnlyPath]);

        var intersection = await service.FindSafeCompleteAsync("vault1");

        await Assert.That(intersection).IsNotNull();
        await Assert.That(intersection!.ReusedExisting).IsTrue();
        await Assert.That(intersection.Fingerprints.Count(value => value.BaselineKept)).IsEqualTo(1);
        await Assert.That(intersection.Fingerprints.Single(value => value.BaselineKept).RelativePath)
            .IsEqualTo(commonPath);
        await Assert.That(intersection.IndexedFiles).IsEqualTo(1);
    }

    [Test]
    public async Task DivergentIdentityIsRejectedInsteadOfMerged()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        await vault.CreateAsync(VaultIdentityService.ManifestPath, "{\"schemaVersion\":1,\"vaultId\":\"vault-a\"}\n");
        var identity = await new VaultIdentityService(vault).GetOrCreateAsync();

        await Assert.That(identity.VaultId).IsEqualTo("vault-a");

        var invalid = await NotesTestSupport.Capture<InvalidDataException>(() =>
            VaultIdentityService.Parse("{\"schemaVersion\":1,\"vaultId\":\"\"}"));
        await Assert.That(invalid.Message.Length > 0).IsTrue();
    }

    [Test]
    public async Task DivergentIdentityPreservesBothBranchesBeforeResolution()
    {
        using var vaultDirectory = new TempNotesDirectory();
        using var recoveryDirectory = new TempNotesDirectory();
        var vault = new FileNoteVault(vaultDirectory.Path);
        await vault.CreateAsync(
            VaultIdentityService.ManifestPath,
            "{\"schemaVersion\":1,\"vaultId\":\"vault-current\"}\n");
        await vault.CreateAsync(".unlimotion/review/current.json", "{\"branch\":\"current\"}\n");
        var acceptedLocator = new BlockLocator(
            "Ежедневные/2026-08-24.md", null, MarkdownBlockKind.Paragraph, "accepted", 0);
        var accepted = new VaultIdentityBranchSnapshot(
            "vault-accepted",
            "{\"schemaVersion\":1,\"vaultId\":\"vault-accepted\"}\n",
            "accepted-revision",
            new Dictionary<string, string> { [".unlimotion/review/accepted.json"] = "accepted" },
            [acceptedLocator]);
        var store = new FileVaultIdentityConflictStore(recoveryDirectory.Path);
        var coordinator = new VaultIdentityConflictCoordinator(vault, store);

        var conflict = await coordinator.DetectAndPreserveAsync(accepted);
        var persisted = await store.ListAsync();

        await Assert.That(conflict).IsNotNull();
        await Assert.That(persisted).HasSingleItem();
        await Assert.That(persisted[0].AcceptedBranch.VaultId).IsEqualTo("vault-accepted");
        await Assert.That(persisted[0].CurrentRootBranch.VaultId).IsEqualTo("vault-current");
        await Assert.That(persisted[0].CurrentRootBranch.ReviewArtifacts)
            .ContainsKey(".unlimotion/review/current.json");
    }

    [Test]
    public async Task RepeatedIdentityWatcherEventReusesTheImmutableBundle()
    {
        using var vaultDirectory = new TempNotesDirectory();
        using var recoveryDirectory = new TempNotesDirectory();
        var vault = new FileNoteVault(vaultDirectory.Path);
        await vault.CreateAsync(
            VaultIdentityService.ManifestPath,
            "{\"schemaVersion\":1,\"vaultId\":\"vault-current\"}\n");
        var store = new FileVaultIdentityConflictStore(recoveryDirectory.Path);
        var coordinator = new VaultIdentityConflictCoordinator(vault, store);

        var first = await coordinator.DetectAndPreserveAsync(CreateAcceptedIdentityBranch());
        var second = await coordinator.DetectAndPreserveAsync(CreateAcceptedIdentityBranch());

        await Assert.That(second!.ConflictId).IsEqualTo(first!.ConflictId);
        await Assert.That(await store.ListAsync()).HasSingleItem();
    }

    [Test]
    public async Task IdentityResolutionRechecksRevisionAndKeepsBundleOnDrift()
    {
        using var vaultDirectory = new TempNotesDirectory();
        using var recoveryDirectory = new TempNotesDirectory();
        var vault = new FileNoteVault(vaultDirectory.Path);
        var original = await vault.CreateAsync(
            VaultIdentityService.ManifestPath,
            "{\"schemaVersion\":1,\"vaultId\":\"vault-current\"}\n");
        var store = new FileVaultIdentityConflictStore(recoveryDirectory.Path);
        var coordinator = new VaultIdentityConflictCoordinator(vault, store);
        var conflict = await coordinator.DetectAndPreserveAsync(CreateAcceptedIdentityBranch());
        await vault.WriteAsync(
            VaultIdentityService.ManifestPath,
            "{\"schemaVersion\":1,\"vaultId\":\"vault-drifted\"}\n",
            original.Revision);

        _ = await NotesTestSupport.CaptureAsync<VaultRevisionConflictException>(() => coordinator.ResolveAsync(
            conflict!,
            VaultIdentityConflictResolution.UseCurrentRootIdentity));
        await Assert.That(await store.LoadAsync(conflict!.ConflictId)).IsNotNull();
    }

    [Test]
    public async Task CurrentRootIdentityCannotRebindWhileOldNamespaceHasPendingOperations()
    {
        using var vaultDirectory = new TempNotesDirectory();
        using var recoveryDirectory = new TempNotesDirectory();
        var vault = new FileNoteVault(vaultDirectory.Path);
        await vault.CreateAsync(
            VaultIdentityService.ManifestPath,
            "{\"schemaVersion\":1,\"vaultId\":\"vault-current\"}\n");
        var coordinator = new VaultIdentityConflictCoordinator(
            vault,
            new FileVaultIdentityConflictStore(recoveryDirectory.Path),
            new PendingIdentityRecoveryGuard());
        var conflict = await coordinator.DetectAndPreserveAsync(CreateAcceptedIdentityBranch());

        _ = await NotesTestSupport.CaptureAsync<InvalidOperationException>(() => coordinator.ResolveAsync(
            conflict!,
            VaultIdentityConflictResolution.UseCurrentRootIdentity));
    }

    [Test]
    public async Task IdentityRecoveryExposesAllThreeSafeOutcomesAndLosingLocators()
    {
        using var vaultDirectory = new TempNotesDirectory();
        using var recoveryDirectory = new TempNotesDirectory();
        var vault = new FileNoteVault(vaultDirectory.Path);
        await vault.CreateAsync(
            VaultIdentityService.ManifestPath,
            "{\"schemaVersion\":1,\"vaultId\":\"vault-current\"}\n");
        var coordinator = new VaultIdentityConflictCoordinator(
            vault,
            new FileVaultIdentityConflictStore(recoveryDirectory.Path));
        var conflict = await coordinator.DetectAndPreserveAsync(CreateAcceptedIdentityBranch());

        var useCurrent = await coordinator.ResolveAsync(
            conflict!,
            VaultIdentityConflictResolution.UseCurrentRootIdentity);
        var reconnect = await coordinator.ResolveAsync(
            conflict!,
            VaultIdentityConflictResolution.ReconnectAnotherRoot);
        var readOnly = await coordinator.ResolveAsync(
            conflict!,
            VaultIdentityConflictResolution.StayReadOnly);

        await Assert.That(useCurrent.ResolvedVaultId).IsEqualTo("vault-current");
        await Assert.That(useCurrent.SafePendingLocators).HasSingleItem();
        await Assert.That(reconnect.RequiresReconnect).IsTrue();
        await Assert.That(readOnly.IsReadOnly).IsTrue();
    }

    [Test]
    public async Task UseCurrentRootQuarantinesForeignReviewEventsAndAllowsReconnect()
    {
        using var vaultDirectory = new TempNotesDirectory();
        using var recoveryDirectory = new TempNotesDirectory();
        var vault = new FileNoteVault(vaultDirectory.Path);
        await vault.CreateAsync(
            VaultIdentityService.ManifestPath,
            "{\"schemaVersion\":1,\"vaultId\":\"vault-current\"}\n");
        var eventStore = new PortableReviewEventStore(vault);
        var locator = new BlockLocator(
            "Ежедневные/2026-08-24.md", null, MarkdownBlockKind.Paragraph, "content", 0);
        await eventStore.AppendAsync(new ReviewDecisionEvent(
            "vault-accepted", "foreign-event", new CausalEnvelope("old-device", 1, new Dictionary<string, long>()),
            DateTimeOffset.UtcNow, locator, ReviewDecision.Kept));
        await eventStore.AppendAsync(new ReviewDecisionEvent(
            "vault-current", "current-event", new CausalEnvelope("current-device", 1, new Dictionary<string, long>()),
            DateTimeOffset.UtcNow, locator with { ContentHash = "current" }, ReviewDecision.Kept));
        var coordinator = new VaultIdentityConflictCoordinator(
            vault,
            new FileVaultIdentityConflictStore(recoveryDirectory.Path));
        var conflict = await coordinator.DetectAndPreserveAsync(CreateAcceptedIdentityBranch());

        await coordinator.ResolveAsync(conflict!, VaultIdentityConflictResolution.UseCurrentRootIdentity);

        var reconnected = new FeedReviewSessionCoordinator(
            "vault-current", "reconnected-device", eventStore, new ReviewStateStore());
        await reconnected.InitializeAsync();
        var active = await eventStore.LoadAllAsync();
        var quarantined = await vault.ListFilesAsync(
            $".unlimotion/review/quarantine/{conflict!.ConflictId}",
            "*.json");
        var quarantinedEvent = await vault.ReadAsync(quarantined.Single());
        await Assert.That(active.Decisions).HasSingleItem();
        await Assert.That(active.Decisions[0].VaultId).IsEqualTo("vault-current");
        await Assert.That(quarantined).HasSingleItem();
        await Assert.That(quarantinedEvent!.Text).Contains("vault-accepted");
        await Assert.That(reconnected.State.DecisionEvents).HasSingleItem();
    }

    private static VaultIdentityBranchSnapshot CreateAcceptedIdentityBranch()
    {
        return new VaultIdentityBranchSnapshot(
            "vault-accepted",
            "{\"schemaVersion\":1,\"vaultId\":\"vault-accepted\"}\n",
            "accepted-revision",
            new Dictionary<string, string>(),
            [new BlockLocator(
                "Ежедневные/2026-08-24.md", null, MarkdownBlockKind.Paragraph, "accepted", 0)]);
    }

    private sealed class PendingIdentityRecoveryGuard : IVaultIdentityRecoveryGuard
    {
        public Task<bool> HasPendingOperationsAsync(
            string vaultId,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
