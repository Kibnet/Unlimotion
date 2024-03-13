using System;

namespace Unlimotion.ViewModel;

public interface INotificationManagerWrapper
{
    void Ask(string header, string message, Action yesAction, Action noAction = null);

    void ErrorToast(string message);

    void SuccessToast(string message);
}