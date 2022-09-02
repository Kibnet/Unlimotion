using System;
using Unlimotion.Notification;

namespace Unlimotion;

public class NotificationManagerWrapperWrapper : ViewModel.INotificationManagerWrapper
{
    public NotificationMessageManager Manager { get; } = new ();

    public void Ask(string header, string message, Action yesAction, Action noAction = null)
    {
        Manager.CreateMessage()
            .Accent("#0078D7")
            .Animates(true)
            .Background("#444")
            .HasBadge("Question")
            .HasHeader(header)
            .HasMessage(message)
            .Dismiss().WithButton("Yes", _ => { yesAction?.Invoke();})
            .Dismiss().WithButton("No", _ => { noAction?.Invoke(); })
            .Queue();
    }
}