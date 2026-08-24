using System.Threading.Tasks;
using AppAutomation.Avalonia.Headless.Session;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;

namespace Unlimotion.UiTests.Headless.Infrastructure;

public static class HeadlessSessionHooks
{
    private static HeadlessUnitTestSession? _session;

    [Before(TestSession)]
    public static void SetupSession()
    {
        _session = HeadlessUnitTestSession.StartNew(
            UnlimotionAppLaunchHost.AvaloniaAppType,
            AvaloniaTestIsolationLevel.PerAssembly);
        HeadlessRuntime.SetSession(_session);
    }

    public static void CloseWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        void CloseCore()
        {
            window.DataContext = null;
            window.Content = null;
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            CloseCore();
        }
        else
        {
            HeadlessRuntime.Dispatch(CloseCore);
        }
    }

    [After(TestSession)]
    public static async Task CleanupSession()
    {
        HeadlessRuntime.SetSession(null);
        if (_session != null)
        {
            await _session.DisposeAsync();
        }

        _session = null;
    }
}
