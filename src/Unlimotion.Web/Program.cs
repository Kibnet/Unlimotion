using System.IO;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Web;
using Avalonia.ReactiveUI;
using Unlimotion;
using AutoMapper;
using Microsoft.Extensions.Configuration;
using Splat;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;

[assembly: SupportedOSPlatform("browser")]

internal partial class Program
{
    private static void Main(string[] args) => BuildAvaloniaApp()
        .UseReactiveUI()
        .SetupBrowserApp("out");

    public static AppBuilder BuildAvaloniaApp()
    {
        TaskStorages.DefaultStoragePath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "Tasks");

        var settingsPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "Settings.json");
        if (!File.Exists(settingsPath))
        {
            var stream = File.CreateText(settingsPath);
            stream.Write(@"{}");
            stream.Close();
        }
        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(settingsPath);
        Locator.CurrentMutable.RegisterConstant(configuration, typeof(IConfiguration));
        Locator.CurrentMutable.RegisterConstant(new Dialogs(), typeof(IDialogs));

        var mapper = AppModelMapping.ConfigureMapping();
        Locator.CurrentMutable.Register<IMapper>(() => mapper);

        var taskStorageSettings = configuration.Get<TaskStorageSettings>("TaskStorage");
        if (taskStorageSettings == null)
        {
            taskStorageSettings = new TaskStorageSettings();
            configuration.Set("TaskStorage", taskStorageSettings);
        }
        var isServerMode = taskStorageSettings.IsServerMode;
        TaskStorages.RegisterStorage(isServerMode, configuration);

        var notificationManager = new NotificationManagerWrapperWrapper();
        Locator.CurrentMutable.RegisterConstant<INotificationManagerWrapper>(notificationManager);
        return AppBuilder.Configure<App>();
    }
}