using System;
using System.Collections.Generic;
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
using Microsoft.Extensions.Configuration;
using Unlimotion;
using Unlimotion.ViewModel;
using Unlimotion.Views;
using WritableJsonConfiguration;

namespace Unlimotion.Test;

[ParallelLimiter<SharedUiStateParallelLimit>]
public class SettingsControlResponsiveUiTests
{
    [Test]
    public async Task SettingsControl_TaskOutlineClipboardCheckBoxes_PersistSettings()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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
    public async Task SettingsControl_TaskTreeExpansionStateCheckBox_PersistsSetting()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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

                var checkBox = FindControlByAutomationId<CheckBox>(
                    view,
                    "PersistTaskTreeExpansionStateCheckBox");

                await Assert.That(checkBox.IsChecked).IsFalse();

                await ClickControlAsync(window, checkBox);

                await Assert.That(settings.PersistTaskTreeExpansionState).IsTrue();
                await Assert.That(checkBox.IsChecked).IsTrue();
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
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
    public async Task SettingsControl_UpdateAutoCheckSettings_UpdateViewModelFromControls()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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

                window = CreateWindow(view, 720, 1000);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var autoCheckBox = FindControlByAutomationId<CheckBox>(view, "UpdateAutoCheckEnabledCheckBox");
                var intervalInput = FindControlByAutomationId<NumericUpDown>(view, "UpdateCheckIntervalValueInput");
                var unitComboBox = FindControlByAutomationId<ComboBox>(view, "UpdateCheckIntervalUnitComboBox");

                await Assert.That(autoCheckBox.IsChecked).IsTrue();
                await Assert.That(settings.UpdateCheckIntervalValue).IsEqualTo(1);
                await Assert.That(unitComboBox.SelectedIndex).IsEqualTo(1);

                await ClickControlAsync(window, autoCheckBox);

                await Assert.That(settings.UpdateAutoCheckEnabled).IsFalse();
                await Assert.That(settings.CanEditUpdateCheckInterval).IsFalse();

                await ClickControlAsync(window, autoCheckBox);

                intervalInput.Value = 3;
                unitComboBox.SelectedIndex = 2;
                Dispatcher.UIThread.RunJobs();

                await Assert.That(settings.UpdateAutoCheckEnabled).IsTrue();
                await Assert.That(settings.UpdateCheckIntervalValue).IsEqualTo(3);
                await Assert.That(settings.UpdateCheckIntervalUnit)
                    .IsEqualTo(ApplicationUpdateCheckIntervalUnit.Minutes);
                await Assert.That(settings.UpdateCheckInterval).IsEqualTo(TimeSpan.FromMinutes(3));
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
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
                    .Where(control => !IsTemplatePartInsideInputControl(control))
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
                var tooNarrowInputs = narrowInputs
                    .Where(control => control.Bounds.Width < 140)
                    .Select(DescribeInput)
                    .ToList();

                await Assert.That(narrowInputs.Count).IsGreaterThan(0);
                if (tooNarrowInputs.Count > 0)
                {
                    throw new InvalidOperationException(
                        "Visible settings inputs are too narrow: " +
                        string.Join("; ", tooNarrowInputs));
                }
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            var previousPlatformPicker = Dialogs.PlatformOpenFolderDialogAsync;
            Window? window = null;

            try
            {
                var settings = fixture.MainWindowViewModelTest.Settings;
                var initialPath = Path.Combine(fixture.DefaultTasksFolderPath, "Current");
                var selectedPath = Path.Combine(fixture.DefaultTasksFolderPath, "Selected");
                string? capturedDirectory = null;
                settings.TaskStoragePath = initialPath;
                Dialogs.PlatformOpenFolderDialogAsync = (_, directory) =>
                {
                    capturedDirectory = directory;
                    return Task.FromResult<string?>(selectedPath);
                };
                var browseCommandStub = new TestAsyncCommand(async () =>
                {
                    var path = await new Dialogs().ShowOpenFolderDialogAsync("Data folder", settings.TaskStoragePath);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        settings.TaskStoragePath = path;
                    }
                });
                settings.BrowseTaskStoragePathCommand = browseCommandStub;

