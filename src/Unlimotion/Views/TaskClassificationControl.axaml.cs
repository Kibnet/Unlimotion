using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.Views;

public partial class TaskClassificationControl : UserControl
{
    public const string CurrentTaskAutomationIdPrefix = "CurrentTaskClassification";
    public const string FeedAutomationIdPrefix = "FeedTaskClassification";

    public static readonly StyledProperty<TaskItemViewModel?> TaskProperty =
        AvaloniaProperty.Register<TaskClassificationControl, TaskItemViewModel?>(nameof(Task));

    public static readonly StyledProperty<MainWindowViewModel?> OwnerProperty =
        AvaloniaProperty.Register<TaskClassificationControl, MainWindowViewModel?>(nameof(Owner));

    public static readonly StyledProperty<IEnumerable<FeedAreaOptionViewModel>?> AreaOptionsProperty =
        AvaloniaProperty.Register<TaskClassificationControl, IEnumerable<FeedAreaOptionViewModel>?>(nameof(AreaOptions));

    public static readonly StyledProperty<IEnumerable<TaskClassificationAreaDefinition>?> AreaDefinitionsProperty =
        AvaloniaProperty.Register<TaskClassificationControl, IEnumerable<TaskClassificationAreaDefinition>?>(nameof(AreaDefinitions));

    public static readonly StyledProperty<TaskClassificationEditorViewModel?> EditorProperty =
        AvaloniaProperty.Register<TaskClassificationControl, TaskClassificationEditorViewModel?>(nameof(Editor));

    public static readonly StyledProperty<string> AutomationIdPrefixProperty =
        AvaloniaProperty.Register<TaskClassificationControl, string>(
            nameof(AutomationIdPrefix),
            CurrentTaskAutomationIdPrefix);

    private TaskClassificationEditorViewModel? effectiveEditor;
    private TaskClassificationEditorViewModel? ownedEditor;
    private INotifyCollectionChanged? observedAreaOptions;
    private ServerStorage? observedServer;

