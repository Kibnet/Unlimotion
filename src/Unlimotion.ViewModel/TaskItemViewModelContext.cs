namespace Unlimotion.ViewModel;

public sealed class TaskItemViewModelContext
{
    public string SourceId { get; init; } = TaskSourceDescriptor.DefaultSourceId;
    public INotificationManagerWrapper? NotificationManager { get; init; }
    public MainWindowViewModel? MainWindow { get; set; }
}
