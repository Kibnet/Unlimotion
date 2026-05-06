using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Unlimotion.Behavior;

namespace Unlimotion.Test;

[ParallelLimiter<SharedUiStateParallelLimit>]
public class KeyboardAwareScrollViewerUiTests
{
    [Test]
    public async Task FocusedTextBoxCoveredByKeyboardInset_ScrollsIntoVisibleArea()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            const double keyboardInset = 190;
            Window? window = null;

            try
            {
                var textBox = new TextBox
                {
                    Height = 36,
                    Text = "Focused field"
                };

                var content = new StackPanel
                {
                    Margin = new Thickness(12),
                    Spacing = 8,
                    Children =
                    {
                        new Border { Height = 300, Background = Brushes.Transparent },
                        textBox,
                        new Border { Height = 450, Background = Brushes.Transparent }
                    }
                };

                var scrollViewer = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = content
                };
                KeyboardAwareScrollViewer.SetIsEnabled(scrollViewer, true);
                KeyboardAwareScrollViewer.SetKeyboardInsetBottomOverride(scrollViewer, keyboardInset);

                window = new Window
                {
                    Width = 360,
                    Height = 420,
                    Content = scrollViewer
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var initialOffset = scrollViewer.Offset.Y;
                var focused = textBox.Focus();
                Dispatcher.UIThread.RunJobs();

                var scrolledAboveKeyboard = WaitFor(() =>
                    scrollViewer.Offset.Y > initialOffset + 1 &&
                    IsControlAboveKeyboard(scrollViewer, textBox, keyboardInset));

                await Assert.That(focused).IsTrue();
                await Assert.That(scrolledAboveKeyboard).IsTrue();
                await Assert.That(content.Margin.Bottom).IsEqualTo(12 + keyboardInset);
            }
            finally
            {
                window?.Close();
            }
        }, CancellationToken.None);
    }

    private static bool IsControlAboveKeyboard(ScrollViewer scrollViewer, Control control, double keyboardInset)
    {
        var bottom = control.TranslatePoint(new Point(0, control.Bounds.Height), scrollViewer);
        if (!bottom.HasValue)
        {
            return false;
        }

        var focusedMargin = KeyboardAwareScrollViewer.GetFocusedControlMargin(scrollViewer);
        return bottom.Value.Y <= scrollViewer.Bounds.Height - keyboardInset - focusedMargin + 1;
    }

    private static bool WaitFor(Func<bool> predicate, int timeoutMilliseconds = 2000)
    {
        var start = DateTime.UtcNow;
        while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMilliseconds)
        {
            Dispatcher.UIThread.RunJobs();
            if (predicate())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        Dispatcher.UIThread.RunJobs();
        return predicate();
    }
}
