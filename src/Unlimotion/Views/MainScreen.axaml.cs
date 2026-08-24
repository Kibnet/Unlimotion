using Avalonia.Controls;
using Avalonia.Input;
using Unlimotion.ViewModel;

namespace Unlimotion.Views
{
    public partial class MainScreen : UserControl
    {
        public MainScreen()
        {
            InitializeComponent();
        }

        internal bool TryHandleHotkeyHelpKey(KeyEventArgs e)
        {
            return DataContext is MainWindowViewModel { IsTasksMode: true }
                && MainControl.TryHandleHotkeyHelpKey(e);
        }
    }
}
