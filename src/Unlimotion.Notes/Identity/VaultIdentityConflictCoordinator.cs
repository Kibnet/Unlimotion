using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Unlimotion.Notes.Recovery;
using Unlimotion.Notes.Review;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Notes.Identity;

public sealed record VaultIdentityBranchSnapshot(
    string VaultId,
    string IdentityJson,
    string? IdentityRevision,
    IReadOnlyDictionary<string, string> ReviewArtifacts,
    IReadOnlyList<BlockLocator> ReviewLocators);

public sealed record VaultIdentityConflictBundle(
    int SchemaVersion,
    string ConflictId,
    VaultIdentityBranchSnapshot AcceptedBranch,
    VaultIdentityBranchSnapshot CurrentRootBranch,
    DateTimeOffset CreatedAt);

public enum VaultIdentityConflictResolution
{
    UseCurrentRootIdentity,
    ReconnectAnotherRoot,
    StayReadOnly
}

public sealed record VaultIdentityConflictResolutionResult(
    string ConflictId,
    VaultIdentityConflictResolution Resolution,
    string? ResolvedVaultId,
    bool RequiresReconnect,
    bool IsReadOnly,
    IReadOnlyList<BlockLocator> SafePendingLocators);

public interface IVaultIdentityConflictStore
{
    Task PreserveAsync(VaultIdentityConflictBundle bundle, CancellationToken cancellationToken = default);

    Task<VaultIdentityConflictBundle?> LoadAsync(string conflictId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VaultIdentityConflictBundle>> ListAsync(CancellationToken cancellationToken = default);
}

public interface IVaultIdentityRecoveryGuard
{
    Task<bool> HasPendingOperationsAsync(string vaultId, CancellationToken cancellationToken = default);
}

public sealed class NoPendingVaultIdentityRecoveryGuard : IVaultIdentityRecoveryGuard
{
    public Task<bool> HasPendingOperationsAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }
}

public sealed class FileVaultIdentityConflictStore(string appLocalRoot) : IVaultIdentityConflictStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public async Task PreserveAsync(
        VaultIdentityConflictBundle bundle,
        CancellationToken cancellationToken = default)
    {
        Validate(bundle);
        var path = Resolve(bundle.ConflictId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            var existing = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
            if (HasSamePayload(existing, bundle))
            {
                return;
            }

            throw new IOException("Vault identity conflict bundles are immutable and cannot be overwritten.");
        }

        await FileFeedDraftStore.AtomicJsonWriteAsync(path, bundle, cancellationToken).ConfigureAwait(false);
    }

    public async Task<VaultIdentityConflictBundle?> LoadAsync(
        string conflictId,
        CancellationToken cancellationToken = default)
    {
        var path = Resolve(conflictId);
        return File.Exists(path)
            ? await ReadAsync(path, cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async Task<IReadOnlyList<VaultIdentityConflictBundle>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var directory = ConflictDirectory;
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var result = new List<VaultIdentityConflictBundle>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(static value => value, StringComparer.Ordinal))
        {
            result.Add(await ReadAsync(path, cancellationToken).ConfigureAwait(false));
        }

        return result;
    }

    private string ConflictDirectory => Path.Combine(Path.GetFullPath(appLocalRoot), "identity-conflicts");

    private string Resolve(string conflictId)
    {
        if (string.IsNullOrWhiteSpace(conflictId)
            || conflictId.Any(static value => !char.IsAsciiLetterOrDigit(value) && value is not '-' and not '_'))
        {
            throw new ArgumentException("Conflict IDs must be safe path segments.", nameof(conflictId));
        }

        return Path.Combine(ConflictDirectory, conflictId + ".json");
    }

    private static async Task<VaultIdentityConflictBundle> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var bundle = await JsonSerializer.DeserializeAsync<VaultIdentityConflictBundle>(
                stream,
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("The vault identity conflict bundle is empty.");
        Validate(bundle);
        return bundle;
    }

    private static void Validate(VaultIdentityConflictBundle bundle)
    {
        if (bundle.SchemaVersion != 1
            || string.IsNullOrWhiteSpace(bundle.ConflictId)
            || string.IsNullOrWhiteSpace(bundle.AcceptedBranch.VaultId)
            || string.IsNullOrWhiteSpace(bundle.CurrentRootBranch.VaultId))
        {
            throw new InvalidDataException("The vault identity conflict bundle is invalid.");
        }
    }

    private static bool HasSamePayload(
        VaultIdentityConflictBundle left,
        VaultIdentityConflictBundle right) =>
        left.SchemaVersion == right.SchemaVersion
        && string.Equals(left.ConflictId, right.ConflictId, StringComparison.Ordinal)
        && HasSameBranch(left.AcceptedBranch, right.AcceptedBranch)
        && HasSameBranch(left.CurrentRootBranch, right.CurrentRootBranch);

    private static bool HasSameBranch(
        VaultIdentityBranchSnapshot left,
        VaultIdentityBranchSnapshot right) =>
        string.Equals(left.VaultId, right.VaultId, StringComparison.Ordinal)
        && string.Equals(left.IdentityJson, right.IdentityJson, StringComparison.Ordinal)
        && string.Equals(left.IdentityRevision, right.IdentityRevision, StringComparison.Ordinal)
        && left.ReviewArtifacts.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .SequenceEqual(right.ReviewArtifacts.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        && left.ReviewLocators.Select(static value => value.SemanticKey)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .SequenceEqual(
                right.ReviewLocators.Select(static value => value.SemanticKey)
                    .OrderBy(static value => value, StringComparer.Ordinal),
                StringComparer.Ordinal);
}

