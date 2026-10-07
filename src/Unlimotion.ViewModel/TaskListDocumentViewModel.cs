using System;
using System.Collections.Generic;
using System.Linq;
using Unlimotion.ViewModel.Search;
using Unlimotion.ViewModel.Workspace;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.ViewModel;

public sealed record TaskListFilterSnapshot(
    string SearchText,
    bool IsFuzzySearch,
    string? SortId,
    bool? Wanted,
    IReadOnlyDictionary<DomainTaskStatus, bool> Statuses,
    IReadOnlyDictionary<string, bool> IncludeEmoji,
    IReadOnlyDictionary<string, bool> ExcludeEmoji,
    IReadOnlyDictionary<string, bool> Timing,
    IReadOnlyDictionary<string, bool> Duration,
    string? DateOptionId,
    DateTime? DateFrom,
    DateTime? DateTo,
    bool DateIsCustom,
    bool RoadmapOnlyUnlocked);

/// <summary>One logical list owns its state; task data remains in the common repository.</summary>
public sealed class TaskListDocumentViewModel(MainWindowViewModel owner, TaskListKind kind)
{
    public MainWindowViewModel Owner { get; } = owner;
    public TaskListKind Kind { get; } = kind;

    private TaskListFilterScope? FilterScope => Kind switch
    {
        TaskListKind.LastCreated => Owner.LastCreatedFilter,
        TaskListKind.LastUpdated => Owner.LastUpdatedFilter,
        TaskListKind.InProgress => Owner.InProgressFilter,
        TaskListKind.Completed => Owner.CompletedFilter,
        TaskListKind.Archived => Owner.ArchivedFilter,
        TaskListKind.LastOpened => Owner.LastOpenedFilter,
        TaskListKind.Roadmap => Owner.RoadmapFilter,
        _ => null
    };

    public SearchDefinition Search => Kind switch
    {
        TaskListKind.AllTasks => Owner.Search,
        TaskListKind.Unlocked => Owner.UnlockedSearch,
        _ => FilterScope!.Search
    };

    private IEnumerable<TaskStatusFilter> Statuses => Kind switch
    {
        TaskListKind.AllTasks => Owner.StatusFilters,
        TaskListKind.LastCreated => Owner.LastCreatedStatusFilters,
        TaskListKind.LastUpdated => Owner.LastUpdatedStatusFilters,
        TaskListKind.Unlocked => Owner.UnlockedStatusFilters,
        TaskListKind.InProgress => Owner.InProgressStatusFilters,
        TaskListKind.Completed => Owner.CompletedStatusFilters,
        TaskListKind.Archived => Owner.ArchivedStatusFilters,
        TaskListKind.LastOpened => Owner.LastOpenedStatusFilters,
        TaskListKind.Roadmap => Owner.RoadmapStatusFilters,
        _ => []
    };

    private IEnumerable<EmojiFilter> IncludeEmoji => Kind switch
    {
        TaskListKind.AllTasks => Owner.EmojiFilters,
        TaskListKind.Unlocked => Owner.UnlockedEmojiFilters,
        _ => FilterScope!.EmojiFilters
    };

    private IEnumerable<EmojiFilter> ExcludeEmoji => Kind switch
    {
        TaskListKind.AllTasks => Owner.EmojiExcludeFilters,
        TaskListKind.Unlocked => Owner.UnlockedEmojiExcludeFilters,
        _ => FilterScope!.EmojiExcludeFilters
    };

    private DateFilter? Date => Kind switch
    {
        TaskListKind.LastCreated => Owner.LastCreatedDateFilter,
        TaskListKind.LastUpdated => Owner.LastUpdatedDateFilter,
        TaskListKind.Completed => Owner.CompletedDateFilter,
        TaskListKind.Archived => Owner.ArchivedDateFilter,
        _ => null
    };

    public TaskListFilterSnapshot CaptureFilters() => new(
        Search.SearchText ?? string.Empty,
        Search.IsFuzzySearch,
        Kind == TaskListKind.AllTasks ? Owner.CurrentSortDefinition?.Id
            : Kind == TaskListKind.Unlocked ? Owner.CurrentSortDefinitionForUnlocked?.Id : null,
        Kind == TaskListKind.Unlocked ? Owner.ShowWanted
            : Kind == TaskListKind.Roadmap ? Owner.Graph.ShowWanted : null,
        Statuses.ToDictionary(filter => filter.Status, filter => filter.ShowTasks),
        IncludeEmoji.GroupBy(filter => filter.Emoji).ToDictionary(group => group.Key, group => group.First().ShowTasks),
        ExcludeEmoji.GroupBy(filter => filter.Emoji).ToDictionary(group => group.Key, group => group.First().ShowTasks),
        Kind == TaskListKind.Unlocked
            ? Owner.UnlockedTimeFilters.ToDictionary(filter => filter.ResourceKey, filter => filter.ShowTasks)
            : new Dictionary<string, bool>(),
        Kind == TaskListKind.Unlocked
            ? Owner.DurationFilters.ToDictionary(filter => filter.ResourceKey, filter => filter.ShowTasks)
            : new Dictionary<string, bool>(),
        Date?.CurrentOption?.Id,
        Date?.From,
        Date?.To,
        Date?.IsCustom ?? false,
        Kind == TaskListKind.Roadmap && Owner.Graph.OnlyUnlocked);

    public void RestoreFilters(TaskListFilterSnapshot state)
    {
        Search.SearchText = state.SearchText;
        Search.IsFuzzySearch = state.IsFuzzySearch;

        if (state.SortId is { } sortId && Owner.SortDefinitions.FirstOrDefault(sort => sort.Id == sortId) is { } definition)
        {
            if (Kind == TaskListKind.AllTasks) Owner.CurrentSortDefinition = definition;
            else if (Kind == TaskListKind.Unlocked) Owner.CurrentSortDefinitionForUnlocked = definition;
        }
        if (Kind == TaskListKind.Unlocked) Owner.ShowWanted = state.Wanted;
        if (Kind == TaskListKind.Roadmap)
        {
            Owner.Graph.ShowWanted = state.Wanted;
            Owner.Graph.OnlyUnlocked = state.RoadmapOnlyUnlocked;
        }
        foreach (var filter in Statuses)
            if (state.Statuses.TryGetValue(filter.Status, out var value)) filter.ShowTasks = value;
        foreach (var filter in IncludeEmoji)
            if (state.IncludeEmoji.TryGetValue(filter.Emoji, out var value)) filter.ShowTasks = value;
        foreach (var filter in ExcludeEmoji)
            if (state.ExcludeEmoji.TryGetValue(filter.Emoji, out var value)) filter.ShowTasks = value;
        if (Kind == TaskListKind.Unlocked)
        {
            foreach (var filter in Owner.UnlockedTimeFilters)
                if (state.Timing.TryGetValue(filter.ResourceKey, out var value)) filter.ShowTasks = value;
            foreach (var filter in Owner.DurationFilters)
                if (state.Duration.TryGetValue(filter.ResourceKey, out var value)) filter.ShowTasks = value;
        }
        if (Date is { } date)
        {
            date.CurrentOption = DateFilterDefinition.FindById(state.DateOptionId);
            date.From = state.DateFrom;
            date.To = state.DateTo;
            date.IsCustom = state.DateIsCustom;
        }
    }
}
