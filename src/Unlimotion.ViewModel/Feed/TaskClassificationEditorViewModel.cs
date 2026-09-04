using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using ReactiveUI;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel.Feed;

public sealed class TaskClassificationEditorViewModel : ReactiveObject, IDisposable
{
    private readonly TaskItemViewModel? task;
    private readonly Func<bool> supportsEditing;
    private readonly Action<TaskClassificationSnapshot>? draftChanged;
    private readonly ObservableCollection<string> draftAreaIds = new();
    private TaskClassificationAreaDefinition[] areaDefinitions = [];
    private bool draftIsGoal;
    private bool isSynchronizing;
    private bool isEditingSupported;

    private TaskClassificationEditorViewModel(
        TaskItemViewModel? task,
        bool draftIsGoal,
        IEnumerable<string> selectedAreaIds,
        IEnumerable<TaskClassificationAreaDefinition> areas,
        Func<bool>? supportsEditing,
        Action<TaskClassificationSnapshot>? draftChanged)
    {
        this.task = task;
        this.draftIsGoal = draftIsGoal;
        this.supportsEditing = supportsEditing ?? (() => true);
        this.draftChanged = draftChanged;
        foreach (var areaId in selectedAreaIds.Where(static id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
        {
            draftAreaIds.Add(areaId);
        }

        if (task is not null)
        {
            ((INotifyPropertyChanged)task).PropertyChanged += TaskOnPropertyChanged;
            task.AreaIds.CollectionChanged += TaskAreaIdsOnCollectionChanged;
        }

        RefreshCapability();
        ReplaceAreas(areas);
    }

    public ObservableCollection<TaskClassificationAreaOptionViewModel> Areas { get; } = new();

    public ObservableCollection<TaskClassificationChipViewModel> SelectedAreas { get; } = new();

    public bool IsGoal
    {
        get => task?.IsGoal ?? draftIsGoal;
        set => TrySetGoal(value);
    }

    public bool IsEditingSupported
    {
        get => isEditingSupported;
        private set
        {
            this.RaiseAndSetIfChanged(ref isEditingSupported, value);
            this.RaisePropertyChanged(nameof(IsEditingBlocked));
            this.RaisePropertyChanged(nameof(EditingBlockedExplanation));
        }
    }

    public bool IsEditingBlocked => !IsEditingSupported;

    public string EditingBlockedExplanation => IsEditingBlocked
        ? L10n.Get("TaskClassificationOldServerExplanation")
        : string.Empty;

    public static TaskClassificationEditorViewModel ForTask(
        TaskItemViewModel task,
        IEnumerable<TaskClassificationAreaDefinition> areas,
        Func<bool>? supportsEditing = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new TaskClassificationEditorViewModel(
            task,
            task.IsGoal,
            task.AreaIds,
            areas,
            supportsEditing,
            draftChanged: null);
    }

    public static TaskClassificationEditorViewModel ForDraft(
        bool isGoal,
        IEnumerable<string>? selectedAreaIds,
        IEnumerable<TaskClassificationAreaDefinition> areas,
        Action<TaskClassificationSnapshot>? changed = null,
        Func<bool>? supportsEditing = null) =>
        new(
            task: null,
            isGoal,
            selectedAreaIds ?? [],
            areas,
            supportsEditing,
            changed);

    public bool TrySetGoal(bool value)
    {
        RefreshCapability();
        if (!IsEditingSupported)
        {
            this.RaisePropertyChanged(nameof(IsGoal));
            return false;
        }

        if (task is not null)
        {
            task.IsGoal = value;
        }
        else if (draftIsGoal != value)
        {
            draftIsGoal = value;
            this.RaisePropertyChanged(nameof(IsGoal));
            NotifyDraftChanged();
        }

        return true;
    }

    public bool TrySetAreaSelected(string areaId, bool selected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(areaId);
        RefreshCapability();
        if (!IsEditingSupported)
        {
            SynchronizeSelection();
            return false;
        }

        var option = Areas.FirstOrDefault(area => area.Id == areaId);
        if (selected && option?.IsArchived == true && !CurrentAreaIds.Contains(areaId, StringComparer.Ordinal))
        {
            SynchronizeSelection();
            return false;
        }

        var target = task?.AreaIds ?? draftAreaIds;
        var changed = false;
        isSynchronizing = true;
        try
        {
            if (selected && !target.Contains(areaId, StringComparer.Ordinal))
            {
                target.Add(areaId);
                changed = true;
            }
            else if (!selected)
            {
                for (var index = target.Count - 1; index >= 0; index--)
                {
                    if (string.Equals(target[index], areaId, StringComparison.Ordinal))
                    {
                        target.RemoveAt(index);
                        changed = true;
                    }
                }
            }
        }
        finally
        {
            isSynchronizing = false;
        }

        SynchronizeSelection();
        if (changed && !selected && option?.IsArchived == true)
        {
            ReplaceAreas(areaDefinitions);
        }

        if (changed && task is null)
        {
            NotifyDraftChanged();
        }

        return true;
    }

    public void ReplaceAreas(IEnumerable<TaskClassificationAreaDefinition> areas)
    {
        ArgumentNullException.ThrowIfNull(areas);
        areaDefinitions = areas.ToArray();
        foreach (var option in Areas)
        {
            option.SelectionChanged -= HandleAreaSelectionChanged;
        }

        Areas.Clear();
        var selectedIds = CurrentAreaIds.ToHashSet(StringComparer.Ordinal);
        foreach (var area in areaDefinitions
                     .Where(static area => !string.IsNullOrWhiteSpace(area.Id))
                     .DistinctBy(static area => area.Id, StringComparer.Ordinal)
                     .Where(area => !area.IsArchived || selectedIds.Contains(area.Id))
                     .OrderBy(static area => area.SortOrder)
                     .ThenBy(static area => area.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var option = new TaskClassificationAreaOptionViewModel(
                area.Id,
                area.Name,
                area.IsArchived,
                selectedIds.Contains(area.Id));
            option.SelectionChanged += HandleAreaSelectionChanged;
            Areas.Add(option);
        }

        SynchronizeSelection();
    }

    public void RefreshCapability() => IsEditingSupported = supportsEditing();

    public void Dispose()
    {
        if (task is not null)
        {
            ((INotifyPropertyChanged)task).PropertyChanged -= TaskOnPropertyChanged;
            task.AreaIds.CollectionChanged -= TaskAreaIdsOnCollectionChanged;
        }

        foreach (var option in Areas)
        {
            option.SelectionChanged -= HandleAreaSelectionChanged;
        }
    }

    private IReadOnlyList<string> CurrentAreaIds => task?.AreaIds ?? draftAreaIds;

    private void HandleAreaSelectionChanged(TaskClassificationAreaOptionViewModel option, bool selected)
    {
        if (!isSynchronizing)
        {
            TrySetAreaSelected(option.Id, selected);
        }
    }

    private void TaskOnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is null or nameof(TaskItemViewModel.IsGoal))
        {
            this.RaisePropertyChanged(nameof(IsGoal));
        }
    }

    private void TaskAreaIdsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (!isSynchronizing)
        {
            SynchronizeSelection();
        }
    }

