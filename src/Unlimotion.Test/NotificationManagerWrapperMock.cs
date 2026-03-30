using System;
using System.Collections.Generic;
using Unlimotion.ViewModel;

namespace Unlimotion.Test
{
    public class NotificationManagerWrapperMock : INotificationManagerWrapper
    {
        public bool AskResult { get; set; }
        public string? LastErrorMessage { get; private set; }
        public string? LastSuccessMessage { get; private set; }
        public List<string> ErrorMessages { get; } = new();
        public List<string> SuccessMessages { get; } = new();

        public void Ask(string header, string message, Action yesAction, Action? noAction = null)
        {
            if (AskResult)
            {
                yesAction.Invoke();
            }
            else
            {
                noAction?.Invoke();
            }
        }

        public void ErrorToast(string message)
        {
            LastErrorMessage = message;
            ErrorMessages.Add(message);
        }

        public void SuccessToast(string message)
        {
            LastSuccessMessage = message;
            SuccessMessages.Add(message);
        }

        public void ClearMessages()
        {
            LastErrorMessage = null;
            LastSuccessMessage = null;
            ErrorMessages.Clear();
            SuccessMessages.Clear();
        }
    }
}
