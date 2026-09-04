using PropertyChanged;
using ReactiveUI;
using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using System.Windows.Input;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel.Feed;

public enum FeedPendingRecoveryKind
{
    TaskConversion,
    NoteExtraction,
    MoveToToday,
    HeadingAreaConversion
}

[AddINotifyPropertyChangedInterface]
public sealed class FeedPendingRecoveryViewModel : IDisposable
{
    private readonly CompositeDisposable disposables = new();

    public FeedPendingRecoveryViewModel(
        string operationId,
        FeedPendingRecoveryKind kind,
        string sourcePath,
        string message,
        bool canKeepBoth,
        Func<FeedPendingRecoveryViewModel, Task> finishAsync,
        Func<FeedPendingRecoveryViewModel, Task> keepBothAsync)
    {
        OperationId = operationId;
        Kind = kind;
        SourcePath = sourcePath;
        Message = message;
        CanKeepBoth = canKeepBoth;
        var finish = ReactiveCommand.CreateFromTask(() => finishAsync(this));
        FinishCommand = finish;
        disposables.Add(finish);
        var keepBoth = ReactiveCommand.CreateFromTask(
            () => keepBothAsync(this),
            this.WhenAnyValue(static value => value.CanKeepBoth));
        KeepBothCommand = keepBoth;
        disposables.Add(keepBoth);
    }

    public string OperationId { get; }

    public FeedPendingRecoveryKind Kind { get; }

    public string SourcePath { get; }

    public string Message { get; set; }

    public bool CanKeepBoth { get; set; }

    public string DisplayKind => Kind switch
    {
        FeedPendingRecoveryKind.TaskConversion => L10n.Get("FeedPendingRecoveryKindTask"),
        FeedPendingRecoveryKind.NoteExtraction => L10n.Get("FeedPendingRecoveryKindNote"),
        FeedPendingRecoveryKind.MoveToToday => L10n.Get("FeedPendingRecoveryKindMove"),
        FeedPendingRecoveryKind.HeadingAreaConversion => L10n.Get("FeedPendingRecoveryKindArea"),
        _ => Kind.ToString()
    };

    public ICommand FinishCommand { get; }

    public ICommand KeepBothCommand { get; }

    public void Dispose() => disposables.Dispose();
}
