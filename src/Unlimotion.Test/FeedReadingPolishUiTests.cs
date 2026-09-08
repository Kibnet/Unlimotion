using System;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel.Feed;
using Unlimotion.Views;
using Unlimotion.Notes.Markdown;
using Avalonia.Controls.Primitives;
using ReactiveUI;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class FeedReadingPolishUiTests
{
    [Test]
    public async Task ReadingPolish_BackgroundBusyNotificationUpdatesNavigationOnUiThread()
    {
        await WithFeed(async (feed, view, window, directory) =>
        {
            var scroller = Find<ScrollViewer>(view, "FeedChronologyList");
            scroller.Offset = new Vector(0, 500);
            Layout();
            var button = Find<Button>(view, "FeedReturnToCurrentDayButton");
            await Assert.That(button.IsEffectivelyVisible).IsTrue();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var resume = new ManualResetEventSlim();
            System.ComponentModel.PropertyChangedEventHandler onBusy = (_, change) =>
            {
                if (change.PropertyName != nameof(feed.IsBusy) || !feed.IsBusy) return;
                started.TrySetResult();
                if (!resume.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("UI did not observe busy navigation.");
            };
            ((System.ComponentModel.INotifyPropertyChanged)feed).PropertyChanged += onBusy;
            var command = Task.Run(async () =>
                await ((ReactiveCommand<Unit, Unit>)feed.LeaveReviewCommand).Execute().ToTask());
            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Layout();
                await Assert.That(button.IsEnabled).IsFalse();
            }
            finally
            {
                resume.Set();
                await command;
                ((System.ComponentModel.INotifyPropertyChanged)feed).PropertyChanged -= onBusy;
            }
            Layout();
            await Assert.That(button.IsEnabled).IsTrue();
        });
    }

    [Test]
    public async Task ReadingPolish_LateSaveFailureDoesNotRestoreFocusBehindNewOverlay()
    {
        await WithFeed(async (feed, view, window, directory) =>
        {
            var editor = feed.Days[0].MarkdownEditor;
            var block = editor.Blocks.First(item => item.Block.Kind == MarkdownBlockKind.Paragraph);
            editor.BeginEdit(block);
            Layout();
            var text = Find<TextBox>(view, block.EditorAutomationId);
            text.Focus();
            var completion = new TaskCompletionSource<MarkdownBlockCommitResult>();
            editor.CommitBlockAsync = (_, _) => completion.Task;
            text.Text += " Draft";
            var returning = view.ReturnToCurrentDayAsync();
            feed.FilesDrawer!.IsOpen = true;
            view.Focusable = true;
            view.Focus();
            Layout();
            var currentFocus = window.FocusManager!.GetFocusedElement();
            completion.SetResult(MarkdownBlockCommitResult.Rejected("Rejected after overlay opened"));
            await returning;
            Layout();
            await Assert.That(window.FocusManager.GetFocusedElement()).IsSameReferenceAs(currentFocus);
            await Assert.That(text.IsFocused).IsFalse();
            await Assert.That(feed.FilesDrawer.IsOpen).IsTrue();
            await Assert.That(Find<TextBlock>(view, "FeedReturnNavigationError").IsEffectivelyVisible).IsFalse();
            editor.CancelActiveEdit();
        });
    }

    [Test]
    public async Task ReadingPolish_ReturnMaterializesUnrealizedHeadingWithoutRealizingHistory()
    {
        await WithFeed(async (feed, view, window, directory) =>
        {
            while (feed.HasMoreDays) await feed.LoadOlderDaysAsync();
            var items = Find<ItemsControl>(view, "FeedChronologyItems");
            var scroller = Find<ScrollViewer>(view, "FeedChronologyList");
            items.ScrollIntoView(feed.VisibleDays[^1]);
            Layout();
            await Assert.That(items.GetVisualDescendants().OfType<TextBlock>()
                .Any(control => AutomationProperties.GetAutomationId(control) == feed.VisibleDays[0].HeaderAutomationId)).IsFalse();
            var button = Find<Button>(view, "FeedReturnToCurrentDayButton");
            await Assert.That(button.IsEffectivelyVisible).IsTrue();
            Click(window, button);
            await PumpUntil(() => scroller.Offset.Y < 2);
            var header = Find<TextBlock>(view, feed.VisibleDays[0].HeaderAutomationId);
            var headerTop = header.TranslatePoint(default, scroller)!.Value.Y;
            await Assert.That(headerTop).IsGreaterThanOrEqualTo(0);
            await Assert.That(headerTop).IsLessThan(scroller.Viewport.Height);
            await Assert.That(view.GetVisualDescendants().OfType<MarkdownBlockLivePreviewEditor>().Count()).IsLessThan(10);
        }, olderDays: 60);
    }

    [Test]
    public async Task ReadingPolish_UnknownMetadataBecomingKnownKeepsItsEditorAndFocus()
    {
        await WithFeed(async (feed, view, window, directory) =>
        {
            var editor = feed.Days[0].MarkdownEditor;
            editor.Load(editor.Snapshot! with { Raw = "---\nareas: [work]\ncustom: value\n---\nBody\n" });
            var block = editor.Blocks[0];
            editor.BeginEdit(block);
            Layout();
            var text = Find<TextBox>(view, block.EditorAutomationId);
            text.Focus();
            text.Text = "---\nareas: [work]\n---";
            text.CaretIndex = 15;
            Layout();
            await Assert.That(text.IsEffectivelyVisible).IsTrue();
            await Assert.That(text.IsFocused).IsTrue();
            await Assert.That(editor.IsServiceFrontMatterExpanded).IsTrue();
            await Assert.That(block.IsDirty).IsTrue();
            editor.CancelActiveEdit();
        });
    }

    [Test]
    public async Task ReadingPolish_MetadataTogglePreservesFileAndUnknownEditStaysVisible()
    {
        await WithFeed(async (feed, view, window, directory) =>
        {
            var editor = feed.Days[0].MarkdownEditor;
            var path = Path.Combine(directory, "Ежедневные", "2026-09-06.md");
            var original = File.ReadAllBytes(path);
            var toggle = Find<ToggleButton>(view, "FeedDay-20260906-ServiceDataToggle");
            var raw = Find<Control>(view, editor.Blocks[0].BlockAutomationId);
            await Assert.That(toggle.IsEffectivelyVisible).IsTrue();
            await Assert.That(toggle.IsChecked).IsFalse();
            await Assert.That(raw.IsEffectivelyVisible).IsFalse();
            Click(window, toggle);
            Layout();
            await Assert.That(toggle.IsChecked).IsTrue();
            await Assert.That(raw.IsEffectivelyVisible).IsTrue();
            Click(window, toggle);
            Layout();
            await Assert.That(raw.IsEffectivelyVisible).IsFalse();
            await Assert.That(File.ReadAllBytes(path).SequenceEqual(original)).IsTrue();
            Click(window, toggle);
            editor.BeginEdit(editor.Blocks[0]);
            editor.ActiveBlock!.EditorText = "---\nunlimotion-id: daily-test\ncustom: visible\n---";
            Layout();
            await Assert.That(toggle.IsEffectivelyVisible).IsFalse();
            await Assert.That(raw.IsEffectivelyVisible).IsTrue();
            editor.CancelActiveEdit();
        });
    }

    [Test]
    [Arguments(520)]
    [Arguments(1000)]
    [Arguments(1400)]
    public async Task ReadingPolish_TextColumnBoundsAndThematicTitle(int width)
    {
        await WithFeed(async (feed, view, window, directory) =>
        {
            window.Width = width;
            Layout();
            var editorView = view.GetVisualDescendants().OfType<MarkdownBlockLivePreviewEditor>()
                .First(control => ReferenceEquals(control.DataContext, feed.Days[0].MarkdownEditor));
            var column = editorView.FindControl<Grid>("ReadingColumn")!;
            await Assert.That(Math.Abs(column.Bounds.Width - (width > 1000 ? 960 : editorView.Bounds.Width))).IsLessThan(2);
            var card = Find<Border>(view, feed.Days[0].AutomationId);
            await Assert.That(card.Bounds.Width).IsGreaterThan(column.Bounds.Width);
            await Assert.That(column.Bounds.Left).IsGreaterThanOrEqualTo(0);
            await Assert.That(column.Bounds.Right).IsLessThanOrEqualTo(editorView.Bounds.Width + 1);
            var evidenceDir = Environment.GetEnvironmentVariable("UNLIMOTION_FEED_POLISH_HEADLESS_EVIDENCE");
            if (!string.IsNullOrWhiteSpace(evidenceDir))
            {
                Directory.CreateDirectory(evidenceDir);
                using var frame = window.CaptureRenderedFrame();
                if (frame is null) throw new InvalidOperationException("Headless renderer returned no frame.");
                frame.Save(Path.Combine(evidenceDir, $"reading-{width}.png"));
            }
            await feed.OpenVaultLinkAsync("Тема", null);
            Layout();
            await Assert.That(Find<TextBlock>(view, "FeedThematicTitle").IsEffectivelyVisible).IsFalse();
            var thematic = feed.OpenedThematicFile!.MarkdownEditor;
            await Assert.That(thematic.Blocks[0].PreviewText).Contains("Тема");
            thematic.BeginEdit(thematic.Blocks[0]);
            thematic.ActiveBlock!.EditorText = "# Другое название";
            Layout();
            await Assert.That(Find<TextBlock>(view, "FeedThematicTitle").IsEffectivelyVisible).IsTrue();
            thematic.CancelActiveEdit();
        }, render: true);
    }

    [Test]
    public async Task ReadingPolish_ReturnCommitsDraftAndFailurePreservesFocusSelectionAndOffset()
    {
        await WithFeed(async (feed, view, window, directory) =>
        {
            var model = feed.Days[0].MarkdownEditor;
            var block = model.Blocks.First(item => item.Block.Kind == MarkdownBlockKind.Paragraph);
            model.BeginEdit(block);
            Layout();
            var text = Find<TextBox>(view, block.EditorAutomationId);
            text.Focus();
            text.Text = block.EditorText + " Изменение";
            text.SelectionStart = 3;
            text.SelectionEnd = 8;
            var scroller = Find<ScrollViewer>(view, "FeedChronologyList");
            scroller.Offset = new Vector(0, 500);
            Layout();
            var beforeNavigation = scroller.Offset;
            var save = model.CommitBlockAsync;
            model.CommitBlockAsync = (_, _) => Task.FromResult(MarkdownBlockCommitResult.Rejected("Ошибка записи для теста"));
            await view.ReturnToCurrentDayAsync();
            Layout();
            await Assert.That(text.IsFocused).IsTrue();
            await Assert.That(text.SelectionStart).IsEqualTo(3);
            await Assert.That(text.SelectionEnd).IsEqualTo(8);
            await Assert.That(scroller.Offset).IsEqualTo(beforeNavigation);
            await Assert.That(model.ActiveBlock!.ErrorMessage).IsEqualTo("Ошибка записи для теста");
            await Assert.That(text.Text).Contains("Изменение");
            model.CommitBlockAsync = save;
            await view.ReturnToCurrentDayAsync();
            Layout();
            await Assert.That(scroller.Offset.Y).IsLessThan(2);
            await Assert.That(File.ReadAllText(Path.Combine(directory, "Ежедневные", "2026-09-06.md"))).Contains("Изменение");
        });
    }

    [Test]
    public async Task ReadingPolish_MissingTodayUsesLatestAndEmptyFilterDoesNotResetItself()
    {
        await WithFeed(async (feed, view, window, directory) =>
        {
            var scroller = Find<ScrollViewer>(view, "FeedChronologyList");
            scroller.Offset = new Vector(0, 500);
            Layout();
            var button = Find<Button>(view, "FeedReturnToCurrentDayButton");
            await Assert.That(button.IsEffectivelyVisible).IsTrue();
            await Assert.That(button.Content?.ToString()).IsEqualTo(Unlimotion.ViewModel.Localization.Localization.Get("FeedReturnLatest"));
            view.Focusable = true;
            view.Focus();
            window.KeyPress(Key.Home, RawInputModifiers.Alt, PhysicalKey.Home, null);
            window.KeyRelease(Key.Home, RawInputModifiers.Alt, PhysicalKey.Home, null);
            await PumpUntil(() => scroller.Offset.Y < 2);
            await Assert.That(scroller.Offset.Y).IsLessThan(2);
            await Assert.That(File.Exists(Path.Combine(directory, "Ежедневные", "2026-09-07.md"))).IsFalse();
            feed.FeedAreaFilterOptions.Single(item => item.IsAll).IsSelected = false;
            Layout();
            await Assert.That(feed.VisibleDays.Count).IsEqualTo(0);
            await Assert.That(button.IsEffectivelyVisible).IsFalse();
            await Assert.That(view.CanReturnToCurrentDay).IsFalse();
            await view.ReturnToCurrentDayAsync();
            await Assert.That(feed.IsFeedAreaFilterActive).IsTrue();
            await Assert.That(Find<Button>(view, "FeedAreaFilterResetButton").IsEffectivelyVisible).IsTrue();
        }, today: new DateOnly(2026, 9, 7));
    }

    [Test]
    public async Task ReadingPolish_DrawersHideNavigationAndRestoreChronologyFilter()
    {
        await WithFeed(async (feed, view, window, directory) =>
        {
            var filter = Find<Control>(view, "FeedAreaFilterButton");
            feed.OpenFilesCommand.Execute(null);
            await PumpUntil(() => feed.FilesDrawer!.IsOpen);
            Layout();
            await Assert.That(filter.IsEffectivelyVisible).IsFalse();
            await Assert.That(view.CanReturnToCurrentDay).IsFalse();
            feed.FilesDrawer!.IsOpen = false;
            Layout();
            await Assert.That(filter.IsEffectivelyVisible).IsTrue();
            feed.OpenAreasCommand.Execute(null);
            await PumpUntil(() => feed.AreaManagement!.IsOpen);
            Layout();
            await Assert.That(filter.IsEffectivelyVisible).IsFalse();
            feed.AreaManagement!.IsOpen = false;
            Layout();
            await Assert.That(filter.IsEffectivelyVisible).IsTrue();
        });
    }

    [Test]
    public async Task ReadingPolish_ShellOverlaysAndTaskModeBlockAltHome()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            var model = fixture.MainWindowViewModelTest;
            var directory = Path.Combine(fixture.FixtureDirectoryPath, "ReadingVault");
            Directory.CreateDirectory(Path.Combine(directory, "Ежедневные"));
            File.WriteAllText(Path.Combine(directory, "Ежедневные", "2026-09-06.md"), "Содержимое\n");
            await model.Feed.InitializeVaultAsync(directory);
            model.IsFeedMode = true;
            var screen = new MainScreen { DataContext = model };
            var window = new Window { Width = 800, Height = 700, Content = screen };
            try
            {
                window.Show();
                Layout();
                var feedView = screen.GetVisualDescendants().OfType<FeedControl>().Single();
                await Assert.That(feedView.CanReturnToCurrentDay).IsTrue();
                model.OpenSettings();
                Layout();
                await Assert.That(feedView.CanReturnToCurrentDay).IsFalse();
                await Assert.That(Find<Control>(feedView, "FeedAreaFilterButton").IsEffectivelyVisible).IsFalse();
                model.CloseSettings();
                model.OpenQuickCapture(false);
                Layout();
                await Assert.That(feedView.CanReturnToCurrentDay).IsFalse();
                model.CloseQuickCaptureCommand.Execute(null);
                model.IsFeedMode = false;
                Layout();
                await Assert.That(feedView.CanReturnToCurrentDay).IsFalse();
                model.IsFeedMode = true;
                Layout();
                await Assert.That(feedView.CanReturnToCurrentDay).IsTrue();
            }
            finally { window.Close(); await fixture.CleanTasksAsync(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ReadingPolish_SearchHidesChronologyFilter()
    {
        await WithFeed(async (feed, view, window, directory) =>
        {
            var filter = Find<Control>(view, "FeedAreaFilterButton");
            await Assert.That(filter.IsEffectivelyVisible).IsTrue();
            feed.SearchQuery = "Строка";
            Layout();
            await Assert.That(filter.IsEffectivelyVisible).IsFalse();
            feed.SearchQuery = "";
            Layout();
            await Assert.That(filter.IsEffectivelyVisible).IsTrue();
            await feed.OpenVaultLinkAsync("Тема", null);
            Layout();
            await Assert.That(filter.IsEffectivelyVisible).IsFalse();
        });
    }

    [Test]
    public async Task ReadingPolish_LongDayShowsReturnButtonBeforeLeavingItsCard()
    {
        await WithFeed(async (feed, view, window, directory) =>
        {
            var scroller = Find<ScrollViewer>(view, "FeedChronologyList");
            scroller.Offset = new Vector(0, 500);
            Layout();
            var button = view.GetVisualDescendants().OfType<Button>().FirstOrDefault(control =>
                AutomationProperties.GetAutomationId(control) == "FeedReturnToCurrentDayButton");
            await Assert.That(button).IsNotNull();
            await Assert.That(button!.IsEffectivelyVisible).IsTrue();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await PumpUntil(() => scroller.Offset.Y < 2);
            await Assert.That(scroller.Offset.Y).IsLessThan(2);
        });
    }

    private static async Task WithFeed(Func<FeedViewModel, FeedControl, Window, string, Task> check,
        DateOnly? today = null, bool render = false, int olderDays = 0)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(render ? typeof(SkiaHeadlessAppBuilder) : typeof(App));
        await session.DispatchAsync(async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "unlimotion-reading-polish", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "Ежедневные"));
            File.WriteAllText(Path.Combine(directory, "Ежедневные", "2026-09-06.md"),
                "---\nunlimotion-id: daily-test\nareas: [work, home]\n---\n" +
                string.Join("\n", Enumerable.Range(1, 200).Select(index => $"Строка {index} — содержимое длинного дня.")) + "\n");
            File.WriteAllText(Path.Combine(directory, "Тема.md"), "# Тема\n\nСодержание\n");
            for (var index = 1; index <= olderDays; index++)
                File.WriteAllText(Path.Combine(directory, "Ежедневные", $"{new DateOnly(2026, 9, 6).AddDays(-index):yyyy-MM-dd}.md"),
                    $"Заметка из истории {index}\n\nЕщё один абзац\n");
            using var feed = new FeedViewModel(() => today ?? new DateOnly(2026, 9, 6));
            await feed.InitializeVaultAsync(directory);
            var view = new FeedControl { DataContext = feed };
            var window = new Window { Width = 1400, Height = 700, Content = view };
            try
            {
                window.Show();
                Layout();
                await check(feed, view, window, directory);
            }
            finally
            {
                window.Close();
                feed.Dispose();
                Directory.Delete(directory, true);
            }
        }, CancellationToken.None);
    }

    private static T Find<T>(Control root, string id) where T : Control => root.GetVisualDescendants()
        .OfType<T>().First(control => AutomationProperties.GetAutomationId(control) == id);

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Layout();
    }

    private static void Layout()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task PumpUntil(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 100 && !predicate(); attempt++)
        {
            Layout();
            await Task.Delay(10);
        }
        Layout();
    }
}
