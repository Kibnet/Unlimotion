namespace Unlimotion.TaskTree;

public class TaskStorageUpdateEventArgs : EventArgs
{
    public string Id { get; set; }
    public UpdateType Type { get; set; }
}