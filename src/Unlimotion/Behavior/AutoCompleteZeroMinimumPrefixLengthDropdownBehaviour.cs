using System;
using System.ComponentModel;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;

namespace TestAutoCompleteBehaviour.Behaviours
{
    public class AutoCompleteZeroMinimumPrefixLengthDropdownBehaviour : Behavior<AutoCompleteBox>
    {
        static AutoCompleteZeroMinimumPrefixLengthDropdownBehaviour()
        {
        }

        protected override void OnAttached()
        {
            if (AssociatedObject is not null)
            {
                AssociatedObject.KeyUp += OnKeyUp;
                AssociatedObject.DropDownOpening += DropDownOpening;
                AssociatedObject.GotFocus += OnGotFocus;
                AssociatedObject.PointerReleased += PointerReleased;

                Task.Delay(500).ContinueWith(_ => Dispatcher.UIThread.Invoke(() => { CreateDropdownButton(); }));
            }

            base.OnAttached();
        }

        protected override void OnDetaching()
        {
            if (AssociatedObject is not null)
            {
                AssociatedObject.KeyUp -= OnKeyUp;
                AssociatedObject.DropDownOpening -= DropDownOpening;
                AssociatedObject.GotFocus -= OnGotFocus;
                AssociatedObject.PointerReleased -= PointerReleased;
            }

            base.OnDetaching();
        }

        //have to use KeyUp as AutoCompleteBox eats some of the KeyDown events
        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            if ((e.Key == Key.Down || e.Key == Key.F4))
            {
                if (string.IsNullOrEmpty(AssociatedObject?.Text))
                {
                    ShowDropdown();
                }
            }
        }

        private void DropDownOpening(object? sender, CancelEventArgs e)
        {
            var prop = AssociatedObject?.GetType().GetProperty("TextBox", BindingFlags.Instance | BindingFlags.NonPublic);
            var tb = (TextBox?)prop?.GetValue(AssociatedObject);
            if (tb is not null && tb.IsReadOnly)
            {
                e.Cancel = true;
            }
        }

        private void PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (string.IsNullOrEmpty(AssociatedObject?.Text))
            {
                ShowDropdown();
            }
        }

        private void ShowDropdown()
        {
            if (AssociatedObject is not null && !AssociatedObject.IsDropDownOpen)
            {
                typeof(AutoCompleteBox).GetMethod("PopulateDropDown", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(AssociatedObject, new object[] { AssociatedObject, EventArgs.Empty });
                typeof(AutoCompleteBox).GetMethod("OpeningDropDown", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(AssociatedObject, new object[] { false });

                if (!AssociatedObject.IsDropDownOpen)
                {
                    //We *must* set the field and not the property as we need to avoid the changed event being raised (which prevents the dropdown opening).
                    var ipc = typeof(AutoCompleteBox).GetField("_ignorePropertyChange", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (!(bool)ipc?.GetValue(AssociatedObject))
                        ipc?.SetValue(AssociatedObject, true);

                    AssociatedObject.SetCurrentValue(AutoCompleteBox.IsDropDownOpenProperty, true);
                }
            }
        }

        private void CreateDropdownButton()
        {
            if (AssociatedObject != null)
            {
                var prop = AssociatedObject.GetType().GetProperty("TextBox", BindingFlags.Instance | BindingFlags.NonPublic);
                var tb = (TextBox?)prop?.GetValue(AssociatedObject);
                if (tb is not null && tb.InnerRightContent is not Button)
                {
                    var btn = new Button
                    {
                        /* grab symbol from https://www.amp-what.com/unicode/search/down */
                        Content = "⯆",
                        Margin = new(3),
                        ClickMode = ClickMode.Press
                    };
                    btn.Click += (s, e) =>
                    {
                        AssociatedObject.Text = string.Empty;
                        ShowDropdown();
                    };

                    tb.InnerRightContent = btn;
                }
            }
        }

        private void OnGotFocus(object? sender, RoutedEventArgs e)
        {
            if (AssociatedObject != null)
            {
                CreateDropdownButton();
            }
        }
    }
}
