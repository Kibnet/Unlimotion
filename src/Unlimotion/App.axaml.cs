//#define LIVE

using System;
using System.Reactive;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Notification;
#if LIVE
using Live.Avalonia;
#endif
using ReactiveUI;
using Splat;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion
{
    public partial class App : Application
#if LIVE
        ,ILiveView
#endif
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public event EventHandler OnLoaded;

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
#if LIVE
                if (Debugger.IsAttached && !IsProduction())
                {
                    // Here, we create a new LiveViewHost, located in the 'Live.Avalonia'
                    // namespace, and pass an ILiveView implementation to it. The ILiveView
                    // implementation should have a parameterless constructor! Next, we
                    // start listening for any changes in the source files. And then, we
                    // show the LiveViewHost window. Simple enough, huh?
                    var window = new LiveViewHost(this, Console.WriteLine);
                    window.StartWatchingSourceFilesForHotReloading();
                    window.Show();
                }
                else
#endif
                {
                    desktop.MainWindow = new MainWindow
                    {
                        DataContext = GetMainWindowViewModel(),
                    };
                }

                RxApp.DefaultExceptionHandler = Observer.Create<Exception>(Console.WriteLine);

                TaskStorages.SetSettingsCommands();
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                singleViewPlatform.MainView = new MainControl
                {
                    DataContext = GetMainWindowViewModel(),
                };
            }

            MainWindowViewModel GetMainWindowViewModel()
            {
                var notificationMessageManager = new NotificationMessageManager();
                Locator.CurrentMutable.RegisterConstant<INotificationMessageManager>(notificationMessageManager);
                RxApp.DefaultExceptionHandler = new ObservableExceptionHandler();
                return new MainWindowViewModel
                {
                    ToastNotificationManager = notificationMessageManager
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        public App()
        {
            DataContext = new ApplicationViewModel();
        }

        private static bool IsProduction()
        {
#if DEBUG
            return false;
#else
        return true;
#endif
        }
    }
}
