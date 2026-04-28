using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Xaml.Interactivity;

namespace Unlimotion.Behavior
{
    public class LostFocusUpdateBindingBehavior : Behavior<TextBox>
    {
        private object? focusedDataContext;

        static LostFocusUpdateBindingBehavior()
        {
            TextProperty.Changed.Subscribe(e =>
            {
                ((LostFocusUpdateBindingBehavior) e.Sender).OnBindingValueChanged();
            });
        }
        

        public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<LostFocusUpdateBindingBehavior, string?>(
            "Text", defaultBindingMode: BindingMode.TwoWay);

        public string? Text
        {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        protected override void OnAttached()
        {
            if (AssociatedObject != null)
            {
                AssociatedObject.GotFocus += OnGotFocus;
                AssociatedObject.LostFocus += OnLostFocus;
            }

            base.OnAttached();
        }

        protected override void OnDetaching()
        {
            if (AssociatedObject != null)
            {
                AssociatedObject.GotFocus -= OnGotFocus;
                AssociatedObject.LostFocus -= OnLostFocus;
            }

            focusedDataContext = null;
            base.OnDetaching();
        }

        private void OnGotFocus(object? sender, RoutedEventArgs e)
        {
            focusedDataContext = AssociatedObject?.DataContext;
        }
        
        private void OnLostFocus(object? sender, RoutedEventArgs e)
        {
            if (AssociatedObject != null && ReferenceEquals(focusedDataContext, AssociatedObject.DataContext))
                Text = AssociatedObject.Text;

            focusedDataContext = null;
        }
        
        private void OnBindingValueChanged()
        {
            if (AssociatedObject != null)
                AssociatedObject.Text = Text;
        }
    }
}
