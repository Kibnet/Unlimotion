using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;

namespace Unlimotion.Views
{
    public partial class MainControl : UserControl
    {
        private enum TreeCommandRoute
        {
            Hotkey,
            ContextMenu
        }

        // Static dependencies - set once during app initialization
        public static IDialogs? DialogsInstance { get; set; }
        private const int MaxTitleFocusRetries = 5;
        private IDisposable? _titleFocusSubscription;
        private MainWindowViewModel? _treeCommandViewModel;
        private TreeView? _activeTaskTree;
        private TreeView? _contextMenuTree;
        private TaskWrapperViewModel? _contextMenuWrapper;

        public MainControl()
        {
            InitializeComponent();
            AddHandler(DragDrop.DropEvent, Drop);
            AddHandler(DragDrop.DragOverEvent, DragOver);
            DataContextChanged += MainWindow_DataContextChanged;
        }

        private void MainWindow_DataContextChanged(object? sender, EventArgs e)
        {
            _titleFocusSubscription?.Dispose();
            _titleFocusSubscription = null;
            if (_treeCommandViewModel != null)
            {
                _treeCommandViewModel.ExecuteTreeCommandAction = null;
                _treeCommandViewModel = null;
            }

            _activeTaskTree = null;
            _contextMenuTree = null;
            _contextMenuWrapper = null;

            if (DataContext is MainWindowViewModel vm)
            {
                _treeCommandViewModel = vm;
                vm.ExecuteTreeCommandAction = ExecuteTreeCommand;
                _titleFocusSubscription = vm.WhenAnyValue(m => m.TitleFocusRequestVersion)
                    .Subscribe(requestVersion => QueueTitleFocus(requestVersion, vm.CurrentTaskItem?.Id, MaxTitleFocusRetries));
                vm.MoveToPath = ReactiveCommand.CreateFromTask(async () =>
                {
                    if (vm.CurrentTaskItem == null)
                        return;
                    var dialogs = DialogsInstance;
                    if (dialogs == null) return;
                    
                    var path = await dialogs.ShowOpenFolderDialogAsync("Task Storage Path");

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

        private const string CustomFormat = "application/xxx-unlimotion-task";

        private async void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var control = sender as Control;
            if (control != null)
            {
                UpdateActiveTreeContext(control);
            }

            if (control == null || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            {
                return;
            }

            var dc = control?.DataContext;
            if (dc == null)
            {
                return;
            }

            var dragData = new DataObject();
            dragData.Set(CustomFormat, dc);

            var result = await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }

        public static void DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.Contains(CustomFormat) || e.Data.Contains(GraphControl.CustomFormat))
            {
                if (GetTasks(e, out var task, out var subItem)) return;

                if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
                {
                    e.DragEffects &= DragDropEffects.Copy;
                }
                else if (e.KeyModifiers == KeyModifiers.Shift)
                {
                    if (subItem.CanMoveInto(task))
                    {
                        e.DragEffects &= DragDropEffects.Move;
                    }
                    else
                    {
                        e.DragEffects = DragDropEffects.None;
                    }
                }
                else if (e.KeyModifiers == KeyModifiers.Control)
                {
                    e.DragEffects &= DragDropEffects.Link;
                }
                else if (e.KeyModifiers == KeyModifiers.Alt)
                {
                    e.DragEffects &= DragDropEffects.Link;
                }
                else
                {
                    if (subItem.CanMoveInto(task))
                    {
                        e.DragEffects &= DragDropEffects.Copy;
                    }
                    else
                    {
                        e.DragEffects = DragDropEffects.None;
                    }
                }
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
        }

        private static bool GetTasks(DragEventArgs e, out TaskItemViewModel? task, out TaskItemViewModel? subItem)
        {
            var control = e.Source as Control;
            task = control?.FindParentDataContext<TaskWrapperViewModel>()?.TaskItem ??
                   control?.FindParentDataContext<TaskItemViewModel>();

            var sub = e.Data.Get(CustomFormat) ?? e.Data.Get(GraphControl.CustomFormat);
            subItem = sub switch
            {
                TaskWrapperViewModel taskWrapperViewModel => taskWrapperViewModel?.TaskItem,
                TaskItemViewModel taskItemViewModel => taskItemViewModel,
                _ => null
            };

            if (subItem == null || task == null)
            {
                e.DragEffects = DragDropEffects.None;
                return true;
            }

            return false;
        }

        public static async Task Drop(object sender, DragEventArgs e)
        {
            if (e.Data.Contains(CustomFormat) || e.Data.Contains(GraphControl.CustomFormat))
            {
                if (GetTasks(e, out var task, out var subItem)) return;

                if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
                {
                    e.DragEffects &= DragDropEffects.Copy;
                    await subItem.CloneInto(task);
                    UpdateGraph(e.Source);
                    e.Handled = true;
                }
                else if (e.KeyModifiers == KeyModifiers.Shift)
                {
                    if (subItem.CanMoveInto(task))
                    {
                        TaskItemViewModel parent = null;
                        var breakFlag = false;
                        if (subItem.Parents.Count <= 1)
                        {
                            parent = subItem.ParentsTasks.FirstOrDefault();
                        }
                        else if ((e.Data.Get(CustomFormat) ?? e.Data.Get(GraphControl.CustomFormat)) is TaskWrapperViewModel parentWrapper)
                        {
                            parent = parentWrapper.Parent.TaskItem;
                        }
                        else
                        {
                            e.DragEffects &= DragDropEffects.None;
                            breakFlag = true;
                        }

                        if (!breakFlag)
                        {
                            e.DragEffects &= DragDropEffects.Move;
                            await subItem.MoveInto(task, parent);
                            UpdateGraph(e.Source);
                            e.Handled = true;
                        }
                    }
                    else
                    {
                        e.DragEffects = DragDropEffects.None;
                    }

                }
                else if (e.KeyModifiers == KeyModifiers.Control) //The dragged task blocks the target task
                {
                    e.DragEffects &= DragDropEffects.Link;
                    task.BlockBy(subItem); //subItem блокирует task
                    UpdateGraph(e.Source);
                    e.Handled = true;
                }
                else if (e.KeyModifiers == KeyModifiers.Alt) //The target task blocks the dragged task
                {
                    e.DragEffects &= DragDropEffects.Link;
                    subItem.BlockBy(task); //task блокирует subItem
                    UpdateGraph(e.Source);
                    e.Handled = true;
                }
                else
                {
                    if (subItem.CanMoveInto(task))
                    {
                        e.DragEffects &= DragDropEffects.Copy;
                        await subItem.CopyInto(task);
                        UpdateGraph(e.Source);
                        e.Handled = true;
                    }
                    else
                    {
                        e.DragEffects = DragDropEffects.None;
                    }
                }
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
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

        private void RelationPickerEditor_OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is not Control { DataContext: TaskRelationPickerViewModel picker })
            {
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (picker.ConfirmCommand.CanExecute(null))
                {
                    picker.ConfirmCommand.Execute(null);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                if (picker.CancelCommand.CanExecute(null))
                {
                    picker.CancelCommand.Execute(null);
                    e.Handled = true;
                }
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

        private void TaskTreeContextMenuItem_OnClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem ||
                !Enum.TryParse<TreeCommandKind>(menuItem.Tag?.ToString(), out var kind))
            {
                return;
            }

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
            ExecuteTreeCommand(kind, TreeCommandRoute.Hotkey);
        }

        private void ExecuteTreeCommand(TreeCommandKind kind, TreeCommandRoute route)
        {
            try
            {
                if (DataContext is not MainWindowViewModel vm || !TryGetCommandTree(route, out var tree))
                {
                    return;
                }

                if (route == TreeCommandRoute.Hotkey && IsTextInputFocused())
                {
                    return;
                }

                var roots = GetTreeRoots(tree);
                if (roots == null)
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
                        vm.ExpandAllNodes(roots);
                        break;
                    case TreeCommandKind.CollapseAll:
                        vm.CollapseAllNodes(roots);
                        break;
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

        private void UpdateActiveTreeContext(Control control)
        {
            var tree = control.FindParent<TreeView>();
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

            if (DataContext is not MainWindowViewModel vm)
            {
                return false;
            }

            if (TryGetValidatedActiveTree(out var activeTree) && ShouldUseActiveTreeForHotkey(vm, activeTree))
            {
                tree = activeTree;
                return true;
            }

            return TryGetCurrentModeTree(vm, out tree);
        }

        private bool TryGetCurrentModeTree(MainWindowViewModel vm, out TreeView tree)
        {
            tree = null!;

            if (!TryGetCurrentModeTreeName(vm, out var treeName))
            {
                return false;
            }

            var candidate = this.FindControl<TreeView>(treeName);
            return TryGetValidatedTree(candidate, out tree);
        }

        private static bool ShouldUseActiveTreeForHotkey(MainWindowViewModel vm, TreeView activeTree)
        {
            if (!IsKnownMainTaskTree(activeTree))
            {
                return true;
            }

            return !TryGetCurrentModeTreeName(vm, out var currentModeTreeName) ||
                   StringComparer.Ordinal.Equals(activeTree.Name, currentModeTreeName);
        }

        private static bool TryGetCurrentModeTreeName(MainWindowViewModel vm, out string treeName)
        {
            treeName = vm switch
            {
                _ when vm.AllTasksMode => "AllTasksTree",
                _ when vm.LastCreatedMode => "LastCreatedTree",
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
            return tree.Name switch
            {
                "AllTasksTree" => true,
                "LastCreatedTree" => true,
                "UnlockedTree" => true,
                "CompletedTree" => true,
                "ArchivedTree" => true,
                "LastOpenedTree" => true,
                _ => false
            };
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

            return GetSelectedWrapperForTree(vm, tree);
        }

        private static TaskWrapperViewModel? GetSelectedWrapperForTree(MainWindowViewModel vm, TreeView tree)
        {
            return tree.SelectedItem as TaskWrapperViewModel ?? tree.Name switch
            {
                "AllTasksTree" => vm.CurrentAllTasksItem,
                "LastCreatedTree" => vm.CurrentLastCreated,
                "UnlockedTree" => vm.CurrentUnlockedItem,
                "CompletedTree" => vm.CurrentCompletedItem,
                "ArchivedTree" => vm.CurrentArchivedItem,
                "LastOpenedTree" => vm.CurrentLastOpenedItem,
                _ => null
            };
        }

        private void ClearContextMenuContext()
        {
            _contextMenuTree = null;
            _contextMenuWrapper = null;
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

        private static bool IsTextInputControlOrAncestor(Control control)
        {
            Control? current = control;
            while (current != null)
            {
                if (current is TextBox or AutoCompleteBox or NumericUpDown)
                {
                    return true;
                }

                current = current.FindParent<Control>();
            }

            return false;
        }
    }
}
