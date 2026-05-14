using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace Unlimotion.Behavior
{
    public sealed class LostFocusUpdateBindingBehavior
    {
        private LostFocusUpdateBindingBehavior()
        {
        }

        public static readonly AttachedProperty<string?> TextProperty =
            AvaloniaProperty.RegisterAttached<LostFocusUpdateBindingBehavior, TextBox, string?>(
                "Text",
                defaultBindingMode: BindingMode.TwoWay);

        private static readonly AttachedProperty<LostFocusUpdateBindingState?> StateProperty =
            AvaloniaProperty.RegisterAttached<LostFocusUpdateBindingBehavior, TextBox, LostFocusUpdateBindingState?>(
                "State");

        static LostFocusUpdateBindingBehavior()
        {
            TextProperty.Changed.Subscribe(e =>
            {
                if (e.Sender is TextBox textBox)
                {
                    GetOrCreateState(textBox).OnBindingValueChanged();
                }
            });
        }

        public static string? GetText(TextBox textBox)
        {
            return textBox.GetValue(TextProperty);
        }

        public static void SetText(TextBox textBox, string? value)
        {
            textBox.SetValue(TextProperty, value);
        }

        private static LostFocusUpdateBindingState GetOrCreateState(TextBox textBox)
        {
            var state = textBox.GetValue(StateProperty);
            if (state == null)
            {
                state = new LostFocusUpdateBindingState(textBox);
                textBox.SetValue(StateProperty, state);
                state.Attach();
            }

            return state;
        }

        private sealed class LostFocusUpdateBindingState
        {
            private readonly TextBox textBox;
            private object? changedTextDataContext;
            private bool isUpdatingTextFromBinding;
            private bool hasChangedTextForCurrentDataContext;

            public LostFocusUpdateBindingState(TextBox textBox)
            {
                this.textBox = textBox;
            }

            public void Attach()
            {
                textBox.GotFocus += OnGotFocus;
                textBox.DataContextChanged += OnDataContextChanged;
                textBox.TextChanged += OnTextChanged;
                textBox.LostFocus += OnLostFocus;
            }

            public void OnBindingValueChanged()
            {
                isUpdatingTextFromBinding = true;
                try
                {
                    var bindingText = GetText(textBox);
                    textBox.Text = string.IsNullOrEmpty(bindingText) ? null : bindingText;
                }
                finally
                {
                    isUpdatingTextFromBinding = false;
                }
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

                changedTextDataContext = textBox.DataContext;
                hasChangedTextForCurrentDataContext = true;
            }

            private void OnLostFocus(object? sender, RoutedEventArgs e)
            {
                if (hasChangedTextForCurrentDataContext &&
                    ReferenceEquals(changedTextDataContext, textBox.DataContext))
                {
                    SetText(textBox, textBox.Text);
                }

                ClearChangedTextContext();
            }

            private void ClearChangedTextContext()
            {
                changedTextDataContext = null;
                hasChangedTextForCurrentDataContext = false;
            }
        }
    }
}
