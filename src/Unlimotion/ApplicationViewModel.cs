using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ReactiveUI;
using System.Windows.Input;

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
                        case Avalonia.Controls.WindowState.Normal:
                            lifetime.MainWindow.WindowState = Avalonia.Controls.WindowState.Minimized;
                            break;
                        case Avalonia.Controls.WindowState.Minimized:
                            lifetime.MainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
                            break;
                        case Avalonia.Controls.WindowState.Maximized:
                            lifetime.MainWindow.WindowState = Avalonia.Controls.WindowState.Minimized;
                            break;
                        case Avalonia.Controls.WindowState.FullScreen:
                            lifetime.MainWindow.WindowState = Avalonia.Controls.WindowState.Minimized;
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
