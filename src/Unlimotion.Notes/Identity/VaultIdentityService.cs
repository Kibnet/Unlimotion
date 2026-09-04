using System.Text.Json;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Notes.Identity;

public sealed record VaultIdentityManifest(int SchemaVersion, string VaultId);

public sealed class VaultIdentityService(INoteVault vault)
{
    public const string ManifestPath = ".unlimotion/vault.json";

    public async Task<VaultIdentityManifest> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var existing = await vault.ReadAsync(ManifestPath, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Parse(existing.Text);
        }

        var created = new VaultIdentityManifest(1, Guid.NewGuid().ToString("N"));
        var json = Serialize(created);
        try
        {
            await vault.CreateAsync(ManifestPath, json, cancellationToken: cancellationToken).ConfigureAwait(false);
            return created;
        }
        catch (VaultRevisionConflictException)
        {
            var winner = await vault.ReadAsync(ManifestPath, cancellationToken).ConfigureAwait(false)
                ?? throw new IOException("The vault identity appeared concurrently but could not be read.");
            return Parse(winner.Text);
        }
    }

    public static VaultIdentityManifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<VaultIdentityManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("The vault identity manifest is empty.");
        if (manifest.SchemaVersion != 1 || string.IsNullOrWhiteSpace(manifest.VaultId))
        {
            throw new InvalidDataException("Unsupported or invalid vault identity manifest.");
        }

        return manifest;
    }

    private static string Serialize(VaultIdentityManifest manifest) =>
        JsonSerializer.Serialize(manifest, JsonOptions) + "\n";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}

public sealed class VaultRootRegistry
{
    private readonly object sync = new();
    private readonly Dictionary<string, (string RootPath, int Attachments)> rootsByVaultId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HandoffReservation> handoffsByVaultId = new(StringComparer.Ordinal);

    public void Attach(string vaultId, string rootPath)
    {
        var canonical = CanonicalizeRootPath(rootPath);
        lock (sync)
        {
            if (handoffsByVaultId.ContainsKey(vaultId))
            {
                throw new InvalidOperationException(
                    $"Vault '{vaultId}' is being handed off between local roots.");
            }

            if (!rootsByVaultId.TryGetValue(vaultId, out var existing))
            {
                rootsByVaultId[vaultId] = (canonical, 1);
                return;
            }

            if (!AreEquivalentRootPaths(existing.RootPath, canonical))
            {
                throw new InvalidOperationException($"Vault '{vaultId}' is already attached at another local root.");
            }

            rootsByVaultId[vaultId] = (existing.RootPath, existing.Attachments + 1);
        }
    }

    /// <summary>
    /// Reserves an atomic relocation of exactly one active local attachment.
    /// Until the lease is committed or cancelled, no new attachment may join
    /// this vault identity. Generic detaches of the expected root are frozen;
    /// the owner must separately confirm that the old attachment has actually
    /// stopped before it can commit the handoff.
    /// </summary>
    public VaultRootHandoffLease BeginHandoff(
        string vaultId,
        string expectedCurrentRootPath,
        string nextRootPath)
    {
        var expectedCurrent = CanonicalizeRootPath(expectedCurrentRootPath);
        var next = CanonicalizeRootPath(nextRootPath);
        lock (sync)
        {
            if (handoffsByVaultId.ContainsKey(vaultId))
            {
                throw new InvalidOperationException(
                    $"Vault '{vaultId}' already has an active local root handoff.");
            }

            if (!rootsByVaultId.TryGetValue(vaultId, out var existing) ||
                !AreEquivalentRootPaths(existing.RootPath, expectedCurrent))
            {
                throw new InvalidOperationException(
                    $"Vault '{vaultId}' is not attached at the expected local root.");
            }

            if (existing.Attachments != 1)
            {
                throw new InvalidOperationException(
                    $"Vault '{vaultId}' cannot be handed off while it has multiple local attachments.");
            }

            var token = Guid.NewGuid();
            handoffsByVaultId[vaultId] = new HandoffReservation(token, expectedCurrent, next);
            return new VaultRootHandoffLease(this, vaultId, token);
        }
    }

    /// <summary>
    /// Moves a previously reserved single attachment from the expected root to
    /// its next root after its owner confirms that the old session detached.
    /// The lease is consumed and cannot be committed again.
    /// </summary>
    public void CommitHandoff(VaultRootHandoffLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (sync)
        {
            var reservation = RequireActiveHandoff(lease);
            if (!reservation.OldAttachmentDetachedConfirmed)
            {
                throw new InvalidOperationException(
                    $"Vault '{lease.VaultId}' cannot complete a local root handoff before its old attachment is confirmed detached.");
            }

            if (rootsByVaultId.ContainsKey(lease.VaultId))
            {
                throw new InvalidOperationException(
                    $"Vault '{lease.VaultId}' still has a local attachment while its root handoff is pending.");
            }

            rootsByVaultId[lease.VaultId] = (reservation.NextRootPath, 1);
            handoffsByVaultId.Remove(lease.VaultId);
        }
    }

