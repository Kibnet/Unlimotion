using System;
using Avalonia.Media;
using Unlimotion.Views;

namespace Unlimotion;

public class MessageBoxManagerWrapperWrapper : ViewModel.INotificationManagerWrapper
{
    public void Ask(string header, string message, Action yesAction, Action noAction = null)
    {
        var taskConfirmDeletion = new MessageBoxBuilder();
        
        var window = taskConfirmDeletion
            .SetBackgroundBrush(Brushes.Blue)
            .SetHeader(header)
            .SetMessage(message)
            .SetYesAction(yesAction)
            .SetNoAction(noAction)
            .Build();
        
        window.Show(); 
    } 
}