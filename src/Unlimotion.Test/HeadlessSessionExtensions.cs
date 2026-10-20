using System;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;

namespace Unlimotion.Test;

public static class HeadlessSessionExtensions
{
    private const string HeadlessDisposeStackFrame = "Avalonia.Headless.HeadlessUnitTestSession.DisposeAsync";
    private static readonly FieldInfo SessionWorkerField = typeof(HeadlessUnitTestSession)
        .GetField("_dispatchTask", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new NotSupportedException("Avalonia Headless worker task is unavailable.");

    internal static async Task<TResult> MonitorWorkerFailureAsync<TResult>(
        this HeadlessUnitTestSession session, Task<TResult> dispatch)
    {
        var worker = SessionWorkerField.GetValue(session) as Task;
        if (worker is not null && !dispatch.IsCompleted)
        {
            await Task.WhenAny(dispatch, worker).ConfigureAwait(false);
            if (!dispatch.IsCompleted)
            {
                // Avalonia 12 can fault while resetting its application, before
                // completing Dispatch's promise. Surface that fault instead of hanging.
                await worker.ConfigureAwait(false);
                throw new InvalidOperationException("Headless worker exited before completing Dispatch.");
            }
        }
        return await dispatch.ConfigureAwait(false);
    }

    public static Task DispatchAsync(
        this HeadlessUnitTestSession session,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        Exception? exception = null;

        return DispatchAndThrowAsync();

        async Task DispatchAndThrowAsync()
        {
            var dispatch = session.Dispatch<bool>(async () =>
            {
                using var resourceTrace = TestExecutionTrace.Resource("AvaloniaDispatcher");
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
            await session.MonitorWorkerFailureAsync(dispatch);

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
    private readonly IDisposable _resourceTrace;

    private SafeHeadlessUnitTestSession(HeadlessUnitTestSession session, IDisposable resourceTrace)
    {
        _session = session;
        _resourceTrace = resourceTrace;
    }

    public static SafeHeadlessUnitTestSession StartNew(Type appType)
    {
        var trace = TestExecutionTrace.Resource("AvaloniaHeadless");
        try { return new SafeHeadlessUnitTestSession(HeadlessUnitTestSession.StartNew(appType), trace); }
        catch { trace.Dispose(); throw; }
    }

    public Task<TResult> Dispatch<TResult>(
        Func<Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        return _session.MonitorWorkerFailureAsync(_session.Dispatch(action, cancellationToken));
    }

    public Task Dispatch(Func<Task> action, CancellationToken cancellationToken)
    {
        return _session.DispatchAsync(action, cancellationToken);
    }

    public Task DispatchAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        return _session.DispatchAsync(action, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try { await _session.DisposeIgnoringHeadlessTeardownNullReferenceAsync(); }
        finally { _resourceTrace.Dispose(); }
    }
}
