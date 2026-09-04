using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData.Binding;
using ReactiveUI;
using Unlimotion.ViewModel;

namespace Unlimotion.Views;

public partial class TaskRelationsControl : UserControl
{
    public const string CurrentTaskAutomationIdPrefix = "CurrentTaskParentsRelation";
    public const string CurrentTaskParentsTreeAutomationId = "CurrentItemParentsTree";
    public const string FeedAutomationIdPrefix = "FeedTaskParentsRelation";
    public const string FeedParentsTreeAutomationId = "FeedTaskParentsTree";

    private const int MaxFocusRetries = 5;

    public static readonly StyledProperty<MainWindowViewModel?> OwnerProperty =
        AvaloniaProperty.Register<TaskRelationsControl, MainWindowViewModel?>(nameof(Owner));

    public static readonly StyledProperty<TaskItemViewModel?> TargetTaskProperty =
        AvaloniaProperty.Register<TaskRelationsControl, TaskItemViewModel?>(nameof(TargetTask));

    public static readonly StyledProperty<string> AutomationIdPrefixProperty =
        AvaloniaProperty.Register<TaskRelationsControl, string>(
            nameof(AutomationIdPrefix),
            CurrentTaskAutomationIdPrefix);

    public static readonly StyledProperty<string> ParentsTreeAutomationIdProperty =
        AvaloniaProperty.Register<TaskRelationsControl, string>(
            nameof(ParentsTreeAutomationId),
            CurrentTaskParentsTreeAutomationId);

    public static readonly StyledProperty<bool> OpenParentTaskOnDoubleTapProperty =
        AvaloniaProperty.Register<TaskRelationsControl, bool>(nameof(OpenParentTaskOnDoubleTap));

    private TaskWrapperViewModel? _parentsRoot;
    private MainWindowViewModel? _activeOwner;
    private TaskItemViewModel? _activeTargetTask;
    private TaskRelationEditorViewModel? _subscribedEditor;
    private bool _isAttached;
    private bool _isEditorOpen;

    public TaskRelationsControl()
    {
        InitializeComponent();
        UpdateAutomationIds();

        AttachedToVisualTree += TaskRelationsControl_OnAttachedToVisualTree;
        DetachedFromVisualTree += TaskRelationsControl_OnDetachedFromVisualTree;
    }

    public MainWindowViewModel? Owner
    {
        get => GetValue(OwnerProperty);
        set => SetValue(OwnerProperty, value);
    }

    public TaskItemViewModel? TargetTask
    {
        get => GetValue(TargetTaskProperty);
        set => SetValue(TargetTaskProperty, value);
    }

    public string AutomationIdPrefix
    {
        get => GetValue(AutomationIdPrefixProperty);
        set => SetValue(AutomationIdPrefixProperty, value);
    }

    public string ParentsTreeAutomationId
    {
        get => GetValue(ParentsTreeAutomationIdProperty);
        set => SetValue(ParentsTreeAutomationIdProperty, value);
    }

    public bool OpenParentTaskOnDoubleTap
    {
        get => GetValue(OpenParentTaskOnDoubleTapProperty);
        set => SetValue(OpenParentTaskOnDoubleTapProperty, value);
    }

    public TaskWrapperViewModel? ParentsRoot
    {
        get => _parentsRoot;
        private set => SetAndRaise(ParentsRootProperty, ref _parentsRoot, value);
    }

    public static readonly DirectProperty<TaskRelationsControl, TaskWrapperViewModel?> ParentsRootProperty =
        AvaloniaProperty.RegisterDirect<TaskRelationsControl, TaskWrapperViewModel?>(
            nameof(ParentsRoot),
            control => control.ParentsRoot);

    public bool IsEditorOpen
    {
        get => _isEditorOpen;
        private set => SetAndRaise(IsEditorOpenProperty, ref _isEditorOpen, value);
    }

