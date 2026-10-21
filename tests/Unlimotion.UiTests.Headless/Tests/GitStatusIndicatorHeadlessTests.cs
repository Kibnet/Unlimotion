using System.Diagnostics;
using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.TUnit;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Unlimotion.Views;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Headless.Infrastructure;
using Unlimotion.Services;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Localization;
using Unlimotion.UiTests.Authoring.Pages;

namespace Unlimotion.UiTests.Headless.Tests;

public sealed class GitStatusIndicatorHeadlessTests
{
    private const string ChildFlag = "UNLIMOTION_GIT_INDICATOR_HEADLESS_CHILD";

    [Test]
    public async Task GitFixtureCommit_DoesNotTrackTransientStorageLock()
    {
        var directory = Directory.CreateTempSubdirectory("Unlimotion.GitFixtureCommit-");
        try
        {
            Repository.Init(directory.FullName);
            File.WriteAllText(Path.Combine(directory.FullName, "task.json"), "{}");
            var lockPath = Path.Combine(directory.FullName, ".unlimotion.lock");
            File.WriteAllText(lockPath, "temporary");
            Commit(directory.FullName);
            using var repo = new Repository(directory.FullName);
            await Assert.That(repo.Index[".unlimotion.lock"]).IsNull();
            await Assert.That(repo.Head.Tip.Tree["task.json"]).IsNotNull();
            File.Delete(lockPath);
            await Assert.That(repo.RetrieveStatus().IsDirty).IsFalse();
        }
        finally
        {
            // LibGit2 writes read-only objects on Windows; cleanup must not mask
            // the assertion about the index. This is only this test's new temp repo.
            foreach (var file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
                file.Attributes &= ~FileAttributes.ReadOnly;
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task GitIndicator_SettingsShareInformationAndCheckboxControlsVisibility()
    {
        if (await RunRenderedChildIfNeeded("/*/*/GitStatusIndicatorHeadlessTests/GitIndicator_SettingsShareInformationAndCheckboxControlsVisibility")) return;
        var repositoryPath = "";
        using var session = DesktopAppSession.Launch(UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
            afterViewModelPrepared: prepared =>
            {
                repositoryPath = ((FileStorage)prepared.taskRepository!.TaskTreeManager.Storage).Path;
                Repository.Init(repositoryPath);
                Commit(repositoryPath);
            }));
        var window = session.MainWindow;
        using var presentation = new HeadlessWindowPresentation(window);
        var vm = HeadlessRuntime.Dispatch(() => (MainWindowViewModel)window.DataContext!);
        var page = new MainWindowPage(new HeadlessControlResolver(window));
        var directory = Path.GetFullPath(Path.Combine("artifacts", "git-indicator", "settings-rendered", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        var languageBefore = LocalizationService.Current.LanguageMode;
        try
        {
            HeadlessRuntime.Dispatch(() => { window.Width = 800; window.Height = 600; });
            HeadlessRuntime.Dispatch(() => window.GetVisualDescendants().OfType<ToggleButton>()
                .Single(control => control.Content?.ToString() == "↔️").IsChecked = true);
            page.SelectTabItem(static p => p.SettingsTabItem, timeoutMs: 10_000);
            var indicator = HeadlessRuntime.Dispatch(() => window.GetVisualDescendants().OfType<Button>()
                .Single(control => AutomationProperties.GetAutomationId(control) == "GitStatusIndicator"));
            var information = HeadlessRuntime.Dispatch(() => window.GetVisualDescendants().OfType<TextBlock>()
                .Single(control => AutomationProperties.GetAutomationId(control) == "GitSyncInformationText"));
            await Assert.That(HeadlessRuntime.Dispatch(() => ReferenceEquals(vm.GitStatus, vm.Settings.GitStatus))).IsTrue();
            await Wait(() => !indicator.IsVisible && information.Text == Localization.Get("GitIndicatorDisabled"));
            page.SetChecked(static p => p.BackupAutoCheckBox, true);
            await Wait(() => indicator.IsVisible && vm.GitStatus.LastKnown != null && !vm.GitStatus.IsStale);
            var save = HeadlessRuntime.Dispatch(() =>
            {
                vm.CurrentTaskItem!.Title += " saved with sync enabled";
                return vm.taskRepository!.Update(vm.CurrentTaskItem);
            });
            await Wait(() => save.IsCompleted);
            await save;
            await Wait(() => vm.GitStatus.HasDirty && !vm.GitStatus.IsStale && information.Text == vm.GitStatus.Details);
            foreach (var language in new[] { "ru", "en" })
            foreach (var dark in new[] { false, true })
            {
                HeadlessRuntime.Dispatch(() =>
                {
                    Localization.SetLanguage(language);
                    Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                });
                await Wait(() => information.Text == vm.GitStatus.Details);
                await ShowInformation(information);
                Console.WriteLine("Git settings screenshot: " + session.CaptureScreenshot(
                    Path.Combine(directory, $"settings-enabled-{language}-{(dark ? "dark" : "light")}.png")));
                page.SetChecked(static p => p.BackupAutoCheckBox, false);
                await Wait(() => !indicator.IsVisible && information.Text == Localization.Get("GitIndicatorDisabled"));
                await Assert.That(HeadlessRuntime.Dispatch(() => indicator.DesiredSize.Width)).IsEqualTo(0);
                await ShowInformation(information);
                Console.WriteLine("Git settings screenshot: " + session.CaptureScreenshot(
                    Path.Combine(directory, $"settings-disabled-{language}-{(dark ? "dark" : "light")}.png")));
                page.SetChecked(static p => p.BackupAutoCheckBox, true);
                await Wait(() => vm.GitStatus.HasDirty && !vm.GitStatus.IsStale);
            }
            var sourceId = HeadlessRuntime.Dispatch(() => vm.GitStatus.LastKnown!.Request.SourceId);
            var operation = Guid.NewGuid();
            HeadlessRuntime.Dispatch(() =>
            {
                vm.GitStatus.ApplyDiagnostic(new(sourceId, repositoryPath, operation, true));
                indicator.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Dispatcher.UIThread.RunJobs();
            });
            await Assert.That(HeadlessRuntime.Dispatch(() => indicator.Flyout!.IsOpen)).IsTrue();
            // Programmatic config change while the popup is open must also close it safely.
            HeadlessRuntime.Dispatch(() => vm.Settings.GitBackupEnabled = false);
            await Wait(() => !indicator.IsVisible && !indicator.Flyout!.IsOpen && !indicator.IsFocused);
            await Assert.That(HeadlessRuntime.Dispatch(() => vm.GitStatus.IsBusy)).IsTrue();
            page.SetChecked(static p => p.BackupAutoCheckBox, true);
            await Wait(() => indicator.IsVisible && vm.GitStatus.State == GitIndicatorState.Syncing &&
                information.Text == vm.GitStatus.Details && !vm.GitStatus.IsStale);
            await ShowInformation(information);
            Console.WriteLine("Git settings screenshot: " + session.CaptureScreenshot(Path.Combine(directory, "settings-syncing.png")));
            HeadlessRuntime.Dispatch(() => vm.GitStatus.ApplyDiagnostic(new(sourceId, repositoryPath, operation, false, BackupOperationResult.Failed)));
            await Wait(() => vm.GitStatus.State == GitIndicatorState.Attention &&
                information.Text!.Contains(Localization.Get("GitIndicatorOperationError")));
            await ShowInformation(information);
            Console.WriteLine("Git settings screenshot: " + session.CaptureScreenshot(Path.Combine(directory, "settings-operation-error.png")));
            HeadlessRuntime.Dispatch(() => vm.GitStatus.SetSource("server-fixture", "", false));
            await Wait(() => !indicator.IsVisible && information.Text == Localization.Get("GitIndicatorServer"));
            await ShowInformation(information);
            Console.WriteLine("Git settings screenshot: " + session.CaptureScreenshot(Path.Combine(directory, "settings-server.png")));
            // Rebind the live owner, then verify both transfer and DataContext detach with an open popup.
            HeadlessRuntime.Dispatch(() => vm.ConfigureGitStatus(new GitStatusViewModel(new GitWorktreeObserver(),
                action => Dispatcher.UIThread.Post(action))));
            await Wait(() => vm.GitStatus.HasDirty && vm.GitStatus.IsIndicatorVisible);
            HeadlessRuntime.Dispatch(() =>
            {
                indicator.Flyout!.ShowAt(indicator);
                vm.ConfigureGitStatus(new GitStatusViewModel());
            });
            await Wait(() => !indicator.Flyout!.IsOpen && !indicator.IsFocused &&
                ReferenceEquals(vm.GitStatus, vm.Settings.GitStatus));
            // Source binding transfers too: the replacement observes the same active settings toggle.
            page.SetChecked(static p => p.BackupAutoCheckBox, false);
            await Wait(() => !indicator.IsVisible && information.Text == Localization.Get("GitIndicatorDisabled"));
            page.SetChecked(static p => p.BackupAutoCheckBox, true);
            await Wait(() => indicator.IsVisible);
            HeadlessRuntime.Dispatch(() =>
            {
                indicator.Flyout!.ShowAt(indicator);
                window.GetVisualDescendants().OfType<MainControl>().Single().DataContext = null;
            });
            await Wait(() => !indicator.Flyout!.IsOpen && !indicator.IsFocused);
            await File.WriteAllTextAsync(Path.Combine(directory, "flow-trace.txt"),
                "Actual Settings tab + BackupAutoCheckBox off/on RU/EN light/dark; real task save measured Dirty; same owner/text; disabled DesiredSize.Width=0; open flyout closes without hidden focus; Started→off→on remains Syncing→failed Terminal Attention; server presentation is a source fixture (no server network activation); live status replacement transfers source/config binding; open→replacement and open→DataContext null both close popup.\n");
        }
        finally { HeadlessRuntime.Dispatch(() => { Localization.SetLanguage(languageBefore); window.Close(); }); }

        Task Wait(Func<bool> condition) => UiAssert.TextEqualsAsync(() => HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return condition().ToString();
        }), "True");

        async Task ShowInformation(TextBlock information)
        {
            HeadlessRuntime.Dispatch(() =>
            {
                window.UpdateLayout();
                information.BringIntoView();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            });
            await Wait(() => information.TranslatePoint(default, window) is { } point &&
                point.Y >= 52 && point.Y + information.Bounds.Height <= window.Bounds.Height);
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task GitIndicator_SettingsSourceMatrixUsesActualSelector()
    {
        if (await RunRenderedChildIfNeeded("/*/*/GitStatusIndicatorHeadlessTests/GitIndicator_SettingsSourceMatrixUsesActualSelector")) return;
        using var session = DesktopAppSession.Launch(UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
            UnlimotionAutomationScenario.TaskSpaces,
            afterViewModelPrepared: prepared =>
            {
                var path = ((FileStorage)prepared.taskRepository!.TaskTreeManager.Storage).Path;
                Repository.Init(path);
                Repository.Init(Path.Combine(Path.GetDirectoryName(path)!, "Tasks-B"));
            }, createStorageFactory: configuration => new OfflineGitSyncSourceFactory(configuration)));
        var window = session.MainWindow;
        using var presentation = new HeadlessWindowPresentation(window);
        var vm = HeadlessRuntime.Dispatch(() => (MainWindowViewModel)window.DataContext!);
        var evidence = Path.GetFullPath(Path.Combine("artifacts", "git-indicator", "settings-source-matrix", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(evidence);
        HeadlessRuntime.Dispatch(() => window.GetVisualDescendants().OfType<ToggleButton>()
            .Single(control => control.Content?.ToString() == "↔️").IsChecked = true);
        await Wait(() => vm.GitStatus.HasDirty && vm.GitStatus.IsIndicatorVisible);
        var sourceA = HeadlessRuntime.Dispatch(() => vm.GitStatus.LastKnown!.Request.SourceId);
        var pathA = HeadlessRuntime.Dispatch(() => vm.GitStatus.LastKnown!.Request.RepositoryPath);
        await Switch("Space B", false, "Space B task");
        await Assert.That(HeadlessRuntime.Dispatch(() => vm.GitStatus.SettingsInformation)).IsEqualTo(Localization.Get("GitIndicatorDisabled"));
        await Switch("Space C", true, "Space C task");
        await Assert.That(HeadlessRuntime.Dispatch(() => vm.GitStatus.State)).IsEqualTo(GitIndicatorState.Unknown);
        await Assert.That(HeadlessRuntime.Dispatch(() => vm.GitStatus.LastKnown)).IsNull();
        await Switch("Offline server", false, "Offline server task");
        await Assert.That(HeadlessRuntime.Dispatch(() => vm.Settings.GitBackupEnabled)).IsTrue();
        await Assert.That(HeadlessRuntime.Dispatch(() => vm.GitStatus.SettingsInformation)).IsEqualTo(Localization.Get("GitIndicatorServer"));
        var page = new MainWindowPage(new HeadlessControlResolver(window));
        page.SelectTabItem(static p => p.SettingsTabItem, timeoutMs: 10_000);
        HeadlessRuntime.Dispatch(() =>
        {
            window.UpdateLayout();
            window.GetVisualDescendants().OfType<TextBlock>()
                .Single(control => AutomationProperties.GetAutomationId(control) == "GitSyncInformationText").BringIntoView();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        });
        session.CaptureScreenshot(Path.Combine(evidence, "server-settings.png"));
        HeadlessRuntime.Dispatch(() => vm.GitStatus.ApplyDiagnostic(new(sourceA, pathA, Guid.NewGuid(), true)));
        await Assert.That(HeadlessRuntime.Dispatch(() => vm.GitStatus.IsBusy)).IsFalse();
        await Switch("Space A", true, "Space A task");
        await Wait(() => vm.GitStatus.HasDirty);
        var finalWatcher = HeadlessRuntime.Dispatch(() => ((FileStorage)vm.taskRepository!.TaskTreeManager.Storage).Watcher!);
        var watcherCallbacks = 0;
        finalWatcher.SetEnable(false, () => watcherCallbacks++);
        await Assert.That(watcherCallbacks).IsEqualTo(1);
        session.Dispose();
        finalWatcher.SetEnable(false, () => watcherCallbacks++);
        await Assert.That(watcherCallbacks).IsEqualTo(1);
        await File.WriteAllTextAsync(Path.Combine(evidence, "flow-trace.txt"),
            "Real TaskSpaceCoordinator + TaskSpaceSelector A-enabled→B-disabled(.git exists)→C-enabled(no repo)→server-enabled→A; source-specific settings projected and old A operation rejected; server runtime Kind=Server uses an explicit offline local storage carrier, server transport is not exercised; final A watcher accepts callback before session close and rejects it after disposal.\n");

        Task Wait(Func<bool> condition) => UiAssert.TextEqualsAsync(() => HeadlessRuntime.Dispatch(() =>
        { Dispatcher.UIThread.RunJobs(); return condition().ToString(); }), "True");
        async Task Switch(string name, bool visible, string title)
        {
            HeadlessRuntime.Dispatch(() =>
            {
                var option = vm.Settings.TaskSpaces.Single(space => space.DisplayName == name);
                window.GetVisualDescendants().OfType<ComboBox>()
                    .Single(control => AutomationProperties.GetAutomationId(control) == "TaskSpaceSelector").SelectedItem = option;
            });
            await Wait(() => !vm.Settings.IsTaskSpaceSwitching && vm.Settings.HeaderTaskSpace?.DisplayName == name &&
                vm.GitStatus.IsIndicatorVisible == visible && vm.taskRepository!.Tasks.Items.SingleOrDefault()?.Title == title);
            session.CaptureScreenshot(Path.Combine(evidence, name.Replace(' ', '-') + ".png"));
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task GitIndicator_SaveSyncFailureSwitchKeyboardAndRenderedMatrix()
    {
        if (await RunRenderedChildIfNeeded()) return;
        var observer = new FallibleObserver();
        string repositoryPath = "";
        using var session = DesktopAppSession.Launch(UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
            afterViewModelPrepared: vm =>
            {
                var storage = (FileStorage)vm.taskRepository!.TaskTreeManager.Storage;
                repositoryPath = storage.Path;
                Repository.Init(repositoryPath);
                Commit(repositoryPath);
                vm.ConfigureGitStatus(new GitStatusViewModel(observer, action => Dispatcher.UIThread.Post(action)));
            }));
        var window = session.MainWindow;
        using var presentation = new HeadlessWindowPresentation(window);
        var vm = HeadlessRuntime.Dispatch(() => (MainWindowViewModel)window.DataContext!);
        var directory = Path.GetFullPath(Path.Combine("artifacts", "git-indicator", "rendered", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        var trace = new List<string>();
        var previousLanguage = LocalizationService.Current.LanguageMode;
        try
        {
            HeadlessRuntime.Dispatch(() =>
            {
                window.Width = 800; window.Height = 600;
                vm.GitStatus.SetSource("A", repositoryPath, true,
                    (FileStorage)vm.taskRepository!.TaskTreeManager.Storage);
                vm.GitStatus.SetEligibility(true, true);
            });
            await State(GitIndicatorState.Idle);
            var indicator = HeadlessRuntime.Dispatch(() => window.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetAutomationId(button) == "GitStatusIndicator"));
            // Ordinary task persistence through the current storage, not an assigned Dirty property.
            var save = HeadlessRuntime.Dispatch(() =>
            {
                var task = vm.CurrentTaskItem!;
                task.Title += " saved for Git indicator";
                return vm.taskRepository!.Update(task);
            });
            await UiAssert.TextEqualsAsync(() => HeadlessRuntime.Dispatch(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return save.IsCompleted.ToString();
            }), "True");
            await save;
            await State(GitIndicatorState.Dirty);
            await UiAssert.TextEqualsAsync(() => HeadlessRuntime.Dispatch(() => { Dispatcher.UIThread.RunJobs(); return vm.GitStatus.IsStale.ToString(); }), "False");
            trace.Add("save -> measured Dirty " + HeadlessRuntime.Dispatch(() => vm.GitStatus.DirtyBadge));
            await Assert.That(HeadlessRuntime.Dispatch(() => vm.GitStatus.LastKnown!.DirtyFileCount > 0)).IsTrue();

            foreach (var language in new[] { "ru", "en" })
            foreach (var dark in new[] { false, true })
            {
                HeadlessRuntime.Dispatch(() =>
                {
                    Localization.SetLanguage(language);
                    Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                    Dispatcher.UIThread.RunJobs();
                    indicator.Focus();
                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    Dispatcher.UIThread.RunJobs();
                });
                await Assert.That(HeadlessRuntime.Dispatch(() => indicator.Flyout!.IsOpen)).IsTrue();
                await Assert.That(HeadlessRuntime.Dispatch(() => indicator.Bounds.Width >= 32 && indicator.Bounds.Height >= 32)).IsTrue();
                await Assert.That(HeadlessRuntime.Dispatch(() =>
                {
                    var origin = indicator.TranslatePoint(default, window)!.Value;
                    return origin.X >= 0 && origin.X + indicator.Bounds.Width <= window.Bounds.Width && origin.Y >= 0;
                })).IsTrue();
                var content = HeadlessRuntime.Dispatch(() => (Control)((Flyout)indicator.Flyout!).Content!);
                var details = HeadlessRuntime.Dispatch(() => content.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => AutomationProperties.GetAutomationId(text) == "GitStatusDetailsText"));
                await Assert.That(HeadlessRuntime.Dispatch(() => details.Text!.Contains(language == "ru" ? "Файлов:" : "Files:"))).IsTrue();
                await Assert.That(HeadlessRuntime.Dispatch(() => details.Text!.Contains("—"))).IsTrue();
                // Popup is a separate Avalonia top level; capture its pixels as well as the window.
                HeadlessRuntime.Dispatch(() =>
                {
                    var root = TopLevel.GetTopLevel(content)!;
                    root.UpdateLayout();
                    using var frame = ((WindowBase)root).CaptureRenderedFrame() ?? throw new InvalidOperationException("Popup pixels unavailable.");
                    frame.Save(Path.Combine(directory, $"details-{language}-{(dark ? "dark" : "light")}.png"));
                });
                Console.WriteLine("Git indicator screenshot: " + session.CaptureScreenshot(
                    Path.Combine(directory, $"dirty-save-{language}-{(dark ? "dark" : "light")}.png")));
                HeadlessRuntime.Dispatch(() =>
                {
                    var root = (WindowBase)TopLevel.GetTopLevel(content)!;
                    root.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    root.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    Dispatcher.UIThread.RunJobs();
                });
                await Assert.That(HeadlessRuntime.Dispatch(() => indicator.Flyout!.IsOpen)).IsFalse();
                await Assert.That(HeadlessRuntime.Dispatch(() => indicator.IsFocused)).IsTrue();
                HeadlessRuntime.Dispatch(() =>
                {
                    window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                    window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                    Dispatcher.UIThread.RunJobs();
                });
                await Assert.That(HeadlessRuntime.Dispatch(() => indicator.Flyout!.IsOpen)).IsTrue();
                HeadlessRuntime.Dispatch(() => indicator.Flyout!.Hide());
            }

            var operationId = Guid.NewGuid();
            HeadlessRuntime.Dispatch(() => vm.GitStatus.ApplyDiagnostic(new("A", repositoryPath, operationId, true)));
            await State(GitIndicatorState.Syncing);
            observer.Fail = true;
            HeadlessRuntime.Dispatch(vm.GitStatus.RequestRefresh);
            await UiAssert.TextEqualsAsync(() => HeadlessRuntime.Dispatch(() => { Dispatcher.UIThread.RunJobs(); return vm.GitStatus.Details.Contains(Localization.Get("GitIndicatorReadError")).ToString(); }), "True");
            await State(GitIndicatorState.Syncing);
            await Assert.That(HeadlessRuntime.Dispatch(() => vm.GitStatus.HasDirty)).IsTrue();
            Console.WriteLine("Git indicator screenshot: " + session.CaptureScreenshot(Path.Combine(directory, "syncing-stale.png")));
            HeadlessRuntime.Dispatch(() => vm.GitStatus.ApplyDiagnostic(new("A", repositoryPath, operationId, false, BackupOperationResult.Failed)));
            await State(GitIndicatorState.Attention);
            await Assert.That(HeadlessRuntime.Dispatch(() => vm.GitStatus.HasDirty)).IsTrue();
            Console.WriteLine("Git indicator screenshot: " + session.CaptureScreenshot(Path.Combine(directory, "attention-dirty.png")));
            trace.Add("sync -> failed read retains Syncing/Dirty -> failed operation Attention/Dirty");

            var retry = Guid.NewGuid();
            HeadlessRuntime.Dispatch(() => vm.GitStatus.ApplyDiagnostic(new("A", repositoryPath, retry, true)));
            await State(GitIndicatorState.Syncing);
            observer.Fail = false;
            // The title edit also schedules a throttled autosave. Finish the fixture's
            // write producers before the synthetic retry commit, otherwise a late save
            // can legitimately make the repository Dirty again after that commit.
            await Task.WhenAll(HeadlessRuntime.Dispatch(() => vm.taskRepository!.Tasks.Items
                .Select(task => task.SealPendingSaves()).ToArray()));
            Commit(repositoryPath);
            HeadlessRuntime.Dispatch(() => vm.GitStatus.ApplyDiagnostic(new("A", repositoryPath, retry, false, BackupOperationResult.Success)));
            await State(GitIndicatorState.Idle);
            trace.Add("retry success -> new measured Idle");
            HeadlessRuntime.Dispatch(() => vm.GitStatus.SetSource("B", Path.Combine(repositoryPath, "missing"), true));
            await State(GitIndicatorState.Unknown);
            await Assert.That(HeadlessRuntime.Dispatch(() => vm.GitStatus.LastKnown)).IsNull();
            HeadlessRuntime.Dispatch(() => vm.GitStatus.ApplyDiagnostic(new("A", repositoryPath, Guid.NewGuid(), true)));
            await Assert.That(HeadlessRuntime.Dispatch(() => vm.GitStatus.IsBusy)).IsFalse();
            Console.WriteLine("Git indicator screenshot: " + session.CaptureScreenshot(Path.Combine(directory, "unknown-space-B.png")));
            HeadlessRuntime.Dispatch(() => vm.GitStatus.SetSource("server", "", false));
            await State(GitIndicatorState.NotApplicable);
            Console.WriteLine("Git indicator screenshot: " + session.CaptureScreenshot(Path.Combine(directory, "not-applicable.png")));
            trace.Add("A -> B clears sample; late A operation ignored; server NotApplicable");
            await File.WriteAllLinesAsync(Path.Combine(directory, "flow-trace.txt"), trace);
        }
        finally
        {
            HeadlessRuntime.Dispatch(() => { Localization.SetLanguage(previousLanguage); window.Close(); });
        }
        async Task State(GitIndicatorState state)
        {
            try
            {
                await UiAssert.TextEqualsAsync(() => HeadlessRuntime.Dispatch(() =>
                {
                    Dispatcher.UIThread.RunJobs();
                    return vm.GitStatus.State.ToString();
                }), state.ToString());
            }
            catch
            {
                Console.WriteLine(HeadlessRuntime.Dispatch(() => vm.GitStatus.Details));
                Console.WriteLine("Observed: " + HeadlessRuntime.Dispatch(() => vm.GitStatus.LastKnown?.ToString() ?? "none"));
                using var repository = new Repository(repositoryPath);
                Console.WriteLine("Worktree: " + string.Join(", ", repository.RetrieveStatus()
                    .Select(entry => $"{entry.FilePath}: {entry.State}")));
                throw;
            }
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task GitIndicator_ReplacingContextStopsOldTimerAndStartsNewOwner()
    {
        if (await RunRenderedChildIfNeeded("/*/*/GitStatusIndicatorHeadlessTests/GitIndicator_ReplacingContext*")) return;
        var clock = DateTimeOffset.UtcNow;
        var readsA = 0;
        var readsB = 0;
        var changesA = 0;
        var changesB = 0;
        using var session = DesktopAppSession.Launch(UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
            afterViewModelPrepared: vm =>
            {
                vm.ConfigureGitStatus(new GitStatusViewModel(new CountingObserver(() => clock, () => Interlocked.Increment(ref readsA)),
                    action => Dispatcher.UIThread.Post(action), now: () => clock));
            }));
        var window = session.MainWindow;
        using var presentation = new HeadlessWindowPresentation(window);
        var vmA = HeadlessRuntime.Dispatch(() => (MainWindowViewModel)window.DataContext!);
        var main = HeadlessRuntime.Dispatch(() => window.GetVisualDescendants().OfType<MainControl>().Single());
        // Headless initializes/resets its dispatcher while creating the window.
        // Start the first sample only after that boundary, as production binding does.
        HeadlessRuntime.Dispatch(() =>
        {
            vmA.GitStatus.SetSource("A", Path.GetTempPath(), true);
            vmA.GitStatus.SetEligibility(true, true);
        });
        var config = new ConfigurationBuilder().Build();
        using var configLifetime = config as IDisposable;
        using var vmB = HeadlessRuntime.Dispatch(() => new MainWindowViewModel(null, vmA.ManagerWrapper, config));
        await Fresh(vmA.GitStatus);
        HeadlessRuntime.Dispatch(() =>
        {
            vmA.GitStatus.PropertyChanged += (_, _) => Interlocked.Increment(ref changesA);
            main.DataContext = null;
        });
        Interlocked.Exchange(ref changesA, 0);
        var oldReads = Volatile.Read(ref readsA);
        HeadlessRuntime.Dispatch(() => clock = clock.AddMinutes(1));
        await QuietTimer();
        await Assert.That(Volatile.Read(ref readsA)).IsEqualTo(oldReads);
        await Assert.That(Volatile.Read(ref changesA)).IsEqualTo(0);

        // An attached default status has no observer/source and must stay quiet.
        HeadlessRuntime.Dispatch(() =>
        {
            vmB.GitStatus.PropertyChanged += (_, _) => Interlocked.Increment(ref changesB);
            main.DataContext = vmB;
        });
        Interlocked.Exchange(ref changesB, 0);
        await QuietTimer();
        await Assert.That(Volatile.Read(ref changesB)).IsEqualTo(0);
        HeadlessRuntime.Dispatch(() =>
        {
            main.DataContext = null;
            vmB.ConfigureGitStatus(new GitStatusViewModel(new CountingObserver(() => clock, () => Interlocked.Increment(ref readsB)),
                action => Dispatcher.UIThread.Post(action), now: () => clock));
            vmB.GitStatus.SetSource("B", Path.GetTempPath(), true);
            vmB.GitStatus.SetEligibility(true, true);
            main.DataContext = vmB;
        });
        await Fresh(vmB.GitStatus);
        var previousReads = Volatile.Read(ref readsB);
        HeadlessRuntime.Dispatch(() => clock = clock.AddMinutes(1));
        await UiAssert.TextEqualsAsync(() => HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return (Volatile.Read(ref readsB) > previousReads && !vmB.GitStatus.IsStale).ToString();
        }), "True");
        // Parent visibility can change while this control remains locally visible and attached.
        var ancestor = HeadlessRuntime.Dispatch(() => main.GetVisualParent()!);
        await Hidden(() => ancestor.IsVisible = false, () => ancestor.IsVisible = true);
        await Hidden(window.Hide, window.Show);
        await Hidden(() => window.WindowState = WindowState.Minimized, () => window.WindowState = WindowState.Normal);

        HeadlessRuntime.Dispatch(() => window.Content = null);
        await Fresh(vmB.GitStatus);
        previousReads = Volatile.Read(ref readsB);
        HeadlessRuntime.Dispatch(() => clock = clock.AddMinutes(1));
        await QuietTimer();
        await Assert.That(Volatile.Read(ref readsB)).IsEqualTo(previousReads);
        await Assert.That(Volatile.Read(ref readsA)).IsEqualTo(oldReads);
        var evidence = Path.Combine(AppContext.BaseDirectory, "artifacts", "git-indicator", "replacement-lifetime");
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "flow-trace.txt"),
            "Attached A -> null stops A notifications/reads; default B stays quiet; configured B fallback remeasures; detach stops B; no disposal needed to stop A/B timers; ancestor hide/show, Window.Hide/Show and logical WindowState stop/resume fallback\n");

        async Task Hidden(Action hide, Action show)
        {
            HeadlessRuntime.Dispatch(hide);
            await Assert.That(HeadlessRuntime.Dispatch(() => main.IsVisible && main.IsAttachedToVisualTree())).IsTrue();
            var before = Volatile.Read(ref readsB);
            HeadlessRuntime.Dispatch(() => clock = clock.AddMinutes(1));
            await QuietTimer();
            await Assert.That(Volatile.Read(ref readsB)).IsEqualTo(before);
            HeadlessRuntime.Dispatch(show);
            await Fresh(vmB.GitStatus);
            await Assert.That(Volatile.Read(ref readsB) > before).IsTrue();
        }

        async Task Fresh(GitStatusViewModel status)
        {
            try
            {
                await UiAssert.TextEqualsAsync(() => HeadlessRuntime.Dispatch(() =>
                {
                    Dispatcher.UIThread.RunJobs();
                    return (status.LastKnown != null && !status.IsStale).ToString();
                }), "True");
            }
            catch
            {
                Console.WriteLine(HeadlessRuntime.Dispatch(() => $"Fresh timeout: state={status.State}; stale={status.IsStale}; sample={status.LastKnown}; reads A={readsA}, B={readsB}; ancestors={string.Join(",", main.GetVisualAncestors().Select(v => $"{v.GetType().Name}:{v.IsVisible}"))}"));
                throw;
            }
        }
        async Task QuietTimer()
        {
            await Task.Delay(1400);
            HeadlessRuntime.Dispatch(() => Dispatcher.UIThread.RunJobs());
        }
    }

    private sealed class CountingObserver(Func<DateTimeOffset> now, Action read) : IGitWorktreeObserver
    {
        public Task<GitWorktreeObservation> ObserveAsync(GitObservationRequest request, CancellationToken cancellationToken)
        {
            read();
            return Task.FromResult(new GitWorktreeObservation(request, now(), GitObservationKind.Known, 0, false));
        }
    }

    private static void Commit(string path)
    {
        using var repo = new Repository(path);
        // Storage deletes this transient file on close, possibly while Stage
        // enumerates changes. Fixture commits must never include it.
        const string lockRule = "/.unlimotion.lock";
        var excludePath = Path.Combine(repo.Info.Path, "info", "exclude");
        if (!File.Exists(excludePath) || !File.ReadAllLines(excludePath).Contains(lockRule))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(excludePath)!);
            File.AppendAllText(excludePath, Environment.NewLine + lockRule + Environment.NewLine);
        }
        Commands.Stage(repo, "*");
        if (repo.RetrieveStatus().IsDirty)
        {
            var author = new Signature("Fixture", "fixture@example.com", DateTimeOffset.Now);
            repo.Commit("fixture", author, author);
        }
    }
    private sealed class FallibleObserver : IGitWorktreeObserver
    {
        public volatile bool Fail;
        public Task<GitWorktreeObservation> ObserveAsync(GitObservationRequest request, CancellationToken cancellationToken) =>
            Fail ? Task.FromResult(new GitWorktreeObservation(request, DateTimeOffset.UtcNow, GitObservationKind.Unknown, ErrorCode: "InjectedReadFailure")) :
                new GitWorktreeObserver().ObserveAsync(request, cancellationToken);
    }

