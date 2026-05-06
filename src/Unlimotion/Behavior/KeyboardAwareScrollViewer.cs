using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Unlimotion.Behavior
{
    public sealed class KeyboardAwareScrollViewer
    {
        private const double DefaultFocusedControlMargin = 16;

        private KeyboardAwareScrollViewer()
        {
        }

        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<KeyboardAwareScrollViewer, ScrollViewer, bool>("IsEnabled");

        public static readonly AttachedProperty<double> KeyboardInsetBottomOverrideProperty =
            AvaloniaProperty.RegisterAttached<KeyboardAwareScrollViewer, ScrollViewer, double>(
                "KeyboardInsetBottomOverride",
                double.NaN);

        public static readonly AttachedProperty<double> FocusedControlMarginProperty =
            AvaloniaProperty.RegisterAttached<KeyboardAwareScrollViewer, ScrollViewer, double>(
                "FocusedControlMargin",
                DefaultFocusedControlMargin);

        private static readonly AttachedProperty<KeyboardAwareScrollViewerState?> StateProperty =
            AvaloniaProperty.RegisterAttached<KeyboardAwareScrollViewer, ScrollViewer, KeyboardAwareScrollViewerState?>(
                "State");

        static KeyboardAwareScrollViewer()
        {
            IsEnabledProperty.Changed.Subscribe(e =>
            {
                if (e.Sender is ScrollViewer scrollViewer)
                {
                    UpdateState(scrollViewer);
                }
            });

            KeyboardInsetBottomOverrideProperty.Changed.Subscribe(e =>
            {
                if (e.Sender is ScrollViewer scrollViewer)
                {
                    GetState(scrollViewer)?.RefreshKeyboardInsetAndFocusedControl();
                }
            });

            FocusedControlMarginProperty.Changed.Subscribe(e =>
            {
                if (e.Sender is ScrollViewer scrollViewer)
                {
                    GetState(scrollViewer)?.ScrollFocusedControlIntoView();
                }
            });
        }

        public static bool GetIsEnabled(ScrollViewer scrollViewer)
        {
            return scrollViewer.GetValue(IsEnabledProperty);
        }

        public static void SetIsEnabled(ScrollViewer scrollViewer, bool value)
        {
            scrollViewer.SetValue(IsEnabledProperty, value);
        }

        public static double GetKeyboardInsetBottomOverride(ScrollViewer scrollViewer)
        {
            return scrollViewer.GetValue(KeyboardInsetBottomOverrideProperty);
        }

        public static void SetKeyboardInsetBottomOverride(ScrollViewer scrollViewer, double value)
        {
            scrollViewer.SetValue(KeyboardInsetBottomOverrideProperty, value);
        }

        public static double GetFocusedControlMargin(ScrollViewer scrollViewer)
        {
            return scrollViewer.GetValue(FocusedControlMarginProperty);
        }

        public static void SetFocusedControlMargin(ScrollViewer scrollViewer, double value)
        {
            scrollViewer.SetValue(FocusedControlMarginProperty, value);
        }

        private static KeyboardAwareScrollViewerState? GetState(ScrollViewer scrollViewer)
        {
            return scrollViewer.GetValue(StateProperty);
        }

        private static void UpdateState(ScrollViewer scrollViewer)
        {
            var state = GetState(scrollViewer);
            if (GetIsEnabled(scrollViewer))
            {
                if (state == null)
                {
                    state = new KeyboardAwareScrollViewerState(scrollViewer);
                    scrollViewer.SetValue(StateProperty, state);
                    state.Attach();
                }
            }
            else if (state != null)
            {
                state.Detach();
                scrollViewer.ClearValue(StateProperty);
            }
        }

        private sealed class KeyboardAwareScrollViewerState
        {
            private readonly ScrollViewer scrollViewer;
            private TopLevel? topLevel;
            private IInputPane? inputPane;
            private Control? contentControl;
            private Thickness contentBaseMargin;
            private double keyboardInsetBottom;
            private bool isApplyingContentMargin;
            private int scrollRequestVersion;

            public KeyboardAwareScrollViewerState(ScrollViewer scrollViewer)
            {
                this.scrollViewer = scrollViewer;
            }

            public void Attach()
            {
                scrollViewer.AttachedToVisualTree += OnAttachedToVisualTree;
                scrollViewer.DetachedFromVisualTree += OnDetachedFromVisualTree;
                scrollViewer.PropertyChanged += OnScrollViewerPropertyChanged;
                scrollViewer.AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble);

                RefreshContentControl();
                RefreshInputPaneSubscription();
                RefreshKeyboardInsetAndFocusedControl();
            }

            public void Detach()
            {
                scrollViewer.AttachedToVisualTree -= OnAttachedToVisualTree;
                scrollViewer.DetachedFromVisualTree -= OnDetachedFromVisualTree;
                scrollViewer.PropertyChanged -= OnScrollViewerPropertyChanged;
                scrollViewer.RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);

                if (inputPane != null)
                {
                    inputPane.StateChanged -= OnInputPaneStateChanged;
                    inputPane = null;
                }

                topLevel = null;
                RestoreContentMargin();
                contentControl = null;
            }

            public void RefreshKeyboardInsetAndFocusedControl()
            {
                RefreshInputPaneSubscription();
                keyboardInsetBottom = ResolveKeyboardInsetBottom();
                ApplyContentMargin();
                ScrollFocusedControlIntoView();
            }

            public void ScrollFocusedControlIntoView()
            {
                var focusedControl = GetFocusedKeyboardInputControl();
                if (focusedControl != null)
                {
                    QueueScrollIntoView(focusedControl);
                }
            }

            private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
            {
                RefreshInputPaneSubscription();
                RefreshKeyboardInsetAndFocusedControl();
            }

            private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
            {
                if (inputPane != null)
                {
                    inputPane.StateChanged -= OnInputPaneStateChanged;
                    inputPane = null;
                }

                topLevel = null;
            }

            private void OnScrollViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property == ContentControl.ContentProperty)
                {
                    RefreshContentControl();
                    ApplyContentMargin();
                    ScrollFocusedControlIntoView();
                }
            }

            private void OnInputPaneStateChanged(object? sender, InputPaneStateEventArgs e)
            {
                RefreshKeyboardInsetAndFocusedControl();
            }

            private void OnGotFocus(object? sender, RoutedEventArgs e)
            {
                if (e.Source is Control control && IsKeyboardInputControlOrDescendant(control))
                {
                    QueueScrollIntoView(control);
                }
            }

            private void RefreshInputPaneSubscription()
            {
                var currentTopLevel = TopLevel.GetTopLevel(scrollViewer);
                var currentInputPane = currentTopLevel?.InputPane;
                if (ReferenceEquals(currentInputPane, inputPane))
                {
                    topLevel = currentTopLevel;
                    return;
                }

                if (inputPane != null)
                {
                    inputPane.StateChanged -= OnInputPaneStateChanged;
                }

                topLevel = currentTopLevel;
                inputPane = currentInputPane;

                if (inputPane != null)
                {
                    inputPane.StateChanged += OnInputPaneStateChanged;
                }
            }

            private void RefreshContentControl()
            {
                var currentContentControl = scrollViewer.Content as Control;
                if (ReferenceEquals(currentContentControl, contentControl))
                {
                    return;
                }

                RestoreContentMargin();
                contentControl = currentContentControl;
                contentBaseMargin = contentControl?.Margin ?? default;
            }

            private void ApplyContentMargin()
            {
                RefreshContentControl();
                if (contentControl == null)
                {
                    return;
                }

                isApplyingContentMargin = true;
                try
                {
                    contentControl.Margin = new Thickness(
                        contentBaseMargin.Left,
                        contentBaseMargin.Top,
                        contentBaseMargin.Right,
                        contentBaseMargin.Bottom + keyboardInsetBottom);
                }
                finally
                {
                    isApplyingContentMargin = false;
                }
            }

            private void RestoreContentMargin()
            {
                if (contentControl == null || isApplyingContentMargin)
                {
                    return;
                }

                contentControl.Margin = contentBaseMargin;
            }

            private double ResolveKeyboardInsetBottom()
            {
                var overrideInset = GetKeyboardInsetBottomOverride(scrollViewer);
                if (!double.IsNaN(overrideInset))
                {
                    return Math.Max(0, overrideInset);
                }

                if (topLevel == null ||
                    inputPane == null ||
                    inputPane.State != InputPaneState.Open ||
                    inputPane.OccludedRect.Height <= 0)
                {
                    return 0;
                }

                var scrollViewerTop = scrollViewer.TranslatePoint(default, topLevel)?.Y ?? 0;
                var scrollViewerBottom = scrollViewer.TranslatePoint(
                    new Point(0, scrollViewer.Bounds.Height),
                    topLevel)?.Y ?? scrollViewer.Bounds.Height;
                var overlapTop = Math.Max(scrollViewerTop, inputPane.OccludedRect.Top);
                var overlapBottom = Math.Min(scrollViewerBottom, inputPane.OccludedRect.Bottom);

                return Math.Max(0, overlapBottom - overlapTop);
            }

            private Control? GetFocusedKeyboardInputControl()
            {
                var focused = topLevel?.FocusManager?.GetFocusedElement() as Control;
                if (focused == null ||
                    !IsInsideScrollViewer(focused) ||
                    !IsKeyboardInputControlOrDescendant(focused))
                {
                    return null;
                }

                return focused;
            }

            private bool IsInsideScrollViewer(Control control)
            {
                for (Visual? current = control; current != null; current = current.GetVisualParent())
                {
                    if (ReferenceEquals(current, scrollViewer))
                    {
                        return true;
                    }
                }

                return false;
            }

            private bool IsKeyboardInputControlOrDescendant(Control control)
            {
                if (IsKeyboardInputControl(control))
                {
                    return true;
                }

                for (Visual? current = control.GetVisualParent(); current != null; current = current.GetVisualParent())
                {
                    if (ReferenceEquals(current, scrollViewer))
                    {
                        return false;
                    }

                    if (current is Control ancestor && IsKeyboardInputControl(ancestor))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool IsKeyboardInputControl(Control control)
            {
                return control is TextBox or NumericUpDown or AutoCompleteBox;
            }

            private void QueueScrollIntoView(Control control)
            {
                var requestVersion = ++scrollRequestVersion;
                Dispatcher.UIThread.Post(
                    () =>
                    {
                        if (requestVersion == scrollRequestVersion)
                        {
                            ScrollIntoVisibleArea(control);
                        }
                    },
                    DispatcherPriority.Loaded);
            }

            private void ScrollIntoVisibleArea(Control control)
            {
                if (!control.IsAttachedToVisualTree() || !IsInsideScrollViewer(control))
                {
                    return;
                }

                RefreshInputPaneSubscription();
                keyboardInsetBottom = ResolveKeyboardInsetBottom();
                ApplyContentMargin();
                scrollViewer.UpdateLayout();

                var topLeft = control.TranslatePoint(default, scrollViewer);
                if (!topLeft.HasValue)
                {
                    return;
                }

                var margin = Math.Max(0, GetFocusedControlMargin(scrollViewer));
                var visibleTop = margin;
                var visibleBottom = Math.Max(visibleTop, scrollViewer.Bounds.Height - keyboardInsetBottom - margin);
                var controlTop = topLeft.Value.Y;
                var controlBottom = controlTop + control.Bounds.Height;
                var desiredOffsetY = scrollViewer.Offset.Y;

                if (controlBottom > visibleBottom)
                {
                    desiredOffsetY += controlBottom - visibleBottom;
                }
                else if (controlTop < visibleTop)
                {
                    desiredOffsetY -= visibleTop - controlTop;
                }

                var maxOffsetY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
                desiredOffsetY = Math.Clamp(desiredOffsetY, 0, maxOffsetY);

                if (Math.Abs(desiredOffsetY - scrollViewer.Offset.Y) > 0.5)
                {
                    scrollViewer.Offset = new Vector(scrollViewer.Offset.X, desiredOffsetY);
                }
            }
        }
    }
}
