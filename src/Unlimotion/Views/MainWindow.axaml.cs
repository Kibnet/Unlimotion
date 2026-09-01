using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Unlimotion.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            AddHandler(KeyDownEvent, MainWindow_OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        private void MainWindow_OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (MainScreen.TryHandleShellHotkey(e))
            {
                e.Handled = true;
                return;
            }

            MainScreen.TryHandleHotkeyHelpKey(e);
        }
    }
}
