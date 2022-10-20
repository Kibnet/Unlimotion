using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using Unlimotion.ViewModel;

namespace Unlimotion;

public class Dialogs : IDialogs
{
    public Task<string> ShowOpenFolderDialogAsync(string title = null, string directory = null)
    {
        var dialog = new OpenFolderDialog();
        if (title != null)
        {
            dialog.Title = title;
        }

        if (directory != null)
        {
            dialog.Directory = directory;
        }
        
        return dialog.ShowAsync();
    }
}

public static class DialogExtensions
{
    public static Task<string?>? ShowAsync(this OpenFolderDialog? dlg)
    {
        var lifetime = App.Current.ApplicationLifetime;

        Window window = null;
        switch (lifetime)
        {
            case null:
                break;
            case IClassicDesktopStyleApplicationLifetime classicDesktopStyleApplicationLifetime:
                window = classicDesktopStyleApplicationLifetime.MainWindow;
                break;
            case IControlledApplicationLifetime controlledApplicationLifetime:
                //TODO Непонятно, как тут найти окно
                break;
            case ISingleViewApplicationLifetime singleViewApplicationLifetime:
                window = singleViewApplicationLifetime.MainView.GetVisualRoot() as Window;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        if (window != null)
        {
            return dlg.ShowAsync(window);
        }

        return null;
    }
}