public sealed class VaultIdentityConflictCoordinator(
    INoteVault vault,
    IVaultIdentityConflictStore store,
    IVaultIdentityRecoveryGuard? recoveryGuard = null,
    TimeProvider? timeProvider = null)
{
    private readonly IVaultIdentityRecoveryGuard guard = recoveryGuard ?? new NoPendingVaultIdentityRecoveryGuard();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<VaultIdentityConflictBundle?> DetectAndPreserveAsync(
        VaultIdentityBranchSnapshot acceptedBranch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acceptedBranch);
        var identityDocument = await vault.ReadAsync(VaultIdentityService.ManifestPath, cancellationToken)
            .ConfigureAwait(false);
        if (identityDocument is null)
        {
            throw new InvalidDataException("The current root no longer contains a vault identity manifest.");
        }

        var currentIdentity = VaultIdentityService.Parse(identityDocument.Text);
        if (string.Equals(currentIdentity.VaultId, acceptedBranch.VaultId, StringComparison.Ordinal))
        {
            return null;
        }

        var reviewArtifacts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in await vault.ListFilesAsync(".unlimotion/review", "*.json", cancellationToken)
                     .ConfigureAwait(false))
        {
            var artifact = await vault.ReadAsync(path, cancellationToken).ConfigureAwait(false);
            if (artifact is not null)
            {
                reviewArtifacts[path] = artifact.Text;
            }
        }

        IReadOnlyList<BlockLocator> currentLocators;
        try
        {
            var portableReview = await new PortableReviewEventStore(vault).LoadAllAsync(cancellationToken)
                .ConfigureAwait(false);
            currentLocators = portableReview.Decisions
                .Select(static value => value.Input)
                .DistinctBy(static value => value.SemanticKey)
                .ToArray();
        }
        catch (InvalidDataException)
        {
            // The immutable raw branch above is still the recovery source of truth.
            // Malformed review data cannot be promoted automatically to safe locators.
            currentLocators = [];
        }

        var currentBranch = new VaultIdentityBranchSnapshot(
            currentIdentity.VaultId,
            identityDocument.Text,
            identityDocument.Revision,
            reviewArtifacts,
            currentLocators);
        var conflictId = CreateConflictId(
            acceptedBranch.VaultId,
            currentBranch.VaultId,
            currentBranch.IdentityRevision);
        var bundle = new VaultIdentityConflictBundle(
            1,
            conflictId,
            acceptedBranch,
            currentBranch,
            clock.GetUtcNow());
        await store.PreserveAsync(bundle, cancellationToken).ConfigureAwait(false);
        return bundle;
    }

    public async Task<VaultIdentityConflictResolutionResult> ResolveAsync(
        VaultIdentityConflictBundle conflict,
        VaultIdentityConflictResolution resolution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        var current = await vault.ReadAsync(VaultIdentityService.ManifestPath, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(current?.Revision, conflict.CurrentRootBranch.IdentityRevision, StringComparison.Ordinal))
        {
            throw new VaultRevisionConflictException(
                VaultIdentityService.ManifestPath,
                conflict.CurrentRootBranch.IdentityRevision,
                current?.Revision);
        }

        return resolution switch
        {
            VaultIdentityConflictResolution.UseCurrentRootIdentity =>
                await UseCurrentRootAsync(conflict, cancellationToken).ConfigureAwait(false),
            VaultIdentityConflictResolution.ReconnectAnotherRoot => new VaultIdentityConflictResolutionResult(
                conflict.ConflictId,
                resolution,
                null,
                RequiresReconnect: true,
                IsReadOnly: true,
                SafePendingLocators: conflict.AcceptedBranch.ReviewLocators),
            VaultIdentityConflictResolution.StayReadOnly => new VaultIdentityConflictResolutionResult(
                conflict.ConflictId,
                resolution,
                conflict.AcceptedBranch.VaultId,
                RequiresReconnect: false,
                IsReadOnly: true,
                SafePendingLocators: conflict.CurrentRootBranch.ReviewLocators),
            _ => throw new ArgumentOutOfRangeException(nameof(resolution))
        };
    }

    private async Task<VaultIdentityConflictResolutionResult> UseCurrentRootAsync(
        VaultIdentityConflictBundle conflict,
        CancellationToken cancellationToken)
    {
        if (await guard.HasPendingOperationsAsync(conflict.AcceptedBranch.VaultId, cancellationToken)
            .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The previous recovery namespace has pending operations and cannot be rebound yet.");
        }

        await new PortableReviewEventStore(vault)
            .QuarantineMismatchedVaultEventsAsync(
                conflict.CurrentRootBranch.VaultId,
                conflict.ConflictId,
                cancellationToken)
            .ConfigureAwait(false);

        return new VaultIdentityConflictResolutionResult(
            conflict.ConflictId,
            VaultIdentityConflictResolution.UseCurrentRootIdentity,
            conflict.CurrentRootBranch.VaultId,
            RequiresReconnect: false,
            IsReadOnly: false,
            SafePendingLocators: conflict.AcceptedBranch.ReviewLocators);
    }

    private static string CreateConflictId(string acceptedVaultId, string currentVaultId, string? currentRevision)
    {
        var raw = string.Join("\n", acceptedVaultId, currentVaultId, currentRevision ?? "<missing>");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }
}
