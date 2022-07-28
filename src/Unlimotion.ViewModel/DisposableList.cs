using System;
using System.Collections.Generic;

namespace Unlimotion.ViewModel;

public abstract class DisposableList : IDisposable
{
    public List<IDisposable> Disposables { get; } = new();

    public void Dispose()
    {
        foreach (var disposable in Disposables)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception e)
            {
                
            }
        }
    }
}

public class DisposableListRealization : DisposableList
{

}