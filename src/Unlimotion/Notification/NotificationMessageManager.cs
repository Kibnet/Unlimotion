using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Unlimotion.Notification;

/// <summary>
/// The notification message manager.
/// </summary>
/// <seealso cref="INotificationMessageManager" />
public class NotificationMessageManager : INotificationMessageManager
{
    private readonly List<INotificationMessage> queuedMessages = new ();


    /// <summary>
    /// Occurs when new notification message is queued.
    /// </summary>
    public event NotificationMessageManagerEventHandler OnMessageQueued;

    /// <summary>
    /// Occurs when notification message is dismissed.
    /// </summary>
    public event NotificationMessageManagerEventHandler OnMessageDismissed;

    /// <summary>
    /// Gets or sets the factory.
    /// </summary>
    /// <value>
    /// The factory.
    /// </value>
    public INotificationMessageFactory Factory { get; set; } = new NotificationMessageFactory();


    /// <summary>
    /// Queues the specified message.
    /// This will ignore the <c>null</c> message or already queued notification message.
    /// </summary>
    /// <param name="message">The message.</param>
    public void Queue(INotificationMessage message)
    {
        if (message == null || queuedMessages.Contains(message))
            return;

        // TODO убрать после добавления возможности перекрытий сообщений друг другом
        if (queuedMessages.Count == 0)
        {
            queuedMessages.Add(message);
            TriggerMessageQueued(message);
        }
        
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow.FindControl<UserControl>("MainControl").IsEnabled = false;
            desktop.MainWindow.FindControl<ItemsControl>("Notification").IsEnabled = true;
        }
    }

    /// <summary>
    /// Triggers the message queued event.
    /// </summary>
    /// <param name="message">The message.</param>
    private void TriggerMessageQueued(INotificationMessage message)
    {
        OnMessageQueued?.Invoke(this, new NotificationMessageManagerEventArgs(message));
    }

    /// <summary>
    /// Dismisses the specified message.
    /// This will ignore the <c>null</c> or not queued notification message.
    /// </summary>
    /// <param name="message">The message.</param>
    public void Dismiss(INotificationMessage message)
    {
        if (message == null || !queuedMessages.Contains(message))
            return;

        this.queuedMessages.Remove(message);

        if (message is INotificationAnimation animatableMessage)
        {
            // var animation = animatableMessage.AnimationOut;
            if (
                animatableMessage.Animates &&
                animatableMessage.AnimatableElement != null)
            {
                animatableMessage.AnimatableElement.DismissAnimation = true;
                Task.Delay(500).ContinueWith(
                    _ => { TriggerMessageDismissed(message); },
                    TaskScheduler.FromCurrentSynchronizationContext());
            }
            else
            {
                this.TriggerMessageDismissed(message);
            }
        }
        else
        {
            this.TriggerMessageDismissed(message);
        }
    }

    /// <summary>
    /// Triggers the message dismissed event.
    /// </summary>
    /// <param name="message">The message.</param>
    private void TriggerMessageDismissed(INotificationMessage message)
    {
        OnMessageDismissed?.Invoke(this, new NotificationMessageManagerEventArgs(message));
    }
}