    /// <summary>
    /// Confirms that the owner of a pending lease has finished disposing the
    /// single old attachment. Generic <see cref="Detach"/> calls for the
    /// expected root are frozen while the lease is pending.
    /// </summary>
    public void ConfirmHandoffOldAttachmentDetached(
        VaultRootHandoffLease lease,
        string expectedRootPath)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var expectedRoot = CanonicalizeRootPath(expectedRootPath);
        lock (sync)
        {
            var reservation = RequireActiveHandoff(lease);
            if (!AreEquivalentRootPaths(reservation.ExpectedRootPath, expectedRoot))
            {
                throw new InvalidOperationException(
                    $"Vault '{lease.VaultId}' handoff confirmation did not name its expected local root.");
            }

            if (reservation.OldAttachmentDetachedConfirmed)
            {
                return;
            }

            if (!rootsByVaultId.TryGetValue(lease.VaultId, out var existing) ||
                existing.Attachments != 1 ||
                !AreEquivalentRootPaths(existing.RootPath, reservation.ExpectedRootPath))
            {
                throw new InvalidOperationException(
                    $"Vault '{lease.VaultId}' no longer has its expected local attachment to confirm.");
            }

            rootsByVaultId.Remove(lease.VaultId);
            handoffsByVaultId[lease.VaultId] = reservation with { OldAttachmentDetachedConfirmed = true };
        }
    }

    /// <summary>
    /// Cancels a pending relocation. The original attachment reservation is
    /// restored unless the lease owner explicitly confirmed its old session
    /// had detached.
    /// </summary>
    public void CancelHandoff(VaultRootHandoffLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (sync)
        {
            var reservation = RequireActiveHandoff(lease);
            CancelPendingHandoff(lease.VaultId, reservation);
        }
    }

    public void Detach(string vaultId, string rootPath)
    {
        var canonical = CanonicalizeRootPath(rootPath);
        lock (sync)
        {
            if (handoffsByVaultId.TryGetValue(vaultId, out var reservation) &&
                AreEquivalentRootPaths(canonical, reservation.ExpectedRootPath))
            {
                return;
            }

            if (rootsByVaultId.TryGetValue(vaultId, out var existing)
                && AreEquivalentRootPaths(existing.RootPath, canonical))
            {
                if (existing.Attachments <= 1)
                {
                    rootsByVaultId.Remove(vaultId);
                }
                else
                {
                    rootsByVaultId[vaultId] = (existing.RootPath, existing.Attachments - 1);
                }
            }
        }
    }

    internal void TryCancelHandoff(VaultRootHandoffLease lease)
    {
        lock (sync)
        {
            if (ReferenceEquals(lease.Owner, this) &&
                handoffsByVaultId.TryGetValue(lease.VaultId, out var reservation) &&
                reservation.Token == lease.Token)
            {
                CancelPendingHandoff(lease.VaultId, reservation);
            }
        }
    }

    private void CancelPendingHandoff(string vaultId, HandoffReservation reservation)
    {
        handoffsByVaultId.Remove(vaultId);
        if (reservation.OldAttachmentDetachedConfirmed)
        {
            return;
        }

        if (!rootsByVaultId.ContainsKey(vaultId))
        {
            rootsByVaultId[vaultId] = (reservation.ExpectedRootPath, 1);
        }
    }

    private HandoffReservation RequireActiveHandoff(VaultRootHandoffLease lease)
    {
        if (!ReferenceEquals(lease.Owner, this))
        {
            throw new InvalidOperationException("The vault root handoff lease belongs to another registry.");
        }

        if (!handoffsByVaultId.TryGetValue(lease.VaultId, out var reservation) ||
            reservation.Token != lease.Token)
        {
            throw new InvalidOperationException("The vault root handoff lease is no longer active.");
        }

        return reservation;
    }

    private static string CanonicalizeRootPath(string rootPath) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));

    private static bool AreEquivalentRootPaths(string left, string right) =>
        string.Equals(
            left,
            right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed record HandoffReservation(
        Guid Token,
        string ExpectedRootPath,
        string NextRootPath,
        bool OldAttachmentDetachedConfirmed = false);
}

/// <summary>
/// Opaque, one-use reservation returned by <see cref="VaultRootRegistry.BeginHandoff"/>.
/// Disposing an uncommitted lease cancels it. An unconfirmed cancellation
/// restores the original attachment reservation; a confirmed one releases it.
/// Disposal after a commit is a no-op.
/// </summary>
public sealed class VaultRootHandoffLease : IDisposable
{
    internal VaultRootHandoffLease(VaultRootRegistry owner, string vaultId, Guid token)
    {
        Owner = owner;
        VaultId = vaultId;
        Token = token;
    }

    internal VaultRootRegistry Owner { get; }

    internal string VaultId { get; }

    internal Guid Token { get; }

    public void Dispose() => Owner.TryCancelHandoff(this);
}
