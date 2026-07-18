using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogHostAvalonia;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class NotificationManagerWrapperTests
{
    [Test]
    public Task ConfirmAsync_YesCommand_CompletesTrueExactlyOnce() =>
        RunMountedAsync(async (_, _, wrapper) =>
        {
            var opened = OpenConfirmation(wrapper);

            opened.ViewModel.YesCommand.Execute(null);
            var result = await AwaitWithDispatcherPumpAsync(opened.Completion);
            opened.ViewModel.NoAction?.Invoke();
            Dispatcher.UIThread.RunJobs();

            using (Assert.Multiple())
            {
                await Assert.That(result).IsTrue();
                await Assert.That(await opened.Completion).IsTrue();
                await Assert.That(opened.Session.IsEnded).IsTrue();
            }
        });

    [Test]
    public Task ConfirmAsync_NoCommand_CompletesFalseExactlyOnce() =>
        RunMountedAsync(async (_, _, wrapper) =>
        {
            var opened = OpenConfirmation(wrapper);

            opened.ViewModel.NoCommand.Execute(null);
            var result = await AwaitWithDispatcherPumpAsync(opened.Completion);
            opened.ViewModel.YesAction.Invoke();
            Dispatcher.UIThread.RunJobs();

            using (Assert.Multiple())
            {
                await Assert.That(result).IsFalse();
                await Assert.That(await opened.Completion).IsFalse();
                await Assert.That(opened.Session.IsEnded).IsTrue();
            }
        });

    [Test]
    public Task ConfirmAsync_ProgrammaticClose_CompletesFalseExactlyOnce() =>
        RunMountedAsync(async (_, _, wrapper) =>
        {
            var opened = OpenConfirmation(wrapper);

            opened.Session.Close(false);
            var result = await AwaitWithDispatcherPumpAsync(opened.Completion);
            opened.ViewModel.YesAction.Invoke();
            Dispatcher.UIThread.RunJobs();

            using (Assert.Multiple())
            {
                await Assert.That(result).IsFalse();
                await Assert.That(await opened.Completion).IsFalse();
                await Assert.That(opened.Session.IsEnded).IsTrue();
            }
        });

    [Test]
    public Task ConfirmAsync_ClickAway_CompletesFalseExactlyOnce() =>
        RunMountedAsync(async (window, _, wrapper) =>
        {
            var opened = OpenConfirmation(wrapper);

            window.MouseDown(new Point(2, 2), MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            window.MouseUp(new Point(2, 2), MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            var result = await AwaitWithDispatcherPumpAsync(opened.Completion);
            opened.ViewModel.YesAction.Invoke();
            Dispatcher.UIThread.RunJobs();

            using (Assert.Multiple())
            {
                await Assert.That(result).IsFalse();
                await Assert.That(await opened.Completion).IsFalse();
                await Assert.That(opened.Session.IsEnded).IsTrue();
            }
        });

    [Test]
    public Task ConfirmAsync_SecondDialogInfrastructureFailure_PropagatesExceptionWithoutHanging() =>
        RunMountedAsync(async (_, _, wrapper) =>
        {
            var first = OpenConfirmation(wrapper);
            var second = wrapper.ConfirmAsync("Second confirmation", "Must fail while the host is occupied");

            await WaitForCompletionAsync(second);
            await Assert.That(async () => await second).Throws<InvalidOperationException>();

            first.Session.Close(false);
            await Assert.That(await AwaitWithDispatcherPumpAsync(first.Completion)).IsFalse();
        });

    private static async Task RunMountedAsync(
        Func<Window, DialogHost, NotificationManagerWrapper, Task> test)
    {
        await using var headlessSession = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await headlessSession.DispatchAsync(async () =>
        {
            Window? window = null;
            try
            {
                var mainScreen = new MainScreen();
                window = new Window
                {
                    Width = 1200,
                    Height = 800,
                    Content = mainScreen
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var dialogHost = mainScreen.GetVisualDescendants()
                    .OfType<DialogHost>()
                    .Single(host => string.Equals(host.Identifier, "Ask", StringComparison.Ordinal));
                dialogHost.DisableOpeningAnimation = true;

                await test(window, dialogHost, new NotificationManagerWrapper(null));
            }
            finally
            {
                CloseAnyOpenConfirmationDialogs();
                window?.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }, CancellationToken.None);
    }

    private static OpenedConfirmation OpenConfirmation(NotificationManagerWrapper wrapper)
    {
        var completion = wrapper.ConfirmAsync("Confirm status change", "Apply the requested transition?");
        DialogSession? dialogSession = null;
        var opened = WaitFor(() =>
        {
            dialogSession = DialogHost.GetDialogSession("Ask");
            return dialogSession is { IsEnded: false };
        });

        if (!opened || dialogSession?.Content is not AskViewModel viewModel)
        {
            throw new InvalidOperationException("The mounted Ask dialog did not open with AskViewModel content.");
        }

        return new OpenedConfirmation(completion, dialogSession, viewModel);
    }

    private static async Task<bool> AwaitWithDispatcherPumpAsync(Task<bool> task)
    {
        await WaitForCompletionAsync(task);
        return await task;
    }

    private static Task WaitForCompletionAsync(Task task)
    {
        if (!WaitFor(() => task.IsCompleted))
        {
            throw new TimeoutException("Confirmation did not complete within the headless UI timeout.");
        }

        return Task.CompletedTask;
    }

    private static bool WaitFor(Func<bool> predicate, int timeoutMilliseconds = 3000) =>
        SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return predicate();
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));

    private static void CloseAnyOpenConfirmationDialogs()
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var session = DialogHost.GetDialogSession("Ask");
            if (session is null)
            {
                return;
            }

            if (!session.IsEnded)
            {
                session.Close(false);
            }

            Dispatcher.UIThread.RunJobs();
        }
    }

    private sealed record OpenedConfirmation(
        Task<bool> Completion,
        DialogSession Session,
        AskViewModel ViewModel);
}
