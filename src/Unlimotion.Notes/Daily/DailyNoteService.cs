using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Notes.Daily;

public sealed record DailyNoteSummary(
    DateOnly Date,
    string RelativePath,
    string Revision,
    int ContentBlockCount,
    string Text,
    bool HasUtf8Bom,
    string NewLine);

public sealed record DailyNotePath(DateOnly Date, string RelativePath);

public sealed class DailyNoteService
{
    private readonly INoteVault vault;
    private readonly IMarkdownDocumentParser parser;
    private readonly MarkdownMutationService mutations;

    public DailyNoteService(
        INoteVault vault,
        IMarkdownDocumentParser parser,
        MarkdownMutationService mutations,
        DailyNoteNaming? naming = null)
    {
        this.vault = vault;
        this.parser = parser;
        this.mutations = mutations;
        Naming = naming ?? DailyNoteNaming.Default;
    }

    public DailyNoteNaming Naming { get; }

    /// <summary>
    /// Legacy default path helper. Consumers with an active vault naming should use <see cref="DailyNoteNaming.GetRelativePath"/>.
    /// </summary>
    public static string GetRelativePath(DateOnly date) => DailyNoteNaming.Default.GetRelativePath(date);

    public Task<VaultDocument?> OpenDayAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        vault.ReadAsync(Naming.GetRelativePath(date), cancellationToken);

    public async Task<IReadOnlyList<DailyNoteSummary>> ListDaysAsync(CancellationToken cancellationToken = default)
    {
        var paths = await ListDayPathsAsync(cancellationToken).ConfigureAwait(false);
        return await OpenPageAsync(paths, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DailyNotePath>> ListDayPathsAsync(CancellationToken cancellationToken = default)
    {
        var paths = await vault.ListMarkdownFilesAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<DailyNotePath>();
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = path.Replace('\\', '/');
            if (!Naming.TryParseRelativePath(normalized, out var day))
            {
                continue;
            }

            result.Add(new DailyNotePath(day, normalized));
        }

        return result.OrderByDescending(static day => day.Date).ToArray();
    }

    public async Task<(IReadOnlyList<DailyNoteSummary> Days, int TotalCount)> ListDaysPageAsync(
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        var paths = await ListDayPathsAsync(cancellationToken).ConfigureAwait(false);
        var selected = paths.Skip(skip).Take(take).ToArray();
        return (await OpenPageAsync(selected, cancellationToken).ConfigureAwait(false), paths.Count);
    }

    private async Task<IReadOnlyList<DailyNoteSummary>> OpenPageAsync(
        IReadOnlyList<DailyNotePath> paths,
        CancellationToken cancellationToken)
    {
        var result = new List<DailyNoteSummary>(paths.Count);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = await vault.ReadAsync(path.RelativePath, cancellationToken).ConfigureAwait(false);
            if (document is not null)
            {
                result.Add(new DailyNoteSummary(
                    path.Date,
                    path.RelativePath,
                    document.Revision,
                    parser.Parse(document.Text).Blocks.Count(static block => block.IsContent),
                    document.Text,
                    document.HasUtf8Bom,
                    document.NewLine));
            }
        }

        return result;
    }

    public async Task<VaultDocument> AppendCaptureAsync(
        DateOnly date,
        string capture,
        AreaReference? area = null,
        string? expectedRevision = null,
        CancellationToken cancellationToken = default)
    {
        var path = Naming.GetRelativePath(date);
        var existing = await vault.ReadAsync(path, cancellationToken).ConfigureAwait(false);
        if (existing is not null && expectedRevision is not null && !string.Equals(existing.Revision, expectedRevision, StringComparison.Ordinal))
        {
            throw new VaultRevisionConflictException(path, expectedRevision, existing.Revision);
        }

        var updated = mutations.AppendQuickCapture(existing?.Text ?? string.Empty, capture, area);
        if (existing is null)
        {
            await vault.CreateAsync(path, updated, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await vault.WriteAsync(path, updated, existing.Revision, existing.HasUtf8Bom, cancellationToken).ConfigureAwait(false);
        }

        return await vault.ReadAsync(path, cancellationToken).ConfigureAwait(false)
            ?? throw new IOException("The daily note was written but could not be read back.");
    }
}
