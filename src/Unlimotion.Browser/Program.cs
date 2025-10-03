using AutoMapper;
using Avalonia;
using Avalonia.Browser;
using Avalonia.ReactiveUI;
using Microsoft.Extensions.Configuration;
using Splat;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Unlimotion;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;

internal sealed partial class Program
{
    private static async Task Main(string[] args) => await BuildAvaloniaApp()
            .WithCustomFont()
            .UseReactiveUI()
            .StartBrowserAppAsync("out");

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

        var notificationManager = new NotificationManagerWrapper();
        Locator.CurrentMutable.RegisterConstant<INotificationManagerWrapper>(notificationManager);
        return AppBuilder.Configure<App>();
    }
}