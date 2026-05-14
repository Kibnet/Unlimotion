using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TUnit;
using ReactiveUI;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;
using Unlimotion.ViewModel;

namespace Unlimotion.UiTests.Headless.Tests;

public sealed class SettingsRemoteTypeHeadlessTests
    : UiTestBase<MainWindowHeadlessTests.HeadlessRuntimeSession, MainWindowPage>
{
    private MainWindowViewModel? _vm;

    protected override MainWindowHeadlessTests.HeadlessRuntimeSession LaunchSession()
    {
        return new MainWindowHeadlessTests.HeadlessRuntimeSession(
            DesktopAppSession.Launch(
                UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
                    UnlimotionAutomationScenario.GitRemoteSwitch,
                    afterViewModelPrepared: vm => _vm = vm)));
    }

    protected override MainWindowPage CreatePage(MainWindowHeadlessTests.HeadlessRuntimeSession session)
    {
        return new MainWindowPage(new HeadlessControlResolver(session.Inner.MainWindow));
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Settings_remote_type_switch_creates_ssh_copy_for_single_http_remote()
    {
        Page.SelectTabItem(static page => page.SettingsTabItem, timeoutMs: 10_000);
        _ = WaitUntil(
            () => TryResolveDuringWait(() => Page.SettingsRoot),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Settings root did not become available.")!;

        Page.SetChecked(static page => page.BackupAutoCheckBox, true);
        var tokenSection = WaitUntil(
            () => TryResolveDuringWait(() => Page.TokenAuthSection),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Token auth section did not become available for HTTP remote.")!;
        var sshButton = WaitUntil(
            () => TryResolveDuringWait(() => Page.SwitchRemoteToSshButton),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "SSH remote switch button did not become available.")!;
        _ = WaitUntil(
            () => _vm?.Settings.CanSwitchRemoteConnectionType == true,
            static canSwitch => canSwitch,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Remote connection type switch did not become enabled.")!;
        Page.WaitUntilIsEnabled(static page => page.SwitchRemoteToSshButton, true, 10_000);
        var vm = _vm ?? throw new InvalidOperationException("ViewModel was not captured.");
        await Assert.That(vm.Settings.GitRemoteName).IsEqualTo("origin");
        await Assert.That(vm.Settings.GitRemoteUrl).IsEqualTo("https://github.com/org/unlimotion-backup.git");
        await Assert.That(vm.Settings.SwitchRemoteToSshCommand?.CanExecute(null)).IsTrue();

        var commandErrors = new List<Exception>();
        using var commandErrorSubscription =
            (vm.Settings.SwitchRemoteToSshCommand as IReactiveCommand)?.ThrownExceptions.Subscribe(commandErrors.Add);
        vm.Settings.SwitchRemoteToSshCommand!.Execute(null);

        var sshSection = WaitUntil(
            () => TryResolveDuringWait(() => Page.SshKeysSection),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "SSH keys section did not become available after switching remote type.")!;
        var selectedRemoteUrl = WaitUntil(
            () =>
            {
                return new RemoteSwitchWaitState(
                    _vm?.Settings.GitRemoteName,
                    _vm?.Settings.GitRemoteUrl,
                    _vm?.Settings.IsSshAuthSelected,
                    commandErrors.FirstOrDefault());
            },
            static state => state.Error is not null ||
                            string.Equals(state.SelectedRemoteName, "origin-ssh", StringComparison.Ordinal) &&
                            string.Equals(
                                state.SelectedRemoteUrl,
                                "git@github.com:org/unlimotion-backup.git",
                                StringComparison.Ordinal) &&
                            state.IsSshAuthSelected == true,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Selected Git remote state was not updated after switching remote type.")!;
        if (selectedRemoteUrl.Error is not null)
        {
            throw selectedRemoteUrl.Error;
        }

        using (Assert.Multiple())
        {
            await Assert.That(tokenSection.AutomationId).IsEqualTo("TokenAuthSection");
            await Assert.That(sshButton.AutomationId).IsEqualTo("SwitchRemoteToSshButton");
            await Assert.That(sshSection.AutomationId).IsEqualTo("SshKeysSection");
            await Assert.That(selectedRemoteUrl.SelectedRemoteName).IsEqualTo("origin-ssh");
            await Assert.That(selectedRemoteUrl.IsSshAuthSelected).IsTrue();
            await Assert.That(selectedRemoteUrl.SelectedRemoteUrl).IsEqualTo("git@github.com:org/unlimotion-backup.git");
            await Assert.That(vm.Settings.BackupConnectionState).IsEqualTo(BackupStatusState.NotConfigured);
            await Assert.That(vm.Settings.BackupStatusText).IsEqualTo("Select an SSH key.");
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Settings_refresh_metadata_fills_empty_remote_url_from_current_local_storage()
    {
        Page.SelectTabItem(static page => page.SettingsTabItem, timeoutMs: 10_000);
        _ = WaitUntil(
            () => TryResolveDuringWait(() => Page.SettingsRoot),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Settings root did not become available.")!;

        Page.SetChecked(static page => page.BackupAutoCheckBox, true);
        var refreshButton = WaitUntil(
            () => TryResolveDuringWait(() => Page.RefreshGitMetadataButton),
            static control => control is not null,
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Refresh Git metadata button did not become available.")!;
        _vm!.Settings.TaskStoragePath = string.Empty;
        _vm.Settings.GitRemoteName = null;
        _vm.Settings.GitRemoteUrl = string.Empty;
        await Assert.That(_vm.Settings.RefreshGitMetadataCommand?.CanExecute(null)).IsTrue();

        _vm.Settings.RefreshGitMetadataCommand!.Execute(null);

        var selectedRemote = WaitUntil(
            () => new RemoteMetadataWaitState(_vm?.Settings.GitRemoteName, _vm?.Settings.GitRemoteUrl),
            static state => string.Equals(state.RemoteName, "origin", StringComparison.Ordinal) &&
                            string.Equals(
                                state.RemoteUrl,
                                "https://github.com/org/unlimotion-backup.git",
                                StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            timeoutMessage: "Git remote metadata was not loaded from current local storage.")!;

        using (Assert.Multiple())
        {
            await Assert.That(refreshButton.AutomationId).IsEqualTo("RefreshGitMetadataButton");
            await Assert.That(selectedRemote.RemoteName).IsEqualTo("origin");
            await Assert.That(selectedRemote.RemoteUrl).IsEqualTo("https://github.com/org/unlimotion-backup.git");
        }
    }

    private sealed record RemoteSwitchWaitState(
        string? SelectedRemoteName,
        string? SelectedRemoteUrl,
        bool? IsSshAuthSelected,
        Exception? Error);

    private sealed record RemoteMetadataWaitState(string? RemoteName, string? RemoteUrl);

    private static TControl? TryResolveDuringWait<TControl>(Func<TControl> resolve)
        where TControl : class
    {
        try
        {
            return resolve();
        }
        catch
        {
            return null;
        }
    }
}