    private void SynchronizeSelection()
    {
        var selectedIds = CurrentAreaIds.ToHashSet(StringComparer.Ordinal);
        isSynchronizing = true;
        try
        {
            foreach (var option in Areas)
            {
                option.IsSelected = selectedIds.Contains(option.Id);
            }
        }
        finally
        {
            isSynchronizing = false;
        }

        var names = Areas.ToDictionary(static area => area.Id, static area => area.Name, StringComparer.Ordinal);
        SelectedAreas.Clear();
        foreach (var areaId in CurrentAreaIds.Distinct(StringComparer.Ordinal))
        {
            SelectedAreas.Add(new TaskClassificationChipViewModel(
                areaId,
                names.TryGetValue(areaId, out var name) ? name : areaId,
                !names.ContainsKey(areaId)));
        }
    }

    private void NotifyDraftChanged() => draftChanged?.Invoke(new TaskClassificationSnapshot(
        draftIsGoal,
        draftAreaIds.Distinct(StringComparer.Ordinal).ToArray()));
}

public sealed record TaskClassificationSnapshot(bool IsGoal, IReadOnlyList<string> AreaIds);

public sealed record TaskClassificationAreaDefinition(
    string Id,
    string Name,
    bool IsArchived = false,
    int SortOrder = 0);

public sealed class TaskClassificationAreaOptionViewModel : ReactiveObject
{
    private bool isSelected;

    public TaskClassificationAreaOptionViewModel(string id, string name, bool isArchived, bool isSelected)
    {
        Id = id;
        Name = name;
        IsArchived = isArchived;
        this.isSelected = isSelected;
    }

    public event Action<TaskClassificationAreaOptionViewModel, bool>? SelectionChanged;

    public string Id { get; }

    public string Name { get; }

    public bool IsArchived { get; }

    public bool CanSelect => !IsArchived || IsSelected;

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected != value)
            {
                this.RaiseAndSetIfChanged(ref isSelected, value);
                this.RaisePropertyChanged(nameof(CanSelect));
                SelectionChanged?.Invoke(this, value);
            }
        }
    }
}

public sealed record TaskClassificationChipViewModel(string Id, string Name, bool IsUnresolved);
