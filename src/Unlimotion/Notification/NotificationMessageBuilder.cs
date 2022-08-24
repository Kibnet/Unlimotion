﻿using System;
using System.Threading.Tasks;
using Avalonia.Media;
    
namespace Unlimotion.Notification;

/// <summary>
/// The notification message builder.
/// </summary>
public class NotificationMessageBuilder
{
    /// <summary>
    /// Gets or sets the message.
    /// </summary>
    /// <value>
    /// The message.
    /// </value>
    public INotificationMessage Message { get; set; }

    /// <summary>
    /// Gets or sets the manager.
    /// </summary>
    /// <value>
    /// The manager.
    /// </value>
    public INotificationMessageManager Manager { get; set; }


    /// <summary>
    /// Creates the message.
    /// </summary>
    /// <returns>Returns new instance of notification message builder that is used to create notification message.</returns>
    public static NotificationMessageBuilder CreateMessage()
    {
        return new NotificationMessageBuilder();
    }

    /// <summary>
    /// Sets the header.
    /// </summary>
    /// <param name="header">The header.</param>
    public void SetHeader(string header)
    {
        this.Message.Header = header;
    }

    /// <summary>
    /// Sets the message.
    /// </summary>
    /// <param name="message">The message.</param>
    public void SetMessage(string message)
    {
        this.Message.Message = message;
    }

    /// <summary>
    /// Adds the button.
    /// </summary>
    /// <param name="button">The button.</param>
    public void AddButton(INotificationMessageButton button)
    {
        if (button == null)
            throw new ArgumentNullException(nameof(button));

        this.Message.Buttons.Add(button);
    }

    /// <summary>
    /// Sets the badge.
    /// </summary>
    /// <param name="badgeText">The badge text.</param>
    public void SetBadge(string badgeText)
    {
        this.Message.BadgeText = badgeText;
    }

    /// <summary>
    /// Sets the accent.
    /// </summary>
    /// <param name="accentBrush">The accent brush.</param>
    public void SetAccent(Brush accentBrush)
    {
        this.Message.AccentBrush = accentBrush;
    }

    /*/// <summary>
    /// Sets the background.
    /// </summary>
    /// <param name="backgroundBrush">The background brush.</param>
    public void SetBackground(Brush backgroundBrush)
    {
        this.Message.Background = backgroundBrush;
    }*/

    /// <summary>
    /// Sets the overlay.
    /// </summary>
    /// <param name="overlay">The overlay.</param>
    public void SetOverlay(object overlay)
    {
        this.Message.OverlayContent = overlay;
    }

    /// <summary>
    /// Sets the top additional content.
    /// </summary>
    /// <param name="additionalContentTop">The additional content.</param>
    public void SetAdditionalContentTop(object additionalContentTop)
    {
        this.Message.AdditionalContentTop = additionalContentTop;
    }

    /// <summary>
    /// Sets the bottom additional content.
    /// </summary>
    /// <param name="additionalContentBottom">The additional content.</param>
    public void SetAdditionalContentBottom(object additionalContentBottom)
    {
        this.Message.AdditionalContentBottom = additionalContentBottom;
    }

    /// <summary>
    /// Sets the left additional content.
    /// </summary>
    /// <param name="additionalContentLeft">The additional content.</param>
    public void SetAdditionalContentLeft(object additionalContentLeft)
    {
        this.Message.AdditionalContentLeft = additionalContentLeft;
    }

    /// <summary>
    /// Sets the right additional content.
    /// </summary>
    /// <param name="additionalContentRight">The additional content.</param>
    public void SetAdditionalContentRight(object additionalContentRight)
    {
        this.Message.AdditionalContentRight = additionalContentRight;
    }

    /// <summary>
    /// Sets the center additional content.
    /// </summary>
    /// <param name="additionalContentMain">The additional content.</param>
    public void SetAdditionalContentMain(object additionalContentMain)
    {
        this.Message.AdditionalContentMain = additionalContentMain;
    }

    /// <summary>
    /// Sets the additional content over the badge.
    /// </summary>
    /// <param name="additionalContentOverBadge">The additional content.</param>
    public void SetAdditionalContentOverBadge(object additionalContentOverBadge)
    {
        this.Message.AdditionalContentOverBadge = additionalContentOverBadge;
    }

    /*/// <summary>
    /// Sets the text brush.
    /// </summary>
    public void SetForeground(Brush brush)
    {
        this.Message.Foreground = brush;
    }*/

    /// <summary>
    /// Sets whether or not the message animates.
    /// </summary>
    /// <param name="animates"></param>
    public void SetAnimates(bool animates)
    {
        if (this.Message is INotificationAnimation animation)
        {
            animation.Animates = animates;
        }
    }


    /// <summary>
    /// Sets the text brush.
    /// </summary>
    public void SetForeground(Brush brush)
    {
        this.Message.Foreground = brush;
    }

    /// <summary>
    /// Sets the background.
    /// </summary>
    /// <param name="backgroundBrush">The background brush.</param>
    public void SetBackground(Brush backgroundBrush)
    {
        this.Message.Background = backgroundBrush;
    }

    /// <summary>
    /// Queues the message to manager.
    /// </summary>
    /// <returns>Returns the queued message.</returns>
    public INotificationMessage Queue()
    {
        this.Manager.Queue(this.Message);
    

        return this.Message;
    }

    /// <summary>
    /// Executes the action after specified delay time.
    /// </summary>
    /// <param name="delayMilliseconds">The delay in milliseconds.</param>
    /// <param name="action">The action.</param>
    public void Delay(int delayMilliseconds, Action<INotificationMessage> action)
    {
        this.Delay(TimeSpan.FromMilliseconds(delayMilliseconds), action);
    }

    /// <summary>
    /// Executes the action after specified delay time.
    /// </summary>
    /// <param name="delay">The delay.</param>
    /// <param name="action">The action.</param>
    public void Delay(TimeSpan delay, Action<INotificationMessage> action)
    {
        Task.Delay(delay).ContinueWith(
            context => action(this.Message),
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>
    /// The notification message button that is required to dismiss the notification.
    /// </summary>
    public class DismissNotificationMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DismissNotificationMessage"/> class.
        /// </summary>
        /// <param name="builder">The builder.</param>
        /// <exception cref="ArgumentNullException">builder</exception>
        public DismissNotificationMessage(NotificationMessageBuilder builder)
        {
            this.Builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        /// <summary>
        /// Gets or sets the builder.
        /// </summary>
        /// <value>
        /// The builder.
        /// </value>
        public NotificationMessageBuilder Builder { get; set; }
    }
}