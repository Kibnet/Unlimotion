using System;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using Unlimotion.Notes.Review;

namespace Unlimotion.ViewModel.Feed;

public sealed class FeedReviewRecoveryViewModel : ReactiveObject, IDisposable
{
    private readonly FeedReviewSessionCoordinator coordinator;
    private bool isOpen = true;
    private bool isResolving;
    private string? errorMessage;

    public FeedReviewRecoveryViewModel(
        FeedReviewSessionCoordinator coordinator,
        ForeignReviewSession session)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        ContinueCommand = ReactiveCommand.CreateFromTask(ContinueAsync);
        AbandonCommand = ReactiveCommand.CreateFromTask(AbandonAsync);
    }

    public ForeignReviewSession Session { get; }

    public string ReviewSessionId => Session.ReviewSessionId;

    public string OwnerDeviceId => Session.OwnerDeviceId;

    public Func<bool, Task>? ResolvedCallbackAsync { get; set; }

    public ReactiveCommand<Unit, Unit> ContinueCommand { get; }

    public ReactiveCommand<Unit, Unit> AbandonCommand { get; }

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

    private Task ContinueAsync() => ResolveAsync(takeOver: true);

    private Task AbandonAsync() => ResolveAsync(takeOver: false);

    private async Task ResolveAsync(bool takeOver)
    {
        if (!IsOpen || IsResolving)
        {
            return;
        }

        IsResolving = true;
        ErrorMessage = null;
        try
        {
            if (takeOver)
            {
                await coordinator.TakeOverAsync(Session.ReviewSessionId).ConfigureAwait(true);
            }
            else
            {
                await coordinator.AbandonAsync(Session.ReviewSessionId).ConfigureAwait(true);
            }

            if (ResolvedCallbackAsync is not null)
            {
                await ResolvedCallbackAsync(takeOver).ConfigureAwait(true);
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
        ContinueCommand.Dispose();
        AbandonCommand.Dispose();
    }
}
