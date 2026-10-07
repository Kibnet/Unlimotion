using System;
using System.Windows.Input;

namespace Unlimotion.ViewModel;

/// <summary>Card state belongs to its routed task, never the shell's last selection.</summary>
public sealed class TaskCardDocumentViewModel : IDisposable
{
    public TaskCardDocumentViewModel(MainWindowViewModel owner, TaskItemViewModel task)
    {
        Owner = owner;
        Task = task;
        RelationEditor = owner.CreateTaskDocumentRelationEditor();
        CurrentItemContains = owner.CreateTaskDocumentRelationRoot(task, TaskRelationKind.Containing);
        CurrentItemBlockedBy = owner.CreateTaskDocumentRelationRoot(task, TaskRelationKind.Blocking);
        CurrentItemBlocks = owner.CreateTaskDocumentRelationRoot(task, TaskRelationKind.Blocked);
        Remove = owner.CreateTaskDocumentRemoveCommand(task);
        MoveToPath = owner.CreateTaskDocumentMoveCommand(task);
    }

    public MainWindowViewModel Owner { get; }
    public TaskItemViewModel Task { get; }
    public TaskRelationEditorViewModel RelationEditor { get; }
    public TaskWrapperViewModel CurrentItemContains { get; }
    public TaskWrapperViewModel CurrentItemBlockedBy { get; }
    public TaskWrapperViewModel CurrentItemBlocks { get; }
    public ICommand Remove { get; }
    public ICommand MoveToPath { get; }

    public void Dispose()
    {
        RelationEditor.Dispose();
        CurrentItemContains.Dispose();
        CurrentItemBlockedBy.Dispose();
        CurrentItemBlocks.Dispose();
        (Remove as IDisposable)?.Dispose();
        (MoveToPath as IDisposable)?.Dispose();
    }
}
