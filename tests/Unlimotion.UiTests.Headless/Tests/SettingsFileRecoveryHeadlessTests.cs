using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.Session.Contracts;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Runtime.Versioning;
using System.Text.Json;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.Services;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Localization;
using Unlimotion.Views;

namespace Unlimotion.UiTests.Headless.Tests;

[NotInParallel("DesktopUi")]
[SupportedOSPlatform("windows")]
public sealed class SettingsFileRecoveryHeadlessTests
{
    [Test]
    [Arguments(SettingsFileDamage.EmptyWithoutBackup)]
    [Arguments(SettingsFileDamage.TruncatedWithoutBackup)]
    [Arguments(SettingsFileDamage.InvalidRootWithoutBackup)]
    [Arguments(SettingsFileDamage.MissingWithInvalidBackup)]
    [Arguments(SettingsFileDamage.ValidWithRestrictedBackup)]
    public void Blocked_result_renders_only_recovery_controls_with_selectable_path(SettingsFileDamage damage)
    {
        RequireWindows();
        using var fixture = new SettingsFileRecoveryTestFixture(damage);
        var result = SettingsFileRecovery.Prepare(fixture.ConfigPath);
        if (result.Status != SettingsRecoveryStatus.Blocked)
        {
            throw new InvalidOperationException("Invalid settings without a usable backup must block startup.");
        }

        using var session = LaunchRecoveryView(result);
        HeadlessRuntime.Dispatch(() =>
        {
            var window = session.MainWindow;
            var view = window.Content as SettingsRecoveryView
                       ?? throw new InvalidOperationException("Expected the early recovery view.");
            var path = ById<SelectableTextBlock>(view, "SettingsRecoveryPath");
            if (path.Text != fixture.ConfigPath || path.TextWrapping != TextWrapping.Wrap ||
                string.IsNullOrWhiteSpace(ById<TextBlock>(view, "SettingsRecoveryMessage").Text))
            {
                throw new InvalidOperationException("The view must show an explanation and the complete selectable path.");
            }
            if (damage == SettingsFileDamage.ValidWithRestrictedBackup &&
                (result.Error != SettingsRecoveryError.AccessDenied ||
                 ById<TextBlock>(view, "SettingsRecoveryMessage").Text !=
                 LocalizationService.Current.Get("SettingsRecoveryAccessDenied")))
            {
                throw new InvalidOperationException("Different backup rights must show the permissions error, not a corrupt-JSON error.");
            }
            var buttons = view.GetVisualDescendants().OfType<Button>().ToArray();
            if (buttons.Length != 2 || !ById<Button>(view, "SettingsRecoveryOpenFolder").IsEnabled ||
                !ById<Button>(view, "SettingsRecoveryClose").IsEnabled)
            {
                throw new InvalidOperationException("Only Open folder and Close must be available.");
            }
            if (window.DataContext is MainWindowViewModel || window is MainWindow ||
                window.GetVisualDescendants().OfType<MainControl>().Any())
            {
                throw new InvalidOperationException("The recovery shell must not contain the normal task surface.");
            }
        });

        if (!SameBytes(fixture.InitialMain, SettingsFileRecoveryTestFixture.ReadOptional(fixture.ConfigPath)) ||
            !SameBytes(fixture.InitialBackup, SettingsFileRecoveryTestFixture.ReadOptional(fixture.BackupPath)) ||
            fixture.InitialTaskFiles != fixture.TaskFilesFingerprint())
        {
            throw new InvalidOperationException("Blocked recovery changed the synthetic profile.");
        }
    }

