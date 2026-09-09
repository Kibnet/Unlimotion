using System;
using System.Collections.ObjectModel;
using System.Linq;
using ReactiveUI;

namespace Unlimotion.ViewModel.Feed;

public sealed class FeedDocumentWorkspaceViewModel : ReactiveObject, IDisposable
{
    private FeedThematicDocumentViewModel? activeDocument;
    public ObservableCollection<FeedThematicDocumentViewModel> Documents { get; } = [];
    public FeedThematicDocumentViewModel? ActiveDocument
    {
        get => activeDocument;
        set => this.RaiseAndSetIfChanged(ref activeDocument, value);
    }

    public FeedThematicDocumentViewModel? Find(string relativePath) => Documents.FirstOrDefault(document =>
        string.Equals(document.RelativePath.Replace('\\', '/'), relativePath.Replace('\\', '/'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        ?? Documents.FirstOrDefault(document => document.PendingRelativePath is { } pending
            && string.Equals(pending.Replace('\\', '/'), relativePath.Replace('\\', '/'),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

    public void Dispose()
    {
        ActiveDocument = null;
        foreach (var document in Documents) document.Dispose();
        Documents.Clear();
    }
}
