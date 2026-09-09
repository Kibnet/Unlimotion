using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Notes.Conflicts;
using Unlimotion.Notes.Recovery;
using Unlimotion.Notes.Vault;
using Unlimotion.Notes.Watching;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel.Feed;

public sealed partial class FeedViewModel
{
    private async Task HandleDocumentReloadAsync(DocumentReloadSignal signal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var sourceVault = vault;
        if (sourceVault is null) return;
        var change = signal.Change;
        var editorPath = change.OldRelativePath ?? change.RelativePath;
        var tabs = change.Kind == VaultWatchChangeKind.RescanRequired
            ? DocumentWorkspace.Documents.ToArray()
            : DocumentWorkspace.Documents.Where(tab => SameDocumentPath(tab.RelativePath, editorPath)
                || SameDocumentPath(tab.PendingRelativePath, editorPath)).ToArray();
        foreach (var tab in tabs)
        {
            var path = change.Kind == VaultWatchChangeKind.Renamed ? change.RelativePath! : tab.RelativePath;
            // Read current state rather than replaying stale bytes from an earlier notification.
            var disk = await sourceVault.ReadAsync(path, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(sourceVault, vault) || !DocumentWorkspace.Documents.Contains(tab)) return;
            if (tab.MarkdownEditor.ActiveBlock?.IsDirty == true)
            {
                // Input may have arrived after the coordinator classified the document as clean.
                var editor = tab.MarkdownEditor;
                var patch = editor.ActiveBlock.CreatePatch(editor.Snapshot!);
                if (disk?.Text == patch.PatchedDocumentRaw && disk.HasUtf8Bom == patch.HasUtf8Bom)
                {
                    // The coordinator acknowledged these exact bytes; do not re-register them
                    // as dirty and loop through another clean notification.
                    editor.CancelActiveEdit();
                    if (await PrepareDocumentPathAsync(tab, path, disk, token))
                        ReplaceOpenDocumentEditor(tab, path, disk, sourceVault);
                    continue;
                }
                if (watchRuntime is { } runtime)
                {
                    runtime.DirtyDocuments.Set(new(tab.RelativePath, patch.PatchedDocumentRaw,
                        patch.ReplacementRaw, patch.BlockIndex, patch.ExpectedRevisionHash, patch.HasUtf8Bom));
                    await runtime.ConflictCoordinator.HandleAsync(new(VaultWatchScope.Markdown,
                        change.Kind == VaultWatchChangeKind.Renamed ? VaultWatchChangeKind.Renamed
                            : disk is null ? VaultWatchChangeKind.Deleted : VaultWatchChangeKind.Changed,
                        path, change.Kind == VaultWatchChangeKind.Renamed ? tab.RelativePath : null, disk?.Revision), token);
                }
                continue;
            }
            if (disk is null)
            {
                await PreserveMissingDocumentAsync(tab, token);
                token.ThrowIfCancellationRequested();
                if (tab.MarkdownEditor.ActiveBlock?.IsDirty == true)
                    await RefreshDocumentConflictAsync(tab, tab.RelativePath, token);
                MarkDocumentUnavailable(tab, missing: true);
            }
            else if (tab.PendingRelativePath is null && await PrepareDocumentPathAsync(tab, path, disk, token))
                ReplaceOpenDocumentEditor(tab, path, disk, sourceVault);
        }
        await RefreshMarkdownFromWatcherAsync(token).ConfigureAwait(true);
        ReuseLoadedDailyDocumentEditors();
    }

    private async Task PreserveMissingDocumentAsync(FeedThematicDocumentViewModel tab, CancellationToken token)
    {
        if (watchRuntime is not { } runtime || tab.MarkdownEditor.Snapshot is not { } snapshot) return;
        var block = tab.MarkdownEditor.Blocks.FirstOrDefault(block => block.Block.IsContent);
        await runtime.Drafts.SaveAsync(new FeedDraft(1, runtime.VaultId, tab.RelativePath,
            block?.Index ?? 0, snapshot.ExpectedRevisionHash, block?.Block.Raw ?? snapshot.Raw,
            DateTimeOffset.UtcNow, snapshot.Raw, snapshot.HasUtf8Bom), token);
    }

