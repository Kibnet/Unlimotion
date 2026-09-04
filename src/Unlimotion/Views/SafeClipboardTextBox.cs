using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Views;

/// <summary>Contains clipboard failures without replacing a newer edit after an asynchronous paste.</summary>
public class SafeClipboardTextBox : TextBox
{
    public static readonly StyledProperty<string?> ClipboardErrorProperty =
        AvaloniaProperty.Register<SafeClipboardTextBox, string?>(nameof(ClipboardError));
    public static readonly StyledProperty<bool> HasClipboardErrorProperty =
        AvaloniaProperty.Register<SafeClipboardTextBox, bool>(nameof(HasClipboardError));
    private bool isPasting;

    protected override Type StyleKeyOverride => typeof(TextBox);
    public string? ClipboardError { get => GetValue(ClipboardErrorProperty); private set => SetValue(ClipboardErrorProperty, value); }
    public bool HasClipboardError { get => GetValue(HasClipboardErrorProperty); private set => SetValue(HasClipboardErrorProperty, value); }

    public SafeClipboardTextBox()
    {
        PastingFromClipboard += async (_, args) =>
        {
            args.Handled = true; // TextBox.Paste is async void and only catches TimeoutException.
            if (isPasting || IsReadOnly || !IsEnabled) return;
            isPasting = true;
            ClipboardError = null;
            HasClipboardError = false;
            var textBefore = Text;
            var start = SelectionStart;
            var end = SelectionEnd;
            var context = DataContext;
            var window = TopLevel.GetTopLevel(this);
            try
            {
                var text = await ReadClipboardTextAsync().WaitAsync(TimeSpan.FromSeconds(5));
                if (Text != textBefore || SelectionStart != start || SelectionEnd != end
                    || !ReferenceEquals(DataContext, context) || TopLevel.GetTopLevel(this) != window
                    || !IsEnabled || IsReadOnly || !IsEffectivelyVisible)
                {
                    ShowClipboardError("FeedClipboardTargetChanged");
                    return;
                }
                if (!string.IsNullOrEmpty(text))
                    RaiseEvent(new TextInputEventArgs { RoutedEvent = TextInputEvent, Text = text });
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceError("Clipboard paste failed: {0}", exception);
                ShowClipboardError("FeedClipboardUnavailable");
            }
            finally { isPasting = false; }
        };
    }

    protected virtual Task<string?> ReadClipboardTextAsync() =>
        TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard
            ? clipboard.TryGetTextAsync() : Task.FromResult<string?>(null);

    private void ShowClipboardError(string key)
    {
        ClipboardError = L10n.Get(key);
        HasClipboardError = true;
    }
}
