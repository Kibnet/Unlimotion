using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ReactiveUI;

namespace Unlimotion;

public sealed class AppToastNotificationManager
{
    private static readonly TimeSpan DefaultDismissDelay = TimeSpan.FromSeconds(7);

    private readonly TimeSpan _dismissDelay;

    public AppToastNotificationManager()
        : this(DefaultDismissDelay)
    {
    }

    internal AppToastNotificationManager(TimeSpan dismissDelay)
    {
        _dismissDelay = dismissDelay;
    }

    public ObservableCollection<AppToastNotification> Messages { get; } = [];

    public void ErrorToast(string message)
    {
        Add(message, "#DC483D", "ToastNotificationError", "ToastNotificationErrorCloseButton");
    }

    public void SuccessToast(string message)
    {
        Add(message, "#008800", "ToastNotificationSuccess", "ToastNotificationSuccessCloseButton");
    }

    private void Add(
        string message,
        string background,
        string automationId,
        string closeAutomationId)
    {
        Dispatcher.UIThread.VerifyAccess();

        var notification = new AppToastNotification(
            message,
            Brush.Parse(background),
            automationId,
            closeAutomationId,
            Remove);
        Messages.Add(notification);
        notification.StartAutoDismiss(_dismissDelay);
    }

    private void Remove(AppToastNotification notification)
    {
        Dispatcher.UIThread.VerifyAccess();

        notification.StopAutoDismiss();
        Messages.Remove(notification);
    }
}

public sealed class AppToastNotification
{
    private readonly Action<AppToastNotification> _close;
    private DispatcherTimer? _dismissTimer;

    public AppToastNotification(
        string message,
        IBrush background,
        string automationId,
        string closeAutomationId,
        Action<AppToastNotification> close)
    {
        Message = message;
        Background = background;
        AutomationId = automationId;
        CloseAutomationId = closeAutomationId;
        _close = close;
        CloseCommand = ReactiveCommand.Create(Close);
    }

    public string Message { get; }
    public IBrush Background { get; }
    public string AutomationId { get; }
    public string CloseAutomationId { get; }
    public ICommand CloseCommand { get; }

    internal void StartAutoDismiss(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        _dismissTimer = new DispatcherTimer
        {
            Interval = delay
        };
        _dismissTimer.Tick += (_, _) => Close();
        _dismissTimer.Start();
    }

    internal void StopAutoDismiss()
    {
        _dismissTimer?.Stop();
        _dismissTimer = null;
    }

    private void Close()
    {
        _close(this);
    }
}
