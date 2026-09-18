using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ReactiveUI;
using Unlimotion.Notes.Operations;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel.Feed;

public sealed partial class FeedViewModel
{
    public Func<FeedTaskSourceIdentity?>? TaskSourceIdentityProvider { get; private set; }
    private Func<string, string, string, string?>? readAreaRoot;
    private Func<string, string, string, string?, Task>? saveAreaRoot;
    private FeedTaskParentDraftViewModel? reviewParents;
    private string? parentsSelectionKey;
    private string? reviewParentDefaultsError;
    private string? quickParentDefaultsError;
    public FeedTaskParentDraftViewModel? ReviewParents => reviewParents;
    private FeedTaskParentDraftViewModel? quickCaptureParents;
    private string? quickCaptureParentsScope;
    public FeedTaskParentDraftViewModel? QuickCaptureParents => quickCaptureParents;
    private ICommand? applyQuickCaptureAreaParentsCommand;
    public ICommand ApplyQuickCaptureAreaParentsCommand => applyQuickCaptureAreaParentsCommand ??= new FeedActionCommand(_ => RefreshQuickCaptureParents(force: true));
    private ICommand? applyAreaParentDefaultsCommand;
    public ICommand ApplyAreaParentDefaultsCommand => applyAreaParentDefaultsCommand ??= new FeedActionCommand(_ => RefreshAreaParents(force: true));

    public void ConfigureTaskSourceParents(Func<FeedTaskSourceIdentity?> source,
        Func<string, string, string, string?> read, Func<string, string, string, string?, Task> save)
    {
        TaskSourceIdentityProvider = source;
        readAreaRoot = read;
        saveAreaRoot = save;
        ConfigureAreaRootEditor();
        RefreshQuickCaptureParents();
    }

    private void ConfigureAreaRootEditor()
    {
        if (AreaManagement is not { } management || TaskOwner is null || vaultId is null
            || readAreaRoot is null || saveAreaRoot is null || TaskSourceIdentityProvider?.Invoke() is not { } source) return;
        var capturedVault = vaultId;
        management.RootTaskPicker?.Dispose();
        var picker = new FeedTaskParentDraftViewModel(TaskOwner);
        management.RootTaskPicker = picker;
        management.ConfigureRootTasks(areaId =>
        {
            FeedTaskSourceIdentity.RequireCurrent(source, TaskSourceIdentityProvider);
            var id = readAreaRoot(source.SourceId, capturedVault, areaId);
            return id is null ? null : new AreaRootTaskReference(id, TaskResolver?.Invoke(id)?.Title ?? id);
        }, async (areaId, root) =>
        {
            FeedTaskSourceIdentity.RequireCurrent(source, TaskSourceIdentityProvider);
            if (vaultId != capturedVault) throw new InvalidOperationException(L10n.Get("FeedTaskSourceMismatch"));
            if (root is not null && TaskResolver?.Invoke(root.Id)?.IsCompleted is null)
                throw new InvalidOperationException(L10n.Get("AreaRootTaskUnavailable"));
            await saveAreaRoot(source.SourceId, capturedVault, areaId, root?.Id);
        }, picker.PickSingleAsync, id => NavigateToTask(TaskResolver?.Invoke(id)));
    }

    private IReadOnlyList<AreaRootTaskReference> ReadDefaultParents(IEnumerable<string> areaIds)
    {
        var source = TaskSourceIdentityProvider?.Invoke();
        if (source is null || vaultId is null || readAreaRoot is null) return [];
        return areaIds.Select(id => readAreaRoot(source.SourceId, vaultId, id))
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)
            .Select(id => new AreaRootTaskReference(id!, TaskResolver?.Invoke(id!)?.Title ?? id!)).ToArray();
    }

    private void RefreshAreaParents(bool force = false)
    {
        if (TaskOwner is null) return;
        var key = currentCandidate?.Locator.ToString();
        if (reviewParents is null || parentsSelectionKey != key)
        {
            reviewParents?.Dispose();
            reviewParents = new FeedTaskParentDraftViewModel(TaskOwner);
            parentsSelectionKey = key;
            this.RaisePropertyChanged(nameof(ReviewParents));
        }
        try
        {
            var defaults = ReadDefaultParents(ReviewTaskAreas.Where(area => area.IsSelected)
                .Select(area => area.Area.StableAreaId!).Where(id => id is not null));
            reviewParentDefaultsError = null;
            reviewParents.ApplyDefaults(defaults, force);
        }
        catch (Exception error) { reviewParentDefaultsError = error.Message; ErrorMessage = error.Message; }
    }

    private IReadOnlyList<string> EffectiveParentIds(IEnumerable<string> areaIds, bool useReviewDraft)
    {
        if ((useReviewDraft ? reviewParentDefaultsError : quickParentDefaultsError) is { } error)
            throw new InvalidOperationException(error);
        var draft = useReviewDraft ? ReviewParents : QuickCaptureParents;
        var parents = draft is not null ? draft.Parents.ToArray() : ReadDefaultParents(areaIds);
        foreach (var parent in parents)
            if (TaskResolver?.Invoke(parent.Id)?.IsCompleted is null)
                throw new InvalidOperationException(L10n.Get("AreaRootTaskUnavailable"));
        return parents.Select(parent => parent.Id).Distinct(StringComparer.Ordinal).ToArray();
    }

    private void EnsureTaskRecoverySource(FeedTaskConversionRecord operation) =>
        FeedTaskSourceIdentity.RequireCurrent(operation.TaskSourceIdentity, TaskSourceIdentityProvider);

    private void RefreshQuickCaptureParents(bool force = false)
    {
        if (TaskOwner is null) return;
        var scope = TaskSourceIdentityProvider?.Invoke()?.ToString() + "\n" + vaultId;
        if (quickCaptureParents is null || quickCaptureParentsScope != scope)
        {
            quickCaptureParents?.Dispose();
            quickCaptureParents = new FeedTaskParentDraftViewModel(TaskOwner);
            quickCaptureParentsScope = scope;
            this.RaisePropertyChanged(nameof(QuickCaptureParents));
        }
        try
        {
            var defaults = pendingQuickTaskCapture is { } pending
                ? (pending.ParentTaskIds ?? []).Select(id => new AreaRootTaskReference(id, TaskResolver?.Invoke(id)?.Title ?? id)).ToArray()
                : ReadDefaultParents(SelectedArea?.StableAreaId is { } id ? [id] : []);
            quickParentDefaultsError = null;
            quickCaptureParents.ApplyDefaults(defaults, force || pendingQuickTaskCapture is not null);
        }
        catch (Exception error) { quickParentDefaultsError = error.Message; ErrorMessage = error.Message; }
    }

    private void OnSelectedAreaChanged() => RefreshQuickCaptureParents();
    private void OnQuickCaptureTextChanged()
    {
        if (string.IsNullOrEmpty(QuickCaptureText)) RefreshQuickCaptureParents(force: true);
    }

    private void ResetQuickCaptureParentDraft()
    {
        quickCaptureParents?.Dispose();
        quickCaptureParents = null;
        quickCaptureParentsScope = null;
        quickParentDefaultsError = null;
        this.RaisePropertyChanged(nameof(QuickCaptureParents));
    }
}
