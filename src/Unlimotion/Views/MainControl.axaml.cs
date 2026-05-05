using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Views
{
    public partial class MainControl : UserControl
    {
        private enum TreeCommandRoute
        {
            Hotkey,
            ContextMenu
        }

        private static readonly string[] KnownMainTaskTreeNames =
        [
            "AllTasksTree",
            "LastCreatedTree",
            "LastUpdatedTree",
            "UnlockedTree",
            "CompletedTree",
            "ArchivedTree",
            "LastOpenedTree"
        ];

        // Static dependencies - set once during app initialization
        public static IDialogs? DialogsInstance { get; set; }
        private const int MaxTitleFocusRetries = 5;
        private const int MaxRelationEditorFocusRetries = 5;
        private IDisposable? _titleFocusSubscription;
        private IDisposable? _relationEditorFocusSubscription;
        private MainWindowViewModel? _treeCommandViewModel;
        private TreeView? _activeTaskTree;
        private TreeView? _contextMenuTree;
        private TaskWrapperViewModel? _contextMenuWrapper;
        private PendingTreeDragContext? _pendingTreeDrag;
        private TextBox? _activeInlineTitleEditor;
        private TreeView? _lastInlineTitleClickTree;
        private string? _lastInlineTitleClickTaskId;
        private bool _treeDragInProgress;

        private sealed class PendingTreeDragContext(
            Control control,
            TreeView tree,
            TaskWrapperViewModel wrapper,
            Point startPoint,
            IReadOnlyList<TaskWrapperViewModel> selectionSnapshot,
            bool wasSelected)
        {
            public Control Control { get; } = control;
            public TreeView Tree { get; } = tree;
            public TaskWrapperViewModel Wrapper { get; } = wrapper;
            public Point StartPoint { get; } = startPoint;
            public IReadOnlyList<TaskWrapperViewModel> SelectionSnapshot { get; } = selectionSnapshot;
            public bool WasSelected { get; } = wasSelected;
        }

        private sealed class TaskTreeDragData(IReadOnlyList<TaskWrapperViewModel> wrappers)
        {
            public IReadOnlyList<TaskWrapperViewModel> Wrappers { get; } =
                wrappers.NormalizeForDeleteBatch();
        }

        private enum BatchDropOperationKind
        {
            CopyInto,
            MoveInto,
            CloneInto,
            SourcesBlockTarget,
            TargetBlocksSources
        }

        private sealed class DragSourceOperationItem(
            TaskItemViewModel taskItem,
            TaskItemViewModel? sourceParent = null,
            TaskWrapperViewModel? wrapper = null)
        {
            public TaskItemViewModel TaskItem { get; } = taskItem;
            public TaskItemViewModel? SourceParent { get; } = sourceParent;
            public TaskWrapperViewModel? Wrapper { get; } = wrapper;
        }

        public MainControl()
        {
            InitializeComponent();
            AddHandler(KeyDownEvent, MainControl_OnKeyDown, RoutingStrategies.Tunnel);
            AddHandler(DragDrop.DropEvent, Drop);
            AddHandler(DragDrop.DragOverEvent, DragOver);
            DataContextChanged += MainWindow_DataContextChanged;
        }

        private void MainControl_OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Handled || !IsSelectAllHotkey(e) || IsTextInputFocused())
            {
                return;
            }

            ExecuteTreeCommand(TreeCommandKind.SelectAll);
            e.Handled = true;
        }

        private void MainWindow_DataContextChanged(object? sender, EventArgs e)
        {
            _titleFocusSubscription?.Dispose();
            _titleFocusSubscription = null;
            _relationEditorFocusSubscription?.Dispose();
            _relationEditorFocusSubscription = null;
            if (_treeCommandViewModel != null)
            {
                _treeCommandViewModel.ExecuteTreeCommandAction = null;
                _treeCommandViewModel.SetClipboardTextAsync = null;
                _treeCommandViewModel.GetClipboardTextAsync = null;
                _treeCommandViewModel = null;
            }

            _activeTaskTree = null;
            _contextMenuTree = null;
            _contextMenuWrapper = null;
            ClearInlineTitleEditState();

            if (DataContext is MainWindowViewModel vm)
            {
                _treeCommandViewModel = vm;
                vm.ExecuteTreeCommandAction = ExecuteTreeCommand;
                vm.SetClipboardTextAsync = SetClipboardTextAsync;
                vm.GetClipboardTextAsync = GetClipboardTextAsync;
                _titleFocusSubscription = vm.WhenAnyValue(m => m.TitleFocusRequestVersion)
                    .Subscribe(requestVersion => QueueTitleFocus(requestVersion, vm.CurrentTaskItem?.Id, MaxTitleFocusRetries));
                _relationEditorFocusSubscription = vm.CurrentRelationEditor
                    .WhenAnyValue(m => m.FocusRequestVersion)
                    .Subscribe(requestVersion =>
                        QueueRelationEditorFocus(
                            requestVersion,
                            vm.CurrentRelationEditor.InputAutomationId,
                            MaxRelationEditorFocusRetries));
                vm.MoveToPath = ReactiveCommand.CreateFromTask(async () =>
                {
                    if (vm.CurrentTaskItem == null)
                        return;
                    var dialogs = DialogsInstance;
                    if (dialogs == null) return;
                    
                    var path = await dialogs.ShowOpenFolderDialogAsync(L10n.Get("FolderPickerTaskStoragePath"));

                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        var fileStorage = new FileStorage(path);
                        var set = new HashSet<string>();
                        var queue = new Queue<TaskItemViewModel>();
                        queue.Enqueue(vm.CurrentTaskItem);
                        while (queue.Count > 0)
                        {
                            var task = queue.Dequeue();
                            if (!set.Contains(task.Id))
                            {
                                set.Add(task.Id);
                                await fileStorage.Save(task.Model);
                                foreach (var item in task.ContainsTasks)
                                {
                                    queue.Enqueue(item);
                                }
                            }
                        }

                        var currentTaskStorage = vm.taskRepository?.TaskTreeManager.Storage;
                        if (currentTaskStorage != null)
                        {
                            foreach (var id in set)
                            {
                                await currentTaskStorage.Remove(id);
                            }
                        }
                    }
                });
            }
        }

        private async Task SetClipboardTextAsync(string text)
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.ManagerWrapper?.ErrorToast(L10n.Get("ClipboardUnavailable"));
                }

                return;
            }

            await clipboard.SetTextAsync(text);
        }

        private async Task<string?> GetClipboardTextAsync()
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.ManagerWrapper?.ErrorToast(L10n.Get("ClipboardUnavailable"));
                }

                return null;
            }

            return await clipboard.GetTextAsync();
        }

        private void QueueTitleFocus(long requestVersion, string? targetTaskId, int retriesRemaining)
        {
            if (requestVersion <= 0 || string.IsNullOrWhiteSpace(targetTaskId))
            {
                return;
            }

            Dispatcher.UIThread.Post(
                () => TryFocusTitle(requestVersion, targetTaskId, retriesRemaining),
                DispatcherPriority.Background);
        }

        private void TryFocusTitle(long requestVersion, string targetTaskId, int retriesRemaining)
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            if (vm.TitleFocusRequestVersion != requestVersion || vm.CurrentTaskItem?.Id != targetTaskId)
            {
                return;
            }

            var titleTextBox = this.FindControl<TextBox>("CurrentTaskTitleTextBox");
            if (titleTextBox == null || !titleTextBox.IsAttachedToVisualTree() || !titleTextBox.IsVisible || !titleTextBox.IsEnabled)
            {
                RetryTitleFocus(requestVersion, targetTaskId, retriesRemaining);
                return;
            }

            if (!titleTextBox.Focus())
            {
                RetryTitleFocus(requestVersion, targetTaskId, retriesRemaining);
                return;
            }

            if (string.IsNullOrEmpty(titleTextBox.Text))
            {
                titleTextBox.CaretIndex = titleTextBox.Text?.Length ?? 0;
            }
            else
            {
                titleTextBox.SelectAll();
            }
        }

        private void RetryTitleFocus(long requestVersion, string targetTaskId, int retriesRemaining)
        {
            if (retriesRemaining <= 0)
            {
                return;
            }

            QueueTitleFocus(requestVersion, targetTaskId, retriesRemaining - 1);
        }

        private void QueueRelationEditorFocus(long requestVersion, string? automationId, int retriesRemaining)
        {
            if (requestVersion <= 0 || string.IsNullOrWhiteSpace(automationId))
            {
                return;
            }

            Dispatcher.UIThread.Post(
                () => TryFocusRelationEditor(requestVersion, automationId, retriesRemaining),
                DispatcherPriority.Background);
        }

        private void TryFocusRelationEditor(long requestVersion, string automationId, int retriesRemaining)
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            var editor = vm.CurrentRelationEditor;
            if (!editor.IsOpen ||
                editor.FocusRequestVersion != requestVersion ||
                !string.Equals(editor.InputAutomationId, automationId, StringComparison.Ordinal))
            {
                return;
            }

            var input = this.GetVisualDescendants()
                .OfType<TextBox>()
                .FirstOrDefault(candidate =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(candidate),
                        automationId,
                        StringComparison.Ordinal) &&
                    candidate.IsAttachedToVisualTree() &&
                    candidate.IsVisible &&
                    candidate.IsEnabled);

            if (input == null)
            {
                RetryRelationEditorFocus(requestVersion, automationId, retriesRemaining);
                return;
            }

            if (!input.Focus())
            {
                RetryRelationEditorFocus(requestVersion, automationId, retriesRemaining);
                return;
            }

            input.CaretIndex = input.Text?.Length ?? 0;
        }

        private void RetryRelationEditorFocus(long requestVersion, string automationId, int retriesRemaining)
        {
            if (retriesRemaining <= 0)
            {
                return;
            }

            QueueRelationEditorFocus(requestVersion, automationId, retriesRemaining - 1);
        }

        private const string CustomFormat = "application/xxx-unlimotion-task";
        private const string CustomBatchFormat = "application/xxx-unlimotion-task-batch";
        private const double TreeDragThreshold = 4;

        private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control control)
            {
                return;
            }

            UpdateActiveTreeContext(control);

            var tree = TryGetTaskTree(control);
            var wrapper = TryGetWrapper(control) ?? TryGetWrapper(e.Source);
            var point = e.GetCurrentPoint(control);

            if (tree != null && wrapper != null && point.Properties.IsRightButtonPressed)
            {
                NormalizeSelectionForContextMenu(tree, wrapper);
                _contextMenuTree = tree;
                _contextMenuWrapper = wrapper;
                ClearPendingTreeDrag();
                return;
            }

            if (tree == null || wrapper == null || !point.Properties.IsLeftButtonPressed)
            {
                ClearPendingTreeDrag();
                return;
            }

            if (HasSelectionModifier(e.KeyModifiers))
            {
                ClearPendingTreeDrag();
                return;
            }

            var selectionSnapshot = GetSelectedWrappersForTree(tree);
            var wasSelected = ContainsWrapper(selectionSnapshot, wrapper);
            if (!wasSelected)
            {
                selectionSnapshot = [wrapper];
            }
            else if (selectionSnapshot.Count > 1)
            {
                // Prevent TreeView from collapsing an existing multi-selection to the drag source
                // before the drag gesture crosses the threshold.
                e.Handled = true;
            }

            _pendingTreeDrag = new PendingTreeDragContext(
                control,
                tree,
                wrapper,
                e.GetPosition(control),
                selectionSnapshot,
                wasSelected);
        }

        private async void InputElement_OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (sender is not Control control ||
                _pendingTreeDrag == null ||
                _treeDragInProgress ||
                !ReferenceEquals(_pendingTreeDrag.Control, control))
            {
                return;
            }

            if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            {
                ClearPendingTreeDrag();
                return;
            }

            if (!HasExceededDragThreshold(_pendingTreeDrag.StartPoint, e.GetPosition(control)))
            {
                return;
            }

            var pending = _pendingTreeDrag;
            _pendingTreeDrag = null;
            _treeDragInProgress = true;

            try
            {
                await StartTreeDragAsync(pending, e);
            }
            finally
            {
                ClearPendingTreeDrag();
            }
        }

        private void InputElement_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            ClearPendingTreeDrag();
        }

        public static void DragOver(object sender, DragEventArgs e)
        {
            var mainControl = TryResolveMainControl(sender, e.Source);
            if (mainControl == null || !ContainsSupportedDragData(e.Data))
            {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            mainControl.HandleDragOver(e);
        }

        public static async Task Drop(object sender, DragEventArgs e)
        {
            var mainControl = TryResolveMainControl(sender, e.Source);
            if (mainControl == null || !ContainsSupportedDragData(e.Data))
            {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            await mainControl.HandleDropAsync(e);
        }

        private async Task StartTreeDragAsync(PendingTreeDragContext pending, PointerEventArgs e)
        {
            var wrappers = GetWrappersForTreeDrag(pending);

            if (wrappers.Count == 0)
            {
                return;
            }

            EnsureTreeSelectionForDragStart(pending.Tree, wrappers, pending.WasSelected);

            var dragData = BuildTreeDragData(wrappers);
            await DragDrop.DoDragDrop(
                e,
                dragData,
                DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }

        private static IReadOnlyList<TaskWrapperViewModel> GetWrappersForTreeDrag(PendingTreeDragContext pending)
        {
            return pending.WasSelected
                ? pending.SelectionSnapshot.NormalizeForDeleteBatch()
                : pending.SelectionSnapshot;
        }

        private void EnsureTreeSelectionForDragStart(
            TreeView tree,
            IReadOnlyList<TaskWrapperViewModel> wrappers,
            bool preserveExistingSelection)
        {
            if (preserveExistingSelection || wrappers.Count == 0)
            {
                return;
            }

            SelectSingleWrapper(tree, wrappers[0]);
        }

        private static DataObject BuildTreeDragData(IReadOnlyList<TaskWrapperViewModel> wrappers)
        {
            var dragData = new DataObject();
            dragData.Set(CustomBatchFormat, new TaskTreeDragData(wrappers));
            dragData.Set(CustomFormat, wrappers[0]);
            return dragData;
        }

        private void HandleDragOver(DragEventArgs e)
        {
            if (!TryGetDropTargetTask(e, out var targetTask))
            {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            var operationKind = GetBatchDropOperationKind(e.KeyModifiers);
            if (!TryBuildOperationItems(e, operationKind, out var items, out _))
            {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            e.DragEffects = CanApplyDrop(targetTask, items, operationKind)
                ? GetDragEffects(operationKind)
                : DragDropEffects.None;
        }

        private async Task HandleDropAsync(DragEventArgs e)
        {
            if (!TryGetDropTargetTask(e, out var targetTask))
            {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            var operationKind = GetBatchDropOperationKind(e.KeyModifiers);
            if (!TryBuildOperationItems(e, operationKind, out var items, out var buildError))
            {
                e.DragEffects = DragDropEffects.None;
                ShowBatchDropError(buildError);
                e.Handled = true;
                return;
            }

            if (!CanApplyDrop(targetTask, items, operationKind))
            {
                e.DragEffects = DragDropEffects.None;
                ShowBatchDropError(null);
                e.Handled = true;
                return;
            }

            if (!await ConfirmBatchDropAsync(operationKind, items.Count, targetTask))
            {
                e.DragEffects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.DragEffects = GetDragEffects(operationKind);
            await ApplyDropAsync(targetTask, items, operationKind);
            UpdateGraph(e.Source);
            e.Handled = true;
        }

        private static MainControl? TryResolveMainControl(object? sender, object? source)
        {
            return sender as MainControl ??
                   (sender as Control)?.FindParent<MainControl>() ??
                   (source as Control)?.FindParent<MainControl>();
        }

        private static bool ContainsSupportedDragData(IDataObject data)
        {
            return data.Contains(CustomBatchFormat) ||
                   data.Contains(CustomFormat) ||
                   data.Contains(GraphControl.CustomFormat);
        }

        private static bool TryGetDropTargetTask(DragEventArgs e, out TaskItemViewModel targetTask)
        {
            var control = e.Source as Control;
            targetTask = control?.FindParentDataContext<TaskWrapperViewModel>()?.TaskItem ??
                         control?.FindParentDataContext<TaskItemViewModel>()!;
            return targetTask != null;
        }

        private bool TryBuildOperationItems(
            DragEventArgs e,
            BatchDropOperationKind operationKind,
            out IReadOnlyList<DragSourceOperationItem> items,
            out string? errorMessage)
        {
            errorMessage = null;

            if (e.Data.Get(CustomBatchFormat) is TaskTreeDragData batchData)
            {
                var normalizedWrappers = operationKind == BatchDropOperationKind.MoveInto
                    ? batchData.Wrappers.NormalizeForMoveBatch()
                    : batchData.Wrappers.NormalizeForNonMoveBatch();

                var result = new List<DragSourceOperationItem>(normalizedWrappers.Count);
                foreach (var wrapper in normalizedWrappers)
                {
                    if (wrapper?.TaskItem == null)
                    {
                        continue;
                    }

                    TaskItemViewModel? sourceParent = null;
                    if (operationKind == BatchDropOperationKind.MoveInto &&
                        !TryResolveMoveSourceParent(wrapper, out sourceParent))
                    {
                        items = Array.Empty<DragSourceOperationItem>();
                        errorMessage = L10n.Get("BatchMoveMissingParents");
                        return false;
                    }

                    result.Add(new DragSourceOperationItem(
                        wrapper.TaskItem,
                        operationKind == BatchDropOperationKind.MoveInto ? sourceParent : null,
                        wrapper));
                }

                items = result;
                return items.Count > 0;
            }

            var singleSource = e.Data.Get(CustomFormat) ?? e.Data.Get(GraphControl.CustomFormat);
            if (!TryBuildSingleOperationItem(singleSource, operationKind, out var item, out errorMessage))
            {
                items = Array.Empty<DragSourceOperationItem>();
                return false;
            }

            items = [item];
            return true;
        }

        private static bool TryBuildSingleOperationItem(
            object? dragSource,
            BatchDropOperationKind operationKind,
            out DragSourceOperationItem item,
            out string? errorMessage)
        {
            errorMessage = null;
            item = null!;

            switch (dragSource)
            {
                case TaskWrapperViewModel wrapper when wrapper.TaskItem != null:
                {
                    TaskItemViewModel? sourceParent = null;
                    if (operationKind == BatchDropOperationKind.MoveInto &&
                        !TryResolveMoveSourceParent(wrapper, out sourceParent))
                    {
                        errorMessage = L10n.Get("MoveMissingParent");
                        return false;
                    }

                    item = new DragSourceOperationItem(
                        wrapper.TaskItem,
                        operationKind == BatchDropOperationKind.MoveInto ? sourceParent : null,
                        wrapper);
                    return true;
                }
                case TaskItemViewModel taskItem:
                {
                    TaskItemViewModel? sourceParent = null;
                    if (operationKind == BatchDropOperationKind.MoveInto)
                    {
                        if (taskItem.Parents.Count <= 1)
                        {
                            sourceParent = taskItem.ParentsTasks.FirstOrDefault();
                        }
                        else
                        {
                            errorMessage = L10n.Get("MoveMissingParent");
                            return false;
                        }
                    }

                    item = new DragSourceOperationItem(taskItem, sourceParent);
                    return true;
                }
                default:
                    errorMessage = L10n.Get("UnsupportedDragSource");
                    return false;
            }
        }

        private static bool TryResolveMoveSourceParent(TaskWrapperViewModel wrapper, out TaskItemViewModel? sourceParent)
        {
            sourceParent = null;
            if (wrapper?.TaskItem == null)
            {
                return false;
            }

            if (wrapper.TaskItem.Parents.Count <= 1)
            {
                sourceParent = wrapper.TaskItem.ParentsTasks.FirstOrDefault();
                return true;
            }

            if (wrapper.Parent?.TaskItem != null)
            {
                sourceParent = wrapper.Parent.TaskItem;
                return true;
            }

            return false;
        }

        private static bool CanApplyDrop(
            TaskItemViewModel targetTask,
            IReadOnlyList<DragSourceOperationItem> items,
            BatchDropOperationKind operationKind)
        {
            return items.Count > 0 &&
                   items.All(item => item.TaskItem != null) &&
                   operationKind switch
                   {
                       BatchDropOperationKind.CopyInto => items.All(item => item.TaskItem.CanMoveInto(targetTask)),
                       BatchDropOperationKind.MoveInto => items.All(item =>
                           item.TaskItem.CanMoveInto(targetTask)),
                       BatchDropOperationKind.CloneInto => items.All(item => item.TaskItem.CanMoveInto(targetTask)),
                       BatchDropOperationKind.SourcesBlockTarget => items.All(item => targetTask.CanCreateBlockingRelation(item.TaskItem)),
                       BatchDropOperationKind.TargetBlocksSources => items.All(item => item.TaskItem.CanCreateBlockingRelation(targetTask)),
                       _ => false
                   };
        }

        private async Task<bool> ConfirmBatchDropAsync(
            BatchDropOperationKind operationKind,
            int itemCount,
            TaskItemViewModel targetTask)
        {
            if (itemCount <= 1 || DataContext is not MainWindowViewModel vm)
            {
                return true;
            }

            var manager = vm.ManagerWrapper;
            if (manager == null)
            {
                return true;
            }

            var tcs = new TaskCompletionSource<bool>();
            manager.Ask(
                GetBatchConfirmationHeader(operationKind),
                GetBatchConfirmationMessage(operationKind, itemCount, targetTask),
                () => tcs.TrySetResult(true),
                () => tcs.TrySetResult(false));

            return await tcs.Task;
        }

        private static string GetBatchConfirmationHeader(BatchDropOperationKind operationKind)
        {
            return operationKind switch
            {
                BatchDropOperationKind.CopyInto => L10n.Get("CopyTasksHeader"),
                BatchDropOperationKind.MoveInto => L10n.Get("MoveTasksHeader"),
                BatchDropOperationKind.CloneInto => L10n.Get("CloneTasksHeader"),
                BatchDropOperationKind.SourcesBlockTarget => L10n.Get("AddBlockingTasksHeader"),
                BatchDropOperationKind.TargetBlocksSources => L10n.Get("AddBlockedTasksHeader"),
                _ => L10n.Get("ConfirmBatchOperationHeader")
            };
        }

        private static string GetBatchConfirmationMessage(
            BatchDropOperationKind operationKind,
            int itemCount,
            TaskItemViewModel targetTask)
        {
            return operationKind switch
            {
                BatchDropOperationKind.CopyInto =>
                    L10n.Format("CopyTasksMessage", itemCount, targetTask.Title),
                BatchDropOperationKind.MoveInto =>
                    L10n.Format("MoveTasksMessage", itemCount, targetTask.Title),
                BatchDropOperationKind.CloneInto =>
                    L10n.Format("CloneTasksMessage", itemCount, targetTask.Title),
                BatchDropOperationKind.SourcesBlockTarget =>
                    L10n.Format("AddBlockingTasksMessage", itemCount, targetTask.Title),
                BatchDropOperationKind.TargetBlocksSources =>
                    L10n.Format("AddBlockedTasksMessage", targetTask.Title, itemCount),
                _ => L10n.Format("ConfirmBatchOperationMessage", itemCount)
            };
        }

        private static async Task ApplyDropAsync(
            TaskItemViewModel targetTask,
            IReadOnlyList<DragSourceOperationItem> items,
            BatchDropOperationKind operationKind)
        {
            foreach (var item in items)
            {
                switch (operationKind)
                {
                    case BatchDropOperationKind.CopyInto:
                        await item.TaskItem.CopyInto(targetTask);
                        break;
                    case BatchDropOperationKind.MoveInto:
                        await item.TaskItem.MoveInto(targetTask, item.SourceParent);
                        break;
                    case BatchDropOperationKind.CloneInto:
                        await item.TaskItem.CloneInto(targetTask);
                        break;
                    case BatchDropOperationKind.SourcesBlockTarget:
                        targetTask.BlockBy(item.TaskItem);
                        break;
                    case BatchDropOperationKind.TargetBlocksSources:
                        item.TaskItem.BlockBy(targetTask);
                        break;
                }
            }
        }

        private void ShowBatchDropError(string? errorMessage)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ManagerWrapper?.ErrorToast(errorMessage ?? L10n.Get("BatchOperationInvalid"));
            }
        }

        private static BatchDropOperationKind GetBatchDropOperationKind(KeyModifiers modifiers)
        {
            var relevantModifiers = modifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt);
            return relevantModifiers switch
            {
                KeyModifiers.Control | KeyModifiers.Shift => BatchDropOperationKind.CloneInto,
                KeyModifiers.Shift => BatchDropOperationKind.MoveInto,
                KeyModifiers.Control => BatchDropOperationKind.SourcesBlockTarget,
                KeyModifiers.Alt => BatchDropOperationKind.TargetBlocksSources,
                _ => BatchDropOperationKind.CopyInto
            };
        }

        private static DragDropEffects GetDragEffects(BatchDropOperationKind operationKind)
        {
            return operationKind switch
            {
                BatchDropOperationKind.CopyInto => DragDropEffects.Copy,
                BatchDropOperationKind.MoveInto => DragDropEffects.Move,
                BatchDropOperationKind.CloneInto => DragDropEffects.Copy,
                BatchDropOperationKind.SourcesBlockTarget => DragDropEffects.Link,
                BatchDropOperationKind.TargetBlocksSources => DragDropEffects.Link,
                _ => DragDropEffects.None
            };
        }

        private static bool HasSelectionModifier(KeyModifiers modifiers)
        {
            var relevantModifiers = modifiers & (KeyModifiers.Control | KeyModifiers.Shift);
            return relevantModifiers != KeyModifiers.None;
        }

        private static bool HasExceededDragThreshold(Point startPoint, Point currentPoint)
        {
            return Math.Abs(currentPoint.X - startPoint.X) >= TreeDragThreshold ||
                   Math.Abs(currentPoint.Y - startPoint.Y) >= TreeDragThreshold;
        }

        private void ClearPendingTreeDrag()
        {
            _pendingTreeDrag = null;
            _treeDragInProgress = false;
        }

        private async void BreadScrumbs_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var dragData = new DataObject();
            var dc = (DataContext as MainWindowViewModel)?.CurrentTaskItem;
            if (dc == null)
            {
                return;
            }

            dragData.Set(CustomFormat, dc);

            var result = await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }

        private void Task_OnDoubleTapped(object sender, TappedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                var control = sender as Control;
                if (control?.DataContext is TaskWrapperViewModel wrapper)
                {
                    vm.CurrentTaskItem = wrapper.TaskItem;
                }
            }
        }

        private void RelationAddButton_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm ||
                sender is not Control control ||
                !Enum.TryParse<TaskRelationKind>(control.Tag?.ToString(), out var kind))
            {
                return;
            }

            vm.OpenRelationEditor(kind);
        }

        private void RelationEditorControl_OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is not Control { DataContext: TaskRelationEditorViewModel editor })
            {
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (editor.ConfirmCommand.CanExecute(null))
                {
                    editor.ConfirmCommand.Execute(null);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                if (editor.CancelCommand.CanExecute(null))
                {
                    editor.CancelCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        private void RelationEditorSuggestions_OnDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (sender is not Control { DataContext: TaskRelationEditorViewModel editor })
            {
                return;
            }

            if (editor.ConfirmCommand.CanExecute(null))
            {
                editor.ConfirmCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void TaskTree_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not TreeView tree)
            {
                return;
            }

            _activeTaskTree = tree;
            UpdateContextMenuContext(tree, e);
        }

        private void InlineTaskTitleText_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Handled ||
                sender is not Control control ||
                e.KeyModifiers != KeyModifiers.None ||
                !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            {
                return;
            }

            var tree = TryGetTaskTree(control);
            if (tree == null || !IsKnownMainTaskTree(tree))
            {
                return;
            }

            var task = TryGetTaskItem(control.DataContext);
            if (task == null || string.IsNullOrWhiteSpace(task.Id))
            {
                return;
            }

            if (e.ClickCount > 1)
            {
                ClearInlineTitleClickState();
                return;
            }

            var isRepeatedTitleClick =
                ReferenceEquals(_lastInlineTitleClickTree, tree) &&
                string.Equals(_lastInlineTitleClickTaskId, task.Id, StringComparison.Ordinal);

            if (TryGetWrapper(control) is { } wrapper)
            {
                SelectSingleWrapper(tree, wrapper);
            }
            else if (DataContext is MainWindowViewModel vm && vm.CurrentTaskItem != task)
            {
                vm.CurrentTaskItem = task;
            }

            if (isRepeatedTitleClick && FocusCurrentTaskInlineTitleEditor(tree, task.Id))
            {
                ClearInlineTitleClickState();
                e.Handled = true;
                return;
            }

            _lastInlineTitleClickTree = tree;
            _lastInlineTitleClickTaskId = task.Id;
        }

        private void TaskTree_OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Handled ||
                sender is not TreeView tree ||
                DataContext is not MainWindowViewModel vm ||
                IsTextInputFocused())
            {
                return;
            }

            if (!KnownMainTaskTreeNames.Contains(tree.Name, StringComparer.Ordinal))
            {
                return;
            }

            if (e.KeyModifiers == KeyModifiers.None && IsInlineTitleEditKey(e))
            {
                ClearInlineTitleClickState();
                e.Handled = FocusCurrentTaskInlineTitleEditor(tree, vm.CurrentTaskItem?.Id);
            }
        }

        private static bool IsSelectAllHotkey(KeyEventArgs e)
        {
            return e.KeyModifiers == KeyModifiers.Control && e.Key == Key.A;
        }

        private static bool IsInlineTitleEditKey(KeyEventArgs e)
        {
            return e.Key == Key.F2;
        }

        private bool FocusCurrentTaskInlineTitleEditor(TreeView tree, string? currentTaskId)
        {
            if (string.IsNullOrWhiteSpace(currentTaskId))
            {
                return false;
            }

            var titleEditor = FindInlineTitleEditor(tree, currentTaskId) ??
                              CreateInlineTitleEditor(tree, currentTaskId);

            if (titleEditor == null)
            {
                return false;
            }

            if (!FocusInlineTitleEditor(titleEditor))
            {
                if (ReferenceEquals(_activeInlineTitleEditor, titleEditor))
                {
                    ClearActiveInlineTitleEditor();
                }

                return false;
            }

            return true;
        }

        private TextBox? CreateInlineTitleEditor(TreeView tree, string currentTaskId)
        {
            var titleText = FindInlineTitleTextBlock(tree, currentTaskId);
            var task = TryGetTaskItem(titleText?.DataContext);
            if (titleText?.Parent is not Panel parent || task == null)
            {
                return null;
            }

            ClearActiveInlineTitleEditor();

            var titleEditor = new TextBox
            {
                DataContext = task,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = titleText.VerticalAlignment,
                MinWidth = Math.Max(titleText.Bounds.Width, 48)
            };
            titleEditor.Classes.Add("InlineTaskTitleEditor");
            if (task.Wanted)
            {
                titleEditor.Classes.Add("IsWanted");
            }

            if (!task.IsCanBeCompleted)
            {
                titleEditor.Classes.Add("IsCanBeCompleted");
            }

            AutomationProperties.SetAutomationId(titleEditor, "InlineTaskTitleTextBox");
            titleEditor.Bind(
                TextBox.TextProperty,
                new Binding(nameof(TaskItemViewModel.Title))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });

            Grid.SetColumn(titleEditor, Grid.GetColumn(titleText));
            Grid.SetColumnSpan(titleEditor, Grid.GetColumnSpan(titleText));
            Grid.SetRow(titleEditor, Grid.GetRow(titleText));
            Grid.SetRowSpan(titleEditor, Grid.GetRowSpan(titleText));

            titleEditor.LostFocus += InlineTitleEditor_OnLostFocus;
            parent.Children.Add(titleEditor);
            _activeInlineTitleEditor = titleEditor;

            return titleEditor;
        }

        private static TextBlock? FindInlineTitleTextBlock(TreeView tree, string currentTaskId)
        {
            return tree.GetVisualDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(control =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(control),
                        "InlineTaskTitleTextBlock",
                        StringComparison.Ordinal) &&
                    TryGetTaskItem(control.DataContext)?.Id == currentTaskId &&
                    control.IsAttachedToVisualTree() &&
                    control.IsVisible &&
                    control.IsEnabled);
        }

        private static TextBox? FindInlineTitleEditor(TreeView tree, string currentTaskId)
        {
            return tree.GetVisualDescendants()
                .OfType<TextBox>()
                .FirstOrDefault(control =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(control),
                        "InlineTaskTitleTextBox",
                        StringComparison.Ordinal) &&
                    TryGetTaskItem(control.DataContext)?.Id == currentTaskId &&
                    control.IsAttachedToVisualTree() &&
                    control.IsVisible &&
                    control.IsEnabled);
        }

        private static bool FocusInlineTitleEditor(TextBox titleEditor)
        {
            if (!titleEditor.Focus())
            {
                return false;
            }

            if (!string.IsNullOrEmpty(titleEditor.Text))
            {
                titleEditor.SelectAll();
            }
            else
            {
                titleEditor.CaretIndex = 0;
            }

            return true;
        }

        private void InlineTitleEditor_OnLostFocus(object? sender, RoutedEventArgs e)
        {
            if (ReferenceEquals(sender, _activeInlineTitleEditor))
            {
                ClearActiveInlineTitleEditor();
            }
        }

        private void ClearInlineTitleEditState()
        {
            ClearInlineTitleClickState();
            ClearActiveInlineTitleEditor();
        }

        private void ClearInlineTitleClickState()
        {
            _lastInlineTitleClickTree = null;
            _lastInlineTitleClickTaskId = null;
        }

        private void ClearActiveInlineTitleEditor()
        {
            var titleEditor = _activeInlineTitleEditor;
            if (titleEditor == null)
            {
                return;
            }

            titleEditor.LostFocus -= InlineTitleEditor_OnLostFocus;
            if (titleEditor.Parent is Panel parent)
            {
                parent.Children.Remove(titleEditor);
            }

            _activeInlineTitleEditor = null;
        }

        private void TaskTreeContextMenuItem_OnClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem ||
                !Enum.TryParse<TreeCommandKind>(menuItem.Tag?.ToString(), out var kind))
            {
                return;
            }

            RestoreContextMenuContextFromPlacementTarget(menuItem);
            ExecuteTreeCommand(kind, TreeCommandRoute.ContextMenu);
        }
        
        private void TaskTree_OnDoubleTapped(object sender, TappedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.DetailsAreOpen = !vm.DetailsAreOpen;
            }
        }

        private static void UpdateGraph(object? eSource)
        {
            var control = eSource as Control;
            var vm = control?.FindParentDataContext<MainWindowViewModel>();
            if (vm?.Graph?.UpdateGraph != null)
            {
                vm.Graph.UpdateGraph = !vm.Graph.UpdateGraph;
            }
        }

        private void ExecuteTreeCommand(TreeCommandKind kind)
        {
            ExecuteTreeCommand(kind, TreeCommandRoute.Hotkey, allowDeferredRetry: true);
        }

        private void ExecuteTreeCommand(TreeCommandKind kind, TreeCommandRoute route)
        {
            ExecuteTreeCommand(kind, route, allowDeferredRetry: false);
        }

        private void ExecuteTreeCommand(TreeCommandKind kind, TreeCommandRoute route, bool allowDeferredRetry)
        {
            try
            {
                if (route == TreeCommandRoute.Hotkey)
                {
                    if (IsTextInputFocused())
                    {
                        return;
                    }

                    if (allowDeferredRetry && IsTabHeaderFocused())
                    {
                        Dispatcher.UIThread.Post(
                            () => ExecuteTreeCommand(kind, route, allowDeferredRetry: false),
                            DispatcherPriority.Background);
                        return;
                    }
                }

                if (DataContext is not MainWindowViewModel vm || !TryGetCommandTree(route, out var tree))
                {
                    return;
                }

                switch (kind)
                {
                    case TreeCommandKind.ExpandCurrentNested:
                    {
                        var current = GetCurrentWrapperForRoute(vm, tree, route);
                        if (current != null)
                        {
                            vm.ExpandNodeAndDescendants(current);
                        }

                        break;
                    }
                    case TreeCommandKind.CollapseCurrentNested:
                    {
                        var current = GetCurrentWrapperForRoute(vm, tree, route);
                        if (current != null)
                        {
                            vm.CollapseNodeDescendants(current);
                        }

                        break;
                    }
                    case TreeCommandKind.ExpandAll:
                    {
                        var roots = GetTreeRoots(tree);
                        if (roots == null)
                        {
                            return;
                        }

                        vm.ExpandAllNodes(roots);
                        break;
                    }
                    case TreeCommandKind.CollapseAll:
                    {
                        var roots = GetTreeRoots(tree);
                        if (roots == null)
                        {
                            return;
                        }

                        vm.CollapseAllNodes(roots);
                        break;
                    }
                    case TreeCommandKind.DeleteSelection:
                    {
                        if (!IsKnownMainTaskTree(tree))
                        {
                            return;
                        }

                        var wrappers = GetSelectedWrappersForTree(vm, tree);
                        if (wrappers.Count == 0)
                        {
                            return;
                        }

                        vm.RemoveSelectedWrappers(wrappers);
                        break;
                    }
                    case TreeCommandKind.SelectAll:
                        tree.SelectAll();
                        break;
                    case TreeCommandKind.CopyOutline:
                    {
                        var current = GetCurrentWrapperForRoute(vm, tree, route);
                        ExecuteTaskOutlineCommandAsync(vm, () => current != null
                            ? vm.CopyTaskOutline(current)
                            : vm.CopyTaskOutline(vm.CurrentTaskItem));
                        break;
                    }
                    case TreeCommandKind.PasteOutline:
                    {
                        var destination = GetCurrentWrapperForRoute(vm, tree, route)?.TaskItem ?? vm.CurrentTaskItem;
                        ExecuteTaskOutlineCommandAsync(vm, () => vm.PasteTaskOutline(destination));
                        break;
                    }
                }
            }
            finally
            {
                if (route == TreeCommandRoute.ContextMenu)
                {
                    ClearContextMenuContext();
                }
            }
        }

        private static async void ExecuteTaskOutlineCommandAsync(MainWindowViewModel vm, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                vm.ManagerWrapper?.ErrorToast(L10n.Format("ReactiveUnhandledError", ex.Message));
            }
        }

        private void UpdateActiveTreeContext(Control control)
        {
            var tree = TryGetTaskTree(control);
            if (tree == null)
            {
                return;
            }

            _activeTaskTree = tree;
        }

        private void UpdateContextMenuContext(TreeView tree, PointerPressedEventArgs e)
        {
            ClearContextMenuContext();

            if (!e.GetCurrentPoint(tree).Properties.IsRightButtonPressed)
            {
                return;
            }

            _contextMenuTree = tree;
            _contextMenuWrapper = TryGetWrapper(e.Source);
        }

        private bool TryGetValidatedActiveTree(out TreeView tree)
        {
            if (TryGetValidatedTree(_activeTaskTree, out tree))
            {
                return true;
            }

            _activeTaskTree = null;
            return false;
        }

        private bool TryGetValidatedContextMenuTree(out TreeView tree)
        {
            if (TryGetValidatedTree(_contextMenuTree, out tree))
            {
                return true;
            }

            ClearContextMenuContext();
            return false;
        }

        private bool TryGetFocusedTaskTree(out TreeView tree)
        {
            tree = null!;

            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            var candidate = TryGetTaskTree(focused);
            return TryGetValidatedTree(candidate, out tree);
        }

        private bool TryGetCommandTree(TreeCommandRoute route, out TreeView tree)
        {
            if (route == TreeCommandRoute.ContextMenu && TryGetValidatedContextMenuTree(out tree))
            {
                return true;
            }

            if (route == TreeCommandRoute.Hotkey)
            {
                return TryGetHotkeyTree(out tree);
            }

            return TryGetValidatedActiveTree(out tree);
        }

        private bool TryGetHotkeyTree(out TreeView tree)
        {
            tree = null!;

            if (TryGetFocusedTaskTree(out var focusedTree) && ShouldUseActiveTreeForHotkey(focusedTree))
            {
                _activeTaskTree = focusedTree;
                tree = focusedTree;
                return true;
            }

            if (TryGetValidatedActiveTree(out var activeTree) && ShouldUseActiveTreeForHotkey(activeTree))
            {
                tree = activeTree;
                return true;
            }

            if (TryGetVisibleMainTaskTree(out tree))
            {
                return true;
            }

            if (DataContext is not MainWindowViewModel vm)
            {
                return false;
            }

            return TryGetCurrentModeTree(vm, out tree);
        }

        private bool TryGetCurrentModeTree(MainWindowViewModel vm, out TreeView tree)
        {
            tree = null!;

            if (TryGetVisibleMainTaskTree(out tree))
            {
                return true;
            }

            if (!TryGetCurrentModeTreeName(vm, out var treeName))
            {
                return false;
            }

            var candidate = this.FindControl<TreeView>(treeName);
            return TryGetValidatedTree(candidate, out tree);
        }

        private bool ShouldUseActiveTreeForHotkey(TreeView activeTree)
        {
            if (!IsKnownMainTaskTree(activeTree))
            {
                return true;
            }

            return !TryGetVisibleMainTaskTree(out var visibleTree) ||
                   StringComparer.Ordinal.Equals(activeTree.Name, visibleTree.Name);
        }

        private static bool TryGetCurrentModeTreeName(MainWindowViewModel vm, out string treeName)
        {
            treeName = vm switch
            {
                _ when vm.AllTasksMode => "AllTasksTree",
                _ when vm.LastCreatedMode => "LastCreatedTree",
                _ when vm.LastUpdatedMode => "LastUpdatedTree",
                _ when vm.UnlockedMode => "UnlockedTree",
                _ when vm.CompletedMode => "CompletedTree",
                _ when vm.ArchivedMode => "ArchivedTree",
                _ when vm.LastOpenedMode => "LastOpenedTree",
                _ => string.Empty
            };

            return !string.IsNullOrWhiteSpace(treeName);
        }

        private static bool IsKnownMainTaskTree(TreeView tree)
        {
            return KnownMainTaskTreeNames.Contains(tree.Name, StringComparer.Ordinal);
        }

        private static TaskItemViewModel? TryGetTaskItem(object? source)
        {
            return source switch
            {
                TaskItemViewModel taskItem => taskItem,
                TaskWrapperViewModel wrapper => wrapper.TaskItem,
                _ => null
            };
        }

        private bool TryGetVisibleMainTaskTree(out TreeView tree)
        {
            foreach (var treeName in KnownMainTaskTreeNames)
            {
                var candidate = this.FindControl<TreeView>(treeName);
                if (TryGetValidatedTree(candidate, out tree))
                {
                    return true;
                }
            }

            tree = null!;
            return false;
        }

        private bool TryGetValidatedTree(TreeView? candidate, out TreeView tree)
        {
            tree = candidate;
            if (tree == null)
            {
                return false;
            }

            if (!tree.IsAttachedToVisualTree() || !tree.IsEnabled || !IsVisibleInVisualTree(tree))
            {
                return false;
            }

            var parentMainControl = tree.FindParent<MainControl>();
            return ReferenceEquals(parentMainControl, this);
        }

        private static bool IsVisibleInVisualTree(Control control)
        {
            for (var current = control; current != null; current = current.GetVisualParent() as Control)
            {
                if (!current.IsVisible)
                {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<TaskWrapperViewModel>? GetTreeRoots(TreeView tree)
        {
            return tree.ItemsSource switch
            {
                IEnumerable<TaskWrapperViewModel> roots => roots,
                IEnumerable roots => roots.OfType<TaskWrapperViewModel>(),
                _ => null
            };
        }

        private TaskWrapperViewModel? GetCurrentWrapperForRoute(MainWindowViewModel vm, TreeView tree, TreeCommandRoute route)
        {
            if (route == TreeCommandRoute.ContextMenu &&
                ReferenceEquals(tree, _contextMenuTree) &&
                _contextMenuWrapper != null)
            {
                return _contextMenuWrapper;
            }

            return GetSelectedWrappersForTree(vm, tree).LastOrDefault();
        }

        private IReadOnlyList<TaskWrapperViewModel> GetSelectedWrappersForTree(MainWindowViewModel vm, TreeView tree)
        {
            var selectedWrappers = GetSelectedWrappersForTree(tree);
            if (selectedWrappers.Count > 0)
            {
                return selectedWrappers;
            }

            var currentWrapper = tree.Name switch
            {
                "AllTasksTree" => vm.CurrentAllTasksItem,
                "LastCreatedTree" => vm.CurrentLastCreated,
                "LastUpdatedTree" => vm.CurrentLastUpdated,
                "UnlockedTree" => vm.CurrentUnlockedItem,
                "CompletedTree" => vm.CurrentCompletedItem,
                "ArchivedTree" => vm.CurrentArchivedItem,
                "LastOpenedTree" => vm.CurrentLastOpenedItem,
                _ => null
            };

            return currentWrapper != null ? [currentWrapper] : Array.Empty<TaskWrapperViewModel>();
        }

        private static IReadOnlyList<TaskWrapperViewModel> GetSelectedWrappersForTree(TreeView tree)
        {
            var selectedWrappers = tree.SelectedItems?
                .OfType<TaskWrapperViewModel>()
                .ToList();

            if (selectedWrappers?.Count > 0)
            {
                return selectedWrappers;
            }

            return tree.SelectedItem is TaskWrapperViewModel selectedWrapper
                ? [selectedWrapper]
                : Array.Empty<TaskWrapperViewModel>();
        }

        private void NormalizeSelectionForContextMenu(TreeView tree, TaskWrapperViewModel wrapper)
        {
            var selectedWrappers = GetSelectedWrappersForTree(tree);
            if (ContainsWrapper(selectedWrappers, wrapper))
            {
                return;
            }

            SelectSingleWrapper(tree, wrapper);
        }

        private void SelectSingleWrapper(TreeView tree, TaskWrapperViewModel wrapper)
        {
            SetTreeSelection(tree, [wrapper]);
        }

        private void SetTreeSelection(TreeView tree, IReadOnlyList<TaskWrapperViewModel> wrappers)
        {
            var selectedItems = tree.SelectedItems;
            if (selectedItems == null)
            {
                return;
            }

            selectedItems.Clear();
            foreach (var wrapper in wrappers)
            {
                selectedItems.Add(wrapper);
            }

            var currentWrapper = wrappers.LastOrDefault();
            tree.SelectedItem = currentWrapper;
            SyncCurrentWrapperForTree(tree, currentWrapper);
        }

        private void SyncCurrentWrapperForTree(TreeView tree, TaskWrapperViewModel? wrapper)
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            switch (tree.Name)
            {
                case "AllTasksTree":
                    vm.CurrentAllTasksItem = wrapper;
                    break;
                case "LastCreatedTree":
                    vm.CurrentLastCreated = wrapper;
                    break;
                case "LastUpdatedTree":
                    vm.CurrentLastUpdated = wrapper;
                    break;
                case "UnlockedTree":
                    vm.CurrentUnlockedItem = wrapper;
                    break;
                case "CompletedTree":
                    vm.CurrentCompletedItem = wrapper;
                    break;
                case "ArchivedTree":
                    vm.CurrentArchivedItem = wrapper;
                    break;
                case "LastOpenedTree":
                    vm.CurrentLastOpenedItem = wrapper;
                    break;
            }
        }

        private static bool ContainsWrapper(
            IReadOnlyList<TaskWrapperViewModel> wrappers,
            TaskWrapperViewModel candidate)
        {
            var candidatePath = candidate.GetWrapperPathKey();
            return wrappers.Any(wrapper =>
                ReferenceEquals(wrapper, candidate) ||
                StringComparer.Ordinal.Equals(wrapper.GetWrapperPathKey(), candidatePath));
        }

        private void ClearContextMenuContext()
        {
            _contextMenuTree = null;
            _contextMenuWrapper = null;
        }

        private void RestoreContextMenuContextFromPlacementTarget(MenuItem menuItem)
        {
            var contextMenu = menuItem.FindParent<ContextMenu>();
            if (contextMenu?.PlacementTarget is not Control placementTarget)
            {
                return;
            }

            _contextMenuTree = TryGetTaskTree(placementTarget) ?? _contextMenuTree;
            _contextMenuWrapper ??= TryGetWrapper(placementTarget);
        }

        private static TaskWrapperViewModel? TryGetWrapper(object? source)
        {
            return source switch
            {
                TaskWrapperViewModel wrapper => wrapper,
                Control control when control.DataContext is TaskWrapperViewModel wrapper => wrapper,
                Control control => control.FindParentDataContext<TaskWrapperViewModel>(),
                _ => null
            };
        }

        private bool IsTextInputFocused()
        {
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            if (focused == null)
            {
                return false;
            }

            return IsTextInputControlOrAncestor(focused);
        }

        private bool IsTabHeaderFocused()
        {
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            return focused != null && IsControlOrAncestor<TabItem>(focused);
        }

        private static bool IsTextInputControlOrAncestor(Control control)
        {
            return IsControlOrAncestor<TextBox>(control) ||
                   IsControlOrAncestor<AutoCompleteBox>(control) ||
                   IsControlOrAncestor<NumericUpDown>(control) ||
                   IsRelationEditorSuggestionsOrAncestor(control);
        }

        private static bool IsRelationEditorSuggestionsOrAncestor(Control control)
        {
            Control? current = control;
            while (current != null)
            {
                if (current is ListBox listBox && listBox.DataContext is TaskRelationEditorViewModel)
                {
                    return true;
                }

                current = current.FindParent<Control>();
            }

            return false;
        }

        private static bool IsControlOrAncestor<TControl>(Control control)
            where TControl : Control
        {
            Control? current = control;
            while (current != null)
            {
                if (current is TControl)
                {
                    return true;
                }

                current = current.FindParent<Control>();
            }

            return false;
        }

        private static TreeView? TryGetTaskTree(Control? control)
        {
            return control as TreeView ?? control?.FindParent<TreeView>();
        }
    }
}