    public static readonly DirectProperty<TaskRelationsControl, bool> IsEditorOpenProperty =
        AvaloniaProperty.RegisterDirect<TaskRelationsControl, bool>(
            nameof(IsEditorOpen),
            control => control.IsEditorOpen);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == OwnerProperty || change.Property == TargetTaskProperty)
        {
            UpdateTargetContext();
        }
        else if (change.Property == AutomationIdPrefixProperty ||
                 change.Property == ParentsTreeAutomationIdProperty)
        {
            UpdateAutomationIds();
        }
    }

    private void TaskRelationsControl_OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        UpdateTargetContext();
        SubscribeToEditor();
        RebuildParentsRoot();
        RefreshEditorState();
    }

    private void TaskRelationsControl_OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        CloseOwnedEditor();
        UnsubscribeFromEditor();
        DisposeParentsRoot();
        _isAttached = false;
    }

    private void UpdateTargetContext()
    {
        var nextOwner = Owner;
        var nextTargetTask = TargetTask;
        var ownerChanged = !ReferenceEquals(_activeOwner, nextOwner);
        var targetChanged = !ReferenceEquals(_activeTargetTask, nextTargetTask);

        if (!ownerChanged && !targetChanged)
        {
            return;
        }

        CloseOwnedEditor();
        UnsubscribeFromEditor();
        DisposeParentsRoot();

        _activeOwner = nextOwner;
        _activeTargetTask = nextTargetTask;

        if (_isAttached)
        {
            SubscribeToEditor();
            RebuildParentsRoot();
        }

        RefreshEditorState();
    }

    private void SubscribeToEditor()
    {
        var editor = _activeOwner?.CurrentRelationEditor;
        if (ReferenceEquals(editor, _subscribedEditor))
        {
            return;
        }

        UnsubscribeFromEditor();
        _subscribedEditor = editor;
        if (_subscribedEditor != null)
        {
            _subscribedEditor.PropertyChanged += RelationEditor_OnPropertyChanged;
        }
    }

    private void UnsubscribeFromEditor()
    {
        if (_subscribedEditor != null)
        {
            _subscribedEditor.PropertyChanged -= RelationEditor_OnPropertyChanged;
            _subscribedEditor = null;
        }
    }

    private void RelationEditor_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshEditorState();
        if (e.PropertyName == nameof(TaskRelationEditorViewModel.FocusRequestVersion))
        {
            QueueInputFocus(MaxFocusRetries);
        }
    }

    private void RefreshEditorState()
    {
        IsEditorOpen = _activeOwner?.CurrentRelationEditor.IsOpenFor(
            TaskRelationKind.Parents,
            _activeTargetTask) == true;
    }

    private void CloseOwnedEditor()
    {
        _activeOwner?.CurrentRelationEditor.CloseFor(TaskRelationKind.Parents, _activeTargetTask);
        RefreshEditorState();
    }

    private void RebuildParentsRoot()
    {
        DisposeParentsRoot();
        if (_activeTargetTask == null)
        {
            return;
        }

        var sortComparer = _activeOwner == null
            ? Comparers.Default
            : _activeOwner.WhenAnyValue(owner => owner.CurrentSortDefinition)
                .Where(definition => definition != null)
                .Select(definition => definition.Comparer);

        var actions = new TaskWrapperActions
        {
            ChildSelector = task => task.ParentsTasks.ToObservableChangeSet(),
            RemoveAction = wrapper =>
                wrapper.TaskItem.DeleteParentChildRelationCommand.Execute(wrapper.Parent!.TaskItem),
            SortComparer = sortComparer
        };

        ParentsRoot = new TaskWrapperViewModel(null, _activeTargetTask, actions);
    }

    private void DisposeParentsRoot()
    {
        var previousRoot = ParentsRoot;
        ParentsRoot = null;
        previousRoot?.Dispose();
    }

    private void UpdateAutomationIds()
    {
        if (ParentAddButton == null || ParentsTree == null)
        {
            return;
        }

        var prefix = string.IsNullOrWhiteSpace(AutomationIdPrefix)
            ? CurrentTaskAutomationIdPrefix
            : AutomationIdPrefix.Trim();
        var treeAutomationId = string.IsNullOrWhiteSpace(ParentsTreeAutomationId)
            ? CurrentTaskParentsTreeAutomationId
            : ParentsTreeAutomationId.Trim();

        AutomationProperties.SetAutomationId(ParentAddButton, $"{prefix}AddButton");
        AutomationProperties.SetAutomationId(ParentInput, $"{prefix}AddInput");
        AutomationProperties.SetAutomationId(ParentSuggestions, $"{prefix}Suggestions");
        AutomationProperties.SetAutomationId(ParentCancelButton, $"{prefix}AddCancelButton");
        AutomationProperties.SetAutomationId(ParentConfirmButton, $"{prefix}AddConfirmButton");
        AutomationProperties.SetAutomationId(ParentsTree, treeAutomationId);
    }

    private void ParentAddButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _activeOwner?.CurrentRelationEditor.Open(TaskRelationKind.Parents, _activeTargetTask);
    }

    private void RelationEditorControl_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_activeOwner?.CurrentRelationEditor is not { } editor || !IsEditorOpen)
        {
            return;
        }

        if (e.Key == Key.Enter && editor.ConfirmCommand.CanExecute(null))
        {
            editor.ConfirmCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && editor.CancelCommand.CanExecute(null))
        {
            editor.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void RelationEditorSuggestions_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        var editor = _activeOwner?.CurrentRelationEditor;
        if (editor?.ConfirmCommand.CanExecute(null) == true && IsEditorOpen)
        {
            editor.ConfirmCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ParentTask_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (!OpenParentTaskOnDoubleTap ||
            _activeOwner == null ||
            sender is not Control { DataContext: TaskWrapperViewModel wrapper })
        {
            return;
        }

        _activeOwner.CurrentTaskItem = wrapper.TaskItem;
        e.Handled = true;
    }

    private void QueueInputFocus(int retriesRemaining)
    {
        Dispatcher.UIThread.Post(
            () => TryFocusInput(retriesRemaining),
            DispatcherPriority.Loaded);
    }

    private void TryFocusInput(int retriesRemaining)
    {
        if (!_isAttached ||
            !IsEffectivelyVisible ||
            !IsEditorOpen ||
            _activeOwner?.CurrentRelationEditor.IsOpenFor(TaskRelationKind.Parents, _activeTargetTask) != true)
        {
            return;
        }

        var input = this.GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(candidate =>
                ReferenceEquals(candidate, ParentInput) &&
                candidate.IsAttachedToVisualTree() &&
                candidate.IsVisible &&
                candidate.IsEnabled);

        if (input?.Focus() == true)
        {
            input.CaretIndex = input.Text?.Length ?? 0;
            return;
        }

        if (retriesRemaining > 0)
        {
            QueueInputFocus(retriesRemaining - 1);
        }
    }
}
