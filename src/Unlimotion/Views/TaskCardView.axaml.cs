using System;
using Avalonia;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI;
using Unlimotion.ViewModel;
using Unlimotion.Services;
using Unlimotion.TaskTree;

namespace Unlimotion.Views;

/// <summary>Only a routed card, without the legacy list or category tab strip.</summary>
public partial class TaskCardView : TaskPresentationControl
{
    private IDisposable? _criterionFocus;
    private IDisposable? _relationFocus;
    private IDisposable? _historySave;
    private IDisposable? _historyWatcher;
    private IDisposable? _historyScope;
    private IDisposable? _historyRoute;
    private TaskHistoryFieldChangeView? _expandedHistoryField;
    public TaskHistoryPaneViewModel TaskHistory { get; }

    public TaskCardView() : this(new GitTaskHistoryProvider()) { }

    private TaskCardView(ITaskHistoryProvider provider) : base(isDocumentPresentation: true)
    {
        TaskHistory = new TaskHistoryPaneViewModel(provider);
        InitializeComponent();
        InitializePresentation();
        SizeChanged += (_, _) => TaskHistoryExpander.MaxHeight = Math.Max(28, Math.Min(480, Bounds.Height * .45));
        DetachedFromVisualTree += (_, _) => { if (!IsPresentationDisposed) { CloseTaskHistoryDetails(); TaskHistory.Dispose(); } };
        AttachedToVisualTree += (_, _) => RefreshTaskHistoryIfVisible();
    }

    public TaskCardView(MainWindowViewModel owner, TaskItemViewModel task)
        : this(owner, task, new GitTaskHistoryProvider()) { }

    internal TaskCardView(MainWindowViewModel owner, TaskItemViewModel task, ITaskHistoryProvider provider) : this(provider)
    {
        CardContext = new TaskCardDocumentViewModel(owner, task);
        DataContext = owner;
        RouteTaskItem = task;
        _criterionFocus = task.WhenAnyValue(item => item.CompletionCriterionFocusRequestVersion)
            .Skip(1).Subscribe(request => QueueCompletionCriterionFocus(request,
                task.Id, task.CompletionCriterionFocusTargetId, 5));
        _relationFocus = CardContext.RelationEditor.WhenAnyValue(editor => editor.FocusRequestVersion)
            .Skip(1).Subscribe(_ => FocusRelationInput());
        SelectHistoryTask();
        _historySave = task.SaveItemCommand.Subscribe(_ => Dispatcher.UIThread.Post(RefreshTaskHistoryIfVisible));
        if ((owner.taskRepository?.TaskTreeManager.Storage as FileStorage)?.Watcher is IRawDatabaseWatcher watcher)
            _historyWatcher = Observable.FromEventPattern<EventHandler<DbUpdatedEventArgs>, DbUpdatedEventArgs>(
                    handler => watcher.OnRawUpdated += handler, handler => watcher.OnRawUpdated -= handler)
                .Throttle(TimeSpan.FromMilliseconds(250))
                .Subscribe(_ => Dispatcher.UIThread.Post(RefreshTaskHistoryIfVisible));
        _historyScope = owner.WorkspaceNavigation.WhenAnyValue(nav => nav.ScopeRevision).Skip(1)
            .Subscribe(revision =>
            {
                CloseTaskHistoryDetails();
                TaskHistory.Dispose();
                _historyOwnerScope = revision;
                Dispatcher.UIThread.Post(RefreshTaskHistoryIfVisible);
            });
        _historyRoute = this.GetObservable(RouteTaskItemProperty).Skip(1)
            .Subscribe(route => { if (!ReferenceEquals(route, task)) { CloseTaskHistoryDetails(); TaskHistory.Dispose(); } });
    }

    private bool CanUseHistory => !IsPresentationDisposed && CardContext is { } context &&
        ReferenceEquals(RouteTaskItem, context.Task) &&
        ReferenceEquals(context.Owner.ResolveTaskById(context.Task.Id), context.Task) &&
        context.Owner.WorkspaceNavigation.ScopeRevision == _historyOwnerScope;
    private long _historyOwnerScope;

