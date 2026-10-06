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
                try
                {
                    var (action, context) = queue.Take(cancellation.Token);
                    if (context is null) action();
                    else ExecutionContext.Run(context, state => ((Action)state!)(), action);
                }
                catch (OperationCanceledException) { }
            }
        }, CancellationToken.None, TaskCreationOptions.DenyChildAttach);
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
}
