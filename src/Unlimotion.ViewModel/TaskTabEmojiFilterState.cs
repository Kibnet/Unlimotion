using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;

namespace Unlimotion.ViewModel;

/// <summary>One tab's selection; catalog metadata can be shared, mutable filter items cannot.</summary>
public sealed class TaskTabEmojiFilterState : IDisposable
{
    private readonly ObservableCollectionExtended<EmojiFilter> include = new();
    private readonly ObservableCollectionExtended<EmojiFilter> exclude = new();
    private readonly CompositeDisposable subscriptions = new();
    private readonly EmojiFilter allInclude = new() { Title = "All", SortText = "\u0000" };
    private readonly EmojiFilter allExclude = new() { Title = "All", SortText = "\u0000" };

    public TaskTabEmojiFilterState()
    {
        IncludeFilters = new(include);
        ExcludeFilters = new(exclude);
        subscriptions.Add(allInclude.WhenAnyValue(filter => filter.ShowTasks)
            .Subscribe(value => ToggleAll(include, value)));
        subscriptions.Add(allExclude.WhenAnyValue(filter => filter.ShowTasks)
            .Subscribe(value => ToggleAll(exclude, value)));
    }

    public ReadOnlyObservableCollection<EmojiFilter> IncludeFilters { get; }
    public ReadOnlyObservableCollection<EmojiFilter> ExcludeFilters { get; }

    internal void UpdateInclude(IEnumerable<EmojiFilter> catalog) => Synchronize(include, allInclude, catalog);
    internal void UpdateExclude(IEnumerable<EmojiFilter> catalog) => Synchronize(exclude, allExclude, catalog);

    public void Reset()
    {
        ToggleAll(include, false);
        ToggleAll(exclude, false);
    }

    internal void Clear()
    {
        Reset();
        include.Clear();
        exclude.Clear();
    }

    internal IObservable<Func<TaskItemViewModel, bool>> CreatePredicate(bool excluding) =>
        (excluding ? ExcludeFilters : IncludeFilters).ToObservableChangeSet()
            .AutoRefreshOnObservable(filter => filter.WhenAnyValue(item => item.ShowTasks, item => item.CatalogRevision))
            .ToCollection()
            .Select(filters =>
            {
                var selected = filters.Where(filter => filter.ShowTasks).ToArray();
                bool Predicate(TaskItemViewModel task)
                {
                    if (selected.Length == 0) return true;
                    var matches = selected.Any(filter => !string.IsNullOrEmpty(filter.Emoji) &&
                        (task.GetAllEmoji.Contains(filter.Emoji) || (task.Title ?? "").Contains(filter.Emoji)));
                    return excluding ? !matches : matches;
                }
                return (Func<TaskItemViewModel, bool>)Predicate;
            });

    private static void ToggleAll(IEnumerable<EmojiFilter> filters, bool value)
    {
        // Notifications can regroup the catalog reentrantly.
        foreach (var filter in filters.ToArray()) filter.ShowTasks = value;
    }

    private static void Synchronize(ObservableCollectionExtended<EmojiFilter> target,
        EmojiFilter all, IEnumerable<EmojiFilter> catalog)
    {
        var source = catalog.ToArray();
        var keys = source.Select(filter => filter.Emoji).ToHashSet(StringComparer.Ordinal);
        foreach (var old in target.Where(filter => !keys.Contains(filter.Emoji)).ToArray()) target.Remove(old);
        for (var index = 0; index < source.Length; index++)
        {
            var metadata = source[index];
            var local = target.FirstOrDefault(filter => filter.Emoji == metadata.Emoji);
            if (local == null)
            {
                local = string.IsNullOrEmpty(metadata.Emoji) ? all : new EmojiFilter { Emoji = metadata.Emoji };
                local.ShowTasks = false;
                target.Insert(index, local);
            }
            local.Title = metadata.Title;
            local.SortText = metadata.SortText;
            local.Source = metadata.Source;
            local.CatalogRevision = metadata.CatalogRevision;
            var currentIndex = target.IndexOf(local);
            if (currentIndex != index) target.Move(currentIndex, index);
        }
        // The UI builds hierarchy from Source using this tab's local filter instances.
    }

    public void Dispose() => subscriptions.Dispose();
}
