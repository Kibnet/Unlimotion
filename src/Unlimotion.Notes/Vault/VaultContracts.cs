using System.Security.Cryptography;
using System.Text;

namespace Unlimotion.Notes.Vault;

public sealed record VaultDocument(
    string RelativePath,
    string Text,
    string Revision,
    bool HasUtf8Bom,
    string NewLine);

public sealed record VaultWriteResult(string RelativePath, string Revision);

public sealed class VaultRevisionConflictException(string relativePath, string? expected, string? actual)
    : IOException($"The note '{relativePath}' changed. Expected revision '{expected ?? "<new>"}', actual '{actual ?? "<missing>"}'.")
{
    public string RelativePath { get; } = relativePath;

    public string? ExpectedRevision { get; } = expected;

    public string? ActualRevision { get; } = actual;
}

public interface INoteVault
{
    string RootPath { get; }

    Task<VaultDocument?> ReadAsync(string relativePath, CancellationToken cancellationToken = default);

    Task<VaultWriteResult> WriteAsync(
        string relativePath,
        string text,
        string? expectedRevision,
        bool hasUtf8Bom = false,
        CancellationToken cancellationToken = default);

    Task<VaultWriteResult> CreateAsync(
        string relativePath,
        string text,
        bool hasUtf8Bom = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListMarkdownFilesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListFilesAsync(
        string relativeDirectory,
        string searchPattern,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    Task<bool> DeleteAsync(
        string relativePath,
        string? expectedRevision,
        CancellationToken cancellationToken = default) =>
        Task.FromException<bool>(new NotSupportedException("This note vault does not support deletion."));

    string ResolveSafePath(string relativePath);
}

internal static class VaultRevision
{
    public static string Compute(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static byte[] Encode(string text, bool hasBom)
    {
        var body = Encoding.UTF8.GetBytes(text);
        if (!hasBom)
        {
            return body;
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }
}