    private void MarkDocumentConflict(DocumentConflictState conflict)
    {
        if (DocumentWorkspace.Find(conflict.EditorRelativePath) is not { } tab) return;
        if (!SameDocumentPath(conflict.EditorRelativePath, conflict.DiskRelativePath))
            tab.PendingRelativePath = conflict.DiskRelativePath;
        MarkDocumentUnavailable(tab, conflict.DiskDocument is null);
    }

    private async Task ProtectDocumentCollisionAsync(DocumentConflictState conflict, CancellationToken token)
    {
        if (SameDocumentPath(conflict.EditorRelativePath, conflict.DiskRelativePath)) return;
        var target = DocumentWorkspace.Documents.FirstOrDefault(tab => SameDocumentPath(tab.RelativePath, conflict.DiskRelativePath));
        if (target is null) return;
        if (target.MarkdownEditor.ActiveBlock?.IsDirty == true)
            await RefreshDocumentConflictAsync(target, conflict.DiskRelativePath, token);
        else
        {
            await PreserveMissingDocumentAsync(target, token);
            MarkDocumentUnavailable(target, missing: false);
        }
    }

    private async Task<bool> PrepareDocumentPathAsync(FeedThematicDocumentViewModel source, string path,
        VaultDocument disk, CancellationToken token)
    {
        var target = DocumentWorkspace.Documents.FirstOrDefault(tab => !ReferenceEquals(tab, source)
            && SameDocumentPath(tab.RelativePath, path));
        if (target is null) return true;
        if (target.MarkdownEditor.ActiveBlock?.IsDirty == true)
        {
            source.PendingRelativePath = path;
            MarkDocumentUnavailable(source, missing: false);
            await RefreshDocumentConflictAsync(target, path, token);
            return false;
        }
        // External overwrite removed the target's old bytes, even when its editor was clean.
        await PreserveMissingDocumentAsync(target, token);
        token.ThrowIfCancellationRequested();
        if (!DocumentWorkspace.Documents.Contains(target) || !DocumentWorkspace.Documents.Contains(source)) return false;
        if (source.MarkdownEditor.ActiveBlock?.IsDirty == true)
        {
            await RefreshDocumentConflictAsync(source, path, token);
            return false;
        }
        if (target.MarkdownEditor.ActiveBlock?.IsDirty == true)
        {
            source.PendingRelativePath = path;
            MarkDocumentUnavailable(source, missing: false);
            await RefreshDocumentConflictAsync(target, path, token);
            return false;
        }
        if (ReferenceEquals(OpenedThematicFile, target))
        {
            RemoveMergedDocument(source, target);
            ReplaceOpenDocumentEditor(target, path, disk, vault!);
            return false;
        }
        RemoveMergedDocument(target, source);
        return true;
    }

    private void RemoveMergedDocument(FeedThematicDocumentViewModel removed, FeedThematicDocumentViewModel retained)
    {
        BlockSelection.Remove(removed.MarkdownEditor);
        if (ReferenceEquals(OpenedThematicFile, removed))
        {
            OpenedThematicFile = retained;
            DocumentWorkspace.ActiveDocument = retained;
        }
        DocumentWorkspace.Documents.Remove(removed);
        removed.Dispose();
    }

