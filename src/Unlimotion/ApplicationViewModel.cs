using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ReactiveUI;

namespace Unlimotion
{
    public class ApplicationViewModel
    {
        public ApplicationViewModel()
        {
            ExitCommand = ReactiveCommand.Create(() =>
            {
                if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                {
                    lifetime.Shutdown();
                }
            });

            ShowWindowCommand = ReactiveCommand.Create(() => 
            {
                if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                {
                    switch (lifetime.MainWindow.WindowState)
                    {
                        case WindowState.Normal:
                            lifetime.MainWindow.WindowState = WindowState.Minimized;
                            break;
                        case WindowState.Minimized:
                            lifetime.MainWindow.WindowState = WindowState.Normal;
                            break;
                        case WindowState.Maximized:
                            lifetime.MainWindow.WindowState = WindowState.Minimized;
                            break;
                        case WindowState.FullScreen:
                            lifetime.MainWindow.WindowState = WindowState.Minimized;
                            break;
                    }
                }
            });
        }

        public ICommand ExitCommand { get; }

        public ICommand ToggleCommand { get; }
        public ICommand ShowWindowCommand { get; }
    }
}
