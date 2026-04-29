using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Notification;
using Avalonia.Threading;
using DialogHostAvalonia;
using ReactiveUI;
using Unlimotion.ViewModel;

namespace Unlimotion;

public class NotificationManagerWrapper : INotificationManagerWrapper
{
    private INotificationMessageManager? _notificationManager;

    public NotificationManagerWrapper(INotificationMessageManager? notificationManager)
    {
        _notificationManager = notificationManager;
    }

    public void SetManager(INotificationMessageManager? notificationManager)
    {
        _notificationManager = notificationManager;
    }

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

    public Task<bool> ConfirmTaskOutlinePasteAsync(TaskOutlinePastePreview preview)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var dialogViewModel = new TaskOutlinePastePreviewDialogViewModel(preview)
                {
                    ConfirmAction = () => completion.TrySetResult(true),
                    CancelAction = () => completion.TrySetResult(false),
                };

                var dialogTask = DialogHost.Show(dialogViewModel, "Ask");
                dialogViewModel.CloseAction = () => DialogHost.GetDialogSession("Ask")?.Close(false);
                _ = dialogTask.ContinueWith(
                    _ => completion.TrySetResult(false),
                    TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return completion.Task;
    }

    public void ErrorToast(string message)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _notificationManager?.CreateMessage()
            .Background("#DC483D")
            .HasMessage(message)
            .Dismiss().WithDelay(TimeSpan.FromSeconds(7))
            .WithCloseButton()
            .Queue();
        });
    }

    public void SuccessToast(string message)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _notificationManager?.CreateMessage()
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

public class TaskOutlinePastePreviewDialogViewModel
{
    public TaskOutlinePastePreviewDialogViewModel(TaskOutlinePastePreview preview)
    {
        Header = preview.Header;
        DestinationLabel = preview.DestinationLabel;
        TaskCountText = preview.TaskCountText;
        PreviewText = preview.PreviewText;
    }

    public string Header { get; }

    public string DestinationLabel { get; }

    public string TaskCountText { get; }

    public string PreviewText { get; }

    public Action ConfirmAction { get; set; }

    public Action CancelAction { get; set; }

    public Action CloseAction { get; set; }

    public ICommand ConfirmCommand => ReactiveCommand.Create(() =>
    {
        ConfirmAction?.Invoke();
        CloseAction?.Invoke();
    });

    public ICommand CancelCommand => ReactiveCommand.Create(() =>
    {
        CancelAction?.Invoke();
        CloseAction?.Invoke();
    });
}
