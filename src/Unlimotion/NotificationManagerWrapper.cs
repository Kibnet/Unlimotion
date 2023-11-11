using DialogHostAvalonia;
using System;
using System.Windows.Input;
using ReactiveUI;

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

        var id = DialogHost.Show(askViewModel);
        askViewModel.CloseAction = () => DialogHost.GetDialogSession("Ask")?.Close(false);
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