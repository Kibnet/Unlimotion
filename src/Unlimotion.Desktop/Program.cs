using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Logging;
using ServiceStack;
using ReactiveUI.Avalonia;
using Unlimotion.Desktop.Services;
using Unlimotion.Services;
using Velopack;

namespace Unlimotion.Desktop
{
    class Program
    {
        private const string DefaultConfigName = "Settings.json";
        private const string TasksFolderName = "Tasks";
        private const string UnlimotionFolderName = "Unlimotion";

        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            VelopackApp.Build().Run();
            App.ConfigureUpdateService(new VelopackApplicationUpdateService());

#if DEBUG
            var defaultTaskStoragePath = TasksFolderName;
#else
            var defaultTaskStoragePath = Path.GetDirectoryName(DefaultConfigName).CombineWith(TasksFolderName);
#endif

            //Получение адреса конфига
            var configArg = args.FirstOrDefault(s =>
                s.StartsWith("-config=", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("--config=", StringComparison.OrdinalIgnoreCase));

#if DEBUG
            var configPath = DefaultConfigName;
#else
            var unlimotionFolder = Environment.GetFolderPath(Environment.SpecialFolder.Personal).CombineWith(UnlimotionFolderName);
            var configPath = unlimotionFolder.CombineWith(DefaultConfigName);
#endif

            if (configArg != null)
            {
                var path = configArg.Split("=").Last();
                if (!path.IsNullOrEmpty())
                {
                    configPath = path;
                }
            }
            else
            {
#if !DEBUG
                Directory.CreateDirectory(unlimotionFolder);
#endif
            }

            App.Init(
                configPath,
                new UnlimotionClientOptions
                {
                    DefaultTaskStoragePath = defaultTaskStoragePath,
                    GetAbsolutePath = path => new DirectoryInfo(path).FullName
                });
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            var builder = AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithCustomFont();

#if DEBUG
            return builder
                .WithDeveloperTools()
                .LogToTrace(LogEventLevel.Debug, LogArea.Binding)
                .UseReactiveUI(App.ConfigureReactiveUIBuilder);
#else
            return builder
                .LogToTrace()
                .UseReactiveUI(App.ConfigureReactiveUIBuilder);
#endif
        }
    }
}