    private async Task RefreshDocumentConflictAsync(FeedThematicDocumentViewModel tab, string diskPath, CancellationToken token)
    {
        if (watchRuntime is not { } runtime || tab.MarkdownEditor.ActiveBlock is not { IsDirty: true } block
            || tab.MarkdownEditor.Snapshot is not { } snapshot) return;
        var patch = block.CreatePatch(snapshot);
        runtime.DirtyDocuments.Set(new(tab.RelativePath, patch.PatchedDocumentRaw, patch.ReplacementRaw,
            patch.BlockIndex, patch.ExpectedRevisionHash, patch.HasUtf8Bom));
        await runtime.ConflictCoordinator.HandleAsync(new(VaultWatchScope.Markdown,
            SameDocumentPath(tab.RelativePath, diskPath) ? VaultWatchChangeKind.Changed : VaultWatchChangeKind.Renamed,
            diskPath, SameDocumentPath(tab.RelativePath, diskPath) ? null : tab.RelativePath, null), token);
        token.ThrowIfCancellationRequested();
        if (!ReferenceEquals(runtime, watchRuntime)) return;
        var latest = runtime.ConflictCoordinator.ActiveConflicts.FirstOrDefault(conflict =>
            SameDocumentPath(conflict.EditorRelativePath, tab.RelativePath));
        if (latest is not null) await ShowDocumentConflictAsync(latest, token);
    }

    private async Task RefreshOtherDocumentConflictsAsync(string diskPath, CancellationToken token)
    {
        foreach (var tab in DocumentWorkspace.Documents.Where(tab => SameDocumentPath(tab.PendingRelativePath, diskPath)
            || SameDocumentPath(tab.RelativePath, diskPath)).ToArray())
            await RefreshDocumentConflictAsync(tab, diskPath, token);
    }

    private void MarkDocumentUnavailable(FeedThematicDocumentViewModel tab, bool missing)
    {
        tab.IsMissing = missing;
        tab.ExternalChangeMessage = L10n.Get(missing ? "FeedDocumentMissing" : "FeedDocumentConflictPending");
        // Preserve the buffer and recovery, but never recreate an old path by autosaving it.
        tab.MarkdownEditor.CommitBlockAsync = (_, _) => Task.FromResult(
            MarkdownBlockCommitResult.Rejected(tab.ExternalChangeMessage));
        tab.MarkdownEditor.MoveBlocksAsync = (_, _) => Task.FromResult(
            MarkdownBlocksMoveResult.Rejected(tab.ExternalChangeMessage));
    }

    private async Task ReloadResolvedDocumentAsync(DocumentConflictState conflict,
        DocumentConflictResolutionResult result, CancellationToken token)
    {
        var sourceVault = vault;
        var tab = DocumentWorkspace.Find(conflict.EditorRelativePath);
        if (sourceVault is null || tab is null) return;
        var document = await sourceVault.ReadAsync(result.RelativePath, token).ConfigureAwait(true);
        token.ThrowIfCancellationRequested();
        if (!ReferenceEquals(sourceVault, vault) || !DocumentWorkspace.Documents.Contains(tab)) return;
        if (document is null)
        {
            tab.PendingRelativePath = null;
            MarkDocumentUnavailable(tab, missing: true);
            return;
        }
        if (await PrepareDocumentPathAsync(tab, result.RelativePath, document, token))
            ReplaceOpenDocumentEditor(tab, result.RelativePath, document, sourceVault);
        await RefreshOtherDocumentConflictsAsync(result.RelativePath, token);
        foreach (var alias in DocumentWorkspace.Documents.Where(other => other.PendingRelativePath is not null
            && SameDocumentPath(other.PendingRelativePath, result.RelativePath)
            && other.MarkdownEditor.ActiveBlock?.IsDirty != true).ToArray())
        {
            if (await PrepareDocumentPathAsync(alias, result.RelativePath, document, token))
                ReplaceOpenDocumentEditor(alias, result.RelativePath, document, sourceVault);
        }
        CoalesceCleanDocumentAliases(result.RelativePath);
    }

