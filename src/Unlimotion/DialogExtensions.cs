using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Splat;
using System;
using System.Threading.Tasks;
using Unlimotion.ViewModel;

namespace Unlimotion;

public class Dialogs : IDialogs
{
    public static async Task<IStorageFolder?> PickFolderAsync(string title = null, string directory = null)
    {
        var top = GetTopLevel();
        if (top is null || top.StorageProvider is null)
            return null;

        // На Android под капотом вызовется ACTION_OPEN_DOCUMENT_TREE (SAF)
        var folders = await top.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });
        if (AfterPick != null)
        {
            AfterPick.Invoke(folders[0]?.Path?.ToString());
        }
        return folders.Count > 0 ? folders[0] : null;
    }

    public static Action<string> AfterPick { get; set; }
    

    public async Task<string> ShowOpenFolderDialogAsync(string title = null, string directory = null)
    {
        var result = await PickFolderAsync(title, directory);

        return result.TryGetLocalPath() ?? result.Path.ToString();
    }

    public static Visual? GetVisual()
    {
        var lifetime = App.Current?.ApplicationLifetime;
        Control window = null;
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
                window = singleViewApplicationLifetime.MainView;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        return window?.GetVisualParent();
    }

    public static TopLevel? GetTopLevel() => TopLevel.GetTopLevel(GetVisual());
    public static IStorageProvider? GetStorageProvider() => GetTopLevel()?.StorageProvider;
}