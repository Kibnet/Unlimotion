using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;

namespace Unlimotion.Test;

public static class HeadlessSessionExtensions
{
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
}
