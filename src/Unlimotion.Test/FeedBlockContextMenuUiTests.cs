using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel.Feed;
using Unlimotion.Views;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class FeedBlockContextMenuUiTests
{
    [Test]
    public Task RightClickUsesStandardMenuWithoutStartingEditOrShowingHoverToolbar() => WithFeed(async (window, view, feed) =>
    {
        var block = ContentBlock(feed, 9);
        var previous = ContentBlock(feed, 8);
        previous.Owner.SelectMoveBlock(previous, false, false);
        var preview = Find<MarkdownBlockPreviewControl>(view, block.PreviewAutomationId);
        var handle = Find<ToggleButton>(view, block.MoveHandleAutomationId);
        window.MouseMove(new Point(1, 1));
        Dispatcher.UIThread.RunJobs();
        await Assert.That(handle.Opacity).IsEqualTo(0d);
        var point = Center(window, preview);
        window.MouseMove(point);
        Dispatcher.UIThread.RunJobs();
        await Assert.That(view.GetVisualDescendants().OfType<Button>().Any(control =>
            control.Classes.Contains("BlockToolbarAction") && control.IsVisible)).IsFalse();
        await Assert.That(handle.Opacity).IsEqualTo(1d);
        await Assert.That(handle.Bounds.Width).IsLessThanOrEqualTo(20d);

        Click(window, preview, MouseButton.Right);
        var row = Find<Grid>(view, block.BlockAutomationId);
        var menu = row.ContextMenu;
        await Assert.That(menu).IsNotNull();
        await Assert.That(menu!.IsOpen).IsTrue();
        await Assert.That(block.Owner.ActiveBlock).IsNull();
        await Assert.That(block.IsMoveSelected).IsTrue();
        await Assert.That(previous.IsMoveSelected).IsFalse();
        var headers = menu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray();
        foreach (var key in new[] { "FeedToolbarTask", "FeedToolbarNote", "FeedSearchArea", "FeedBlockMoveUp",
                     "FeedBlockMoveDown", "FeedBlockPlainText", "FeedBlockBulletedList", "FeedBlockNumberedList",
                     "FeedBlockChecklist", "FeedBlockConvertToArea" })
            await Assert.That(headers.Contains(L10n.Get(key))).IsTrue();
        await Assert.That(menu.Items.OfType<Separator>().Any()).IsTrue();
        menu.Close();
        previous.Owner.SelectMoveBlock(previous, true, false);
        Click(window, preview, MouseButton.Right);
        await Assert.That(block.IsMoveSelected).IsTrue();
        await Assert.That(previous.IsMoveSelected).IsTrue();
        menu = row.ContextMenu!;
        await Assert.That(menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, L10n.Get("FeedToolbarNote"))).IsEnabled)
            .IsFalse();
        await Assert.That(menu.Items.OfType<MenuItem>().Any(item => Equals(item.Header, L10n.Get("FeedSelectOneNote")))).IsTrue();
        menu.Close();
    });

    [Test]
    public Task ClickingSelectedHandleClearsAllDaysOnReleaseAndHighlightsWholeRows() => WithFeed(async (window, view, feed) =>
    {
        var first = ContentBlock(feed, 9);
        var second = ContentBlock(feed, 8);
        var firstHandle = Find<ToggleButton>(view, first.MoveHandleAutomationId);
        var secondHandle = Find<ToggleButton>(view, second.MoveHandleAutomationId);
        Click(window, firstHandle, MouseButton.Left);
        Click(window, secondHandle, MouseButton.Left, RawInputModifiers.Control);
        await Assert.That(first.IsMoveSelected).IsTrue();
        await Assert.That(second.IsMoveSelected).IsTrue();
        foreach (var block in new[] { first, second })
        {
            var row = Find<Grid>(view, block.BlockAutomationId);
            await Assert.That(row.Classes.Contains("MoveSelected")).IsTrue();
            await Assert.That(row.Background).IsNotNull();
            await Assert.That(row.Background is ISolidColorBrush { Color.A: > 0, Opacity: > 0 }).IsTrue();
            await Assert.That(row.Bounds.Width).IsGreaterThan(20d);
        }
        var point = Center(window, firstHandle);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        await Assert.That(first.IsMoveSelected).IsTrue();
        await Assert.That(second.IsMoveSelected).IsTrue();
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        await Assert.That(feed.Days.Any(day => day.MarkdownEditor.HasMoveSelection)).IsFalse();
    });

    [Test]
    public Task ConflictDocumentDisablesMutationsButKeepsCopyAvailable() => WithFeed(async (window, view, feed) =>
    {
        var block = ContentBlock(feed, 9);
        var tab = new FeedThematicDocumentViewModel(block.Owner.Snapshot!.RelativePath, block.Owner, ownsEditor: false)
        {
            ExternalChangeMessage = "Требуется разрешить конфликт файла"
        };
        feed.DocumentWorkspace.Documents.Add(tab);
        Click(window, Find<MarkdownBlockPreviewControl>(view, block.PreviewAutomationId), MouseButton.Right);
        var menu = Find<Grid>(view, block.BlockAutomationId).ContextMenu!;
        await Assert.That(menu.IsOpen).IsTrue();
        await Assert.That(menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, L10n.Get("Copy"))).IsEnabled).IsTrue();
        foreach (var key in new[] { "FeedToolbarTask", "FeedToolbarNote", "FeedSearchArea", "FeedBlockMoveToArea" })
            await Assert.That(menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, L10n.Get(key))).IsEnabled).IsFalse();
        menu.Close();
        await Assert.That(block.Owner.ActiveBlock).IsNull();
    });

    [Test]
    public Task ClickingFeedBackgroundClearsSelectionAcrossDays() => WithFeed(async (window, view, feed) =>
    {
        var first = ContentBlock(feed, 9);
        var second = ContentBlock(feed, 8);
        Click(window, Find<ToggleButton>(view, first.MoveHandleAutomationId), MouseButton.Left);
        Click(window, Find<ToggleButton>(view, second.MoveHandleAutomationId), MouseButton.Left, RawInputModifiers.Control);
        await Assert.That(first.IsMoveSelected && second.IsMoveSelected).IsTrue();
        // The reading column leaves real empty space alongside the day's text.
        var row = Find<Grid>(view, first.BlockAutomationId);
        var point = row.TranslatePoint(new Point(-5, row.Bounds.Height / 2), window)!.Value;
        await Assert.That(new Rect(window.ClientSize).Contains(point)).IsTrue();
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        await Assert.That(feed.Days.Any(day => day.MarkdownEditor.HasMoveSelection)).IsFalse();
    });

    [Test]
    public Task EditingContextPreservesDraftAndCommitsCurrentBlockBeforeTaskAction() => WithFeed(async (window, view, feed) =>
    {
        var block = ContentBlock(feed, 9);
        var editor = block.Owner;
        Click(window, Find<MarkdownBlockPreviewControl>(view, block.PreviewAutomationId), MouseButton.Left);
        await WaitUntil(() => block.IsEditing);
        var text = Find<TextBox>(view, block.EditorAutomationId);
        text.Focus();
        window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, null);
        window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.A, null);
        const string draft = "# Изменённый заголовок";
        window.KeyTextInput(draft);
        Dispatcher.UIThread.RunJobs();
        await Assert.That(block.EditorText).IsEqualTo(draft);
        Click(window, text, MouseButton.Right);
        await Assert.That(editor.ActiveBlock).IsSameReferenceAs(block);
        await Assert.That(block.EditorText).IsEqualTo(draft);
        var row = Find<Grid>(view, block.BlockAutomationId);
        var menu = row.ContextMenu;
        await Assert.That(menu).IsNotNull();
        await Assert.That(menu!.IsOpen).IsTrue();
        var headers = menu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray();
        foreach (var key in new[] { "Cut", "Copy", "Paste", "SelectAll" })
            await Assert.That(headers.Contains(L10n.Get(key))).IsTrue();
        var task = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, L10n.Get("FeedToolbarTask")));
        await Assert.That(task.IsEnabled).IsTrue();
        task.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, task));
        menu.Close();
        await WaitUntil(() => feed.CurrentReview is not null);
        await Assert.That(feed.CurrentReview!.SelectedMarkdown.Trim()).IsEqualTo(draft);
        await Assert.That(feed.CurrentReview.RelativePath).IsEqualTo(block.Owner.Snapshot!.RelativePath);
        await Assert.That(editor.Snapshot!.Raw.Contains(draft, StringComparison.Ordinal)).IsTrue();
    });

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task KeyboardOpensNativeContextMenu(bool useMenuKey) => WithFeed(async (window, view, feed) =>
    {
        var block = ContentBlock(feed, 9);
        var handle = Find<ToggleButton>(view, block.MoveHandleAutomationId);
        handle.BringIntoView();
        handle.Focus();
        Dispatcher.UIThread.RunJobs();
        if (useMenuKey)
        {
            window.KeyPress(Key.Apps, RawInputModifiers.None, default, null);
            window.KeyRelease(Key.Apps, RawInputModifiers.None, default, null);
        }
        else
        {
            window.KeyPress(Key.F10, RawInputModifiers.Shift, PhysicalKey.F10, null);
            window.KeyRelease(Key.F10, RawInputModifiers.Shift, PhysicalKey.F10, null);
        }
        Dispatcher.UIThread.RunJobs();
        var row = Find<Grid>(view, block.BlockAutomationId);
        var menu = handle.ContextMenu is { IsOpen: true } direct ? direct : row.ContextMenu;
        await Assert.That(menu).IsNotNull();
        await Assert.That(menu!.IsOpen).IsTrue();
        await Assert.That(menu.Items.OfType<MenuItem>().Any(item => Equals(item.Header, L10n.Get("FeedToolbarNote")))).IsTrue();
        await Assert.That(block.Owner.ActiveBlock).IsNull();
        menu.Close();
    });

    private static MarkdownLiveBlockViewModel ContentBlock(FeedViewModel feed, int day) =>
        feed.Days.Single(item => item.Date == new DateOnly(2026, 9, day)).MarkdownEditor.Blocks.First(block => block.Block.IsContent);

    private static T Find<T>(Control root, string id) where T : Control => root.GetVisualDescendants().OfType<T>()
        .First(control => AutomationProperties.GetAutomationId(control) == id);

    private static Point Center(Window window, Control control)
    {
        control.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The test control has no window coordinates.");
        if (!window.Bounds.Contains(point)) throw new InvalidOperationException("The test control is outside the viewport.");
        return point;
    }

    private static void Click(Window window, Control control, MouseButton button, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = Center(window, control);
        window.MouseMove(point);
        window.MouseDown(point, button, modifiers);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(point, button, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return;
            await Task.Delay(25);
        }
        throw new InvalidOperationException("The requested editor/menu state was not reached.");
    }

    private static async Task WithFeed(Func<Window, FeedControl, FeedViewModel, Task> action)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            Directory.CreateDirectory(Path.Combine(directory.Path, "Ежедневные"));
            await File.WriteAllTextAsync(Path.Combine(directory.Path, "Ежедневные", "2026-09-09.md"), "# Первый день\n\nПервый текст\n");
            await File.WriteAllTextAsync(Path.Combine(directory.Path, "Ежедневные", "2026-09-08.md"), "# Второй день\n\nВторой текст\n");
            using var feed = new FeedViewModel(() => new DateOnly(2026, 9, 10));
            await feed.InitializeVaultAsync(directory.Path);
            var view = new FeedControl { DataContext = feed };
            var window = new Window { Content = view, Width = 1000, Height = 900 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                await action(window, view, feed);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
