#define Android

using Android;
using Android.App;
using Android.Content.PM;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using AutoMapper;
using Avalonia;
using Avalonia.Android;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using Microsoft.Extensions.Configuration;
using Splat;
using System;
using System.IO;
using Unlimotion.Services;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;

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
    private const string DefaultConfigName = "Settings.json";
    private const string TasksFolderName = "Tasks";
    const int RequestStorageId = 0;

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {

        // Проверяем, есть ли у нас разрешение на запись во внешнее хранилище
        if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.WriteExternalStorage) != Permission.Granted)
        {
            // Если разрешение не предоставлено, запрашиваем его
            ActivityCompat.RequestPermissions(this, new string[] { Manifest.Permission.WriteExternalStorage }, RequestStorageId);
        }
        else
        {
            // Разрешение уже предоставлено, продолжаем работу
            AccessExternalStorage();
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

        //Задание дефолтного пути для хранения задач
        TaskStorages.DefaultStoragePath = Path.Combine(dataDir, TasksFolderName);

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

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode == RequestStorageId)
        {
            if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
            {
                // Разрешение предоставлено, продолжаем работу
                AccessExternalStorage();
            }
            else
            {
                // Разрешение не предоставлено, уведомляем пользователя
                Toast.MakeText(this, "Разрешение на доступ к внешнему хранилищу не предоставлено", ToastLength.Short).Show();
            }
        }
    }

    private void AccessExternalStorage()
    {
        // Здесь ваш код для доступа к внешнему хранилищу
        var externalDataDir = GetExternalFilesDir(null)?.AbsolutePath;
        Toast.MakeText(this, $"Путь внешнего хранилища: {externalDataDir}", ToastLength.Long).Show();
    }
}

