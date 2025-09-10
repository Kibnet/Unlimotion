using System;
using Unlimotion.ViewModel;

namespace Unlimotion.Test
{
    public class NotificationManagerWrapperMock : INotificationManagerWrapper
    {
        public bool AskResult { get; set; }
        public void Ask(string header, string message, Action yesAction, Action noAction = null)
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
           
        }

        public void SuccessToast(string message)
        {
            
        }
    }
}
