using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using ReactiveUI.Avalonia;
using Unlimotion;

internal sealed class Program
{
    private static async Task Main(string[] args) => await BuildAvaloniaApp()
            .WithCustomFont()
            .UseReactiveUI(App.ConfigureReactiveUIBuilder)
            .StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp()
    {
        var defaultTaskStoragePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tasks");

        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Settings.json");
        if (!File.Exists(settingsPath))
        {
            var stream = File.CreateText(settingsPath);
            stream.Write(@"{}");
            stream.Close();
        }

        App.Init(
            settingsPath,
            new UnlimotionClientOptions
            {
                DefaultTaskStoragePath = defaultTaskStoragePath
            });

        return AppBuilder.Configure<App>();
    }
}
