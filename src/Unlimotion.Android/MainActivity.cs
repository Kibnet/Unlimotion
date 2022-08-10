using System.IO;
using Android.App;
using Android.Content.PM;
using Avalonia.Android;
using Avalonia;
using Microsoft.Extensions.Configuration;
using Splat;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;
using AutoMapper;
using ReactiveUI;
using System.Linq;

namespace Unlimotion.Android
{
    [Activity(Label = "Unlimotion.Android",
        Theme = "@style/MyTheme.NoActionBar",
        Icon = "@drawable/icon",
        LaunchMode = LaunchMode.SingleInstance,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]

    public class MainActivity : AvaloniaActivity<App>
    {
        private string defaultPath;
        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
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

            var mapper = AppModelMapping.ConfigureMapping();
            Locator.CurrentMutable.Register<IMapper>(() => mapper);

            var taskStorageSettings = configuration.Get<TaskStorageSettings>("TaskStorage");
            if(taskStorageSettings==null)
            {
                taskStorageSettings = new TaskStorageSettings();
                configuration.Set("TaskStorage", taskStorageSettings);
            }
            var isServerMode = taskStorageSettings.IsServerMode;
            TaskStorages.RegisterStorage(isServerMode, configuration);

            var notificationManager = new NotificationManagerWrapperWrapper();
            Locator.CurrentMutable.RegisterConstant<INotificationManagerWrapper>(notificationManager);

            return base.CustomizeAppBuilder(builder);
        }

        protected override void OnStart()
        {
            TaskStorages.SetSettingsCommands();
            
            base.OnStart();
        }        
    }
}
