using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using DynamicData.Binding;
using ReactiveUI;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel.Localization;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel;

public partial class MainWindowViewModel
{
    public TaskRelationEditorViewModel CreateTaskDocumentRelationEditor() => new(
        () => taskRepository?.Tasks.Items ?? Enumerable.Empty<TaskItemViewModel>(),
        FindTaskById,
        () => Settings.IsFuzzySearch,
        IsRelationCandidateValid,
        TryAddRelationAsync,
        GetRelationCandidateContext,
        ManagerWrapper,
        LocalizationService.Current);

    public TaskWrapperViewModel CreateTaskDocumentRelationRoot(TaskItemViewModel task, TaskRelationKind kind)
    {
        var actions = new TaskWrapperActions
        {
            ChildSelector = item => kind switch
            {
                TaskRelationKind.Parents => item.ParentsTasks.ToObservableChangeSet(),
                TaskRelationKind.Containing => item.ContainsTasks.ToObservableChangeSet(),
                TaskRelationKind.Blocking => item.BlockedByTasks.ToObservableChangeSet(),
                TaskRelationKind.Blocked => item.BlocksTasks.ToObservableChangeSet(),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            },
            RemoveAction = wrapper =>
            {
                var parent = wrapper.Parent!.TaskItem;
                switch (kind)
                {
                    case TaskRelationKind.Parents:
                        wrapper.TaskItem.DeleteParentChildRelationCommand.Execute(parent);
                        break;
                    case TaskRelationKind.Containing:
                        parent.DeleteParentChildRelationCommand.Execute(wrapper.TaskItem);
                        break;
                    case TaskRelationKind.Blocking:
                        parent.UnblockCommand.Execute(wrapper.TaskItem);
                        break;
                    case TaskRelationKind.Blocked:
                        wrapper.TaskItem.UnblockCommand.Execute(parent);
                        break;
                }
            },
            SortComparer = Comparers.Default
        };
        return new TaskWrapperViewModel(null, task, actions);
    }

    public ICommand CreateTaskDocumentRemoveCommand(TaskItemViewModel task) =>
        ReactiveCommand.CreateFromTask(() => RemoveTaskItem(task));

    public ICommand CreateTaskDocumentMoveCommand(TaskItemViewModel task) =>
        ReactiveCommand.CreateFromTask(async () =>
        {
            if (Dialogs is null) return;
            var destination = await Dialogs.ShowOpenFolderDialogAsync(
                L10n.Get("FolderPickerTaskStoragePath"), null);
            if (!string.IsNullOrWhiteSpace(destination) && MoveTaskTreeToFileStorageAsync is { } move)
                await move(task, taskRepository, destination);
        });
}
