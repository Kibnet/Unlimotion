using AppAutomation.Avalonia.Headless.Session;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Unlimotion.UiTests.Headless.Infrastructure;

/// <summary>Attaches controls to a headless visual tree for user-facing enabled-state checks.</summary>
internal sealed class HeadlessWindowPresentation : IDisposable
{
    private readonly Window _window;

    public HeadlessWindowPresentation(Window window)
    {
        _window = window;
        HeadlessRuntime.Dispatch(() =>
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        });
    }

    public void Dispose() => HeadlessRuntime.Dispatch(_window.Close);
}
