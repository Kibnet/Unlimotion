#define Android

using System;
using System.IO;
using Android;
using Android.App;
using Android.Content.PM;
using Android.Views;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Widget;
using Android.Util;
using Android.Runtime;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Avalonia;
using Avalonia.Android;
using Avalonia.ReactiveUI;
using Unlimotion;
using Unlimotion.Services;

namespace Unlimotion.Android;

[Activity(
    Label = "Unlimotion.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ResizeableActivity = true,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        HookCrashLogging();
        EnsureAllFilesAccessIfNeeded();
    }

    protected override void OnResume()
    {
        base.OnResume();

        EnsureAllFilesAccessIfNeeded();
    }

    private const string DefaultConfigName = "Settings.json";
    private const string TasksFolderName = "Tasks";
    const int RequestStorageId = 0;
    private bool _requestedAllFilesAccess;
    private bool _requestedLegacyStorageAccess;

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
    try
    {

        if (Build.VERSION.SdkInt < BuildVersionCodes.R)
        {
            var needsWrite = ContextCompat.CheckSelfPermission(this, Manifest.Permission.WriteExternalStorage) != Permission.Granted;
            var needsRead = ContextCompat.CheckSelfPermission(this, Manifest.Permission.ReadExternalStorage) != Permission.Granted;
            if (needsWrite || needsRead)
            {
                ActivityCompat.RequestPermissions(this, new[] { Manifest.Permission.ReadExternalStorage, Manifest.Permission.WriteExternalStorage }, RequestStorageId);
            }
            else
            {
                AccessExternalStorage();
            }
        }

        string dataDir;
        if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.WriteExternalStorage) != Permission.Granted)
        {
            dataDir = ApplicationContext.FilesDir.AbsolutePath;
        }
        else
        {
            dataDir = ApplicationContext.GetExternalFilesDir(null)?.AbsolutePath;
        }

        BackupViaGitService.GetAbsolutePath = path => Path.Combine(dataDir, path);

        EnsureGitSafeDirectory(dataDir);
        EnsureGitSslCertBundle(dataDir);

        //Задание дефолтного пути для хранения задач
        TaskStorageFactory.DefaultStoragePath = Path.Combine(dataDir, TasksFolderName);

        //Задание дефолтного пути для хранения настроек
        var configPath = Path.Combine(dataDir, DefaultConfigName);
        if (!File.Exists(configPath))
        {
            var stream = File.CreateText(configPath);
            stream.Write(@"{}");
            stream.Close();
        }

        App.Init(configPath);

        return base.CustomizeAppBuilder(builder)
                .WithCustomFont()
                .UseReactiveUI();
    }
    catch (Exception ex)
    {
        WriteStartupError(ex);
        throw;
    }

    }

    private static void WriteStartupError(Exception ex)
    {
        try
        {
            Log.Error("Unlimotion", ex.ToString());
            if (!TryWriteStartupErrorToDownloads(ex.ToString()))
            {
                var dir = global::Android.App.Application.Context.FilesDir;
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

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode == RequestStorageId)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                return;
            }

            if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
            {
                // Разрешение предоставлено, продолжаем работу
                AccessExternalStorage();
            }
            else
            {
                // Разрешение не предоставлено, уведомляем пользователя
                Toast.MakeText(this, "Разрешение на доступ к внешнему хранилищу не предоставлено", ToastLength.Short)?.Show();
            }
        }
    }

    private void EnsureAllFilesAccessIfNeeded()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            if (global::Android.OS.Environment.IsExternalStorageManager)
            {
                return;
            }

            if (_requestedAllFilesAccess)
            {
                return;
            }

            _requestedAllFilesAccess = true;
            Toast.MakeText(this, "Нужно разрешение \"Доступ ко всем файлам\" для работы с папкой задач", ToastLength.Long)?.Show();

            var handler = new Handler(Looper.MainLooper);
            handler.Post(() =>
            {
                try
                {
                    var intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission);
                    intent.SetData(global::Android.Net.Uri.Parse($"package:{PackageName}"));
                    StartActivity(intent);
                }
                catch
                {
                    try
                    {
                        var intent = new Intent(Settings.ActionManageAllFilesAccessPermission);
                        StartActivity(intent);
                    }
                    catch
                    {
                        var intent = new Intent(Settings.ActionApplicationDetailsSettings);
                        intent.SetData(global::Android.Net.Uri.Parse($"package:{PackageName}"));
                        StartActivity(intent);
                    }
                }
            });

            return;
        }

        if (_requestedLegacyStorageAccess)
        {
            return;
        }

        var needsWrite = ContextCompat.CheckSelfPermission(this, Manifest.Permission.WriteExternalStorage) != Permission.Granted;
        var needsRead = ContextCompat.CheckSelfPermission(this, Manifest.Permission.ReadExternalStorage) != Permission.Granted;
        if (needsWrite || needsRead)
        {
            _requestedLegacyStorageAccess = true;
            ActivityCompat.RequestPermissions(this, new[] { Manifest.Permission.ReadExternalStorage, Manifest.Permission.WriteExternalStorage }, RequestStorageId);
        }
    }

    private void EnsureGitSafeDirectory(string dataDir)
    {
        try
        {
            var configPath = Path.Combine(dataDir, ".gitconfig");
            if (!File.Exists(configPath))
            {
                File.WriteAllText(configPath, "[safe]\n\tdirectory = *\n");
            }

            System.Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", configPath);
        }
        catch
        {
        }
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

    private void AccessExternalStorage()
    {
        // Здесь ваш код для доступа к внешнему хранилищу
        var externalDataDir = GetExternalFilesDir(null)?.AbsolutePath;
        Toast.MakeText(this, $"Путь внешнего хранилища: {externalDataDir}", ToastLength.Long)?.Show();
    }
}
