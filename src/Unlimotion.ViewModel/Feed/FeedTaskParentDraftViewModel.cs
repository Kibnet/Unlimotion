using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ReactiveUI;

namespace Unlimotion.ViewModel.Feed;

/// <summary>Uses the existing relation search, but only edits an intent; never writes task relations.</summary>
public sealed class FeedTaskParentDraftViewModel : ReactiveObject, IDisposable
{
    private readonly MainWindowViewModel owner;
    private TaskCompletionSource<AreaRootTaskReference?>? pickCompletion;
    public FeedTaskParentDraftViewModel(MainWindowViewModel owner)
    {
        this.owner = owner;
        Editor = new TaskRelationEditorViewModel(
            () => owner.taskRepository?.Tasks.Items ?? [],
            id => owner.taskRepository?.Tasks.Items.FirstOrDefault(task => task.Id == id),
            () => false,
            (_, _, task) => task.IsCompleted is not null && (pickCompletion is not null || Parents.All(parent => parent.Id != task.Id)),
            (_, _, task) =>
            {
                var reference = new AreaRootTaskReference(task.Id, task.Title);
                if (pickCompletion is { } completion)
                {
                    pickCompletion = null;
                    completion.TrySetResult(reference);
                }
                else
                {
                    Parents.Add(reference);
                    IsManualOverride = true;
                }
                return Task.FromResult(true);
            },
            task => string.Join(" / ", task.ParentsTasks.Select(parent => parent.Title)), owner.ManagerWrapper);
        Editor.PropertyChanged += OnEditorChanged;
        RemoveCommand = ReactiveCommand.Create<AreaRootTaskReference>(parent =>
        {
            Parents.Remove(parent);
            IsManualOverride = true;
        });
    }
    public ObservableCollection<AreaRootTaskReference> Parents { get; } = new();
    public TaskRelationEditorViewModel Editor { get; }
    public bool IsManualOverride { get; private set; }
    public ICommand RemoveCommand { get; }
    public void Open()
    {
        var anchor = owner.taskRepository?.Tasks.Items.FirstOrDefault();
        Editor.Open(TaskRelationKind.Parents, anchor);
    }
    public Task<AreaRootTaskReference?> PickSingleAsync()
    {
        pickCompletion?.TrySetResult(null);
        pickCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = pickCompletion.Task;
        Open();
        if (!Editor.IsOpen) { pickCompletion.TrySetResult(null); pickCompletion = null; }
        return result;
    }
    public void ApplyDefaults(IEnumerable<AreaRootTaskReference> defaults, bool force = false)
    {
        if (IsManualOverride && !force) return;
        Parents.Clear();
        foreach (var parent in defaults.DistinctBy(parent => parent.Id)) Parents.Add(parent);
        IsManualOverride = false;
    }
    private void OnEditorChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(Editor.IsOpen) && !Editor.IsOpen)
        { pickCompletion?.TrySetResult(null); pickCompletion = null; }
    }
    public void Dispose()
    {
        pickCompletion?.TrySetResult(null);
        Editor.PropertyChanged -= OnEditorChanged;
        Editor.Dispose();
        (RemoveCommand as IDisposable)?.Dispose();
    }
}
