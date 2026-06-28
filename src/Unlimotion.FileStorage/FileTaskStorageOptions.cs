namespace Unlimotion.Storage;

public sealed record FileTaskStorageOptions
{
    public string Path { get; init; } = string.Empty;
    public bool UseWatcher { get; init; }
    public bool PreserveUnknownJson { get; init; } = true;
    public bool UseDirectoryLock { get; init; } = true;
}
