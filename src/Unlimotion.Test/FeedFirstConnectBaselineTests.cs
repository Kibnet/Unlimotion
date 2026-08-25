using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Unlimotion.Notes.Daily;
using Unlimotion.Notes.Identity;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Test;

public class FeedFirstConnectBaselineTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    [Test]
    public async Task EmptyStartSnapshotPersistsDurableBoundaryWithoutEmptyManifest()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var service = new FirstConnectBootstrapService(vault, new MarkdownDocumentParser());

        var result = await service.CreateOrResumeAsync(
            "vault1",
            "empty-bootstrap",
            ["Ежедневные/2026-08-20.md"]);
        var persistedArtifacts = await vault.ListFilesAsync(".unlimotion/review/bootstrap", "*.json");
        var manifests = await vault.ListFilesAsync(".unlimotion/review/bootstrap", "manifest.json");
        var safeComplete = await service.FindSafeCompleteAsync("vault1");

        await Assert.That(result.Manifest.State).IsEqualTo("complete");
        await Assert.That(result.Manifest.Files).IsEmpty();
        await Assert.That(result.IndexedFiles).IsZero();
        await Assert.That(result.PendingCheckboxes).IsZero();
        await Assert.That(result.ReusedExisting).IsFalse();
        await Assert.That(persistedArtifacts).HasSingleItem();
        await Assert.That(persistedArtifacts.Single())
            .StartsWith(".unlimotion/review/bootstrap/empty/vault1-");
        await Assert.That((await vault.ReadAsync(persistedArtifacts.Single()))!.Text)
            .Contains("\"dailyFileNameFormat\": \"yyyy-MM-dd\"");
        await Assert.That(manifests).IsEmpty();
        await Assert.That(safeComplete).IsNotNull();
        await Assert.That(safeComplete!.ReusedExisting).IsTrue();
    }

    [Test]
    public async Task EmptyFinalRescanPersistsBoundaryWithoutAnEmptyRecoveryManifest()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var service = new FirstConnectBootstrapService(vault, new MarkdownDocumentParser());
        var startDocument = new BootstrapStartDocument(
            "Ежедневные/2026-08-20.md",
            "frozen-revision",
            "История до удаления\n");

        var result = await service.CreateOrResumeAsync(
            "vault1",
            "deleted-before-bootstrap",
            [startDocument]);
        var manifests = await vault.ListFilesAsync(".unlimotion/review/bootstrap", "manifest.json");
        var emptyMarkers = await vault.ListFilesAsync(".unlimotion/review/bootstrap/empty", "*.json");

        await Assert.That(result.Manifest.State).IsEqualTo("complete");
        await Assert.That(result.IndexedFiles).IsZero();
        await Assert.That(result.Fingerprints).IsEmpty();
        await Assert.That(manifests).IsEmpty();
        await Assert.That(emptyMarkers).HasSingleItem();
    }

    [Test]
    public async Task EmptyFirstConnectBoundaryKeepsLaterContentOutOfBaseline()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var service = new FirstConnectBootstrapService(vault, new MarkdownDocumentParser());
        const string dailyPath = "Ежедневные/2026-08-20.md";

        var empty = await service.CreateOrResumeAsync("vault1", "empty-bootstrap", [dailyPath]);
        await vault.CreateAsync(dailyPath, "Мысль после первого подключения\n");
        var recovered = await service.FindSafeCompleteAsync("vault1");
        var resumed = await service.CreateOrResumeAsync("vault1", "another-device", [dailyPath]);

        using (Assert.Multiple())
        {
            await Assert.That(empty.Fingerprints).IsEmpty();
            await Assert.That(recovered).IsNotNull();
            await Assert.That(recovered!.Fingerprints).IsEmpty();
            await Assert.That(resumed.ReusedExisting).IsTrue();
            await Assert.That(resumed.Fingerprints).IsEmpty();
        }
    }

    [Test]
    public async Task MissingLayoutInCurrentEmptyBoundaryKeepsLaterContentOutOfBaseline()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var service = new FirstConnectBootstrapService(vault, new MarkdownDocumentParser());
        const string dailyPath = "Ежедневные/2026-08-20.md";

        await service.CreateOrResumeAsync("vault1", "empty-bootstrap", [dailyPath]);
        var markerPath = (await vault.ListFilesAsync(".unlimotion/review/bootstrap/empty", "*.json"))
            .Single();
        var marker = await vault.ReadAsync(markerPath);
        await vault.WriteAsync(
            markerPath,
            "{\"schemaVersion\":1,\"vaultId\":\"vault1\",\"operationId\":\"empty-bootstrap\",\"completedAt\":\"2026-08-25T00:00:00+00:00\"}\n",
            marker!.Revision);
        await vault.CreateAsync(dailyPath, "Мысль после первого подключения\n");

        var recovered = await service.FindSafeCompleteAsync("vault1");
        var resumed = await service.CreateOrResumeAsync("vault1", "another-device", [dailyPath]);

        using (Assert.Multiple())
        {
            await Assert.That(recovered).IsNotNull();
            await Assert.That(recovered!.Fingerprints).IsEmpty();
            await Assert.That(resumed.ReusedExisting).IsTrue();
            await Assert.That(resumed.Fingerprints).IsEmpty();
        }
    }

    [Test]
    public async Task EmptyDefaultLayoutBoundaryDoesNotHideExistingDottedLayoutHistory()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var parser = new MarkdownDocumentParser();
        var defaultLayout = new FirstConnectBootstrapService(vault, parser);
        const string defaultPath = "Ежедневные/2026-08-20.md";
        const string dottedPath = "Ежедневные/2026.08.20.md";

        await defaultLayout.CreateOrResumeAsync("vault1", "empty-default", [defaultPath]);
        await vault.CreateAsync(dottedPath, "История точечного формата\n");

        var dottedLayout = new FirstConnectBootstrapService(
            vault,
            parser,
            DailyNoteNaming.Create("yyyy.MM.dd"));
        var inheritedBoundary = await dottedLayout.FindSafeCompleteAsync("vault1");
        var dottedBootstrap = await dottedLayout.CreateOrResumeAsync("vault1", "dotted-history", [dottedPath]);

        using (Assert.Multiple())
        {
            await Assert.That(inheritedBoundary).IsNull();
            await Assert.That(dottedBootstrap.ReusedExisting).IsFalse();
            await Assert.That(dottedBootstrap.IndexedFiles).IsEqualTo(1);
            await Assert.That(dottedBootstrap.Fingerprints.Count(static value => value.BaselineKept)).IsEqualTo(1);
        }
    }

    [Test]
    public async Task DottedLayoutKeepsSchemaV1AndSkipsDefaultLayoutManifest()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string defaultPath = "Ежедневные/2026-08-20.md";
        const string dottedPath = "Ежедневные/2026.08.20.md";
        await vault.CreateAsync(defaultPath, "Дефисная история\n");
        await vault.CreateAsync(dottedPath, "Точечная история\n");
        var parser = new MarkdownDocumentParser();
        var defaultLayout = new FirstConnectBootstrapService(vault, parser);
        await defaultLayout.CreateOrResumeAsync("vault1", "legacy-layout", [defaultPath]);

        var dottedLayout = new FirstConnectBootstrapService(
            vault,
            parser,
            DailyNoteNaming.Create("yyyy.MM.dd"));
        var noApplicableManifest = await dottedLayout.FindSafeCompleteAsync("vault1");
        var dotted = await dottedLayout.CreateOrResumeAsync("vault1", "dotted-layout", [dottedPath]);
        var applicable = await dottedLayout.FindSafeCompleteAsync("vault1");

        await Assert.That(noApplicableManifest).IsNull();
        await Assert.That(dotted.Manifest.SchemaVersion).IsEqualTo(1);
        await Assert.That(dotted.Manifest.Files).HasSingleItem();
        await Assert.That(dotted.Manifest.Files[0].RelativePath).IsEqualTo(dottedPath);
        await Assert.That(applicable).IsNotNull();
        await Assert.That(applicable!.Manifest.OperationId).IsEqualTo("dotted-layout");
    }

    [Test]
    public async Task ExistingOrdinaryBlocksAreBaselineButUnfinishedCheckboxesStayPending()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string daily = "Старая мысль\n\n- [ ] Сделать\n- [x] Готово\n  - [ ] Незавершённый ребёнок\n";
        await vault.CreateAsync("Ежедневные/2026-08-20.md", daily);
        var service = new FirstConnectBootstrapService(vault, new MarkdownDocumentParser());

        var result = await service.CreateOrResumeAsync("vault1", "bootstrap1", ["Ежедневные/2026-08-20.md"]);

        await Assert.That(result.IndexedFiles).IsEqualTo(1);
        await Assert.That(result.PendingCheckboxes).IsEqualTo(2);
        await Assert.That(result.Fingerprints.Count(value => value.BaselineKept)).IsEqualTo(1);
        await Assert.That(result.Fingerprints.Count(value => !value.BaselineKept)).IsEqualTo(2);
        await Assert.That((await vault.ReadAsync("Ежедневные/2026-08-20.md"))!.Text).IsEqualTo(daily);
    }

    [Test]
    public async Task CompletedManifestIsValidatedAndReusedWithoutRescanningNewFiles()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        await vault.CreateAsync("Ежедневные/2026-08-20.md", "Старое\n");
        var service = new FirstConnectBootstrapService(vault, new MarkdownDocumentParser());
        var first = await service.CreateOrResumeAsync("vault1", "bootstrap1", ["Ежедневные/2026-08-20.md"]);
        await vault.CreateAsync("Ежедневные/2026-08-21.md", "Новое после snapshot\n");

        var reused = await service.CreateOrResumeAsync(
            "vault1",
            "bootstrap1",
            ["Ежедневные/2026-08-20.md", "Ежедневные/2026-08-21.md"]);

        await Assert.That(first.ReusedExisting).IsFalse();
        await Assert.That(reused.ReusedExisting).IsTrue();
        await Assert.That(reused.IndexedFiles).IsEqualTo(1);
        await Assert.That(reused.Fingerprints.Any(value => value.RelativePath.EndsWith("2026-08-21.md", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task CorruptedOrOutOfOrderBatchNeverBecomesValidBaseline()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        await vault.CreateAsync("Ежедневные/2026-08-20.md", "Старое\n");
        var service = new FirstConnectBootstrapService(vault, new MarkdownDocumentParser());
        var result = await service.CreateOrResumeAsync("vault1", "bootstrap1", ["Ежедневные/2026-08-20.md"]);
        var batchPath = result.Manifest.Files.Single().BatchPath;
        var batch = await vault.ReadAsync(batchPath);
        await vault.WriteAsync(batchPath, "[]\n", batch!.Revision);

        var failure = await NotesTestSupport.CaptureAsync<InvalidDataException>(() =>
            service.FindValidCompleteAsync("vault1", ["bootstrap1"]));

        await Assert.That(failure.Message.Contains("invalid hash", StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    [Test]
    public async Task MatchingBatchStagedBeforeStartManifestIsReusedAfterCrash()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        await vault.CreateAsync("Ежедневные/2026-08-20.md", "Старое\n");
        var service = new FirstConnectBootstrapService(vault, new MarkdownDocumentParser());
        await service.CreateOrResumeAsync("vault1", "bootstrap1", ["Ежедневные/2026-08-20.md"]);
        File.Delete(vault.ResolveSafePath(".unlimotion/review/bootstrap/bootstrap1/start.json"));
        File.Delete(vault.ResolveSafePath(".unlimotion/review/bootstrap/bootstrap1/manifest.json"));

        var resumed = await service.CreateOrResumeAsync("vault1", "bootstrap1", ["Ежедневные/2026-08-20.md"]);

        await Assert.That(resumed.Manifest.State).IsEqualTo("complete");
        await Assert.That(resumed.IndexedFiles).IsEqualTo(1);
    }

    [Test]
    public async Task FinalRescanKeepsPostSnapshotEditPendingBeforeCompletionManifest()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string path = "Ежедневные/2026-08-20.md";
        var firstRead = await vault.CreateAsync(path, "Старое значение\n");
        var frozen = new BootstrapStartDocument(path, firstRead.Revision, "Старое значение\n");
        await vault.WriteAsync(path, "Новое после start snapshot\n", firstRead.Revision);
        var service = new FirstConnectBootstrapService(vault, new MarkdownDocumentParser());

        var result = await service.CreateOrResumeAsync("vault1", "bootstrap-frozen", [frozen]);
        var currentHash = new MarkdownDocumentParser().Parse("Новое после start snapshot\n")
            .Blocks.Single(static block => block.IsContent)
            .ContentHash;

        var current = result.Fingerprints.Single(value => value.ContentHash == currentHash);
        await Assert.That(current.BaselineKept).IsFalse();
        await Assert.That(result.Manifest.Files.Single().StartRevision).IsNotEqualTo(firstRead.Revision);
        await Assert.That(result.Manifest.RecoveryOfOperationId).IsEqualTo("bootstrap-frozen");
        await Assert.That(result.Manifest.RecoveryMode).IsEqualTo("final-rescan-reconciliation");
    }

    [Test]
    public async Task MismatchedOrphanBatchRecoversBySafeIntersectionWithoutOverwritingTheOrphan()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string path = "Ежедневные/2026-08-20.md";
        var originalDocument = await vault.CreateAsync(
            path,
            "Стабильная мысль\n\nСтабильный сосед\n\nИзменяемая мысль\n\n- [ ] Сделать\n");
        var service = new FirstConnectBootstrapService(vault, new MarkdownDocumentParser());
        var interrupted = await service.CreateOrResumeAsync("vault1", "deterministic-op", [path]);
        var orphanPath = interrupted.Manifest.Files.Single().BatchPath;
        var orphan = await vault.ReadAsync(orphanPath);
        File.Delete(vault.ResolveSafePath(".unlimotion/review/bootstrap/deterministic-op/start.json"));
        File.Delete(vault.ResolveSafePath(".unlimotion/review/bootstrap/deterministic-op/manifest.json"));
        await vault.WriteAsync(
            path,
            "Стабильная мысль\n\nСтабильный сосед\n\nИзменённая мысль\n\nНовая мысль\n\n- [ ] Сделать\n",
            originalDocument.Revision);

        var recovered = await service.CreateOrResumeAsync("vault1", "deterministic-op", [path]);
        var orphanAfterRecovery = await vault.ReadAsync(orphanPath);
        var stableHash = new MarkdownDocumentParser().Parse("Стабильная мысль\n")
            .Blocks.Single(static block => block.IsContent)
            .ContentHash;

        await Assert.That(recovered.Manifest.OperationId).IsNotEqualTo("deterministic-op");
        await Assert.That(recovered.Manifest.RecoveryOfOperationId).IsEqualTo("deterministic-op");
        await Assert.That(recovered.Manifest.RecoveryMode).IsEqualTo("orphan-safe-intersection");
        await Assert.That(recovered.Fingerprints.Single(value => value.ContentHash == stableHash).BaselineKept).IsTrue();
        await Assert.That(recovered.Fingerprints.Count(static value => value.BaselineKept)).IsEqualTo(1);
        await Assert.That(recovered.PendingCheckboxes).IsEqualTo(1);
        await Assert.That(orphanAfterRecovery!.Text).IsEqualTo(orphan!.Text);
        await Assert.That(orphanAfterRecovery.Revision).IsEqualTo(orphan.Revision);

        File.Delete(vault.ResolveSafePath(
            $".unlimotion/review/bootstrap/{recovered.Manifest.OperationId}/start.json"));
        File.Delete(vault.ResolveSafePath(
            $".unlimotion/review/bootstrap/{recovered.Manifest.OperationId}/manifest.json"));

        var resumedRecovery = await service.CreateOrResumeAsync("vault1", "deterministic-op", [path]);
        var reusedRecovery = await service.CreateOrResumeAsync("vault1", "deterministic-op", [path]);

        await Assert.That(resumedRecovery.Manifest.OperationId).IsEqualTo(recovered.Manifest.OperationId);
        await Assert.That(resumedRecovery.Fingerprints.Count(static value => value.BaselineKept)).IsEqualTo(1);
        await Assert.That(reusedRecovery.Manifest.OperationId).IsEqualTo(recovered.Manifest.OperationId);
        await Assert.That(reusedRecovery.ReusedExisting).IsTrue();
    }

    [Test]
    public async Task UnprovableOrphanBatchFallsBackToAllPendingAndDoesNotBlockOnboarding()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string path = "Ежедневные/2026-08-20.md";
        await vault.CreateAsync(path, "История\n\n- [ ] Сделать\n");
        var service = new FirstConnectBootstrapService(vault, new MarkdownDocumentParser());
        var interrupted = await service.CreateOrResumeAsync("vault1", "corrupt-orphan", [path]);
        var orphanPath = interrupted.Manifest.Files.Single().BatchPath;
        var orphan = await vault.ReadAsync(orphanPath);
        File.Delete(vault.ResolveSafePath(".unlimotion/review/bootstrap/corrupt-orphan/start.json"));
        File.Delete(vault.ResolveSafePath(".unlimotion/review/bootstrap/corrupt-orphan/manifest.json"));
        await vault.WriteAsync(orphanPath, "{not-json\n", orphan!.Revision);

        var recovered = await service.CreateOrResumeAsync("vault1", "corrupt-orphan", [path]);
        var preservedOrphan = await vault.ReadAsync(orphanPath);

        await Assert.That(recovered.Manifest.RecoveryOfOperationId).IsEqualTo("corrupt-orphan");
        await Assert.That(recovered.Fingerprints).IsNotEmpty();
        await Assert.That(recovered.Fingerprints.All(static value => !value.BaselineKept)).IsTrue();
        await Assert.That(recovered.PendingCheckboxes).IsEqualTo(1);
        await Assert.That(preservedOrphan!.Text).IsEqualTo("{not-json\n");
    }

    [Test]
    public async Task InvalidCompleteManifestIsPreservedAndRecoveredWithoutBlockingOnboarding()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string path = "Ежедневные/2026-08-20.md";
        await vault.CreateAsync(path, "История\n\n- [ ] Сделать\n");
        var service = new FirstConnectBootstrapService(vault, new MarkdownDocumentParser());
        var interrupted = await service.CreateOrResumeAsync("vault1", "invalid-complete", [path]);
        var invalidManifest = interrupted.Manifest with
        {
            Files = [interrupted.Manifest.Files.Single() with
            {
                BaselineCount = interrupted.Manifest.Files.Single().BaselineCount + 1
            }]
        };
        await RewriteManifestAsync(vault, invalidManifest);
        var invalidDocument = await vault.ReadAsync(
            ".unlimotion/review/bootstrap/invalid-complete/manifest.json");

        var recovered = await service.CreateOrResumeAsync("vault1", "invalid-complete", [path]);
        var invalidDocumentAfterRecovery = await vault.ReadAsync(
            ".unlimotion/review/bootstrap/invalid-complete/manifest.json");

        await Assert.That(recovered.Manifest.OperationId).IsNotEqualTo("invalid-complete");
        await Assert.That(recovered.Manifest.RecoveryOfOperationId).IsEqualTo("invalid-complete");
        await Assert.That(recovered.Fingerprints.Count(static value => value.BaselineKept)).IsEqualTo(1);
        await Assert.That(recovered.PendingCheckboxes).IsEqualTo(1);
        await Assert.That(invalidDocumentAfterRecovery!.Text).IsEqualTo(invalidDocument!.Text);
        await Assert.That(invalidDocumentAfterRecovery.Revision).IsEqualTo(invalidDocument.Revision);
    }

    [Test]
    [Arguments("operation-directory")]
    [Arguments("non-daily-path")]
    [Arguments("noncanonical-batch-path")]
    [Arguments("empty-file-list")]
    [Arguments("duplicate-file-entry")]
    [Arguments("baseline-count")]
    [Arguments("pending-count")]
    [Arguments("batch-revision")]
    [Arguments("fingerprint-path")]
    [Arguments("duplicate-fingerprint")]
    public async Task CompleteManifestAndBatchesRejectNonCanonicalOrInconsistentData(string corruption)
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string path = "Ежедневные/2026-08-20.md";
        await vault.CreateAsync(path, "История\n\n- [ ] Сделать\n");
        var service = new FirstConnectBootstrapService(vault, new MarkdownDocumentParser());
        var result = await service.CreateOrResumeAsync("vault1", "strict-op", [path]);
        var manifest = result.Manifest;
        var operationToRead = "strict-op";

        switch (corruption)
        {
            case "operation-directory":
                operationToRead = "wrong-directory";
                await vault.CreateAsync(
                    ".unlimotion/review/bootstrap/wrong-directory/manifest.json",
                    Serialize(manifest));
                break;
            case "non-daily-path":
                manifest = manifest with
                {
                    Files = [manifest.Files.Single() with { RelativePath = "Темы/2026-08-20.md" }]
                };
                await RewriteManifestAsync(vault, manifest);
                break;
            case "noncanonical-batch-path":
                manifest = manifest with
                {
                    Files = [manifest.Files.Single() with
                    {
                        BatchPath = ".unlimotion/review/bootstrap/strict-op/files/alias.json"
                    }]
                };
                await RewriteManifestAsync(vault, manifest);
                break;
            case "empty-file-list":
                manifest = manifest with { Files = [] };
                await RewriteManifestAsync(vault, manifest);
                break;
            case "duplicate-file-entry":
                manifest = manifest with { Files = [manifest.Files.Single(), manifest.Files.Single()] };
                await RewriteManifestAsync(vault, manifest);
                break;
            case "baseline-count":
                manifest = manifest with
                {
                    Files = [manifest.Files.Single() with
                    {
                        BaselineCount = manifest.Files.Single().BaselineCount + 1
                    }]
                };
                await RewriteManifestAsync(vault, manifest);
                break;
            case "pending-count":
                manifest = manifest with
                {
                    Files = [manifest.Files.Single() with
                    {
                        PendingCheckboxCount = manifest.Files.Single().PendingCheckboxCount + 1
                    }]
                };
                await RewriteManifestAsync(vault, manifest);
                break;
            case "batch-revision":
                manifest = await RewriteBatchEnvelopeAsync(
                    vault,
                    manifest,
                    batch => batch with { StartRevision = "another-revision" });
                await RewriteManifestAsync(vault, manifest);
                break;
            case "fingerprint-path":
                manifest = await RewriteBatchAsync(
                    vault,
                    manifest,
                    values => [values[0] with { RelativePath = "Ежедневные/2026-08-21.md" }, .. values.Skip(1)]);
                await RewriteManifestAsync(vault, manifest);
                break;
            case "duplicate-fingerprint":
                manifest = await RewriteBatchAsync(
                    vault,
                    manifest,
                    values => [.. values, values[0]]);
                manifest = manifest with
                {
                    Files = [manifest.Files.Single() with
                    {
                        BaselineCount = manifest.Files.Single().BaselineCount + 1
                    }]
                };
                await RewriteManifestAsync(vault, manifest);
                break;
            default:
                throw new InvalidOperationException($"Unknown corruption case '{corruption}'.");
        }

        _ = await NotesTestSupport.CaptureAsync<InvalidDataException>(() =>
            service.FindValidCompleteAsync("vault1", [operationToRead]));
    }

    [Test]
    public async Task StrictValidationStillReturnsOnlyTheConcurrentSafeIntersection()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string commonPath = "Ежедневные/2026-08-19.md";
        const string firstOnlyPath = "Ежедневные/2026-08-20.md";
        const string secondOnlyPath = "Ежедневные/2026-08-21.md";
        await vault.CreateAsync(commonPath, "Общая история\n");
        await vault.CreateAsync(firstOnlyPath, "Первая snapshot\n");
        var service = new FirstConnectBootstrapService(vault, new MarkdownDocumentParser());
        await service.CreateOrResumeAsync("vault1", "device-a", [commonPath, firstOnlyPath]);
        await vault.CreateAsync(secondOnlyPath, "Вторая snapshot\n");
        await service.CreateOrResumeAsync("vault1", "device-b", [commonPath, secondOnlyPath]);

        var intersection = await service.FindSafeCompleteAsync("vault1");

        await Assert.That(intersection).IsNotNull();
        await Assert.That(intersection!.IndexedFiles).IsEqualTo(1);
        await Assert.That(intersection.Fingerprints.Count(static value => value.BaselineKept)).IsEqualTo(1);
        await Assert.That(intersection.Fingerprints.Single(static value => value.BaselineKept).RelativePath)
            .IsEqualTo(commonPath);
    }

    private static async Task RewriteManifestAsync(FileNoteVault vault, BootstrapManifest manifest)
    {
        var path = $".unlimotion/review/bootstrap/{manifest.OperationId}/manifest.json";
        var current = await vault.ReadAsync(path);
        await vault.WriteAsync(path, Serialize(manifest), current!.Revision);
    }

    private static async Task<BootstrapManifest> RewriteBatchAsync(
        FileNoteVault vault,
        BootstrapManifest manifest,
        Func<IReadOnlyList<BootstrapFingerprint>, IReadOnlyList<BootstrapFingerprint>> transform)
    {
        return await RewriteBatchEnvelopeAsync(
            vault,
            manifest,
            batch => batch with { Fingerprints = transform(batch.Fingerprints) });
    }

    private static async Task<BootstrapManifest> RewriteBatchEnvelopeAsync(
        FileNoteVault vault,
        BootstrapManifest manifest,
        Func<BootstrapFileBatch, BootstrapFileBatch> transform)
    {
        var entry = manifest.Files.Single();
        var batch = await vault.ReadAsync(entry.BatchPath);
        var payload = JsonSerializer.Deserialize<BootstrapFileBatch>(batch!.Text, JsonOptions)!;
        var rewritten = JsonSerializer.Serialize(transform(payload), JsonOptions) + "\n";
        await vault.WriteAsync(entry.BatchPath, rewritten, batch.Revision);
        return manifest with
        {
            Files = [entry with { BatchHash = Hash(rewritten) }]
        };
    }

    private static string Serialize(BootstrapManifest manifest) =>
        JsonSerializer.Serialize(manifest, JsonOptions) + "\n";

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
