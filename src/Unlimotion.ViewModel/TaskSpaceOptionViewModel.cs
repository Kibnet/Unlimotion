namespace Unlimotion.ViewModel;

public sealed class TaskSpaceOptionViewModel
{
    public required string SourceId { get; init; }
    public required string DisplayName { get; set; }
    public TaskSourceKind Kind { get; init; }
    public string SourceSummary { get; init; } = string.Empty;
    public bool IsActive { get; set; }
    public string ActiveMarker => IsActive ? "●" : string.Empty;
}
