using System;
using System.Reflection;
using System.Threading.Tasks;
using AppAutomation.Avalonia.Headless.Session;
using Avalonia.Headless;
using Avalonia;
using Avalonia.Threading;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;

namespace Unlimotion.UiTests.Headless.Infrastructure;

public static class HeadlessSessionHooks
{
    private static HeadlessUnitTestSession? _session;
    private static IDisposable? _recoveryScope;

    [Before(TestSession)]
    public static void SetupSession()
    {
        _session = HeadlessUnitTestSession.StartNew(UnlimotionAppLaunchHost.AvaloniaAppType);
        HeadlessRuntime.SetSession(_session);
    }

    [BeforeEvery(Test)]
    public static async Task SetupRecoverySession(TestContext context)
    {
        if (!IsRecoveryTest(context)) return;
        // Only this multi-step asynchronous flow needs a persistent dispatcher. Keep
        // the normal PerTest isolation for all other flows and own the persistent root.
        try
        {
            await DisposeCurrentSession();
            // Avalonia 12 hides this pinned Headless lifecycle API from its ref assembly.
            // Its own PerTest session uses the same scope; keep the adapter test-only.
            _recoveryScope = typeof(AvaloniaLocator).GetMethod("EnterScope", BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, null) as IDisposable
                ?? throw new NotSupportedException("Avalonia Headless locator scope API was not available.");
            _session = HeadlessUnitTestSession.StartNew(typeof(RecoveryHeadlessEntryPoint),
                AvaloniaTestIsolationLevel.PerAssembly);
            HeadlessRuntime.SetSession(_session);
            // Establish the dispatcher on its worker before VM timers can access it.
            // Block only this hook caller, so Avalonia's inline Task completion
            // cannot run the next synchronous test launch on its dispatcher worker.
            _session.Dispatch(() => true, default)
                .WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
        }
        catch
        {
            await RestoreDefaultSession();
            throw;
        }
    }

    [AfterEvery(Test)]
    public static async Task CleanupRecoverySession(TestContext context)
    {
        if (IsRecoveryTest(context) && _recoveryScope is not null) await RestoreDefaultSession();
    }

    private static bool IsRecoveryTest(TestContext context) =>
        context.Metadata.TestName == Tests.MainWindowHeadlessTests.StatusRecoveryTestName &&
        context.Metadata.TestDetails.Class.ClassType == typeof(Tests.MainWindowHeadlessTests);

    private sealed class RecoveryHeadlessEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp()
        {
            var builder = AppBuilder.Configure<global::Unlimotion.App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions());
            var initialize = builder.WindowingSubsystemInitializer
                ?? throw new InvalidOperationException("Headless initializer was not available.");
            return builder.UseWindowingSubsystem(() =>
            {
                // PerAssembly omits PerTest's reset. Perform it on the new worker,
                // immediately before the renderer captures its UI dispatcher.
                var reset = typeof(Dispatcher).GetMethod("ResetBeforeUnitTests",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new NotSupportedException("Avalonia Headless dispatcher reset API was not available.");
                reset.Invoke(null, null);
                Dispatcher.UIThread.VerifyAccess();
                initialize();
            }, "Headless");
        }
    }

    private static async Task RestoreDefaultSession()
    {
        try { await DisposeCurrentSession(); }
        finally
        {
            _recoveryScope?.Dispose();
            _recoveryScope = null;
            SetupSession();
            // PerAssembly disposal stops its worker without resetting Avalonia globals.
            // A normal PerTest dispatch supplies that reset before the next app fixture.
            _session!.Dispatch(() => true, default)
                .WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
        }
    }

    private static async Task DisposeCurrentSession()
    {
        HeadlessRuntime.SetSession(null);
        var session = _session;
        _session = null;
        if (session is not null) await session.DisposeAsync();
    }

    [After(TestSession)]
    public static async Task CleanupSession()
    {
        try { await DisposeCurrentSession(); }
        finally { _recoveryScope?.Dispose(); _recoveryScope = null; }
    }
}
