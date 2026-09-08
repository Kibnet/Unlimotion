using System;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using Unlimotion.Notes.Identity;

namespace Unlimotion.ViewModel.Feed;

public sealed class FeedVaultIdentityConflictViewModel : ReactiveObject, IDisposable
{
    private readonly VaultIdentityConflictCoordinator coordinator;
    private bool isOpen = true;
    private bool isResolving;
    private string? errorMessage;

    public FeedVaultIdentityConflictViewModel(
        VaultIdentityConflictCoordinator coordinator,
        VaultIdentityConflictBundle conflict)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        Conflict = conflict ?? throw new ArgumentNullException(nameof(conflict));
        UseCurrentRootCommand = ReactiveCommand.CreateFromTask(
            () => ResolveAsync(VaultIdentityConflictResolution.UseCurrentRootIdentity));
        ReconnectRootCommand = ReactiveCommand.CreateFromTask(
            () => ResolveAsync(VaultIdentityConflictResolution.ReconnectAnotherRoot));
        StayReadOnlyCommand = ReactiveCommand.CreateFromTask(
            () => ResolveAsync(VaultIdentityConflictResolution.StayReadOnly));
    }

    public VaultIdentityConflictBundle Conflict { get; }

    public Func<VaultIdentityConflictResolutionResult, Task>? ResolvedCallbackAsync { get; set; }

    public ReactiveCommand<Unit, Unit> UseCurrentRootCommand { get; }

    public ReactiveCommand<Unit, Unit> ReconnectRootCommand { get; }

    public ReactiveCommand<Unit, Unit> StayReadOnlyCommand { get; }

    public string AcceptedVaultId => Conflict.AcceptedBranch.VaultId;

    public string CurrentRootVaultId => Conflict.CurrentRootBranch.VaultId;

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

    public async Task ResolveAsync(VaultIdentityConflictResolution resolution)
    {
        if (!IsOpen || IsResolving)
        {
            return;
        }

        IsResolving = true;
        ErrorMessage = null;
        try
        {
            var result = await coordinator.ResolveAsync(Conflict, resolution).ConfigureAwait(true);
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
        UseCurrentRootCommand.Dispose();
        ReconnectRootCommand.Dispose();
        StayReadOnlyCommand.Dispose();
    }
}
