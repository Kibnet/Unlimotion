using System;
//using Avalonia.Notification;

namespace Unlimotion;

public class NotificationManagerWrapperWrapper : Unlimotion.ViewModel.INotificationManagerWrapper
{
    //public NotificationMessageManager Manager { get; } = new NotificationMessageManager();

    public void Ask(string header, string message, Action yesAction, Action noAction = null)
    {
        //Manager.CreateMessage()
        //    .Accent("#0078D7")
        //    .Animates(true)
        //    .Background("#444")
        //    .HasBadge("Question")
        //    .HasHeader(header)
        //    .HasMessage(message)
        //    .Dismiss().WithButton("Yes", button => { yesAction?.Invoke();})
        //    .Dismiss().WithButton("No", button => { noAction?.Invoke(); })
        //    .Queue();
        yesAction?.Invoke();
    }
}