    private void CoalesceCleanDocumentAliases(string path)
    {
        var aliases = DocumentWorkspace.Documents
            .Where(tab => SameDocumentPath(tab.RelativePath, path))
            .ToArray();
        if (aliases.Length < 2
            || aliases.Any(tab => tab.MarkdownEditor.ActiveBlock?.IsDirty == true
                                  || tab.PendingRelativePath is not null))
        {
            return;
        }

        var retained = aliases.FirstOrDefault(tab => ReferenceEquals(tab, OpenedThematicFile))
            ?? aliases.FirstOrDefault(tab => ReferenceEquals(tab, DocumentWorkspace.ActiveDocument))
            ?? aliases[0];
        foreach (var duplicate in aliases.Where(tab => !ReferenceEquals(tab, retained)))
        {
            RemoveMergedDocument(duplicate, retained);
        }
    }

    private void ReplaceOpenDocumentEditor(FeedThematicDocumentViewModel tab, string path,
        VaultDocument document, INoteVault sourceVault)
    {
        if (SameDocumentPath(tab.RelativePath, path) && tab.MarkdownEditor.Snapshot?.ExpectedRevisionHash == document.Revision
            && !tab.IsMissing && tab.PendingRelativePath is null && tab.ExternalChangeMessage is null) return;
        var replacement = CreateMarkdownEditor(sourceVault, document, path,
            tab.MarkdownEditor.AutomationIdPrefix, ScheduleRefreshAfterMarkdownCommit, watchRuntime,
            () => !IsIdentityFrozen, OpenEditorSelectionActionAsync);
        replacement.SelectionCoordinator = BlockSelection;
        RestoreUnchangedDocumentCaret(tab.MarkdownEditor, replacement);
        BlockSelection.Remove(tab.MarkdownEditor);
        tab.ReplaceEditor(replacement, ownsReplacement: true);
        tab.RelativePath = path;
        tab.FullPath = sourceVault.ResolveSafePath(path);
        tab.PendingRelativePath = null;
        tab.IsMissing = false;
        tab.ExternalChangeMessage = null;
    }

    private void ReuseLoadedDailyDocumentEditors()
    {
        foreach (var tab in DocumentWorkspace.Documents)
        {
            if (tab.IsMissing || tab.ExternalChangeMessage is not null || tab.PendingRelativePath is not null
                || tab.MarkdownEditor.ActiveBlock?.IsDirty == true) continue;
            var day = Days.FirstOrDefault(day => SameDocumentPath(day.RelativePath, tab.RelativePath));
            if (day is null || ReferenceEquals(tab.MarkdownEditor, day.MarkdownEditor)) continue;
            BlockSelection.Remove(tab.MarkdownEditor);
            RestoreUnchangedDocumentCaret(tab.MarkdownEditor, day.MarkdownEditor);
            tab.ReplaceEditor(day.MarkdownEditor, ownsReplacement: false);
            day.MarkdownEditor.SelectionCoordinator = BlockSelection;
        }
    }

    private static void RestoreUnchangedDocumentCaret(MarkdownLivePreviewEditorViewModel previous,
        MarkdownLivePreviewEditorViewModel replacement)
    {
        if (previous.Snapshot?.ExpectedRevisionHash != replacement.Snapshot?.ExpectedRevisionHash) return;
        replacement.LastCaretPosition = previous.LastCaretPosition;
        var active = previous.ActiveBlock;
        if (active is null) return;
        var start = active.Block.Start;
        var block = replacement.Blocks.FirstOrDefault(block => block.Block.Start == start && block.IsEditable);
        if (block is null || !replacement.BeginEdit(block)) return;
        block.EditorSelectionStart = Math.Clamp(active.EditorSelectionStart, 0, block.EditorText.Length);
        block.EditorSelectionEnd = Math.Clamp(active.EditorSelectionEnd, 0, block.EditorText.Length);
    }

    private static bool SameDocumentPath(string? left, string? right) => left is not null && right is not null
        && string.Equals(NormalizePath(left), NormalizePath(right), OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
