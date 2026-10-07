using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Workspace;

namespace Unlimotion.Views;

/// <summary>One list projection. No nested mode tabs and no task detail surface.</summary>
public partial class TaskListDocumentView : TaskPresentationControl
{
    private int _restoreVersion;
    private (TreeView Tree, bool Original, int Version)? _pendingAutoScrollRestore;
    public TaskListDocumentView() : base(isDocumentPresentation: true)
    {
        InitializeComponent();
        InitializePresentation();
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(UpdateDocumentAutomationIds,
            DispatcherPriority.Loaded);
        DetachedFromVisualTree += (_, _) => CancelPendingRestore();
        AddHandler(PointerPressedEvent, (_, _) => CancelPendingRestore(), RoutingStrategies.Tunnel, true);
        AddHandler(PointerWheelChangedEvent, (_, _) => CancelPendingRestore(), RoutingStrategies.Tunnel, true);
        AddHandler(KeyDownEvent, (_, _) => CancelPendingRestore(), RoutingStrategies.Tunnel, true);
    }

    public TaskListDocumentView(MainWindowViewModel owner, TaskListKind kind) : this()
    {
        Kind = kind;
        Document = new TaskListDocumentViewModel(owner, kind);
        DataContext = owner;
        if (Resources[$"{kind}DocumentTemplate"] is not IDataTemplate template)
            throw new InvalidOperationException($"No task-list presentation registered for {kind}.");
        Content = template.Build(owner);
        SetDocumentTaskCategory((int)kind);
    }

    public TaskListKind Kind { get; }
    public TaskListDocumentViewModel Document { get; } = null!;
    public TreeView? TaskTree => Kind == TaskListKind.Roadmap
        ? null : FindPresentationControl<TreeView>($"{Kind}Tree");

    private ScrollViewer? ListScroll => TaskTree?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    public override object CaptureViewState()
    {
        var tree = TaskTree;
        var visible = WalkExpanded(tree?.ItemsSource?.Cast<TaskWrapperViewModel>() ?? []).ToArray();
        return new TaskListDocumentState(Kind, Document.CaptureFilters(),
            ListScroll?.Offset.X ?? 0, ListScroll?.Offset.Y ?? 0,
            tree?.SelectedItems?.OfType<TaskWrapperViewModel>().Select(WrapperKey).ToArray() ?? [],
            visible.Where(wrapper => wrapper.IsExpanded).Select(WrapperKey).ToArray(),
            this.GetVisualDescendants().OfType<GraphControl>().FirstOrDefault()?.CaptureDocumentState());
    }

    public override void RestoreViewState(object? state)
    {
        if (state is not TaskListDocumentState snapshot || snapshot.Kind != Kind) return;
        CancelPendingRestore();
        var version = ++_restoreVersion;
        var scope = Document.Owner.WorkspaceNavigation.ScopeRevision;
        if (TaskTree is { } restoringTree)
        {
            _pendingAutoScrollRestore = (restoringTree, restoringTree.AutoScrollToSelectedItem, version);
            restoringTree.SetCurrentValue(TreeView.AutoScrollToSelectedItemProperty, false);
        }
        try { Document.RestoreFilters(snapshot.Filters); }
        catch { RestoreAutoScroll(version); throw; }
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (!IsCurrentRestore(version, scope)) { RestoreAutoScroll(version); return; }
                if (TaskTree is { } tree)
                {
                    var expanded = snapshot.ExpandedIds.ToHashSet(StringComparer.Ordinal);
                    RestoreExpansion(tree.ItemsSource?.Cast<TaskWrapperViewModel>() ?? [], expanded);
                    var selected = snapshot.SelectedIds.ToHashSet(StringComparer.Ordinal);
                    var visible = WalkExpanded(tree.ItemsSource?.Cast<TaskWrapperViewModel>() ?? []);
                    tree.SelectedItems?.Clear();
                    foreach (var wrapper in visible.Where(wrapper => selected.Contains(WrapperKey(wrapper))))
                        tree.SelectedItems?.Add(wrapper);
                }
                if (snapshot.Roadmap is { } roadmap)
                    this.GetVisualDescendants().OfType<GraphControl>().FirstOrDefault()?.RestoreDocumentState(roadmap);
                UpdateDocumentAutomationIds();
                // TreeView otherwise posts BringIntoView at Loaded when a selected
                // container is realized, overriding the explicitly saved viewport.
                (TopLevel.GetTopLevel(this) as Window)?.UpdateLayout();
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (!IsCurrentRestore(version, scope)) return;
                        (TopLevel.GetTopLevel(this) as Window)?.UpdateLayout();
                        if (ListScroll is { } scroll) scroll.Offset = new Vector(snapshot.OffsetX, snapshot.OffsetY);
                    }
                    finally { RestoreAutoScroll(version); }
                }, DispatcherPriority.Loaded);
            }
            catch { RestoreAutoScroll(version); throw; }
        }, DispatcherPriority.Loaded);
    }

    private bool IsCurrentRestore(int version, long scope) => !IsPresentationDisposed
        && this.IsAttachedToVisualTree() && version == _restoreVersion
        && scope == Document.Owner.WorkspaceNavigation.ScopeRevision;

    private void RestoreAutoScroll(int version)
    {
        if (_pendingAutoScrollRestore is not { } pending || pending.Version != version) return;
        _pendingAutoScrollRestore = null;
        pending.Tree.SetCurrentValue(TreeView.AutoScrollToSelectedItemProperty, pending.Original);
    }

    private void CancelPendingRestore()
    {
        _restoreVersion++;
        if (Kind == TaskListKind.AllTasks && Document is not null)
            Document.Owner.CancelAllTasksSelectionRestore();
        if (_pendingAutoScrollRestore is { } pending) RestoreAutoScroll(pending.Version);
    }

    public override void Dispose()
    {
        CancelPendingRestore();
        base.Dispose();
    }

    private void UpdateDocumentAutomationIds()
    {
        foreach (var input in this.GetVisualDescendants().OfType<TextBox>()
                     .Where(input => input.Name == "SearchEditor"))
            AutomationProperties.SetAutomationId(input, "TaskListSearchBox");
    }

    private static string WrapperKey(TaskWrapperViewModel wrapper)
    {
        var path = new Stack<string>();
        for (var current = wrapper; current is not null; current = current.Parent) path.Push(current.TaskItem.Id);
        return string.Join("/", path);
    }

    private static IEnumerable<TaskWrapperViewModel> WalkExpanded(IEnumerable<TaskWrapperViewModel> roots)
    {
        foreach (var wrapper in roots)
        {
            yield return wrapper;
            if (!wrapper.IsExpanded) continue;
            foreach (var child in WalkExpanded(wrapper.SubTasks)) yield return child;
        }
    }

    private static void RestoreExpansion(IEnumerable<TaskWrapperViewModel> roots, HashSet<string> expanded)
    {
        foreach (var wrapper in roots)
        {
            wrapper.IsExpanded = expanded.Contains(WrapperKey(wrapper));
            if (wrapper.IsExpanded) RestoreExpansion(wrapper.SubTasks, expanded);
        }
    }
}
