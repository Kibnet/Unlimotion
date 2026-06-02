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
using Android.Window;
using Avalonia;
using Avalonia.Android;
using LibGit2Sharp;
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
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode,
    EnableOnBackInvokedCallback = true)]
public class MainActivity : AvaloniaMainActivity
{
    private const string DefaultConfigName = "Settings.json";
    private const string TasksFolderName = "Tasks";
    private const int OpenTaskFolderRequestCode = 4201;
    private const int ManageExternalStorageRequestCode = 4202;
    private static readonly object AppServicesGate = new();
    private static bool _coreAppServicesConfigured;
    private static string? _configuredDataDir;
    private string? _dataDir;
    private TaskCompletionSource<string?>? _openTaskFolderCompletion;
    private TaskCompletionSource<bool>? _manageExternalStorageCompletion;
    private IOnBackInvokedCallback? _backInvokedCallback;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        HookCrashLogging();
        ConfigureAppServices();

        base.OnCreate(savedInstanceState);
        RegisterBackInvokedCallback();
    }

    protected override void OnDestroy()
    {
        UnregisterBackInvokedCallback();
        base.OnDestroy();
    }

    public override void OnBackPressed()
    {
        HandleSystemBack();
    }

    private void HandleSystemBack()
    {
        if (App.TryHandleTaskCardBackGesture())
        {
            return;
        }

#pragma warning disable CA1422
        base.OnBackPressed();
#pragma warning restore CA1422
    }

    private void RegisterBackInvokedCallback()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33) || _backInvokedCallback != null)
        {
            return;
        }

        var callback = new BackInvokedCallback(HandleSystemBack);
        try
        {
            OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(
                IOnBackInvokedDispatcher.PriorityDefault,
                callback);
            _backInvokedCallback = callback;
        }
        catch
        {
            callback.Dispose();
            _backInvokedCallback = null;
        }
    }

    private void UnregisterBackInvokedCallback()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33) || _backInvokedCallback == null)
        {
            return;
        }

        try
        {
            OnBackInvokedDispatcher.UnregisterOnBackInvokedCallback(_backInvokedCallback);
        }
        catch
        {
        }
        finally
        {
            if (_backInvokedCallback is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _backInvokedCallback = null;
        }
    }

    private sealed class BackInvokedCallback : Java.Lang.Object, IOnBackInvokedCallback
    {
        private readonly Action _onBackInvoked;

        public BackInvokedCallback(Action onBackInvoked)
        {
            _onBackInvoked = onBackInvoked;
        }

        public void OnBackInvoked()
        {
            _onBackInvoked();
        }
    }

    private void ConfigureAppServices()
    {
        try
        {
            var dataDir = ConfigureCoreAppServices(this);
            _dataDir = dataDir;

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

    internal static string ConfigureCoreAppServices(Context context)
    {
        lock (AppServicesGate)
        {
            if (_coreAppServicesConfigured && !string.IsNullOrWhiteSpace(_configuredDataDir))
            {
                return _configuredDataDir;
            }

            var dataDir = ResolveDataDirectory(context);
            Directory.CreateDirectory(dataDir);

            var tasksPath = Path.Combine(dataDir, TasksFolderName);
            App.DefaultStoragePath = tasksPath;
            TaskStorageFactory.DefaultStoragePath = tasksPath;
            BackupViaGitService.GetAbsolutePath = path => Path.Combine(dataDir, path);

            EnsureGitSafeDirectory(dataDir, dataDir);
            EnsureGitSslCertBundle(context, dataDir);

            var configPath = Path.Combine(dataDir, DefaultConfigName);
            if (!File.Exists(configPath))
            {
                using var stream = File.CreateText(configPath);
                stream.Write(@"{}");
            }

            App.Init(configPath);

            BackupViaGitService.GetAbsolutePath = path => Path.Combine(dataDir, path);
            EnsureGitSafeDirectory(dataDir, TaskStorageFactory.DefaultStoragePath);

            _configuredDataDir = dataDir;
            _coreAppServicesConfigured = true;
            return dataDir;
        }
    }

    private string ResolveDataDirectory() => ResolveDataDirectory(this);

    private static string ResolveDataDirectory(Context context)
    {
        // App-private storage works on modern Android without broad filesystem permissions.
        var externalFilesDir = context.GetExternalFilesDir(null)?.AbsolutePath;
        if (!string.IsNullOrWhiteSpace(externalFilesDir))
        {
            return externalFilesDir;
        }

        var internalFilesDir = context.FilesDir ?? context.ApplicationContext?.FilesDir;
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

    internal static void HookCrashLogging()
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
            EnsureGitSafeDirectory(dataDir, directoryPath);
        }
        catch
        {
        }
    }

    private static void EnsureGitSafeDirectory(string dataDir, string? directoryPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return;
            }

            var configPath = Path.Combine(dataDir, ".gitconfig");
            var safeDirectory = ResolveGitSafeDirectoryPath(dataDir, directoryPath);

            GitSafeDirectoryConfig.EnsureSafeDirectory(configPath, safeDirectory);
            System.Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", configPath);
            GlobalSettings.SetConfigSearchPaths(ConfigurationLevel.Global, [dataDir]);
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

    private static void EnsureGitSslCertBundle(Context context, string dataDir)
    {
        try
        {
            var certPath = Path.Combine(dataDir, "cacert.pem");
            if (!File.Exists(certPath))
            {
                using var input = context.Assets?.Open("cacert.pem");
                if (input == null)
                {
                    return;
                }

                using var output = File.Create(certPath);
                input.CopyTo(output);
            }

            var certDirectory = Directory.Exists("/system/etc/security/cacerts")
                ? "/system/etc/security/cacerts"
                : null;
            LibGit2Interop.SetSslCertificateLocations(certPath, certDirectory);
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

    public override void OnCreate()
    {
        MainActivity.HookCrashLogging();

        try
        {
            MainActivity.ConfigureCoreAppServices(this);
        }
        catch (Exception ex)
        {
            MainActivity.WriteStartupError(ex);
            throw;
        }

        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithCustomFont()
            .UseReactiveUI(App.ConfigureReactiveUIBuilder);
    }
}
