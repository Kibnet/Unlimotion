using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel.Feed;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class FeedDocumentWorkspaceUiTests
{
    [Test]
    public async Task PortableTabsDisambiguateEqualNamesAndKeepOverflowReachable()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            foreach (var folder in new[] { "Работа", "Личное" })
            {
                Directory.CreateDirectory(Path.Combine(directory.Path, folder));
                await File.WriteAllTextAsync(Path.Combine(directory.Path, folder, "Планы.md"), "Планы\n");
            }
            using var feed = new FeedViewModel();
            await feed.InitializeVaultAsync(directory.Path);
            await feed.OpenVaultLinkAsync("Работа/Планы.md", null);
            await feed.OpenVaultLinkAsync("Личное/Планы.md", null);
            var tabs = new FeedDocumentTabs { DataContext = feed };
            var window = new Window { Width = 360, Height = 180, Content = tabs };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                foreach (var path in new[] { "Работа/Планы.md", "Личное/Планы.md" })
                {
                    var button = tabs.GetVisualDescendants().OfType<Button>().Single(control =>
                        AutomationProperties.GetAutomationId(control) == "FeedDocumentTab-" + path);
                    await Assert.That(((TextBlock)button.Content!).Text).IsEqualTo(path);
                }
                var overflow = tabs.GetVisualDescendants().OfType<Button>().Single(control =>
                    AutomationProperties.GetAutomationId(control) == "FeedDocumentTabList");
                var position = overflow.TranslatePoint(default, window)!.Value;
                await Assert.That(position.X + overflow.Bounds.Width).IsLessThanOrEqualTo(window.Bounds.Width);
                await Assert.That(overflow.ContextMenu!.Items.Count).IsEqualTo(3);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task MouseTabSwitchWaitsForFocusSaveAndRestoresCaretOnReturn()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            await File.WriteAllTextAsync(Path.Combine(directory.Path, "A.md"), "Первый документ\n");
            await File.WriteAllTextAsync(Path.Combine(directory.Path, "B.md"), "Второй документ\n");
            using var feed = new FeedViewModel(() => new DateOnly(2026, 9, 9));
            await feed.InitializeVaultAsync(directory.Path);
            var view = new FeedControl { DataContext = feed };
            var window = new Window { Width = 800, Height = 550, Content = view };
            try
            {
                window.Show();
                await feed.OpenVaultLinkAsync("A", null);
                var first = feed.OpenedThematicFile!;
                await feed.OpenVaultLinkAsync("B", null);
                var second = feed.OpenedThematicFile!;
                await feed.ActivateDocumentAsync(first);
                var editor = first.MarkdownEditor;
                editor.BeginEdit(editor.Blocks.First(block => block.Block.IsContent));
                Dispatcher.UIThread.RunJobs();
                var input = view.GetVisualDescendants().OfType<TextBox>().Single(control => control.IsEffectivelyVisible
                    && AutomationProperties.GetAutomationId(control) == editor.ActiveBlock!.EditorAutomationId);
                input.Focus();
                input.Text = "Изменённый первый документ";
                input.SelectionStart = 3;
                input.SelectionEnd = 7;
                var original = editor.CommitBlockAsync!;
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var writes = 0;
                editor.CommitBlockAsync = async (patch, token) =>
                {
                    writes++;
                    started.TrySetResult();
                    await release.Task.WaitAsync(token);
                    return await original(patch, token);
                };
                var button = view.GetVisualDescendants().OfType<Button>().Single(control =>
                    AutomationProperties.GetAutomationId(control) == "FeedDocumentTab-B.md");
                var point = button.TranslatePoint(new Point(8, 8), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
                release.SetResult();
                for (var i = 0; i < 100 && !ReferenceEquals(feed.OpenedThematicFile, second); i++)
                { Dispatcher.UIThread.RunJobs(); await Task.Delay(20); }
                await Assert.That(feed.OpenedThematicFile).IsSameReferenceAs(second);
                await Assert.That(writes).IsEqualTo(1);
                await feed.ActivateDocumentAsync(first);
                Dispatcher.UIThread.RunJobs();
                input = view.GetVisualDescendants().OfType<TextBox>().Single(control => control.IsEffectivelyVisible
                    && AutomationProperties.GetAutomationId(control) == editor.ActiveBlock!.EditorAutomationId);
                await Assert.That(input.SelectionStart).IsEqualTo(3);
                await Assert.That(input.SelectionEnd).IsEqualTo(7);
                await Assert.That(input.IsFocused).IsTrue();
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task LinksDeduplicateShareDailyEditorAndScrollToDocumentEnd()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            Directory.CreateDirectory(Path.Combine(directory.Path, "Ежедневные"));
            await File.WriteAllTextAsync(Path.Combine(directory.Path, "Ежедневные", "2026-09-09.md"), "Сегодня\n");
            await File.WriteAllTextAsync(Path.Combine(directory.Path, "Long.md"), string.Join("\n\n", Enumerable.Range(1, 200).Select(i => $"Строка {i}")));
            using var feed = new FeedViewModel(() => new DateOnly(2026, 9, 9));
            await feed.InitializeVaultAsync(directory.Path);
            var view = new FeedControl { DataContext = feed };
            var window = new Window { Width = 800, Height = 550, Content = view };
            try
            {
                window.Show();
                await feed.OpenVaultLinkAsync("Ежедневные/2026-09-09", null);
                await Assert.That(ReferenceEquals(feed.OpenedThematicFile!.MarkdownEditor, feed.Days[0].MarkdownEditor)).IsTrue();
                await feed.OpenVaultLinkAsync("Long", null);
                var document = feed.OpenedThematicFile;
                await feed.OpenVaultLinkAsync("Long.md", null);
                await Assert.That(ReferenceEquals(document, feed.OpenedThematicFile)).IsTrue();
                await Assert.That(feed.DocumentWorkspace.Documents.Count).IsEqualTo(2);
                Dispatcher.UIThread.RunJobs();
                var scroller = view.GetVisualDescendants().OfType<ScrollViewer>().Single(x => x.Name == "DocumentScroller");
                await Assert.That(scroller.Extent.Height).IsGreaterThan(scroller.Viewport.Height);
                scroller.Offset = new Vector(0, scroller.Extent.Height);
                Dispatcher.UIThread.RunJobs();
                await Assert.That(scroller.Offset.Y).IsGreaterThan(0d);
                var end = view.GetVisualDescendants().OfType<MarkdownBlockPreviewControl>().Last();
                await Assert.That(end.TranslatePoint(default, scroller)!.Value.Y).IsLessThan(scroller.Viewport.Height);
                var lastBlock = document!.MarkdownEditor.Blocks.Last(block => block.Block.IsContent);
                await Assert.That(document.MarkdownEditor.BeginEdit(lastBlock)).IsTrue();
                Dispatcher.UIThread.RunJobs();
                var input = scroller.GetVisualDescendants().OfType<TextBox>().Single(control =>
                    control.IsEffectivelyVisible && control.DataContext == lastBlock);
                input.Focus();
                window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
                window.KeyTextInput(" — сохранено внизу");
                await Assert.That(input.IsFocused).IsTrue();
                await Assert.That(input.TranslatePoint(default, scroller)!.Value.Y).IsGreaterThanOrEqualTo(0d);
                await Assert.That(input.TranslatePoint(default, scroller)!.Value.Y).IsLessThan(scroller.Viewport.Height);
                await Assert.That(await document.MarkdownEditor.CommitActiveAsync()).IsTrue();
                await Assert.That(await File.ReadAllTextAsync(Path.Combine(directory.Path, "Long.md")))
                    .Contains("Строка 200 — сохранено внизу");
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task FailedSaveKeepsTabAndVaultWhileSuccessfulReturnRestoresTabs()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var first = new TempNotesDirectory();
            using var second = new TempNotesDirectory();
            await File.WriteAllTextAsync(Path.Combine(first.Path, "Note.md"), "Исходный текст\n");
            using var feed = new FeedViewModel(() => new DateOnly(2026, 9, 9));
            await feed.InitializeVaultAsync(first.Path);
            await feed.OpenVaultLinkAsync("Note", null);
            var tab = feed.OpenedThematicFile!;
            var editor = tab.MarkdownEditor;
            var originalCommit = editor.CommitBlockAsync;
            editor.CommitBlockAsync = (_, _) => Task.FromResult(MarkdownBlockCommitResult.Rejected("test-save-failure"));
            editor.BeginEdit(editor.Blocks.First(block => block.Block.IsContent));
            editor.ActiveBlock!.EditorText = "Нельзя потерять";
            editor.ActiveBlock.EditorSelectionStart = 3;
            editor.ActiveBlock.EditorSelectionEnd = 7;
            await feed.CloseDocumentAsync(tab);
            await Assert.That(feed.DocumentWorkspace.Documents.Contains(tab)).IsTrue();
            await feed.InitializeVaultAsync(second.Path);
            await Assert.That(feed.VaultRootPath).IsEqualTo(first.Path);
            await Assert.That(editor.ActiveBlock!.EditorText).IsEqualTo("Нельзя потерять");
            editor.CommitBlockAsync = originalCommit;
            await feed.InitializeVaultAsync(second.Path);
            await feed.InitializeVaultAsync(first.Path);
            await Assert.That(feed.DocumentWorkspace.Documents.Count).IsEqualTo(1);
            await Assert.That(feed.OpenedThematicFile!.RelativePath).IsEqualTo("Note.md");
            await Assert.That(feed.OpenedThematicFile.MarkdownEditor.ActiveBlock!.EditorSelectionStart).IsEqualTo(3);
            await Assert.That(feed.OpenedThematicFile.MarkdownEditor.ActiveBlock!.EditorSelectionEnd).IsEqualTo(7);
            await Assert.That(await File.ReadAllTextAsync(Path.Combine(first.Path, "Note.md"))).Contains("Нельзя потерять");
        }, CancellationToken.None);
    }
}
