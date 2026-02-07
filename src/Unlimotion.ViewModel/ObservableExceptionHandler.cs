using System;
using System.Diagnostics;
using System.IO;

namespace Unlimotion.ViewModel;

public class ObservableExceptionHandler : IObserver<Exception>
{
    private readonly INotificationManagerWrapper? _notifyManager;
    
    public ObservableExceptionHandler(INotificationManagerWrapper? notifyManager)
    {
        _notifyManager = notifyManager;
    }
    
    // При перехвате Exception попадает сюда
    public void OnNext(Exception value)
    {
        if (Debugger.IsAttached) Debugger.Break();
        
        var stackFrame = new StackTrace(value, true).GetFrame(0);
       
        _notifyManager?.ErrorToast($"Exception: {value.Message} " +
                                   $"File: {(stackFrame != null ? Path.GetFileName(stackFrame.GetFileName()) : "Unknown")}. " +
                                   $"RowNumber: {(stackFrame != null ? stackFrame.GetFileLineNumber() : 0)}");
        
        // RxApp.MainThreadScheduler.Schedule(() => { throw value; }) ;
    }

    public void OnError(Exception error)
    {
        if (Debugger.IsAttached) Debugger.Break();

        // RxApp.MainThreadScheduler.Schedule(() => { throw error; });
    }

    public void OnCompleted()
    {
        if (Debugger.IsAttached) Debugger.Break();
        
        // RxApp.MainThreadScheduler.Schedule(() => { throw new NotImplementedException(); });
    }
}