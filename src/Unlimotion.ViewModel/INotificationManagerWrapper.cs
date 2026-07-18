using System;
using System.Threading.Tasks;

namespace Unlimotion.ViewModel;

public interface INotificationManagerWrapper
{
    void Ask(string header, string message, Action yesAction, Action? noAction = null);

    Task<bool> ConfirmAsync(string header, string message) => Task.FromResult(false);

    Task<bool> ConfirmTaskOutlinePasteAsync(TaskOutlinePastePreview preview);

    void ErrorToast(string message);

    void SuccessToast(string message);
}
