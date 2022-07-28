using System;

namespace Unlimotion.ViewModel;

public static class DisposableListExtensions
{
    public static void AddToDispose(this IDisposable disposable, DisposableList list)
    {
        list.Disposables.Add(disposable);
    }

    public static T AddToDisposeAndReturn<T>(this T disposable, DisposableList list) where T:IDisposable
    {
        list.Disposables.Add(disposable);
        return disposable;
    }
}