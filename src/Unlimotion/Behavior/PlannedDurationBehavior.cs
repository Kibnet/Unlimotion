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
        private object? changedTextDataContext;
        private bool isUpdatingTextFromBinding;
        private bool hasChangedTextForCurrentDataContext;

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
                AssociatedObject.DataContextChanged += OnDataContextChanged;
                AssociatedObject.TextChanged += OnTextChanged;
                AssociatedObject.LostFocus += OnLostFocus;
            }

            base.OnAttached();
        }

        protected override void OnDetaching()
        {
            if (AssociatedObject != null)
            {
                AssociatedObject.GotFocus -= OnGotFocus;
                AssociatedObject.DataContextChanged -= OnDataContextChanged;
                AssociatedObject.TextChanged -= OnTextChanged;
                AssociatedObject.LostFocus -= OnLostFocus;
            }

            ClearChangedTextContext();
            base.OnDetaching();
        }

        private void OnGotFocus(object? sender, RoutedEventArgs e)
        {
            ClearChangedTextContext();
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            ClearChangedTextContext();
            OnBindingValueChanged();
        }

        private void OnTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (isUpdatingTextFromBinding)
            {
                return;
            }

            changedTextDataContext = AssociatedObject?.DataContext;
            hasChangedTextForCurrentDataContext = true;
        }
        
        private void OnLostFocus(object? sender, RoutedEventArgs e)
        {
            if (AssociatedObject != null &&
                hasChangedTextForCurrentDataContext &&
                ReferenceEquals(changedTextDataContext, AssociatedObject.DataContext))
            {
                Text = AssociatedObject.Text;
            }

            ClearChangedTextContext();
        }
        
        private void OnBindingValueChanged()
        {
            if (AssociatedObject != null)
            {
                isUpdatingTextFromBinding = true;
                try
                {
                    AssociatedObject.Text = Text;
                }
                finally
                {
                    isUpdatingTextFromBinding = false;
                }
            }
        }

        private void ClearChangedTextContext()
        {
            changedTextDataContext = null;
            hasChangedTextForCurrentDataContext = false;
        }
    }
}
