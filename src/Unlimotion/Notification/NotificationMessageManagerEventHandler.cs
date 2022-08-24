﻿namespace Unlimotion.Notification;

/// <summary>
/// The notification message manager event handler.
/// </summary>
/// <param name="sender">The sender.</param>
/// <param name="args">The <see cref="NotificationMessageManagerEventArgs"/> instance containing the event data.</param>
public delegate void NotificationMessageManagerEventHandler(
    object sender,
    NotificationMessageManagerEventArgs args);