                var view = new SettingsControl
                {
                    DataContext = settings
                };

                window = CreateWindow(view, 720, 1000);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var pathInput = FindControlByAutomationId<TextBox>(view, "TaskStoragePathTextBox");
                var browseButton = FindControlByAutomationId<Button>(view, "BrowseTaskStoragePathButton");
                var browseCommand = browseButton.Command ??
                                    throw new InvalidOperationException("Browse button command is not bound.");

                await Assert.That(pathInput.Text).IsEqualTo(initialPath);
                await Assert.That(browseCommand.CanExecute(null)).IsTrue();

                browseCommand.Execute(null);
                if (browseCommandStub.LastExecution != null)
                {
                    await browseCommandStub.LastExecution;
                }

                Dispatcher.UIThread.RunJobs();

                await TestHelpers.WaitUntilAsync(
                    () => settings.TaskStoragePath == selectedPath,
                    TimeSpan.FromSeconds(2));
                await Assert.That(capturedDirectory).IsEqualTo(initialPath);
                await Assert.That(settings.TaskStoragePath).IsEqualTo(selectedPath);
                await Assert.That(pathInput.Text).IsEqualTo(selectedPath);
            }
            finally
            {
                Dialogs.PlatformOpenFolderDialogAsync = previousPlatformPicker;
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task SettingsControl_LocalStorageConnect_UsesEditedTaskStoragePath()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var settings = fixture.MainWindowViewModelTest.Settings;
                var initialPath = Path.Combine(fixture.DefaultTasksFolderPath, "Current");
                var selectedPath = Path.Combine(fixture.DefaultTasksFolderPath, "Selected");
                string? connectedPath = null;
                settings.TaskStoragePath = initialPath;
                settings.ConnectCommand = new TestParameterCommand(_ => connectedPath = settings.TaskStoragePath);

                var view = new SettingsControl
                {
                    DataContext = settings
                };

                window = CreateWindow(view, 720, 800);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var pathInput = FindControlByAutomationId<TextBox>(view, "TaskStoragePathTextBox");
                var connectButton = FindControlByAutomationId<Button>(view, "ConnectLocalStorageButton");

                pathInput.Text = selectedPath;
                Dispatcher.UIThread.RunJobs();

                await WaitForConditionAsync(
                    () => settings.TaskStoragePath == selectedPath,
                    "Task storage path input did not update the SettingsViewModel.");
                await Assert.That(connectButton.IsEnabled).IsTrue();

                connectButton.Command?.Execute(connectButton.CommandParameter);

                await Assert.That(connectedPath).IsEqualTo(selectedPath);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task SettingsControl_SyncConflictResolutionMode_ShowsOpenResolverAction()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var configPath = Path.Combine(Environment.CurrentDirectory, $"SyncConflictSettings_{Guid.NewGuid():N}.json");
            File.WriteAllText(configPath, "{}");
            IDisposable? configurationDisposable = null;
            Window? window = null;

            try
            {
                var configuration = WritableJsonConfigurationFabric.Create(configPath);
                configurationDisposable = configuration as IDisposable;
                configuration.GetSection("Git").GetSection(nameof(GitSettings.BackupEnabled)).Set(true);
                configuration.GetSection("Git").GetSection(nameof(GitSettings.RemoteUrl)).Set("https://example.com/repo.git");

                var backupService = new FakeRemoteBackupService
                {
                    ConflictStatus = new BackupConflictStatus(
                        true,
                        new List<BackupConflictFile>
                        {
                            new(
                                "Tasks/conflicted-task.json",
                                true,
                                true,
                                new List<BackupConflictField>
                                {
                                    new(
                                        "Title",
                                        "Title",
                                        "Current title",
                                        "Incoming title",
                                        "Current title\nIncoming title",
                                        true,
                                        BackupConflictFieldSource.UseCurrent,
                                        BackupConflictFieldChangeKind.BothDifferent)
                                }),
                            new("Tasks/deleted-task.json", true, false)
                        })
                };
                var settings = new SettingsViewModel(configuration, backupService);
                var resolverOpened = false;
                settings.OpenConflictResolutionWindowCommand = new TestParameterCommand(_ => resolverOpened = true);
                settings.RefreshBackupConflictsCommand = new TestParameterCommand(_ => settings.ReloadGitMetadata());

                var view = new SettingsControl
                {
                    DataContext = settings
                };

                window = CreateWindow(view, 720, 800);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var panel = FindControlByAutomationId<Border>(view, "SyncConflictResolutionPanel");
                var openResolverButton = FindControlByAutomationId<Button>(view, "OpenConflictResolutionWindowButton");
                var syncButton = FindControlByAutomationId<Button>(view, "SyncNowButton");

                await Assert.That(panel.IsVisible).IsTrue();
                await Assert.That(syncButton.IsEnabled).IsFalse();
                await Assert.That(openResolverButton.IsEnabled).IsTrue();

                openResolverButton.Command?.Execute(openResolverButton.CommandParameter);

                await Assert.That(resolverOpened).IsTrue();
            }
            finally
            {
                window?.Close();
                configurationDisposable?.Dispose();
                if (File.Exists(configPath))
                {
                    File.Delete(configPath);
                }
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ConflictResolutionControl_ShowsFieldDecisionsAndDeleteSideAction()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var configPath = Path.Combine(Environment.CurrentDirectory, $"ConflictResolver_{Guid.NewGuid():N}.json");
            File.WriteAllText(configPath, "{}");
            IDisposable? configurationDisposable = null;
            Window? window = null;

            try
            {
                var configuration = WritableJsonConfigurationFabric.Create(configPath);
                configurationDisposable = configuration as IDisposable;
                var backupService = new FakeRemoteBackupService
                {
                    ConflictStatus = CreateDemoConflictStatus()
                };
                var settings = new SettingsViewModel(configuration, backupService);
                BackupConflictFile? incomingResolvedConflict = null;
                List<BackupConflictFieldSelection>? appliedFieldSelections = null;
                settings.ResolveConflictUseIncomingCommand = new TestParameterCommand(parameter =>
                    incomingResolvedConflict = (BackupConflictFile?)parameter ?? settings.SelectedBackupConflict);
                settings.ResolveConflictUseFieldSelectionCommand = new TestParameterCommand(_ =>
                    appliedFieldSelections = settings.GetSelectedBackupConflictFieldSelections().ToList());
                settings.RefreshBackupConflictsCommand = new TestParameterCommand(_ => settings.ReloadGitMetadata());
                settings.CommitConflictResolutionCommand = new TestParameterCommand(_ => { });

                var view = new ConflictResolutionControl
                {
                    DataContext = settings
                };

                window = CreateWindow(view, 920, 720);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var conflictList = FindControlByAutomationId<ListBox>(view, "ConflictResolutionFileList");
                var fieldPanel = FindControlByAutomationId<StackPanel>(view, "ConflictResolutionFieldPanel");
                var realConflictBadge = FindControlsByAutomationId<TextBlock>(view, "ConflictResolutionRealConflictBadge")
                    .Single(IsVisibleAndArranged);
                var mergeFieldRadio = FindControlsByAutomationId<RadioButton>(view, "ConflictResolutionMergeRadioButton")
                    .Single(IsVisibleAndArranged);
                var selectedValue = FindControlsByAutomationId<TextBlock>(view, "ConflictResolutionSelectedValueTextBlock")
                    .Single(IsVisibleAndArranged);
                var compactValue = FindControlByAutomationId<TextBlock>(view, "ConflictResolutionCompactValueTextBlock");
                var useIncomingButton = FindControlByAutomationId<Button>(view, "ConflictResolutionUseIncomingButton");
                var applyFieldsButton = FindControlByAutomationId<Button>(view, "ConflictResolutionApplyFieldsButton");
                var commitButton = FindControlByAutomationId<Button>(view, "ConflictResolutionCommitButton");

                await Assert.That(conflictList.ItemCount).IsEqualTo(2);
                await Assert.That(fieldPanel.IsVisible).IsTrue();
                await Assert.That(realConflictBadge.IsVisible).IsTrue();
                await Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Base title")).IsTrue();
                await Assert.That(compactValue.Text).IsEqualTo("task-main");
                await Assert.That(FindControlsByAutomationId<RadioButton>(view, "ConflictResolutionMergeRadioButton")
                    .Count(IsVisibleAndArranged)).IsEqualTo(1);
                await Assert.That(FindControlsByAutomationId<TextBox>(view, "ConflictResolutionEditedMergeTextBox")
                    .Count(IsVisibleAndArranged)).IsEqualTo(0);
                await Assert.That(selectedValue.Text).IsEqualTo("Current title");
                await Assert.That(applyFieldsButton.IsEnabled).IsTrue();
                await Assert.That(commitButton.IsEnabled).IsFalse();
                await Assert.That(commitButton.Classes.Contains("PrimaryConflictAction")).IsTrue();

                mergeFieldRadio.IsChecked = true;
                Dispatcher.UIThread.RunJobs();

                await Assert.That(selectedValue.IsVisible).IsFalse();
                var editedMergeTextBox = FindControlsByAutomationId<TextBox>(view, "ConflictResolutionEditedMergeTextBox")
                    .Single(IsVisibleAndArranged);
                await Assert.That(editedMergeTextBox.Text).IsEqualTo("Current title\nIncoming title");
                editedMergeTextBox.Text = "Edited merged title";
                Dispatcher.UIThread.RunJobs();

                await Assert.That(editedMergeTextBox.Text).IsEqualTo("Edited merged title");

                applyFieldsButton.Command?.Execute(applyFieldsButton.CommandParameter);

                var titleSelection = appliedFieldSelections?.Single(selection => selection.FieldPath == "Title");
                await Assert.That(titleSelection?.Source).IsEqualTo(BackupConflictFieldSource.Merge);
                await Assert.That(titleSelection?.CustomValue).IsEqualTo("Edited merged title");

                conflictList.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();

                await Assert.That(settings.SelectedBackupConflict?.Path).IsEqualTo("Tasks/deleted-task.json");
                await Assert.That(settings.SelectedBackupConflict?.HasIncomingVersion).IsFalse();
                await Assert.That(useIncomingButton.IsEnabled).IsTrue();

                useIncomingButton.Command?.Execute(useIncomingButton.CommandParameter);

                await Assert.That(incomingResolvedConflict?.Path).IsEqualTo("Tasks/deleted-task.json");
            }
            finally
            {
                window?.Close();
                configurationDisposable?.Dispose();
                if (File.Exists(configPath))
                {
                    File.Delete(configPath);
                }
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ConflictResolutionControl_UsesSingleColumnLayoutOnPhoneWidth()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var configPath = Path.Combine(Environment.CurrentDirectory, $"ConflictResolverPhone_{Guid.NewGuid():N}.json");
            File.WriteAllText(configPath, "{}");
            IDisposable? configurationDisposable = null;
            Window? window = null;

            try
            {
                var configuration = WritableJsonConfigurationFabric.Create(configPath);
                configurationDisposable = configuration as IDisposable;
                var settings = new SettingsViewModel(
                    configuration,
                    new FakeRemoteBackupService { ConflictStatus = CreateDemoConflictStatus() });
                var view = new ConflictResolutionControl
                {
                    DataContext = settings
                };

                window = CreateWindow(view, 390, 760);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var filePane = FindControlByAutomationId<Border>(view, "ConflictResolutionFilePane");
                var detailsPane = FindControlByAutomationId<Border>(view, "ConflictResolutionDetailPanel");

                await Assert.That(Grid.GetColumn(filePane)).IsEqualTo(0);
                await Assert.That(Grid.GetRow(filePane)).IsEqualTo(0);
                await Assert.That(Grid.GetColumn(detailsPane)).IsEqualTo(0);
                await Assert.That(Grid.GetRow(detailsPane)).IsEqualTo(1);
            }
            finally
            {
                window?.Close();
                configurationDisposable?.Dispose();
                if (File.Exists(configPath))
                {
                    File.Delete(configPath);
                }
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

    private static BackupConflictStatus CreateDemoConflictStatus()
    {
        return new BackupConflictStatus(
            true,
            new List<BackupConflictFile>
            {
                new(
                    "Tasks/conflicted-task.json",
                    true,
                    true,
                    new List<BackupConflictField>
                    {
                        new(
                            "Id",
                            "Id",
                            "task-main",
                            "task-main",
                            "task-main",
                            string.Empty,
                            false,
                            BackupConflictFieldSource.UseCurrent,
                            BackupConflictFieldChangeKind.Unchanged),
                        new(
                            "Title",
                            "Title",
                            "Base title",
                            "Current title",
                            "Incoming title",
                            "Current title\nIncoming title",
                            true,
                            BackupConflictFieldSource.UseCurrent,
                            BackupConflictFieldChangeKind.BothDifferent,
                            true)
                    }),
                new("Tasks/deleted-task.json", true, false)
            });
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

    private static List<T> FindControlsByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        return root.GetVisualDescendants()
            .OfType<T>()
            .Where(control => AutomationProperties.GetAutomationId(control) == automationId)
            .ToList();
    }

    private static bool IsVisibleAndArranged(Control control)
    {
        return control.IsVisible &&
               control.Bounds.Width > 0 &&
               control.Bounds.Height > 0;
    }

    private static bool IsTemplatePartInsideInputControl(Control control)
    {
        if (control is TextBox or ComboBox or NumericUpDown)
        {
            return false;
        }

        return control.GetVisualAncestors()
            .Any(ancestor => ancestor is TextBox or ComboBox or NumericUpDown);
    }

    private static string DescribeInput(Control control)
    {
        var ancestors = string.Join(">",
            control.GetVisualAncestors()
                .OfType<Control>()
                .Select(ancestor => ancestor.GetType().Name)
                .Take(6));

        return $"{control.GetType().Name}:{control.Name} " +
               $"width={control.Bounds.Width:F1} minWidth={control.MinWidth:F1} ancestors={ancestors}";
    }

    private sealed class TestAsyncCommand(Func<Task> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public Task? LastExecution { get; private set; }

        public bool CanExecute(object? parameter)
        {
            return true;
        }

        public void Execute(object? parameter)
        {
            LastExecution = execute();
        }
    }

    private sealed class TestParameterCommand(Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return true;
        }

        public void Execute(object? parameter)
        {
            execute(parameter);
        }
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

    private sealed class FakeRemoteBackupService : IRemoteBackupService
    {
        public BackupConflictStatus ConflictStatus { get; set; } = BackupConflictStatus.None;

        public List<string> Remotes() => new();
        public string? GetRemoteAuthType(string remoteName) => null;
        public string? GetRemoteUrl(string remoteName) => null;
        public RemoteConnectionTypeSwitchResult SwitchRemoteConnectionType(string remoteName, BackupAuthMode targetMode) =>
            throw new NotSupportedException();
        public List<string> Refs() => new();
        public List<string> GetSshPublicKeys() => new();
        public string GenerateSshKey(string keyName) => throw new NotSupportedException();
        public string? ReadPublicKey(string publicKeyPath) => throw new NotSupportedException();
        public BackupConflictStatus GetConflictStatus() => ConflictStatus;
        public void ResolveConflict(string path, BackupConflictResolution resolution) => throw new NotSupportedException();
        public void ResolveConflictFields(string path, IReadOnlyList<BackupConflictFieldSelection> fieldSelections) => throw new NotSupportedException();
        public void CommitResolvedConflicts(string message) => throw new NotSupportedException();
        public void Push(string msg) => throw new NotSupportedException();
        public void Pull() => throw new NotSupportedException();
        public void PullExistingRepository() => throw new NotSupportedException();
        public BackupRepositoryConnectPreview PreviewConnectRepository() => throw new NotSupportedException();
        public void ConnectRepository(bool allowMergeWithNonEmptyRemote) => throw new NotSupportedException();
        public void CloneOrUpdateRepo() => throw new NotSupportedException();
    }

}
