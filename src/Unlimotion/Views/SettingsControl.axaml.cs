using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Unlimotion;

namespace Unlimotion.Views
{
    public partial class SettingsControl : UserControl
    {
        private const double ContentHorizontalMargin = 32d;

        public SettingsControl()
        {
            InitializeComponent();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            UpdateContentMaxWidth(availableSize.Width);
            return base.MeasureOverride(availableSize);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            UpdateContentMaxWidth(finalSize.Width);
            return base.ArrangeOverride(finalSize);
        }

        private void UpdateContentMaxWidth(double width)
        {
            var maxWidth = width - ContentHorizontalMargin;
            if (double.IsNaN(maxWidth) || maxWidth <= 0)
            {
                return;
            }

            SettingsContent.MaxWidth = Math.Max(0, maxWidth);
        }

        private void ShowHotkeysButton_OnClick(object? sender, RoutedEventArgs e)
        {
            this.FindParent<MainControl>()?.ShowHotkeyHelp();
            e.Handled = true;
        }
    }
}
