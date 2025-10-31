using System;
using System.Diagnostics;
using System.IO;
using Splat;
//using ILogger = Serilog.ILogger;

namespace Unlimotion.ViewModel;

public class ObservableExceptionHandler : IObserver<Exception>
{
    private ILogger _logger;
    private INotificationManagerWrapper _notifyManager;
    
    public ObservableExceptionHandler()
    {
        _logger = Locator.Current.GetService<ILogger>();
        _notifyManager = Locator.Current.GetService<INotificationManagerWrapper>();
    }
    
    // При перехвате Exception попадает сюда
    public void OnNext(Exception value)
    {
        if (Debugger.IsAttached) Debugger.Break();
        
        var stackFrame = new StackTrace(value, true).GetFrame(0);
     
        //_logger.Error($"ObservableExceptionHandler: Type: {value.GetType()}, Message: {value.Message}, StackTrace: {value.StackTrace}");
       
        _notifyManager.ErrorToast($"Exception: {value.Message} " +
                                   $"File: {(stackFrame != null ? Path.GetFileName(stackFrame.GetFileName()) : "Unknown")}. " +
                                   $"RowNumber: {(stackFrame != null ? stackFrame.GetFileLineNumber() : 0)}");
        
        // RxApp.MainThreadScheduler.Schedule(() => { throw value; }) ;
    }

    public void OnError(Exception error)
    {
        if (Debugger.IsAttached) Debugger.Break();

        //_logger.Error($"ObservableExceptionHandler: Type: {error.GetType()}, Message: {error.Message}, StackTrace: {error.StackTrace}");

        // RxApp.MainThreadScheduler.Schedule(() => { throw error; });
    }

    public void OnCompleted()
    {
        if (Debugger.IsAttached) Debugger.Break();
        
        // RxApp.MainThreadScheduler.Schedule(() => { throw new NotImplementedException(); });
    }
}