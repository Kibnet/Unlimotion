using PropertyChanged;

namespace Unlimotion.ViewModel.Feed;

[AddINotifyPropertyChangedInterface]
public sealed class FeedSafePendingLocatorViewModel(
    string relativePath,
    string? areaIdentity,
    string blockKind,
    string contentHash,
    int occurrence)
{
    public string RelativePath { get; } = relativePath;

    public string? AreaIdentity { get; } = areaIdentity;

    public string BlockKind { get; } = blockKind;

    public string ContentHash { get; } = contentHash;

    public int Occurrence { get; } = occurrence;

    public string DisplayDetails => string.IsNullOrWhiteSpace(AreaIdentity)
        ? $"{RelativePath} · {BlockKind}"
        : $"{RelativePath} · {AreaIdentity} · {BlockKind}";
}
