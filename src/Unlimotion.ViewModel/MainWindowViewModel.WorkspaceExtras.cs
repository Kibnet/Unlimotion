using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Configuration;
using ReactiveUI;
using Unlimotion.Notes.Operations;
using Unlimotion.ViewModel.Feed;
using Unlimotion.ViewModel.Workspace;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel;

public sealed record PinnedNoteViewModel(string RelativePath, bool IsAvailable)
{
    public string Title => Path.GetFileNameWithoutExtension(RelativePath);
}

public partial class MainWindowViewModel
{
    private const string PinsSection = "WorkspacePinnedNotes";
    private Dictionary<string, List<string>> pinnedNotesByScope = new(StringComparer.Ordinal);
    public ObservableCollection<PinnedNoteViewModel> PinnedNotes { get; } = [];
    public ICommand OpenNotesCommand { get; private set; } = null!;
    public ICommand OpenPinnedNoteCommand { get; private set; } = null!;
    public ICommand SaveNextStepCommand { get; private set; } = null!;
    public ICommand CloseNextStepCommand { get; private set; } = null!;
    public bool IsNextStepOpen { get; private set; }
    public string NextStepTitle { get; set; } = string.Empty;
    public string? NextStepError { get; private set; }
    public FeedTaskParentDraftViewModel? NextStepParents { get; private set; }
    private string? nextStepScope;
    private string? nextStepOperationId;
    private bool isSavingNextStep;
    private IReadOnlyList<string> nextStepAreaIds = [];

    private void InitializeWorkspaceExtras()
    {
        Disposable.Create(() => NextStepParents?.Dispose()).AddToDispose(this);
        try
        {
            pinnedNotesByScope = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(
                _configuration?.GetSection(PinsSection).Get<string>() ?? "{}") ?? new(StringComparer.Ordinal);
        }
        catch (JsonException) { pinnedNotesByScope = new(StringComparer.Ordinal); }
        OpenNotesCommand = ReactiveCommand.Create(() => Feed.OpenFilesCommand.Execute(null)).AddToDisposeAndReturn(this);
        OpenPinnedNoteCommand = ReactiveCommand.CreateFromTask<PinnedNoteViewModel>(async pin =>
        {
            if (!pin.IsAvailable) { ManagerWrapper?.ErrorToast(L10n.Get("WorkspaceNoteUnavailable")); return; }
            await Feed.OpenVaultLinkAsync(pin.RelativePath, null);
        }).AddToDisposeAndReturn(this);
        SaveNextStepCommand = ReactiveCommand.CreateFromTask(SaveNextStepAsync).AddToDisposeAndReturn(this);
        CloseNextStepCommand = ReactiveCommand.Create(() => { if (!isSavingNextStep) IsNextStepOpen = false; }).AddToDisposeAndReturn(this);
        Feed.WhenAnyValue(feed => feed.VaultRootPath).Subscribe(_ => ReloadPinnedNotes()).AddToDispose(this);
    }

    public void ReloadPinnedNotes()
    {
        PinnedNotes.Clear();
        if (Feed.WorkspaceScopeKey is not { } scope || Feed.VaultRootPath is not { } root
            || !pinnedNotesByScope.TryGetValue(scope, out var paths) || paths is null) return;
        foreach (var path in paths.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
            if (TryNormalizePin(path, out var normalized))
                PinnedNotes.Add(new(normalized, File.Exists(Path.Combine(root, normalized))));
    }

    private static bool TryNormalizePin(string? path, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(path)) { normalized = string.Empty; return false; }
        normalized = path.Replace('\\', '/');
        return !Path.IsPathRooted(path) && !normalized.Split('/').Any(part => part is ".." or "." or "")
            && normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsNotePinned(string path) => PinnedNotes.Any(pin => string.Equals(pin.RelativePath, path.Replace('\\', '/'),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

    public void ToggleNotePin(string path)
    {
        if (Feed.WorkspaceScopeKey is not { } scope || !TryNormalizePin(path, out var normalized)) return;
        var previous = pinnedNotesByScope.TryGetValue(scope, out var existing) && existing is not null ? existing.ToList() : [];
        var next = previous.ToList();
        if (IsNotePinned(normalized)) next.RemoveAll(item => string.Equals(item, normalized,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        else next.Add(normalized);
        pinnedNotesByScope[scope] = next;
        try
        {
            _configuration?.GetSection(PinsSection).Set(JsonSerializer.Serialize(pinnedNotesByScope));
            ReloadPinnedNotes();
        }
        catch (Exception error)
        {
            pinnedNotesByScope[scope] = previous;
            ManagerWrapper?.ErrorToast(error.Message);
        }
    }

    public void OpenNextStep(TaskItemViewModel parent)
    {
        if (isSavingNextStep) return;
        IsQuickCaptureOpen = false;
        IsSettingsOpen = false;
        NextStepParents?.Dispose();
        NextStepParents = new FeedTaskParentDraftViewModel(this);
        NextStepParents.ApplyDefaults([new AreaRootTaskReference(parent.Id, parent.Title)]);
        NextStepTitle = string.Empty;
        NextStepError = null;
        nextStepAreaIds = parent.AreaIds.ToArray();
        nextStepOperationId = Guid.NewGuid().ToString("N");
        nextStepScope = Feed.TaskSourceIdentityProvider?.Invoke()?.ToString();
        IsNextStepOpen = true;
    }

    private async Task SaveNextStepAsync()
    {
        if (!IsNextStepOpen || isSavingNextStep) return;
        if (string.IsNullOrWhiteSpace(NextStepTitle)) { NextStepError = L10n.Get("WorkspaceTaskTitleRequired"); return; }
        isSavingNextStep = true;
        try
        {
            var source = Feed.TaskSourceIdentityProvider?.Invoke();
            if (source?.ToString() != nextStepScope) throw new InvalidOperationException(L10n.Get("FeedTaskSourceMismatch"));
            var target = new TaskStorageFeedTaskCreationTarget(() => taskRepository, Feed.TaskSourceIdentityProvider);
            var created = await target.CreateOrGetAsync(new FeedTaskDraft("step-" + nextStepOperationId,
                nextStepOperationId!, NextStepTitle.Trim(), string.Empty, false, nextStepAreaIds,
                NextStepParents?.Parents.Select(parent => parent.Id).ToArray(), source));
            IsNextStepOpen = false;
            await TryOpenTaskByIdAsync(created.TaskId, WorkspaceOpenDisposition.NewTab);
        }
        catch (Exception error) { NextStepError = error.Message == "FeedTaskSourceMismatch" ? L10n.Get("FeedTaskSourceMismatch") : error.Message; }
        finally { isSavingNextStep = false; }
    }
}
