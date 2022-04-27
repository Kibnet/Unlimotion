using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ReactiveUI;
using Unlimotion.ViewModel;

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