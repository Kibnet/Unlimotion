using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Unlimotion.Views
{
    public partial class SettingsControl : UserControl
    {
        public SettingsControl()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}