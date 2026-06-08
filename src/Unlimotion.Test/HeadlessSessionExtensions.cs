using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;

namespace Unlimotion.Test;

public static class HeadlessSessionExtensions
{
    private const string HeadlessDisposeStackFrame = "Avalonia.Headless.HeadlessUnitTestSession.DisposeAsync";

    public static Task DispatchAsync(
        this HeadlessUnitTestSession session,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        Exception? exception = null;

        return DispatchAndThrowAsync();

        async Task DispatchAndThrowAsync()
        {
            await session.Dispatch(async () =>
            {
                try
                {
                    await action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }

                return true;
            }, cancellationToken);

            if (exception != null)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }
        }
    }

    public static async ValueTask DisposeIgnoringHeadlessTeardownNullReferenceAsync(
        this HeadlessUnitTestSession session)
    {
        try
        {
            await session.DisposeAsync();
        }
        catch (NullReferenceException ex)
            when (ex.StackTrace?.Contains(HeadlessDisposeStackFrame, StringComparison.Ordinal) == true)
        {
        }
    }
}
