using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI;
using Unlimotion;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion.Test;

[ParallelLimiter<SharedUiStateParallelLimit>]
public class SettingsControlResponsiveUiTests
{
    [Test]
    public async Task SettingsControl_TaskOutlineClipboardCheckBoxes_PersistSettings()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var settings = fixture.MainWindowViewModelTest.Settings;
                var view = new SettingsControl
                {
                    DataContext = settings
                };

                window = CreateWindow(view, 720, 800);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var markdownCheckBox = FindControlByAutomationId<CheckBox>(view, "CopyTaskOutlineAsMarkdownCheckBox");
                var descriptionCheckBox = FindControlByAutomationId<CheckBox>(view, "CopyTaskOutlineDescriptionCheckBox");

                await Assert.That(markdownCheckBox.IsChecked).IsFalse();
                await Assert.That(descriptionCheckBox.IsChecked).IsFalse();

                await ClickControlAsync(window, markdownCheckBox);
                await ClickControlAsync(window, descriptionCheckBox);

                await Assert.That(settings.CopyTaskOutlineAsMarkdown).IsTrue();
                await Assert.That(settings.CopyTaskOutlineDescription).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task SettingsControl_UpdateSection_ShowsVersionAndDownloadsAvailableUpdate()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var settings = fixture.MainWindowViewModelTest.Settings;
                var updateService = new FakeApplicationUpdateService
                {
                    CurrentVersion = "1.2.3",
                    NextUpdate = new ApplicationUpdateInfo("1.2.4")
                };
                settings.ConfigureUpdateService(updateService);
                settings.CheckForUpdatesCommand = new TestAsyncCommand(() => settings.CheckForUpdatesAsync());
                settings.DownloadUpdateCommand = new TestAsyncCommand(() => settings.DownloadUpdateAsync());
                settings.ApplyUpdateCommand = new TestAsyncCommand(settings.ApplyUpdateAsync);

                var view = new SettingsControl
                {
                    DataContext = settings
                };

                window = CreateWindow(view, 720, 800);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var currentVersionText = FindControlByAutomationId<TextBlock>(view, "CurrentApplicationVersionText");
                var availableVersionText = FindControlByAutomationId<TextBlock>(view, "AvailableUpdateVersionText");
                var checkButton = FindControlByAutomationId<Button>(view, "CheckForUpdatesButton");
                var downloadButton = FindControlByAutomationId<Button>(view, "DownloadUpdateButton");
                var applyButton = FindControlByAutomationId<Button>(view, "ApplyUpdateButton");

                await Assert.That(currentVersionText.Text).IsEqualTo("1.2.3");
                await Assert.That(checkButton.IsEnabled).IsTrue();
                await Assert.That(downloadButton.IsEnabled).IsFalse();

                await ClickControlAsync(window, checkButton);
                await WaitForConditionAsync(
                    () => settings.UpdateState == ApplicationUpdateState.UpdateAvailable,
                    "Settings update section did not show an available update.");

                await Assert.That(availableVersionText.Text).IsEqualTo("1.2.4");
                await Assert.That(downloadButton.IsEnabled).IsTrue();

                await ClickControlAsync(window, downloadButton);
                await WaitForConditionAsync(
                    () => settings.UpdateState == ApplicationUpdateState.ReadyToApply,
                    "Settings update section did not switch to ready-to-apply after download.");

                await Assert.That(updateService.DownloadCalls).IsEqualTo(1);
                await Assert.That(applyButton.IsEnabled).IsTrue();

                const string permissionStatus = "Grant install permission, then tap Install update again.";
                updateService.ApplyException = new ApplicationUpdateUserActionRequiredException(permissionStatus);

                await ClickControlAsync(window, applyButton);
                await WaitForConditionAsync(
                    () => settings.UpdateState == ApplicationUpdateState.ReadyToApply &&
                          settings.UpdateStatusText == permissionStatus,
                    "Settings update section did not remain ready after Android install permission redirect.");

                var statusText = FindControlByAutomationId<TextBlock>(view, "UpdateStatusText");
                await Assert.That(updateService.ApplyCalls).IsEqualTo(1);
                await Assert.That(statusText.Text).IsEqualTo(permissionStatus);
                await Assert.That(applyButton.IsEnabled).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task SettingsControl_NarrowViewport_DoesNotOverflowHorizontally()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var settings = fixture.MainWindowViewModelTest.Settings;
                settings.StorageModeIndex = 1;
                settings.ServerStorageUrl = "https://server.example.com";
                settings.Login = "user@example.com";
                settings.Password = "super-secret-password";
                settings.GitBackupEnabled = true;
                settings.GitRemoteUrl = "git@github.com:unlimotion/unlimotion-backup.git";
                settings.ShowAdvancedBackupSettings = true;
                settings.ShowServiceActions = true;
                settings.GitCommitterName = "Unlimotion Backup Bot";
                settings.GitCommitterEmail = "backup@example.com";

