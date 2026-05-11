using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Unlimotion.ViewModel;

namespace Unlimotion;

public class Dialogs : IDialogs
{
    public static Func<string?, string?, Task<string?>>? PlatformOpenFolderDialogAsync { get; set; }

    public async Task<string> ShowOpenFolderDialogAsync(string? title = null, string? directory = null)
    {
        var platformOpenFolderDialogAsync = PlatformOpenFolderDialogAsync;
        if (platformOpenFolderDialogAsync != null)
        {
            return await platformOpenFolderDialogAsync(title, directory) ?? string.Empty;
        }

        var topLevel = DialogExtensions.GetTopLevel();
        var storageProvider = topLevel?.StorageProvider;
        if (storageProvider != null && storageProvider.CanPickFolder)
        {
            var options = await CreateFolderPickerOpenOptionsAsync(storageProvider, title, directory);

            var result = await storageProvider.OpenFolderPickerAsync(options);
            var folder = result?.FirstOrDefault();
            if (folder != null)
            {
                var localPath = DialogExtensions.TryGetLocalPath(folder);
                if (!string.IsNullOrWhiteSpace(localPath))
                {
                    return localPath;
                }
            }
        }

        return string.Empty;
    }

    internal static async Task<FolderPickerOpenOptions> CreateFolderPickerOpenOptionsAsync(
        IStorageProvider storageProvider,
        string? title,
        string? directory)
    {
        return new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await TryResolveSuggestedStartLocationAsync(storageProvider, directory)
        };
    }

    private static async Task<IStorageFolder?> TryResolveSuggestedStartLocationAsync(
        IStorageProvider storageProvider,
        string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var directoryUri = TryCreateStorageUri(directory);
        if (directoryUri == null)
        {
            return null;
        }

        try
        {
            return await storageProvider.TryGetFolderFromPathAsync(directoryUri);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Uri? TryCreateStorageUri(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            return uri;
        }

        try
        {
            return new Uri(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or PathTooLongException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

public static class DialogExtensions
{
    public static TopLevel? GetTopLevel()
    {
        var lifetime = App.Current?.ApplicationLifetime;
        switch (lifetime)
        {
            case IClassicDesktopStyleApplicationLifetime classic:
                return classic.MainWindow;
            case ISingleViewApplicationLifetime singleView:
                return TopLevel.GetTopLevel(singleView.MainView);
            default:
                return null;
        }
    }

    public static string? TryGetLocalPath(IStorageFolder folder)
    {
        var localPath = folder.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            return localPath;
        }

        var pathUri = folder.Path;
        if (pathUri != null)
        {
            if (pathUri.IsFile)
            {
                return pathUri.LocalPath;
            }
#if ANDROID
            var androidPath = TryResolveAndroidTreeUri(pathUri);
            if (!string.IsNullOrWhiteSpace(androidPath))
            {
                return androidPath;
            }
#endif
        }

        return null;
    }

#if ANDROID
    private static string? TryResolveAndroidTreeUri(Uri uri)
    {
        try
        {
            if (!string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var androidUri = global::Android.Net.Uri.Parse(uri.ToString());
            var docId = global::Android.Provider.DocumentsContract.GetTreeDocumentId(androidUri);
            if (string.IsNullOrWhiteSpace(docId))
            {
                return null;
            }

            const string primaryPrefix = "primary:";
            if (!docId.StartsWith(primaryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var relative = docId.Substring(primaryPrefix.Length).TrimStart('/');
            var basePath = "/storage/emulated/0";
            return string.IsNullOrEmpty(relative) ? basePath : $"{basePath}/{relative}";
        }
        catch
        {
            return null;
        }
    }
#endif
}
