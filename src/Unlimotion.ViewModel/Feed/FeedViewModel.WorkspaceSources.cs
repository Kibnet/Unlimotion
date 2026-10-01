using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Notes.Operations;
using Unlimotion.ViewModel.Workspace;

namespace Unlimotion.ViewModel.Feed;

public sealed partial class FeedViewModel
{
    public string? WorkspaceScopeKey => vaultId is null ? null : GetDocumentSpaceKey(vaultId);

    public async Task OpenTaskSourceAsync(string taskId, WorkspaceLocation location)
    {
        try
        {
            var sources = await FindTaskSourceLocationsAsync(taskId);
            if (!sources.Any(source => source.Id == location.Id && source.Anchor == location.Anchor)
                || vault is null || await vault.ReadAsync(location.Id, GetSessionToken()) is null)
            {
                ShowTaskSourceUnavailable();
                return;
            }
            if (NavigateToWorkspaceLocationRequested is { } navigate)
                await navigate(location, WorkspaceOpenDisposition.CurrentTab);
        }
        catch (OperationCanceledException) { /* The task space or vault was disconnected. */ }
        catch (IOException) { ShowTaskSourceUnavailable(); }
        catch (UnauthorizedAccessException) { ShowTaskSourceUnavailable(); }
        catch (System.Text.Json.JsonException) { ShowTaskSourceUnavailable(); }
        catch (ArgumentException) { ShowTaskSourceUnavailable(); }
    }

    private void ShowTaskSourceUnavailable()
    {
        ErrorMessage = Unlimotion.ViewModel.Localization.Localization.Get("WorkspaceNoteUnavailable");
        TaskOwner?.ManagerWrapper?.ErrorToast(ErrorMessage);
    }

    // Feed IDs encode the journal operation. Lookup reads one transaction and one document,
    // regardless of the number of days in the vault.
    public async Task<IReadOnlyList<WorkspaceLocation>> FindTaskSourceLocationsAsync(string taskId)
    {
        if (!taskId.StartsWith("feed-", StringComparison.Ordinal) || vault is not { } sourceVault || vaultId is null || markdownParser is null)
            return [];
        var scope = WorkspaceScopeKey;
        var operationId = taskId[5..];
        if (!System.Text.RegularExpressions.Regex.IsMatch(operationId, "^[a-zA-Z0-9_-]+$")) return [];
        var record = await taskJournalFactory(vaultId).LoadAsync(vaultId, operationId, GetSessionToken());
        if (record is null || record.TaskId != taskId || record.VaultId != vaultId
            || record.State != FeedTaskConversionState.Completed
            || record.TaskSourceIdentity is null || record.TaskSourceIdentity != TaskSourceIdentityProvider?.Invoke()) return [];
        if (string.IsNullOrWhiteSpace(record.SourcePath) || Path.IsPathRooted(record.SourcePath)
            || record.SourcePath.Replace('\\', '/').Split('/').Any(part => part is ".." or "." or "")) return [];
        var document = await sourceVault.ReadAsync(record.SourcePath, GetSessionToken());
        if (scope != WorkspaceScopeKey) return [];
        if (document is null)
            return [dailyNoteNaming.TryParseRelativePath(record.SourcePath, out var missingDate)
                ? WorkspaceLocation.ForFeedDay(record.SourcePath, missingDate.ToString("yyyy-MM-dd"))
                : WorkspaceLocation.ForNote(record.SourcePath, Path.GetFileNameWithoutExtension(record.SourcePath))];
        var linkPattern = System.Text.RegularExpressions.Regex.Escape("unlimotion://task/" + taskId) + "(?![a-zA-Z0-9_-])";
        var blocks = markdownParser.Parse(document.Text).Blocks
            .Where(block => System.Text.RegularExpressions.Regex.IsMatch(block.Raw, linkPattern)).ToArray();
        if (blocks.Length == 0) return [];
        return blocks.Select(block => dailyNoteNaming.TryParseRelativePath(record.SourcePath, out var date)
            ? WorkspaceLocation.ForFeedDay(record.SourcePath, date.ToString("yyyy-MM-dd"), block.Index.ToString())
            : WorkspaceLocation.ForNote(record.SourcePath, Path.GetFileNameWithoutExtension(record.SourcePath), block.Index.ToString()))
            .ToArray();
    }
}