    internal static async Task<bool> RunRenderedChildIfNeeded(string filter = "/*/*/GitStatusIndicatorHeadlessTests/GitIndicator_SaveSyncFailureSwitchKeyboardAndRenderedMatrix")
    {
        if (Environment.GetEnvironmentVariable(ChildFlag) == "1") return false;
        var directory = Path.Combine(AppContext.BaseDirectory, "artifacts", "git-indicator", "rendered-child", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { typeof(GitStatusIndicatorHeadlessTests).Assembly.Location,
                     "--treenode-filter", filter, "--maximum-parallel-tests", "1",
                     "--minimum-expected-tests", "1", "--report-trx", "--output", "Detailed",
                     "--results-directory", Path.Combine(directory, "results") }) start.ArgumentList.Add(argument);
        start.Environment[ChildFlag] = "1";
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Cannot start rendered Headless test.");
        var output = child.StandardOutput.ReadToEndAsync();
        var errors = child.StandardError.ReadToEndAsync();
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(5)); }
        catch (TimeoutException)
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            throw;
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "stdout.log"), await output);
            await File.WriteAllTextAsync(Path.Combine(directory, "stderr.log"), await errors);
        }
        Console.WriteLine(await output);
        if (child.ExitCode != 0) throw new InvalidOperationException($"Rendered Git indicator failed ({child.ExitCode}); logs: {directory}");
        return true;
    }
}
