using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using L10n = Unlimotion.ViewModel.Localization.Localization;
using Unlimotion.ViewModel.Workspace;

namespace Unlimotion.ViewModel.Feed;

public sealed partial class FeedViewModel
{
    public FeedDocumentWorkspaceViewModel DocumentWorkspace { get; } = new();
    private long documentActivationGeneration;
    private string? documentSessionKey;
    private string? captureSessionKey;
    private string GetDocumentSpaceKey(string identity) =>
        (TaskSourceIdentityProvider?.Invoke()?.ToString() ?? "local") + "\n" + identity;
    private readonly Dictionary<string, DocumentWorkspaceState> documentStates = new(StringComparer.Ordinal);
    private sealed record DocumentViewState(string Path, double Offset,
        MarkdownLivePreviewEditorViewModel.CaretPosition? Caret);
    private sealed record DocumentWorkspaceState(DocumentViewState[] Documents, string? ActivePath);

    private void RememberDocumentWorkspace()
    {
        ++documentActivationGeneration;
        if (documentSessionKey is not null)
            documentStates[documentSessionKey] = new(DocumentWorkspace.Documents
                .Select(document => new DocumentViewState(document.RelativePath, document.ScrollOffset,
                    document.MarkdownEditor.LastCaretPosition)).ToArray(), OpenedThematicFile?.RelativePath);
        documentSessionKey = null;
    }

    private async Task RestoreDocumentWorkspaceAsync()
    {
        if (vaultId is null) return;
        var session = GetSessionToken();
        documentSessionKey = GetDocumentSpaceKey(vaultId);
        if (!documentStates.TryGetValue(documentSessionKey, out var state)) return;
        foreach (var document in state.Documents)
        {
            if (session.IsCancellationRequested) return;
            // Resolve and read through the normal link pipeline, including shared daily editors.
            await OpenVaultLinkAsync(document.Path, null, wikiLink: true);
            if (session.IsCancellationRequested) return;
            if (DocumentWorkspace.Find(document.Path) is { } restored)
            {
                restored.ScrollOffset = document.Offset;
                restored.MarkdownEditor.LastCaretPosition = document.Caret;
                restored.MarkdownEditor.RestoreCaretPosition();
            }
        }
        if (!session.IsCancellationRequested)
            await ActivateDocumentAsync(state.ActivePath is null ? null : DocumentWorkspace.Find(state.ActivePath));
    }

    public async Task ActivateDocumentAsync(FeedThematicDocumentViewModel? document)
    {
        if (document is not null && !DocumentWorkspace.Documents.Contains(document)) return;
        if (ReferenceEquals(document, OpenedThematicFile)) return;
        var activation = ++documentActivationGeneration;
        var session = GetSessionToken();
        try
        {
            await CommitActiveEditorsAsync(session);
            if (session.IsCancellationRequested || activation != documentActivationGeneration
                || document is not null && !DocumentWorkspace.Documents.Contains(document)) return;
            DocumentWorkspace.ActiveDocument = document;
            document?.MarkdownEditor.RestoreCaretPosition();
            OpenedThematicFile = document;
        }
        catch (Exception exception)
        {
            if (!session.IsCancellationRequested && !isDisposed) ErrorMessage = exception.Message;
        }
    }

    public async Task CloseDocumentAsync(FeedThematicDocumentViewModel? document)
    {
        ++noteNavigationGeneration;
        ++documentActivationGeneration;
        if (document is null) return;
        var session = GetSessionToken();
        try
        {
            if (document.MarkdownEditor.ActiveBlock is not null &&
                !await document.MarkdownEditor.CommitActiveAsync(session))
            {
                ErrorMessage = document.MarkdownEditor.ActiveBlock?.ErrorMessage ?? L10n.Get("MarkdownBlockSaveFailed");
                return;
            }
            if (session.IsCancellationRequested || !DocumentWorkspace.Documents.Contains(document)) return;
            await document.MarkdownEditor.FlushDraftPersistenceAsync();
            if (session.IsCancellationRequested || document.MarkdownEditor.HasDraftPersistenceError)
            {
                ErrorMessage = document.MarkdownEditor.DraftPersistenceError;
                return;
            }
            BlockSelection.Remove(document.MarkdownEditor);
            if (ReferenceEquals(OpenedThematicFile, document))
            {
                OpenedThematicFile = null;
                DocumentWorkspace.ActiveDocument = null;
            }
            DocumentWorkspace.Documents.Remove(document);
            document.Dispose();
        }
        catch (Exception exception)
        {
            if (!session.IsCancellationRequested && !isDisposed) ErrorMessage = exception.Message;
        }
    }

    public async Task RestoreWorkspaceLocationAsync(WorkspaceLocation location)
    {
        if (location.Kind != WorkspaceLocationKind.Feed || location.Id == "feed") return;

        var day = FindDay(location.Id);
        if (day is null)
        {
            await LoadThroughSearchDayAsync(location.Id, GetSessionToken()).ConfigureAwait(true);
            day = FindDay(location.Id);
        }
        if (day is null) return;

        SelectedDay = day;
        if (!useWorkspaceAreaPresentation && location.StateKey is not null)
        {
            string[] selectedValues;
            try
            {
                selectedValues = System.Text.Json.JsonSerializer.Deserialize<string[]>(location.StateKey) ?? [];
            }
            catch (System.Text.Json.JsonException)
            {
                selectedValues = location.StateKey.Split('|', StringSplitOptions.None);
            }
            var selected = selectedValues.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selectAll = selected.Count == 0;
            foreach (var option in FeedAreaFilterOptions)
                option.IsSelected = option.IsAll ? selectAll : selected.Contains(option.Identity ?? string.Empty);
        }
        if (location.ScrollOffset is { } offset)
            ChronologyScrollOffset = Math.Max(0, offset);
        if (int.TryParse(location.Anchor, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var blockIndex))
        {
            SearchNavigationStarting?.Invoke(this, EventArgs.Empty);
            SearchNavigationRequested?.Invoke(this, new FeedSearchNavigationRequestedEventArgs(
                location.Id, day.MarkdownEditor, blockIndex, day));
        }
    }
}
