using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel.Feed;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class FeedDatePresentationUiTests
{
    [Test]
    public async Task CopyPathClipboardFailurePreservesPathTooltipAndReportsLocalizedError()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            Directory.CreateDirectory(Path.Combine(directory.Path, "Ежедневные"));
            var path = Path.Combine(directory.Path, "Ежедневные", "2026-09-09.md");
            await File.WriteAllTextAsync(path, "Запись\n");
            using var feed = new FeedViewModel(() => new DateOnly(2026, 9, 9));
            await feed.InitializeVaultAsync(directory.Path);
            var view = new FailingClipboardFeedControl { DataContext = feed };
            var window = new Window { Width = 1000, Height = 700, Content = view };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var date = view.GetVisualDescendants().OfType<TextBlock>().First(control =>
                    AutomationProperties.GetAutomationId(control) == "FeedDay-20260909-DateText");
                await Assert.That(ToolTip.GetTip(date)).IsEqualTo(Path.GetFullPath(path));
                date.RaiseEvent(new ContextRequestedEventArgs { Source = date });
                var copy = date.ContextMenu!.Items.OfType<MenuItem>().Single();
                copy.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                await Assert.That(view.AttemptedPath).IsEqualTo(Path.GetFullPath(path));
                await Assert.That(feed.ErrorMessage).IsEqualTo(Unlimotion.ViewModel.Localization.Localization.Get("FeedClipboardUnavailable"));
                await Assert.That(ToolTip.GetTip(date)).IsEqualTo(Path.GetFullPath(path));
                await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo("Запись\n");
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private sealed class FailingClipboardFeedControl : FeedControl
    {
        public string? AttemptedPath { get; private set; }
        protected override Task WriteNotePathToClipboardAsync(string path)
        {
            AttemptedPath = path;
            return Task.FromException(new InvalidOperationException("Clipboard locked"));
        }
    }

    [Test]
    public async Task LoadedDayAndReviewFollowDisplaySettingsWithoutRenamingFile()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            using var directory = new TempNotesDirectory();
            Directory.CreateDirectory(Path.Combine(directory.Path, "Ежедневные"));
            var notePath = Path.Combine(directory.Path, "Ежедневные", "2026-09-09.md");
            await File.WriteAllTextAsync(notePath, "- [ ] Проверить дату\n");
            var original = await File.ReadAllTextAsync(notePath);
            using var feed = new FeedViewModel(() => new DateOnly(2026, 9, 10));
            var settings = fixture.MainWindowViewModelTest.Settings;
            settings.LanguageMode = "ru";
            feed.TaskOwner = fixture.MainWindowViewModelTest;
            await feed.InitializeVaultAsync(directory.Path);
            var view = new FeedControl { DataContext = feed };
            var window = new Window { Width = 1000, Height = 700, Content = view };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var date = view.GetVisualDescendants().OfType<TextBlock>().First(control =>
                    AutomationProperties.GetAutomationId(control) == "FeedDay-20260909-DateText");
                await Assert.That(date.Text).IsEqualTo("9 сентября 2026, среда");
                await Assert.That(feed.Days.Single(day => day.Date == new DateOnly(2026, 9, 9)).FullPath)
                    .IsEqualTo(Path.GetFullPath(notePath));

                await feed.StartReviewAsync();
                Dispatcher.UIThread.RunJobs();
                await Assert.That(feed.CurrentReview).IsNotNull();
                settings.DisplayDateFormatDraft = "dd.MM.yyyy";
                Dispatcher.UIThread.RunJobs();
                await Assert.That(date.Text).IsEqualTo("09.09.2026");
                await Assert.That(feed.CurrentReview!.DisplayDate).IsEqualTo("09.09.2026");
                settings.ResetDisplayDateFormat();
                settings.LanguageMode = "en";
                Dispatcher.UIThread.RunJobs();
                await Assert.That(date.Text).IsEqualTo("9 September 2026, Wednesday");
                await Assert.That(feed.CurrentReview!.DisplayDate).IsEqualTo("9 September 2026, Wednesday");
                await Assert.That(File.Exists(notePath)).IsTrue();
                await Assert.That(await File.ReadAllTextAsync(notePath)).IsEqualTo(original);
            }
            finally
            {
                window.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }
}
