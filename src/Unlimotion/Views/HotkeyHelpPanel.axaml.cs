using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Unlimotion.Views
{
    public partial class HotkeyHelpPanel : UserControl
    {
        public event EventHandler? CloseRequested;

        public HotkeyHelpPanel()
        {
            InitializeComponent();
        }

        private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}
