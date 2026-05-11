using System.Threading.Tasks;
using AppAutomation.Avalonia.Headless.Session;
using Avalonia.Headless;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;

namespace Unlimotion.UiTests.Headless.Infrastructure;

public static class HeadlessSessionHooks
{
    private static HeadlessUnitTestSession? _session;

    [Before(TestSession)]
    public static void SetupSession()
    {
        _session = HeadlessUnitTestSession.StartNew(UnlimotionAppLaunchHost.AvaloniaAppType);
        HeadlessRuntime.SetSession(_session);
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
