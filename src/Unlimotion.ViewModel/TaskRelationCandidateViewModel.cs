namespace Unlimotion.ViewModel;

public sealed class TaskRelationCandidateViewModel
{
    public TaskRelationCandidateViewModel(TaskItemViewModel task, string title, string context)
    {
        Task = task;
        Title = title;
        Context = context;
    }

    public TaskItemViewModel Task { get; }

    public string Title { get; }

    public string Context { get; }

    public override string ToString() => Title;
}
