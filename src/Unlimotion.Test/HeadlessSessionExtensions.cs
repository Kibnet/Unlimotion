using System;
using System.Linq;
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
            when (IsKnownHeadlessDisposeNullReference(ex))
        {
        }
    }

    private static bool IsKnownHeadlessDisposeNullReference(Exception ex)
    {
        var firstFrame = ex.StackTrace?
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return firstFrame?.Contains(HeadlessDisposeStackFrame, StringComparison.Ordinal) == true;
    }
}

public sealed class SafeHeadlessUnitTestSession : IAsyncDisposable
{
    private readonly HeadlessUnitTestSession _session;

    private SafeHeadlessUnitTestSession(HeadlessUnitTestSession session)
    {
        _session = session;
    }

    public static SafeHeadlessUnitTestSession StartNew(Type appType)
    {
        return new SafeHeadlessUnitTestSession(HeadlessUnitTestSession.StartNew(appType));
    }

    public Task<TResult> Dispatch<TResult>(
        Func<Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        return _session.Dispatch(action, cancellationToken);
    }

    public Task Dispatch(Func<Task> action, CancellationToken cancellationToken)
    {
        return _session.Dispatch(action, cancellationToken);
    }

    public Task DispatchAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        return _session.DispatchAsync(action, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return _session.DisposeIgnoringHeadlessTeardownNullReferenceAsync();
    }
}
