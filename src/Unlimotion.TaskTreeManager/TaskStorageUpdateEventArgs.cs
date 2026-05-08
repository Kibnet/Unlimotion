namespace Unlimotion.TaskTree;

public class TaskStorageUpdateEventArgs : EventArgs
{
    public string Id { get; set; } = string.Empty;
    public UpdateType Type { get; set; }
}
