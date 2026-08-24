using System;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using Unlimotion.Notes.Conflicts;

namespace Unlimotion.ViewModel.Feed;

public sealed class FeedDocumentConflictViewModel : ReactiveObject, IDisposable
{
    private readonly DocumentConflictCoordinator coordinator;
    private bool isOpen = true;
    private bool isResolving;
    private string? errorMessage;
    private DocumentConflictResolutionResult? resolutionResult;

    public FeedDocumentConflictViewModel(
        DocumentConflictCoordinator coordinator,
        DocumentConflictState conflict)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        Conflict = conflict ?? throw new ArgumentNullException(nameof(conflict));
        UseEditorCommand = ReactiveCommand.CreateFromTask(() => ResolveAsync(DocumentConflictResolution.UseEditor));
        UseDiskCommand = ReactiveCommand.CreateFromTask(() => ResolveAsync(DocumentConflictResolution.UseDisk));
        SaveBothCommand = ReactiveCommand.CreateFromTask(() => ResolveAsync(DocumentConflictResolution.SaveBoth));
    }

    public DocumentConflictState Conflict { get; }

    public Func<DocumentConflictResolutionResult, Task>? ResolvedCallbackAsync { get; set; }

    public ReactiveCommand<Unit, Unit> UseEditorCommand { get; }

    public ReactiveCommand<Unit, Unit> UseDiskCommand { get; }

    public ReactiveCommand<Unit, Unit> SaveBothCommand { get; }

    public string EditorRelativePath => Conflict.EditorRelativePath;

    public string DiskRelativePath => Conflict.DiskRelativePath;

    public string EditorText => Conflict.EditorDocumentText;

    public string DiskText => Conflict.DiskDocument?.Text ?? string.Empty;

    public bool DiskVersionExists => Conflict.DiskDocument is not null;

    public bool IsOpen
    {
        get => isOpen;
        private set => this.RaiseAndSetIfChanged(ref isOpen, value);
    }

    public bool IsResolving
    {
        get => isResolving;
        private set => this.RaiseAndSetIfChanged(ref isResolving, value);
    }

    public string? ErrorMessage
    {
        get => errorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public DocumentConflictResolutionResult? ResolutionResult
    {
        get => resolutionResult;
        private set
        {
            this.RaiseAndSetIfChanged(ref resolutionResult, value);
            this.RaisePropertyChanged(nameof(IsResolved));
        }
    }

    public bool IsResolved => ResolutionResult is not null;

    public async Task ResolveAsync(DocumentConflictResolution resolution)
    {
        if (IsResolved || IsResolving)
        {
            return;
        }

        IsResolving = true;
        ErrorMessage = null;
        try
        {
            var result = await coordinator.ResolveAsync(Conflict.ConflictId, resolution).ConfigureAwait(true);
            ResolutionResult = result;
            if (ResolvedCallbackAsync is not null)
            {
                await ResolvedCallbackAsync(result).ConfigureAwait(true);
            }

            IsOpen = false;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsResolving = false;
        }
    }

    public void Dispose()
    {
        UseEditorCommand.Dispose();
        UseDiskCommand.Dispose();
        SaveBothCommand.Dispose();
    }
}
