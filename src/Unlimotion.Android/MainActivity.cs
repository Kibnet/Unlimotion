#define Android

using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Avalonia;
using Avalonia.Android;
using ReactiveUI.Avalonia;
using Unlimotion;
using Unlimotion.Android.Services;
using Unlimotion.Services;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Android;

[Activity(
    Label = "Unlimotion.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    ResizeableActivity = true,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    private const string DefaultConfigName = "Settings.json";
    private const string TasksFolderName = "Tasks";
    private const int OpenTaskFolderRequestCode = 4201;
    private const int ManageExternalStorageRequestCode = 4202;
    private string? _dataDir;
    private TaskCompletionSource<string?>? _openTaskFolderCompletion;
    private TaskCompletionSource<bool>? _manageExternalStorageCompletion;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        HookCrashLogging();
        ConfigureAppServices();

        base.OnCreate(savedInstanceState);
    }

    private void ConfigureAppServices()
    {
        try
        {
            var dataDir = ResolveDataDirectory();
            _dataDir = dataDir;
            Directory.CreateDirectory(dataDir);

            BackupViaGitService.GetAbsolutePath = path => Path.Combine(dataDir, path);

            EnsureGitSafeDirectory(dataDir);
            EnsureGitSslCertBundle(dataDir);

            TaskStorageFactory.DefaultStoragePath = Path.Combine(dataDir, TasksFolderName);

            var configPath = Path.Combine(dataDir, DefaultConfigName);
            if (!File.Exists(configPath))
            {
                using var stream = File.CreateText(configPath);
                stream.Write(@"{}");
            }

            App.Init(configPath);
            EnsureGitSafeDirectory(TaskStorageFactory.DefaultStoragePath);

            App.ConfigureUpdateService(new AndroidApplicationUpdateService(this));
            Dialogs.PlatformOpenFolderDialogAsync = ShowOpenDocumentTreeAsync;
            TaskStorageFactory.PrepareFileStoragePathAsync = EnsureFileStoragePathAccessAsync;
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            throw;
        }
    }

    private string ResolveDataDirectory()
    {
        // App-private storage works on modern Android without broad filesystem permissions.
        var externalFilesDir = GetExternalFilesDir(null)?.AbsolutePath;
        if (!string.IsNullOrWhiteSpace(externalFilesDir))
        {
            return externalFilesDir;
        }

        var internalFilesDir = ApplicationContext?.FilesDir;
        if (internalFilesDir != null)
        {
            return internalFilesDir.AbsolutePath;
        }

        throw new InvalidOperationException("Could not resolve app data directory.");
    }

    protected override void OnResume()
    {
        base.OnResume();
        CompleteManageExternalStorageRequestIfPending();
    }

    private async Task<string?> ShowOpenDocumentTreeAsync(string? title, string? directory)
    {
        var path = await OpenDocumentTreeAsync();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await EnsureFileStoragePathAccessAsync(path);
        }

        return path;
    }

    private Task<string?> OpenDocumentTreeAsync()
    {
        var completion = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var previousCompletion = Interlocked.Exchange(ref _openTaskFolderCompletion, completion);
        previousCompletion?.TrySetResult(null);

        try
        {
            var intent = new Intent(Intent.ActionOpenDocumentTree);
            intent.AddFlags(
                ActivityFlags.GrantReadUriPermission |
                ActivityFlags.GrantWriteUriPermission |
                ActivityFlags.GrantPersistableUriPermission |
                ActivityFlags.GrantPrefixUriPermission);

            StartActivityForResult(intent, OpenTaskFolderRequestCode);
        }
        catch (Exception ex)
        {
            Interlocked.CompareExchange(ref _openTaskFolderCompletion, null, completion);
            completion.TrySetException(ex);
        }

        return completion.Task;
    }

    private async Task EnsureFileStoragePathAccessAsync(string? path)
    {
        if (RequiresManageExternalStorage(path) && !HasManageExternalStorageAccess())
        {
            await RequestManageExternalStorageAccessAsync();
            if (!HasManageExternalStorageAccess())
            {
                throw new InvalidOperationException(L10n.Get("AndroidAllFilesAccessRequired"));
            }
        }

        EnsureGitSafeDirectory(path);
    }

    private bool RequiresManageExternalStorage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            return false;
        }

        var normalizedPath = NormalizeAndroidPath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return false;
        }

        var externalFilesDir = GetExternalFilesDir(null)?.AbsolutePath;
        if (IsPathWithinDirectory(normalizedPath, NormalizeAndroidPath(externalFilesDir)))
        {
            return false;
        }

        var internalFilesDir = ApplicationContext?.FilesDir?.AbsolutePath;
        if (IsPathWithinDirectory(normalizedPath, NormalizeAndroidPath(internalFilesDir)))
        {
            return false;
        }

        return normalizedPath.StartsWith("/storage/", StringComparison.Ordinal) ||
               normalizedPath.StartsWith("/sdcard/", StringComparison.Ordinal);
    }

    private static string NormalizeAndroidPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').TrimEnd('/');
    }

    private static bool IsPathWithinDirectory(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        if (string.Equals(path, directory, StringComparison.Ordinal))
        {
            return true;
        }

        return path.StartsWith(directory + "/", StringComparison.Ordinal);
    }

    private static bool HasManageExternalStorageAccess()
    {
        return !OperatingSystem.IsAndroidVersionAtLeast(30) ||
               global::Android.OS.Environment.IsExternalStorageManager;
    }

    private Task RequestManageExternalStorageAccessAsync()
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var previousCompletion = Interlocked.Exchange(ref _manageExternalStorageCompletion, completion);
        previousCompletion?.TrySetResult(HasManageExternalStorageAccess());

        try
        {
            StartActivityForResult(CreateManageExternalStorageSettingsIntent(), ManageExternalStorageRequestCode);
        }
        catch
        {
            try
            {
                StartActivityForResult(new Intent(Settings.ActionManageAllFilesAccessPermission), ManageExternalStorageRequestCode);
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref _manageExternalStorageCompletion, null, completion);
                completion.TrySetException(ex);
            }
        }

        return completion.Task;
    }

    private Intent CreateManageExternalStorageSettingsIntent()
    {
        var intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission);
        intent.SetData(global::Android.Net.Uri.Parse($"package:{PackageName}"));
        return intent;
    }

    private void CompleteManageExternalStorageRequestIfPending()
    {
        var completion = _manageExternalStorageCompletion;
        if (completion == null)
        {
            return;
        }

        new Handler(Looper.MainLooper!).PostDelayed(() =>
        {
            var pendingCompletion = Interlocked.Exchange(ref _manageExternalStorageCompletion, null);
            pendingCompletion?.TrySetResult(HasManageExternalStorageAccess());
        }, 250);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode == ManageExternalStorageRequestCode)
        {
            CompleteManageExternalStorageRequestIfPending();
            return;
        }

        if (requestCode != OpenTaskFolderRequestCode)
        {
            return;
        }

        var completion = Interlocked.Exchange(ref _openTaskFolderCompletion, null);
        if (completion == null)
        {
            return;
        }

        if (resultCode != Result.Ok || data?.Data == null)
        {
            completion.TrySetResult(null);
            return;
        }

        var uri = data.Data;
        TryPersistFolderAccess(data, uri);
        completion.TrySetResult(TryResolveDocumentTreeToPath(uri));
    }

    private void TryPersistFolderAccess(Intent data, global::Android.Net.Uri uri)
    {
        try
        {
            var flags = data.Flags & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
            if (flags != 0)
            {
                ContentResolver?.TakePersistableUriPermission(uri, flags);
            }
        }
        catch
        {
        }
    }

    private static string? TryResolveDocumentTreeToPath(global::Android.Net.Uri uri)
    {
        try
        {
            var documentId = DocumentsContract.GetTreeDocumentId(uri);
            if (string.IsNullOrWhiteSpace(documentId))
            {
                return null;
            }

            var separatorIndex = documentId.IndexOf(':');
            if (separatorIndex < 0)
            {
                return null;
            }

            var volume = documentId[..separatorIndex];
            var relative = documentId[(separatorIndex + 1)..].TrimStart('/');
            var rootPath = volume switch
            {
                var value when string.Equals(value, "primary", StringComparison.OrdinalIgnoreCase)
                    => "/storage/emulated/0",
                var value when string.Equals(value, "home", StringComparison.OrdinalIgnoreCase)
                    => "/storage/emulated/0/Documents",
                _ => $"/storage/{volume}"
            };

            return string.IsNullOrWhiteSpace(relative)
                ? rootPath
                : $"{rootPath}/{relative}";
        }
        catch
        {
            return null;
        }
    }

    internal static void WriteStartupError(Exception ex)
    {
        try
        {
            Log.Error("Unlimotion", ex.ToString());
            if (!TryWriteStartupErrorToDownloads(ex.ToString()))
            {
                var dir = global::Android.App.Application.Context.FilesDir;
                if (dir == null)
                {
                    return;
                }

                var path = Path.Combine(dir.AbsolutePath, "startup-error.txt");
                File.AppendAllText(path, ex + System.Environment.NewLine);
            }
        }
        catch
        {
        }
    }

    private static bool _crashLoggingHooked;

    private static void HookCrashLogging()
    {
        if (_crashLoggingHooked)
        {
            return;
        }

        _crashLoggingHooked = true;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                WriteStartupError(ex);
            }
        };
        AndroidEnvironment.UnhandledExceptionRaiser += (_, args) =>
        {
            WriteStartupError(args.Exception);
        };
    }

    private static bool TryWriteStartupErrorToDownloads(string text)
    {
        try
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                return false;
            }

            return TryWriteStartupErrorToDownloadsQ(text);
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("android29.0")]
    private static bool TryWriteStartupErrorToDownloadsQ(string text)
    {
        try
        {
            var resolver = global::Android.App.Application.Context.ContentResolver;
            if (resolver == null)
            {
                return false;
            }

            var values = new ContentValues();
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            values.Put(MediaStore.IMediaColumns.DisplayName, $"unlimotion-startup-{timestamp}.txt");
            values.Put(MediaStore.IMediaColumns.MimeType, "text/plain");
            values.Put(MediaStore.IMediaColumns.RelativePath, global::Android.OS.Environment.DirectoryDownloads);

            var uri = resolver.Insert(MediaStore.Downloads.ExternalContentUri, values);
            if (uri == null)
            {
                return false;
            }

            using var stream = resolver.OpenOutputStream(uri);
            if (stream == null)
            {
                return false;
            }

            using var writer = new StreamWriter(stream);
            writer.Write(text);
            writer.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void EnsureGitSafeDirectory(string? directoryPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return;
            }

            var dataDir = _dataDir ?? ResolveDataDirectory();
            var configPath = Path.Combine(dataDir, ".gitconfig");
            var safeDirectory = ResolveGitSafeDirectoryPath(dataDir, directoryPath);

            GitSafeDirectoryConfig.EnsureSafeDirectory(configPath, safeDirectory);
            System.Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", configPath);
        }
        catch
        {
        }
    }

    private static string ResolveGitSafeDirectoryPath(string dataDir, string directoryPath)
    {
        var normalizedPath = NormalizeAndroidPath(directoryPath);
        return IsAndroidAbsolutePath(normalizedPath)
            ? normalizedPath
            : NormalizeAndroidPath(Path.Combine(dataDir, normalizedPath));
    }

    private static bool IsAndroidAbsolutePath(string path)
    {
        return path.StartsWith("/", StringComparison.Ordinal) ||
               Path.IsPathFullyQualified(path);
    }

    private void EnsureGitSslCertBundle(string dataDir)
    {
        try
        {
            var certPath = Path.Combine(dataDir, "cacert.pem");
            if (!File.Exists(certPath))
            {
                using var input = Assets?.Open("cacert.pem");
                if (input == null)
                {
                    return;
                }

                using var output = File.Create(certPath);
                input.CopyTo(output);
            }

            LibGit2Interop.SetSslCertificateLocations(certPath, null);
        }
        catch
        {
        }
    }
}

[global::Android.App.Application]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithCustomFont()
            .UseReactiveUI(App.ConfigureReactiveUIBuilder);
    }
}
