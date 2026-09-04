using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class SafeClipboardTextBoxUiTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ClipboardFailure_KeepsTextAndAllowsRetry(bool keyboard)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var input = new TestClipboardTextBox { Text = "Сохранить", AcceptsReturn = true,
                Read = () => Task.FromException<string?>(new COMException("Clipboard locked")) };
            var window = new Window { Width = 480, Height = 320, Content = input };
            try
            {
                window.Show();
                input.Focus();
                input.CaretIndex = input.Text.Length;
                if (keyboard) window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, "v");
                else input.Paste();
                Dispatcher.UIThread.RunJobs();
                await Assert.That(input.Text).IsEqualTo("Сохранить");
                await Assert.That(input.HasClipboardError).IsTrue();
                input.Read = () => Task.FromResult<string?>(" ещё\nстрока");
                input.Paste();
                Dispatcher.UIThread.RunJobs();
                await Assert.That(input.Text).IsEqualTo("Сохранить ещё\nстрока");
                await Assert.That(input.HasClipboardError).IsFalse();
                input.Undo();
                await Assert.That(input.Text).IsEqualTo("Сохранить");
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task DelayedClipboard_DoesNotOverwriteNewTypingOrSelection()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var completion = new TaskCompletionSource<string?>();
            var input = new TestClipboardTextBox { Text = "Исходный", Read = () => completion.Task };
            var window = new Window { Width = 480, Height = 320, Content = input };
            try
            {
                window.Show();
                input.Focus();
                input.Paste();
                input.Text = "Новый черновик";
                completion.SetResult("Старая вставка");
                for (var i = 0; i < 20 && !input.HasClipboardError; i++)
                { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
                await Assert.That(input.Text).IsEqualTo("Новый черновик");
                await Assert.That(input.HasClipboardError).IsTrue();
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private sealed class TestClipboardTextBox : SafeClipboardTextBox
    {
        public Func<Task<string?>> Read { get; set; } = () => Task.FromResult<string?>(null);
        protected override Task<string?> ReadClipboardTextAsync() => Read();
    }
}
