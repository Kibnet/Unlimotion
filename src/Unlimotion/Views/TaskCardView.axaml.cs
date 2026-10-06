using System;
using System.Linq;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI;
using Unlimotion.ViewModel;

namespace Unlimotion.Views;

/// <summary>Only a routed card, without the legacy list or category tab strip.</summary>
public partial class TaskCardView : TaskPresentationControl
{
    private IDisposable? _criterionFocus;
    private IDisposable? _relationFocus;

    public TaskCardView() : base(isDocumentPresentation: true)
    {
        InitializeComponent();
        InitializePresentation();
    }

    public TaskCardView(MainWindowViewModel owner, TaskItemViewModel task) : this()
    {
        CardContext = new TaskCardDocumentViewModel(owner, task);
        DataContext = owner;
        RouteTaskItem = task;
        _criterionFocus = task.WhenAnyValue(item => item.CompletionCriterionFocusRequestVersion)
            .Skip(1).Subscribe(request => QueueCompletionCriterionFocus(request,
                task.Id, task.CompletionCriterionFocusTargetId, 5));
        _relationFocus = CardContext.RelationEditor.WhenAnyValue(editor => editor.FocusRequestVersion)
            .Skip(1).Subscribe(_ => FocusRelationInput());
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
        _criterionFocus?.Dispose();
        _relationFocus?.Dispose();
        base.Dispose();
    }
}