                var view = new SettingsControl
                {
                    DataContext = settings
                };

                window = CreateWindow(view, 360, 800);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var overflowingControls = view.GetVisualDescendants()
                    .OfType<Control>()
                    .Where(IsVisibleAndArranged)
                    .Select(control => new
                    {
                        Control = control,
                        RightEdge = GetRightEdge(view, control)
                    })
                    .Where(item => item.RightEdge > view.Bounds.Width + 1)
                    .Select(item => $"{item.Control.GetType().Name}:{item.Control.Name} right={item.RightEdge:F1} width={item.Control.Bounds.Width:F1}")
                    .ToList();

                if (overflowingControls.Count > 0)
                {
                    throw new InvalidOperationException(
                        "Visible settings controls overflow the narrow viewport: " +
                        string.Join("; ", overflowingControls));
                }

                var narrowInputs = view.GetVisualDescendants()
                    .OfType<InputElement>()
                    .Where(control => control is TextBox or ComboBox or NumericUpDown)
                    .Cast<Control>()
                    .Where(IsVisibleAndArranged)
                    .ToList();

                await Assert.That(narrowInputs.Count).IsGreaterThan(0);
                await Assert.That(narrowInputs.All(control => control.Bounds.Width >= 140)).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task SettingsControl_BrowseTaskStoragePath_UpdatesPathFromFolderPicker()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            var previousPlatformPicker = Dialogs.PlatformOpenFolderDialogAsync;
            Window? window = null;

            try
            {
                var settings = fixture.MainWindowViewModelTest.Settings;
                var selectedPath = Path.Combine(fixture.DefaultTasksFolderPath, "Selected");
                Dialogs.PlatformOpenFolderDialogAsync = (_, _) => Task.FromResult<string?>(selectedPath);
                settings.BrowseTaskStoragePathCommand = ReactiveCommand.CreateFromTask(async () =>
                {
                    var path = await new Dialogs().ShowOpenFolderDialogAsync("Data folder");
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        settings.TaskStoragePath = path;
                    }
                });

                var view = new SettingsControl
                {
                    DataContext = settings
                };

                window = CreateWindow(view, 720, 800);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var browseButton = FindControlByAutomationId<Button>(view, "BrowseTaskStoragePathButton");
                await ClickControlAsync(window, browseButton);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(settings.TaskStoragePath).IsEqualTo(selectedPath);
            }
            finally
            {
                Dialogs.PlatformOpenFolderDialogAsync = previousPlatformPicker;
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    private static Window CreateWindow(Control content, double width, double height)
    {
        return new Window
        {
            Width = width,
            Height = height,
            Content = content
        };
    }

    private static async Task ClickControlAsync(Window window, Control control)
    {
        var point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            window);

        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate point for control {control.GetType().Name}.");
        }

        window.MouseDown(point.Value, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point.Value, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        await Task.CompletedTask;
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, string failureMessage)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new InvalidOperationException(failureMessage);
    }

    private static T FindControlByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        return root.GetVisualDescendants()
            .OfType<T>()
            .First(control => AutomationProperties.GetAutomationId(control) == automationId);
    }

    private static bool IsVisibleAndArranged(Control control)
    {
        return control.IsVisible &&
               control.Bounds.Width > 0 &&
               control.Bounds.Height > 0;
    }

    private static double GetRightEdge(Visual relativeTo, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width, 0), relativeTo);
        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate point for control {control.GetType().Name}.");
        }

        return point.Value.X;
    }

    private sealed class FakeApplicationUpdateService : IApplicationUpdateService
    {
        public bool IsSupported { get; set; } = true;
        public string CurrentVersion { get; set; } = "1.0.0";
        public ApplicationUpdateInfo? PendingUpdate { get; set; }
        public ApplicationUpdateInfo? NextUpdate { get; set; }
        public Exception? ApplyException { get; set; }
        public int DownloadCalls { get; private set; }
        public int ApplyCalls { get; private set; }

        public Task<ApplicationUpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(NextUpdate);

        public Task DownloadUpdateAsync(CancellationToken cancellationToken = default)
        {
            DownloadCalls++;
            return Task.CompletedTask;
        }

        public void ApplyUpdateAndRestart()
        {
            ApplyCalls++;

            if (ApplyException != null)
            {
                throw ApplyException;
            }
        }
    }

    private sealed class TestAsyncCommand : ICommand
    {
        private readonly Func<Task> _execute;

        public TestAsyncCommand(Func<Task> execute)
        {
            _execute = execute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            _ = ExecuteAsync();
        }

        private async Task ExecuteAsync()
        {
            await _execute();
        }
    }
}
