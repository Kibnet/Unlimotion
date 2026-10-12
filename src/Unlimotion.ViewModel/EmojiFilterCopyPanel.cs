using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using Unlimotion.ViewModel.Localization;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel;

/// <summary>One-shot selection transfer and one local undo; never shares mutable filter items.</summary>
public sealed class EmojiFilterCopyPanel : ReactiveObject, IDisposable
{
    private readonly Func<ReadOnlyObservableCollection<EmojiFilter>> include;
    private readonly Func<ReadOnlyObservableCollection<EmojiFilter>> exclude;
    private readonly ILocalizationService localization;
    private readonly SerialDisposable observations = new();
    private Snapshot? undo;
    private Snapshot? observed;
    private EmojiFilterCopyPanel? copiedFrom;
    private bool applying;
    private bool attached;

    public EmojiFilterCopyPanel(string titleKey,
        Func<ReadOnlyObservableCollection<EmojiFilter>> include,
        Func<ReadOnlyObservableCollection<EmojiFilter>> exclude)
    {
        TitleKey = titleKey;
        this.include = include;
        this.exclude = exclude;
        localization = LocalizationService.Current;
        localization.CultureChanged += HandleCultureChange;
    }

    public string TitleKey { get; }
    public string Title => L10n.Get(TitleKey);
    public IReadOnlyList<EmojiFilterCopyPanel> Sources { get; internal set; } = Array.Empty<EmojiFilterCopyPanel>();
    public bool CanUndo => undo != null;
    public bool CanCopy => attached;
    public string Feedback => copiedFrom == null ? "" : L10n.Format("EmojiFiltersCopied", copiedFrom.Title);
    public string Preview => PreviewText(limit: 5);
    public string FullPreview => PreviewText(limit: int.MaxValue);

    internal void Attach()
    {
        Detach();
        observed = Capture();
        attached = true;
        this.RaisePropertyChanged(nameof(CanCopy));
        var subscriptions = new CompositeDisposable();
        observations.Disposable = subscriptions;
        subscriptions.Add(include().ToObservableChangeSet().AutoRefreshOnObservable(filter => filter.WhenAnyValue(item => item.ShowTasks))
            .Subscribe(_ => HandleSelectionChange()));
        subscriptions.Add(exclude().ToObservableChangeSet().AutoRefreshOnObservable(filter => filter.WhenAnyValue(item => item.ShowTasks))
            .Subscribe(_ => HandleSelectionChange()));
        RaisePreview();
    }

    internal void Detach()
    {
        attached = false;
        this.RaisePropertyChanged(nameof(CanCopy));
        observations.Disposable = null;
        observed = null;
        ForgetUndo();
    }

    public void CopyFrom(EmojiFilterCopyPanel source)
    {
        if (!attached || !source.attached || !Sources.Contains(source)) return;
        var next = source.Capture();
        var previous = Capture();
        if (previous.SameAs(next)) return;
        Apply(next);
        undo = previous;
        copiedFrom = source;
        RaiseFeedback();
    }

    public void Undo()
    {
        if (undo is not { } previous) return;
        Apply(previous);
        ForgetUndo();
    }

    internal void ForgetUndo()
    {
        undo = null;
        copiedFrom = null;
        RaiseFeedback();
    }

    private void Apply(Snapshot snapshot)
    {
        applying = true;
        try
        {
            ApplySet(include(), snapshot.Include);
            ApplySet(exclude(), snapshot.Exclude);
            observed = Capture();
        }
        finally { applying = false; }
        RaisePreview();
    }

    private static void ApplySet(IEnumerable<EmojiFilter> filters, string[] keys)
    {
        var items = filters.ToArray();
        // The master checkbox changes every item reentrantly. Set it first, then restore exact keys.
        var master = items.FirstOrDefault(filter => string.IsNullOrEmpty(filter.Emoji));
        if (master != null) master.ShowTasks = keys.Contains("", StringComparer.Ordinal);
        foreach (var item in items.Where(filter => !string.IsNullOrEmpty(filter.Emoji)))
            item.ShowTasks = keys.Contains(item.Emoji, StringComparer.Ordinal);
    }

    private Snapshot Capture() => new(Selected(include()), Selected(exclude()));
    private static string[] Selected(IEnumerable<EmojiFilter> filters) => filters
        .Where(filter => filter.ShowTasks).Select(filter => filter.Emoji).Order(StringComparer.Ordinal).ToArray();

    private void HandleSelectionChange()
    {
        var current = Capture();
        if (!applying && undo != null &&
            (observed == null || !observed.SameAs(current) || !KeysExist(include(), undo.Include) || !KeysExist(exclude(), undo.Exclude)))
            ForgetUndo();
        observed = current;
        RaisePreview();
    }

    private static bool KeysExist(IEnumerable<EmojiFilter> filters, string[] keys)
    {
        var available = filters.Select(filter => filter.Emoji).ToHashSet(StringComparer.Ordinal);
        return keys.All(available.Contains);
    }

    private string PreviewText(int limit)
    {
        var snapshot = Capture();
        if (snapshot.Include.Length == 0 && snapshot.Exclude.Length == 0) return L10n.Get("EmojiFiltersUnrestricted");
        return L10n.Format("EmojiFiltersSourcePreview",
            Summary(snapshot.Include, "All", limit), Summary(snapshot.Exclude, "EmojiFiltersNone", limit));
    }

    private static string Summary(string[] keys, string emptyKey, int limit)
    {
        if (keys.Length == 0) return L10n.Get(emptyKey);
        var emojis = keys.Where(key => key.Length > 0).ToArray();
        if (emojis.Length == 0) return L10n.Get("All");
        var text = string.Join(" ", emojis.Take(limit));
        return text + (emojis.Length > limit ? " …" : "");
    }

    private void RaisePreview()
    {
        this.RaisePropertyChanged(nameof(Preview));
        this.RaisePropertyChanged(nameof(FullPreview));
    }

    private void RaiseFeedback()
    {
        this.RaisePropertyChanged(nameof(CanUndo));
        this.RaisePropertyChanged(nameof(Feedback));
    }

    private void HandleCultureChange(object? sender, EventArgs args)
    {
        this.RaisePropertyChanged(nameof(Title));
        RaisePreview();
        RaiseFeedback();
    }

    public void Dispose()
    {
        attached = false;
        ForgetUndo();
        localization.CultureChanged -= HandleCultureChange;
        observations.Dispose();
    }

    private sealed record Snapshot(string[] Include, string[] Exclude)
    {
        public bool SameAs(Snapshot other) => Include.SequenceEqual(other.Include) && Exclude.SequenceEqual(other.Exclude);
    }
}
