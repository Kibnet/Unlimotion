using DialogHostAvalonia;
using System;
using System.Windows.Input;
using Avalonia.Notification;
using Avalonia.Threading;
using ReactiveUI;
using Splat;

namespace Unlimotion;

public class NotificationManagerWrapper : ViewModel.INotificationManagerWrapper
{
    public void Ask(string header, string message, Action yesAction, Action noAction = null)
    {
        var askViewModel = new AskViewModel
        {
            Header = header,
            Message = message,
            YesAction = yesAction,
            NoAction = noAction,
        };

        DialogHost.Show(askViewModel, "Ask");
        askViewModel.CloseAction = () => DialogHost.GetDialogSession("Ask")?.Close(false);
    }


    public void ErrorToast(string message)
    {
        var notify = Locator.Current.GetService<INotificationMessageManager>();
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            notify?.CreateMessage()
            .Background("#DC483D")
            .HasMessage(message)
            .Dismiss().WithDelay(TimeSpan.FromSeconds(7))
            .WithCloseButton()
            .Queue();
        });
    }

    public void SuccessToast(string message)
    {
        var notify = Locator.Current.GetService<INotificationMessageManager>();
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            notify?.CreateMessage()
                .Background("#008800")
                .HasMessage(message)
                .Dismiss().WithDelay(TimeSpan.FromSeconds(7))
                .WithCloseButton()
                .Queue();
        });
    }
}

public class AskViewModel
{
    public string Header { get; set; }
    public string Message { get; set; }
    public Action YesAction { get; set; }
    public Action NoAction { get; set; }
    public ICommand YesCommand => ReactiveCommand.Create(() =>
    {
        YesAction?.Invoke();
        CloseAction?.Invoke();
    });
    public ICommand NoCommand => ReactiveCommand.Create(() =>
    {
        NoAction?.Invoke();
        CloseAction?.Invoke();
    });
    public Action CloseAction { get; set; }
}