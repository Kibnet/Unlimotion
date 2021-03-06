//#define LIVE

using System;
using System.Diagnostics;
using System.Reactive;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Live.Avalonia;
using ReactiveUI;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion
{
    public class App : Application, ILiveView
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
                        DataContext = new MainWindowViewModel(),
                    };
                }

                RxApp.DefaultExceptionHandler = Observer.Create<Exception>(Console.WriteLine);
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                singleViewPlatform.MainView = new MainControl
                {
                    DataContext = new MainWindowViewModel(),
                };
            }

            base.OnFrameworkInitializationCompleted();
            OnLoaded?.Invoke(this, EventArgs.Empty);
        }

        public object CreateView(Window window)
        {
            window.DataContext ??= new MainWindowViewModel();
            return new MainControl();
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
