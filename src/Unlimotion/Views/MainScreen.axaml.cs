using Avalonia.Controls;
using Avalonia.Input;

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
            return MainControl.TryHandleHotkeyHelpKey(e);
        }
    }
}
