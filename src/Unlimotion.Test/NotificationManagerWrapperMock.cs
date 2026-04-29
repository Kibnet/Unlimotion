using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unlimotion.ViewModel;

namespace Unlimotion.Test
{
    public class NotificationManagerWrapperMock : INotificationManagerWrapper
    {
        public bool AskResult { get; set; }
        public string? LastErrorMessage { get; private set; }
        public string? LastSuccessMessage { get; private set; }
        public string? LastAskHeader { get; private set; }
        public string? LastAskMessage { get; private set; }
        public TaskOutlinePastePreview? LastTaskOutlinePastePreview { get; private set; }
        public int AskCount { get; private set; }
        public int TaskOutlinePasteConfirmationCount { get; private set; }
        public Func<TaskOutlinePastePreview, Task<bool>>? ConfirmTaskOutlinePasteHandler { get; set; }
        public List<string> ErrorMessages { get; } = new();
        public List<string> SuccessMessages { get; } = new();

        public void Ask(string header, string message, Action yesAction, Action? noAction = null)
        {
            AskCount++;
            LastAskHeader = header;
            LastAskMessage = message;

            if (AskResult)
            {
                yesAction.Invoke();
            }
            else
            {
                noAction?.Invoke();
            }
        }

        public Task<bool> ConfirmTaskOutlinePasteAsync(TaskOutlinePastePreview preview)
        {
            TaskOutlinePasteConfirmationCount++;
            LastTaskOutlinePastePreview = preview;
            return ConfirmTaskOutlinePasteHandler?.Invoke(preview) ?? Task.FromResult(AskResult);
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
            LastAskHeader = null;
            LastAskMessage = null;
            LastTaskOutlinePastePreview = null;
            AskCount = 0;
            TaskOutlinePasteConfirmationCount = 0;
            ConfirmTaskOutlinePasteHandler = null;
            ErrorMessages.Clear();
            SuccessMessages.Clear();
        }
    }
}
