using AppAutomation.FlaUI.Automation;
using AppAutomation.FlaUI.Session;
using AppAutomation.TUnit;
using FlaUI.Core.Definitions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;
using Unlimotion.UiTests.Authoring.Tests;

namespace Unlimotion.UiTests.FlaUI.Tests;

[InheritsTests]
public sealed class MainWindowFlaUiTests
    : MainWindowScenariosBase<MainWindowFlaUiTests.FlaUiRuntimeSession>
{
    protected override FlaUiRuntimeSession LaunchSession()
    {
        var session = DesktopAppSession.Launch(
            UnlimotionAppLaunchHost.CreateDesktopLaunchOptions(
                mainWindowTimeout: TimeSpan.FromSeconds(90)));

        session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
        session.MainWindow.Focus();

        return new FlaUiRuntimeSession(session);
    }

    protected override MainWindowPage CreatePage(FlaUiRuntimeSession session)
    {
        return new MainWindowPage(
            new FlaUiControlResolver(session.Inner.MainWindow, session.Inner.ConditionFactory));
    }

    public sealed class FlaUiRuntimeSession : IUiTestSession
    {
        public FlaUiRuntimeSession(DesktopAppSession inner)
        {
            Inner = inner;
        }

        public DesktopAppSession Inner { get; }

        public void Dispose()
        {
            Inner.Dispose();
        }
    }
}