    [Test]
    [Arguments("en")]
    [Arguments("ru")]
    public void Recovery_buttons_are_localized_keyboard_focusable_and_handle_folder_failure(string language)
    {
        var previousLanguage = LocalizationService.Current.LanguageMode;
        try
        {
            HeadlessRuntime.Dispatch(() => LocalizationService.Current.SetLanguage(language));
            var configPath = Path.Combine(Path.GetTempPath(), "Unlimotion.AppAutomation", "not-created", "Settings.json");
            using var session = LaunchRecoveryView(new SettingsFileRecoveryResult(
                SettingsRecoveryStatus.Blocked, configPath, SettingsRecoveryError.InvalidSettings));
            HeadlessRuntime.Dispatch(() =>
            {
                var view = (SettingsRecoveryView)session.MainWindow.Content!;
                var openFolder = ById<Button>(view, "SettingsRecoveryOpenFolder");
                var close = ById<Button>(view, "SettingsRecoveryClose");
                if (openFolder.Content?.ToString() != (language == "ru" ? "Открыть папку" : "Open folder") ||
                    close.Content?.ToString() != (language == "ru" ? "Закрыть" : "Close") ||
                    !openFolder.Focusable || !close.Focusable)
                {
                    throw new InvalidOperationException("Recovery actions must be localized and keyboard accessible.");
                }

                string? openedFolder = null;
                view.OpenFolder = folder => openedFolder = folder;
                openFolder.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (openedFolder != Path.GetDirectoryName(configPath))
                {
                    throw new InvalidOperationException("Open folder did not use the resolved configuration directory.");
                }

                view.OpenFolder = _ => throw new IOException("synthetic-secret-do-not-display");
                openFolder.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var error = ById<TextBlock>(view, "SettingsRecoveryFolderError");
                if (!error.IsVisible || string.IsNullOrWhiteSpace(error.Text) ||
                    error.Text.Contains("synthetic-secret-do-not-display", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Folder failures must be visible without raw exception contents.");
                }

                view.OpenFolder = _ => { };
                openFolder.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (error.IsVisible)
                {
                    throw new InvalidOperationException("Successful retry must clear the folder error.");
                }

                var closed = false;
                session.MainWindow.Closed += (_, _) => closed = true;
                close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!closed)
                {
                    throw new InvalidOperationException("Close did not close the recovery window.");
                }
            });
        }
        finally
        {
            HeadlessRuntime.Dispatch(() => LocalizationService.Current.SetLanguage(previousLanguage));
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Recovered_configuration_keeps_the_original_task_projection_in_headless_shell(bool truncated)
    {
        RequireWindows();
        SettingsFileRecoveryResult? result = null;
        string? expectedTaskPath = null;
        byte[]? originalDamage = null;
        // This exercises the production recovery helper with the semantic UI harness.
        // The companion FlaUI tests cover App bootstrap and its one-time warning in a real process.
        using var session = DesktopAppSession.Launch(UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
            prepareConfiguration: path =>
            {
                using var json = JsonDocument.Parse(File.ReadAllBytes(path));
                expectedTaskPath = json.RootElement.GetProperty("TaskStorage").GetProperty("Path").GetString();
                File.Copy(path, path + ".bak");
                originalDamage = truncated ? "{\"TaskStorage\":"u8.ToArray() : [];
                File.WriteAllBytes(path, originalDamage);
                result = SettingsFileRecovery.Prepare(path);
            }));

        HeadlessRuntime.Dispatch(() =>
        {
            var vm = session.MainWindow.DataContext as MainWindowViewModel
                     ?? throw new InvalidOperationException("Expected a normal task window after recovery.");
            if (result?.Status != SettingsRecoveryStatus.Restored ||
                vm.CurrentTaskItem?.Id != UnlimotionAppLaunchHost.CurrentTaskId ||
                vm.Settings.TaskStoragePath != expectedTaskPath ||
                !File.ReadAllBytes(result.PreservedPath!).SequenceEqual(originalDamage!))
            {
                throw new InvalidOperationException("Recovery did not retain the original profile and task projection.");
            }
        });
    }

    private static DesktopAppSession LaunchRecoveryView(SettingsFileRecoveryResult result)
    {
        Window? window = null;
        return DesktopAppSession.Launch(new HeadlessAppLaunchOptions
        {
            CreateMainWindow = () =>
            {
                window = new Window
                {
                    Width = 420,
                    Height = 420,
                    Content = new SettingsRecoveryView(result)
                };
                // The semantic AppAutomation session constructs a window but does not show it.
                // These tests inspect rendered controls, so instantiate the templates and layout.
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                return window;
            },
            DisposeCallback = () => HeadlessRuntime.Dispatch(() => window?.Close())
        });
    }

    private static T ById<T>(Control root, string id) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => AutomationProperties.GetAutomationId(control) == id);

    private static bool SameBytes(byte[]? expected, byte[]? actual) =>
        expected == null ? actual == null : actual != null && expected.SequenceEqual(actual);

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Crash-safe settings recovery is enabled only on Windows in this version.");
        }
    }
}