    private void SelectHistoryTask()
    {
        if (CardContext is not { } context) return;
        _historyOwnerScope = context.Owner.WorkspaceNavigation.ScopeRevision;
        TaskHistory.SelectTask((context.Owner.taskRepository?.TaskTreeManager.Storage as FileStorage)?.Path,
            context.Task.SourceId, context.Task.Id);
    }

    private void RefreshTaskHistoryIfVisible()
    {
        if (!CanUseHistory || TopLevel.GetTopLevel(this) is null || !TaskHistoryExpander.IsExpanded || !TaskHistory.IsGitMode) return;
        CloseTaskHistoryDetails();
        _ = TaskHistory.RefreshAsync();
    }

    private void TaskHistoryExpander_OnExpanded(object? sender, RoutedEventArgs e) => RefreshTaskHistoryIfVisible();
    private void TaskHistoryExpander_OnCollapsed(object? sender, RoutedEventArgs e)
    { CloseTaskHistoryDetails(); TaskHistory.Dispose(); }
    private async void TaskHistoryRefreshButton_OnClick(object? sender, RoutedEventArgs e)
    { if (CanUseHistory) { CloseTaskHistoryDetails(); await TaskHistory.RefreshAsync(); } e.Handled = true; }
    private async void TaskHistoryLoadMoreButton_OnClick(object? sender, RoutedEventArgs e)
    { if (CanUseHistory) await TaskHistory.LoadMoreAsync(); e.Handled = true; }
    private void TaskHistoryGitModeButton_OnClick(object? sender, RoutedEventArgs e)
    { TaskHistory.IsGitMode = true; RefreshTaskHistoryIfVisible(); e.Handled = true; }
    private void TaskHistoryStatusModeButton_OnClick(object? sender, RoutedEventArgs e)
    { CloseTaskHistoryDetails(); TaskHistory.IsGitMode = false; e.Handled = true; }
    private async void TaskHistoryCopyCommit_OnClick(object? sender, RoutedEventArgs e)
    {
        if (CanUseHistory && sender is Button { DataContext: TaskHistoryEntry { CommitSha: { } sha } } &&
            TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(sha);
        e.Handled = true;
    }
    internal string? ResolveTaskHistoryTaskTitle(string taskId)
    {
        if (!CanUseHistory || CardContext?.Owner.taskRepository is not { } repository) return null;
        var task = repository.Tasks.Lookup(taskId);
        return task.HasValue && task.Value.SourceId == CardContext.Task.SourceId ? task.Value.Title : null;
    }
    internal async Task ToggleTaskHistoryDetailsAsync(TaskHistoryFieldChangeView field)
    {
        var selected = ReferenceEquals(_expandedHistoryField, field);
        CloseTaskHistoryDetails();
        if (selected || !CanUseHistory || field.DataContext is not TaskHistoryFieldChange change) return;
        _expandedHistoryField = field;
        await TaskHistory.ShowDetailsAsync(change);
        if (CanUseHistory && ReferenceEquals(_expandedHistoryField, field) && ReferenceEquals(field.DataContext, change) &&
            TopLevel.GetTopLevel(field) is not null && TaskHistory.HasDetails)
            field.ExpandDetails(TaskHistory.DetailOldValue, TaskHistory.DetailNewValue);
    }
    internal void CloseTaskHistoryDetails(TaskHistoryFieldChangeView? field = null)
    {
        if (field is not null && !ReferenceEquals(field, _expandedHistoryField)) return;
        _expandedHistoryField?.CollapseDetails();
        _expandedHistoryField = null;
        TaskHistory.ClearDetails();
    }

    private void FocusRelationInput() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        if (CardContext?.RelationEditor.InputAutomationId is not { } id) return;
        var input = this.GetVisualDescendants()
            .OfType<TextBox>().FirstOrDefault(control => Avalonia.Automation.AutomationProperties.GetAutomationId(control) == id);
        input?.Focus();
    }, DispatcherPriority.Loaded);

    public override void Dispose()
    {
        if (IsPresentationDisposed) return;
        _criterionFocus?.Dispose();
        _relationFocus?.Dispose();
        _historySave?.Dispose();
        _historyWatcher?.Dispose();
        _historyScope?.Dispose();
        _historyRoute?.Dispose();
        CloseTaskHistoryDetails();
        TaskHistory.Dispose();
        base.Dispose();
    }
}
