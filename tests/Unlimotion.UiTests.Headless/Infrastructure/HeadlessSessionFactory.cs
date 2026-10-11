using System.Collections.Concurrent;
using System.Reflection;
using Avalonia;
using Avalonia.Headless;

namespace Unlimotion.UiTests.Headless.Infrastructure;

internal static class HeadlessSessionFactory
{
    // Avalonia.Headless 12.0.4 publishes a session from Task.Run before assigning
    // its worker Task. Keep the pinned factory semantics, but assign a cold Task
    // before starting it, so the framework's DisposeAsync can join the worker.
    internal static HeadlessUnitTestSession StartNew(Type entryPointType,
        AvaloniaTestIsolationLevel isolationLevel = AvaloniaTestIsolationLevel.PerTest)
    {
        var constructor = typeof(HeadlessUnitTestSession).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            [typeof(AppBuilder), typeof(CancellationTokenSource),
                typeof(BlockingCollection<(Action, ExecutionContext?)>), typeof(Task), typeof(bool)], null)
            ?? throw new NotSupportedException("Pinned Avalonia Headless session constructor is unavailable.");
        // This overload is runtime-visible but omitted from Avalonia 12's ref assembly.
        var configure = typeof(AppBuilder).GetMethod("Configure",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null, [typeof(Type)], null)
            ?? throw new NotSupportedException("Pinned Avalonia AppBuilder.Configure(Type) is unavailable.");
        var ready = new TaskCompletionSource<HeadlessUnitTestSession>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellation = new CancellationTokenSource();
        var queue = new BlockingCollection<(Action, ExecutionContext?)>();
        Task worker = null!;
        worker = new Task(() =>
        {
            try
            {
                var builder = (AppBuilder)configure.Invoke(null, [entryPointType])!;
                if (builder.WindowingSubsystemName != "Headless")
                    builder = builder.UseHeadless(new AvaloniaHeadlessPlatformOptions());
                if (builder.TextShapingSubsystemInitializer is null)
                    builder = builder.UseHarfBuzz();
                ready.SetResult((HeadlessUnitTestSession)constructor.Invoke(
                    [builder, cancellation, queue, worker, isolationLevel == AvaloniaTestIsolationLevel.PerTest]));
            }
            catch (Exception exception)
            {
                ready.SetException(exception);
                return;
            }

            while (!cancellation.IsCancellationRequested)
            {
                Action? currentAction = null;
                try
                {
                    var (action, context) = queue.Take(cancellation.Token);
                    currentAction = action;
                    if (context is null) action();
                    else ExecutionContext.Run(context, state => ((Action)state!)(), action);
                }
                catch (OperationCanceledException) when (currentAction is null && cancellation.IsCancellationRequested) { }
                catch (Exception exception)
                {
                    // Framework initialization/disposal can throw outside Dispatch's
                    // TCS catch. Preserve the worker fault, but release its orphaned
                    // callers and reject further dispatch into this terminal session.
                    var cleanupErrors = new List<Exception>();
                    void Cleanup(Action cleanup)
                    {
                        try { cleanup(); }
                        catch (Exception secondary) { cleanupErrors.Add(secondary); }
                    }
                    Cleanup(cancellation.Cancel);
                    Cleanup(queue.CompleteAdding);
                    Cleanup(() => FailFrameworkDispatch(currentAction, exception));
                    Cleanup(() =>
                    {
                        while (queue.TryTake(out var pending))
                            Cleanup(() => FailFrameworkDispatch(pending.Item1, exception));
                    });
                    if (cleanupErrors.Count > 0)
                        exception.Data["HeadlessWorkerCleanupErrors"] = new AggregateException(cleanupErrors);
                    throw;
                }
            }
        }, CancellationToken.None, TaskCreationOptions.DenyChildAttach |
            TaskCreationOptions.RunContinuationsAsynchronously);
        worker.Start(TaskScheduler.Default);
        try { return ready.Task.GetAwaiter().GetResult(); }
        catch
        {
            // Startup never entered the queue loop; preserve the primary failure
            // after the cold worker exits, then release our unpublished resources.
            worker.GetAwaiter().GetResult();
            cancellation.Dispose();
            queue.Dispose();
            throw;
        }
    }

    private static void FailFrameworkDispatch(Action? action, Exception exception)
    {
        // Pinned Avalonia 12.0.4 DispatchCore closure. Arbitrary actions (including
        // the worker-fault negative control) are not framework requests.
        var closure = action?.Target;
        if (closure?.GetType().DeclaringType != typeof(HeadlessUnitTestSession)) return;
        if (!closure.GetType().Name.StartsWith("<>c__DisplayClass12_0`", StringComparison.Ordinal) ||
            action!.Method.Name != "<DispatchCore>b__0")
            throw new NotSupportedException("Pinned Headless DispatchCore closure is unavailable.");
        var completion = closure.GetType().GetField("tcs",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(closure)
            ?? throw new NotSupportedException("Pinned Headless dispatch completion is unavailable.");
        var fail = completion.GetType().GetMethod("TrySetException", [typeof(Exception)])
            ?? throw new NotSupportedException("Pinned Headless dispatch failure API is unavailable.");
        fail.Invoke(completion, [exception]);
    }
}
