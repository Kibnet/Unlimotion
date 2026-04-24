#define Android

using System;
using System.IO;
using System.Runtime.Versioning;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Android.Util;
using Android.Views;
using Android.Runtime;
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
    }

    private const string DefaultConfigName = "Settings.json";
    private const string TasksFolderName = "Tasks";

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        try
        {
            var dataDir = ResolveDataDirectory();
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

    private static void WriteStartupError(Exception ex)
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
}
