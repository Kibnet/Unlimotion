using System;
using System.ComponentModel;
using System.Windows.Input;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using ReactiveUI;

namespace Unlimotion.ViewModel;

/// <summary>Checks admission again on Execute; disabling a control alone does not protect other callers.</summary>
public sealed class AdmissionGuardedCommand : ICommand, IReactiveCommand, IDisposable
{
    private readonly ICommand command;
    private readonly Func<bool> admission;
    private readonly INotifyPropertyChanged? owner;
    private readonly INotifyPropertyChanged? additionalOwner;
    private bool disposed;
    private readonly IScheduler notificationScheduler = RxSchedulers.MainThreadScheduler;

    public AdmissionGuardedCommand(ICommand command, Func<bool> admission, object owner, object? additionalOwner = null)
    {
        this.command = command;
        this.admission = admission;
        this.owner = owner as INotifyPropertyChanged;
        this.additionalOwner = additionalOwner as INotifyPropertyChanged;
        command.CanExecuteChanged += OnAvailabilityChanged;
        if (this.owner != null) this.owner.PropertyChanged += OnOwnerChanged;
        if (this.additionalOwner != null) this.additionalOwner.PropertyChanged += OnOwnerChanged;
    }

    public bool CanExecute(object? parameter) => !disposed && admission() && command.CanExecute(parameter);
    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) command.Execute(parameter);
    }
    public event EventHandler? CanExecuteChanged;
    public IObservable<bool> IsExecuting => (command as IReactiveCommand)?.IsExecuting ?? Observable.Return(false);
    public IObservable<Exception> ThrownExceptions => (command as IReactiveCommand)?.ThrownExceptions ?? Observable.Empty<Exception>();
    IObservable<bool> IReactiveCommand.CanExecute => Observable.FromEventPattern(
            handler => CanExecuteChanged += handler, handler => CanExecuteChanged -= handler)
        .Select(_ => CanExecute(null)).StartWith(CanExecute(null)).DistinctUntilChanged();
    private void OnAvailabilityChanged(object? sender, EventArgs args) => notificationScheduler.Schedule(() =>
    {
        if (!disposed) CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    });
    private void OnOwnerChanged(object? sender, PropertyChangedEventArgs args) => OnAvailabilityChanged(sender, args);
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        command.CanExecuteChanged -= OnAvailabilityChanged;
        if (owner != null) owner.PropertyChanged -= OnOwnerChanged;
        if (additionalOwner != null) additionalOwner.PropertyChanged -= OnOwnerChanged;
        if (command is IDisposable lifetime) lifetime.Dispose();
    }
}
