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
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                {
                    lifetime.Shutdown();
                }
            });

            ShowWindowCommand = ReactiveCommand.Create(() => 
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                {
                    var mainWindow = lifetime.MainWindow;
                    if (mainWindow == null)
                    {
                        return;
                    }

                    switch (mainWindow.WindowState)
                    {
                        case WindowState.Normal:
                            mainWindow.WindowState = WindowState.Minimized;
                            break;
                        case WindowState.Minimized:
                            mainWindow.WindowState = WindowState.Normal;
                            break;
                        case WindowState.Maximized:
                            mainWindow.WindowState = WindowState.Minimized;
                            break;
                        case WindowState.FullScreen:
                            mainWindow.WindowState = WindowState.Minimized;
                            break;
                    }
                }
            });

            ToggleCommand = ReactiveCommand.Create(() => { });
        }

        public ICommand ExitCommand { get; }

        public ICommand ToggleCommand { get; }
        public ICommand ShowWindowCommand { get; }
    }
}