    public TaskClassificationControl()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        UpdateAutomationIds();
    }

    public TaskItemViewModel? Task
    {
        get => GetValue(TaskProperty);
        set => SetValue(TaskProperty, value);
    }

    public MainWindowViewModel? Owner
    {
        get => GetValue(OwnerProperty);
        set => SetValue(OwnerProperty, value);
    }

    public IEnumerable<FeedAreaOptionViewModel>? AreaOptions
    {
        get => GetValue(AreaOptionsProperty);
        set => SetValue(AreaOptionsProperty, value);
    }

    public IEnumerable<TaskClassificationAreaDefinition>? AreaDefinitions
    {
        get => GetValue(AreaDefinitionsProperty);
        set => SetValue(AreaDefinitionsProperty, value);
    }

    public TaskClassificationEditorViewModel? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    public string AutomationIdPrefix
    {
        get => GetValue(AutomationIdPrefixProperty);
        set => SetValue(AutomationIdPrefixProperty, value);
    }

    public TaskClassificationEditorViewModel? EffectiveEditor
    {
        get => effectiveEditor;
        private set => SetAndRaise(EffectiveEditorProperty, ref effectiveEditor, value);
    }

    public static readonly DirectProperty<TaskClassificationControl, TaskClassificationEditorViewModel?> EffectiveEditorProperty =
        AvaloniaProperty.RegisterDirect<TaskClassificationControl, TaskClassificationEditorViewModel?>(
            nameof(EffectiveEditor),
            control => control.EffectiveEditor);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TaskProperty
            || change.Property == OwnerProperty
            || change.Property == AreaOptionsProperty
            || change.Property == AreaDefinitionsProperty
            || change.Property == EditorProperty)
        {
            SubscribeAreaOptions();
            SubscribeServer();
            RebuildEditor();
        }
        else if (change.Property == AutomationIdPrefixProperty)
        {
            UpdateAutomationIds();
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs eventArgs)
    {
        SubscribeAreaOptions();
        SubscribeServer();
        RebuildEditor();
        UpdateAutomationIds();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs eventArgs)
    {
        UnsubscribeAreaOptions();
        UnsubscribeServer();
        ownedEditor?.Dispose();
        ownedEditor = null;
        if (Editor is null)
        {
            EffectiveEditor = null;
        }
    }

    private void RebuildEditor()
    {
        if (Editor is not null)
        {
            ownedEditor?.Dispose();
            ownedEditor = null;
            EffectiveEditor = Editor;
            EffectiveEditor.RefreshCapability();
            return;
        }

        if (Task is null)
        {
            ownedEditor?.Dispose();
            ownedEditor = null;
            EffectiveEditor = null;
            return;
        }

        var definitions = AreaDefinitions?.ToArray()
                          ?? (AreaOptions ?? [])
                              .Where(static option => option.IsClassificationSelectable)
                              .Select(static option => new TaskClassificationAreaDefinition(
                                  option.StableAreaId!,
                                  option.Area!.Name))
                              .ToArray();
        ownedEditor?.Dispose();
        ownedEditor = TaskClassificationEditorViewModel.ForTask(Task, definitions, SupportsClassificationEditing);
        EffectiveEditor = ownedEditor;
    }

    private bool SupportsClassificationEditing()
    {
        var taskTreeManager = Owner?.taskRepository?.TaskTreeManager;
        return taskTreeManager?.Storage is not ServerStorage server || server.SupportsTaskClassification;
    }

    private void SubscribeAreaOptions()
    {
        var next = (AreaDefinitions as INotifyCollectionChanged)
                   ?? AreaOptions as INotifyCollectionChanged;
        if (ReferenceEquals(next, observedAreaOptions))
        {
            return;
        }

        UnsubscribeAreaOptions();
        observedAreaOptions = next;
        if (observedAreaOptions is not null)
        {
            observedAreaOptions.CollectionChanged += OnAreaOptionsChanged;
        }
    }

    private void UnsubscribeAreaOptions()
    {
        if (observedAreaOptions is not null)
        {
            observedAreaOptions.CollectionChanged -= OnAreaOptionsChanged;
            observedAreaOptions = null;
        }
    }

    private void OnAreaOptionsChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs) => RebuildEditor();

    private void SubscribeServer()
    {
        var next = Owner?.taskRepository?.TaskTreeManager?.Storage as ServerStorage;
        if (ReferenceEquals(next, observedServer))
        {
            return;
        }

        UnsubscribeServer();
        observedServer = next;
        if (observedServer is not null)
        {
            observedServer.OnConnected += OnServerConnected;
        }
    }

    private void UnsubscribeServer()
    {
        if (observedServer is not null)
        {
            observedServer.OnConnected -= OnServerConnected;
            observedServer = null;
        }
    }

    private void OnServerConnected() => Dispatcher.UIThread.Post(
        () => EffectiveEditor?.RefreshCapability(),
        DispatcherPriority.Background);

    private void UpdateAutomationIds()
    {
        if (ClassificationRoot is null)
        {
            return;
        }

        var prefix = string.IsNullOrWhiteSpace(AutomationIdPrefix)
            ? CurrentTaskAutomationIdPrefix
            : AutomationIdPrefix.Trim();
        AutomationProperties.SetAutomationId(ClassificationRoot, $"{prefix}Root");
        AutomationProperties.SetAutomationId(GoalCheckBox, $"{prefix}GoalCheckBox");
        AutomationProperties.SetAutomationId(BlockedExplanation, $"{prefix}BlockedExplanation");
        AutomationProperties.SetAutomationId(SelectedAreaChips, $"{prefix}SelectedAreaChips");
        AutomationProperties.SetAutomationId(AreaPickerExpander, $"{prefix}AreaPickerExpander");
        AutomationProperties.SetAutomationId(AreaOptionsList, $"{prefix}AreaOptionsList");
    }
}
