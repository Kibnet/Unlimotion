using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Logging;
using Avalonia.ReactiveUI;
using ServiceStack;
using Unlimotion.Services;

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
            //Задание дефолтного пути для хранения задач
#if DEBUG
            TaskStorages.DefaultStoragePath = TasksFolderName;
#else
            TaskStorages.DefaultStoragePath = Path.GetDirectoryName(DefaultConfigName).CombineWith(TasksFolderName);
#endif

            //Получение адреса конфига

            var configArg = args.FirstOrDefault(s => s.StartsWith("-config="));

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

            BackupViaGitService.GetAbsolutePath = path => new DirectoryInfo(path).FullName;

            App.Init(configPath);

            BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithCustomFont()
#if DEBUG
                .LogToTrace(LogEventLevel.Debug, LogArea.Binding)
#else
                .LogToTrace()
#endif

                .UseReactiveUI();
    }
}

