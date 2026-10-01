namespace Unlimotion.Notes.Operations;

/// <summary>The configured task source and its credential-free storage binding, not a task ID.</summary>
public sealed record FeedTaskSourceIdentity(string SourceId, string StorageBindingFingerprint)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(StorageBindingFingerprint);
    }

    public static void RequireCurrent(FeedTaskSourceIdentity? expected, Func<FeedTaskSourceIdentity?>? current)
    {
        // Unbound in-memory integrations remain compatible. A configured application source is fail-closed.
        if (current is null && expected is null) return;
        var actual = current?.Invoke();
        if (expected is null || actual is null || expected != actual)
            throw new InvalidOperationException("FeedTaskSourceMismatch");
        expected.Validate();
    }
}
