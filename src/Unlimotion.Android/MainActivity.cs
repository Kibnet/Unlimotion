using System.IO;
using System.Text;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using Avalonia;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Splat;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;

namespace Unlimotion.Android
{
    [Activity(Label = "Unlimotion.Android",
        Theme = "@style/MyTheme.NoActionBar",
        Icon = "@drawable/icon",
        LaunchMode = LaunchMode.SingleInstance,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]

    public class MainActivity : AvaloniaActivity<App>
    {
        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            var taskStorage = new FileTaskStorage(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "Tasks"));
            Locator.CurrentMutable.RegisterConstant<ITaskStorage>(taskStorage);
            var taskRepository = new TaskRepository(taskStorage);
            Locator.CurrentMutable.RegisterConstant<ITaskRepository>(taskRepository);
            var notificationManager = new MessageBoxManagerWrapperWrapper();
            Locator.CurrentMutable.RegisterConstant<INotificationManagerWrapper>(notificationManager);
            var settingsPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "Settings.json");
            if (!File.Exists(settingsPath))
            {
                var stream = File.CreateText(settingsPath);
                stream.Write(@"{}");
                stream.Close();
            }
            IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(settingsPath);
            Locator.CurrentMutable.RegisterConstant(configuration, typeof(IConfiguration));

            return base.CustomizeAppBuilder(builder);
        }
    }
}
