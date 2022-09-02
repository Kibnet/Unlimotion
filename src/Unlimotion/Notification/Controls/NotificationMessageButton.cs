﻿using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Unlimotion.Notification.Controls;

/// <summary>
/// The notification message button.
/// </summary>
/// <seealso cref="Button" />
/// <seealso cref="INotificationMessageButton" />
public class NotificationMessageButton : Button, INotificationMessageButton
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationMessageButton"/> class.
    /// </summary>
    public NotificationMessageButton()
        : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationMessageButton"/> class.
    /// </summary>
    /// <param name="content">The content.</param>
    public NotificationMessageButton(object content)
    {
        this.Content = content;
    }

    /// <summary>
    /// Called when a <see cref="T:System.Windows.Controls.Button" /> is clicked.
    /// </summary>
    protected override void OnClick()
    {
        base.OnClick();
        this.Callback?.Invoke(this);
        
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow.FindControl<UserControl>("MainControl").IsEnabled = true;
    }

    /// <summary>
    /// Initializes the <see cref="NotificationMessageButton"/> class.
    /// </summary>
    static NotificationMessageButton()
    {
        // TODO 
        //DefaultStyleKeyProperty.OverrideMetadata(typeof(NotificationMessageButton), new FrameworkPropertyMetadata(typeof(NotificationMessageButton)));
    }


    /// <summary>
    /// Gets or sets the callback.
    /// </summary>
    /// <value>
    /// The callback.
    /// </value>
    public Action<INotificationMessageButton> Callback { get; set; }
}