using System;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace Unlimotion.ViewModel;

internal static class TaskPropertySignals
{
    internal static IObservable<T> Observe<TSource, T>(TSource source, string propertyName, Func<TSource, T> getter)
        where TSource : class =>
        Observable.Create<T>(observer =>
        {
            if (source is not INotifyPropertyChanged notifications)
            {
                observer.OnError(new ArgumentException("The source must implement INotifyPropertyChanged.", nameof(source)));
                return Disposable.Empty;
            }

            T initial;
            try { initial = getter(source); }
            catch (Exception error) { observer.OnError(error); return Disposable.Empty; }

            // Original WhenAny delivers the initial value before attaching the event handler.
            // Keep consumer calls outside the getter catch so their failures propagate.
            observer.OnNext(initial);
            PropertyChangedEventHandler handler = (_, change) =>
            {
                if (!string.IsNullOrEmpty(change.PropertyName) &&
                    !string.Equals(change.PropertyName, propertyName, StringComparison.Ordinal)) return;

                T value;
                try { value = getter(source); }
                catch (Exception error) { observer.OnError(error); return; }
                observer.OnNext(value);
            };
            notifications.PropertyChanged += handler;
            return Disposable.Create(() => notifications.PropertyChanged -= handler);
        }).DistinctUntilChanged();
}
