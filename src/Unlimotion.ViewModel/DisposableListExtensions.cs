using System;

namespace Unlimotion.ViewModel;

public static class DisposableListExtensions
{
    public static void AddToDispose(this IDisposable disposable, DisposableList list)
    {
        list.Disposables.Add(disposable);
    }
}