#nullable enable
using System;
using System.ComponentModel;
using System.Reactive.Linq;

namespace Unlimotion.ViewModel;

internal static class TaskPropertyObservation
{
    internal static IObservable<TValue> Observe<TSource, TValue>(
        TSource source, Func<TSource, TValue> valueProvider, string propertyName)
        where TSource : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(valueProvider);
        ArgumentException.ThrowIfNullOrEmpty(propertyName);
        // Fody supplies INPC after compilation, so it cannot be a generic constraint.
        if (source is not INotifyPropertyChanged notifications)
            throw new ArgumentException("The source must implement INotifyPropertyChanged.", nameof(source));

        return Observable.Defer(() => Observable
            .FromEventPattern<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                handler => notifications.PropertyChanged += handler,
                handler => notifications.PropertyChanged -= handler)
            .Where(change => string.IsNullOrEmpty(change.EventArgs.PropertyName) ||
                string.Equals(change.EventArgs.PropertyName, propertyName, StringComparison.Ordinal))
            .Select(_ => valueProvider(source))
            // Match WhenAny's initial delivery before event attachment, including reentrancy.
            .StartWith(valueProvider(source)))
            .DistinctUntilChanged();
    }
}
