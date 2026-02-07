using System;
using System.IO;
using System.Threading.Tasks;
using AutoMapper;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Notification;
using Avalonia.ReactiveUI;
using Microsoft.Extensions.Configuration;
using Unlimotion;
using Unlimotion.Services;
using Unlimotion.ViewModel;
using Unlimotion.Views;
using WritableJsonConfiguration;

internal sealed class Program
{
    private static async Task Main(string[] args) => await BuildAvaloniaApp()
            .WithCustomFont()
            .UseReactiveUI()
            .StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp()
    {
        TaskStorageFactory.DefaultStoragePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tasks");

        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Settings.json");
        if (!File.Exists(settingsPath))
        {
            var stream = File.CreateText(settingsPath);
            stream.Write(@"{}");
            stream.Close();
        }
        
        // Create configuration
        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(settingsPath);
        
        // Create mapper
        var mapper = AppModelMapping.ConfigureMapping();

        // Create dialogs
        var dialogs = new Dialogs();

        // Create notification services
        var notificationMessageManager = new NotificationMessageManager();
        var notificationManager = new NotificationManagerWrapper(notificationMessageManager);

        // Create storage factory
        var storageFactory = new TaskStorageFactory(configuration, mapper, notificationManager);

        // Get storage settings
        var taskStorageSettings = configuration.Get<TaskStorageSettings>("TaskStorage");
        if (taskStorageSettings == null)
        {
            taskStorageSettings = new TaskStorageSettings();
            configuration.Set("TaskStorage", taskStorageSettings);
        }
        var isServerMode = taskStorageSettings.IsServerMode;
        
        // Create storage
        if (isServerMode)
        {
            storageFactory.CreateServerStorage(taskStorageSettings.URL);
        }
        else
        {
            storageFactory.CreateFileStorage(taskStorageSettings.Path);
        }

        // Set up static dependencies
        TaskItemViewModel.NotificationManagerInstance = notificationManager;
        MainControl.DialogsInstance = dialogs;

        return AppBuilder.Configure<App>();
    }
}