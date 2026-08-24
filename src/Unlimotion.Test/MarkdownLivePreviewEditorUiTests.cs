using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.Notes.Recovery;
using Unlimotion.ViewModel.Feed;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class MarkdownLivePreviewEditorUiTests
{
    [Test]
    public async Task FocusedEditor_RemainsActiveAndFocusedAfterDebouncedAutosave()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var delay = new ManualAutosaveDelay();
            var source = new MarkdownLiveDocumentSnapshot("До\n", "revision-1", false, "note.md");
            using var viewModel = new MarkdownLivePreviewEditorViewModel(
                autosaveDelayAsync: delay.DelayAsync);
            var commitCount = 0;
            viewModel.CommitBlockAsync = (patch, _) =>
            {
                commitCount++;
                return Task.FromResult(MarkdownBlockCommitResult.Accepted(source with
                {
                    Raw = patch.PatchedDocumentRaw,
                    ExpectedRevisionHash = "revision-2"
                }));
            };
            viewModel.Load(source);
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var outside = new Button { Content = "Вне редактора" };
            var content = new StackPanel { Children = { view, outside } };
            var window = new Window { Width = 720, Height = 420, Content = content };
            try
            {
                window.Show();
                RunLayoutJobs();
                var preview = FindControlByAutomationId<MarkdownBlockPreviewControl>(view, "MarkdownLivePreview-BlockPreview-0");
                var editor = FindControlByAutomationId<TextBox>(view, "MarkdownLivePreview-BlockEditor-0");
                preview.Focus();
                PressKey(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.None);
                await Assert.That(WaitFor(() => editor.IsVisible && editor.IsFocused)).IsTrue();

                editor.Text = "После autosave";
                RunLayoutJobs();
                await Assert.That(delay.Count).IsEqualTo(1);
                delay.Release(0);
                await Assert.That(WaitFor(() => viewModel.Snapshot?.ExpectedRevisionHash == "revision-2")).IsTrue();

                await Assert.That(viewModel.ActiveBlock).IsNotNull();
                await Assert.That(viewModel.ActiveBlock!.IsEditing).IsTrue();
                await Assert.That(editor.IsVisible).IsTrue();
                await Assert.That(editor.IsEnabled).IsTrue();
                await Assert.That(editor.IsFocused).IsTrue();

                outside.Focus();
                await Assert.That(WaitFor(() => viewModel.ActiveBlock is null)).IsTrue();
                await Assert.That(commitCount).IsEqualTo(1);
                await Assert.That(viewModel.Snapshot!.Raw).IsEqualTo("После autosave\n");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Preview_RendersRequiredBlocks_AndKeepsUnsafeLinkInactive()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            const string raw = "# Заголовок\n\nАбзац с *акцентом*, [ссылкой](https://example.org) и [опасной](javascript:run()).\n\n- [ ] Задача\n\n> Цитата\n\n```js\nalert('только текст')\n```\n\n---\n\n<div onclick=\"run()\">raw</div>\n";
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            viewModel.CommitBlockAsync = (_, _) => throw new InvalidOperationException("Commit is not expected.");
            viewModel.Load(new MarkdownLiveDocumentSnapshot(raw, "revision-1", false, "note.md"));
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 800, Height = 640, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();

                var safeLink = FindControlByAutomationId<Button>(view, "MarkdownLivePreview-Link-2-0");
                var blockedLink = FindControlByAutomationId<TextBlock>(view, "MarkdownLivePreview-BlockedLink-2-1");
                var rawFallback = view.GetVisualDescendants()
                    .OfType<SelectableTextBlock>()
                    .Single(control => AutomationProperties.GetAutomationId(control)?.StartsWith(
                        "MarkdownLivePreview-RawFallback-",
                        StringComparison.Ordinal) == true);

                await Assert.That(safeLink.IsEnabled).IsTrue();
                await Assert.That(blockedLink.Text).IsEqualTo("опасной");
                await Assert.That(rawFallback.Text).IsEqualTo("<div onclick=\"run()\">raw</div>");
                await Assert.That(viewModel.Blocks.Any(block => block.RenderKind == MarkdownLiveBlockRenderKind.TaskListItem)).IsTrue();
                await Assert.That(viewModel.Blocks.Any(block => block.RenderKind == MarkdownLiveBlockRenderKind.FencedCode)).IsTrue();
                await Assert.That(viewModel.Blocks.Any(block => block.RenderKind == MarkdownLiveBlockRenderKind.HorizontalRule)).IsTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Keyboard_F2Edits_EscapeCancels_AndCtrlEnterCommitsExactBlock()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            const string raw = "Первый\r\n\r\nВторой\r\n";
            var source = new MarkdownLiveDocumentSnapshot(raw, "revision-1", true, "Ежедневные/2026-08-24.md");
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            MarkdownBlockPatch? captured = null;
            viewModel.CommitBlockAsync = (patch, _) =>
            {
                captured = patch;
                return Task.FromResult(MarkdownBlockCommitResult.Accepted(source with
                {
                    Raw = patch.PatchedDocumentRaw,
                    ExpectedRevisionHash = "revision-2"
                }));
            };
            viewModel.Load(source);
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 720, Height = 420, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                var preview = FindControlByAutomationId<MarkdownBlockPreviewControl>(view, "MarkdownLivePreview-BlockPreview-0");
                var editor = FindControlByAutomationId<TextBox>(view, "MarkdownLivePreview-BlockEditor-0");

                preview.Focus();
                PressKey(window, Key.F2, PhysicalKey.F2, RawInputModifiers.None);
                await Assert.That(WaitFor(() => editor.IsVisible && editor.IsFocused)).IsTrue();
                editor.Text = "Отменённое изменение";
                PressKey(window, Key.Escape, PhysicalKey.Escape, RawInputModifiers.None);

                await Assert.That(viewModel.Snapshot!.Raw).IsEqualTo(raw);
                await Assert.That(viewModel.ActiveBlock).IsNull();

                preview = FindControlByAutomationId<MarkdownBlockPreviewControl>(view, "MarkdownLivePreview-BlockPreview-0");
                editor = FindControlByAutomationId<TextBox>(view, "MarkdownLivePreview-BlockEditor-0");
                preview.Focus();
                PressKey(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.None);
                await Assert.That(WaitFor(() => editor.IsVisible && editor.IsFocused)).IsTrue();
                editor.Text = "Сохранённое\nизменение";
                PressKey(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.Control);

                await Assert.That(WaitFor(() => captured is not null)).IsTrue();
                await Assert.That(captured!.ReplacementRaw).IsEqualTo("Сохранённое\r\nизменение\r\n");
                await Assert.That(viewModel.Snapshot!.Raw).IsEqualTo("Сохранённое\r\nизменение\r\n\r\nВторой\r\n");
                await Assert.That(viewModel.ActiveBlock).IsNull();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task Blur_CommitsDirtyBlockThroughExpectedRevisionCallback()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var source = new MarkdownLiveDocumentSnapshot("До\n", "revision-1", false, "note.md");
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            MarkdownBlockPatch? captured = null;
            viewModel.CommitBlockAsync = (patch, _) =>
            {
                captured = patch;
                return Task.FromResult(MarkdownBlockCommitResult.Accepted(source with
                {
                    Raw = patch.PatchedDocumentRaw,
                    ExpectedRevisionHash = "revision-2"
                }));
            };
            viewModel.Load(source);
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var outside = new Button { Content = "Вне редактора" };
            var content = new StackPanel { Children = { view, outside } };
            var window = new Window { Width = 720, Height = 420, Content = content };
            try
            {
                window.Show();
                RunLayoutJobs();
                var preview = FindControlByAutomationId<MarkdownBlockPreviewControl>(view, "MarkdownLivePreview-BlockPreview-0");
                var editor = FindControlByAutomationId<TextBox>(view, "MarkdownLivePreview-BlockEditor-0");
                preview.Focus();
                PressKey(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.None);
                await Assert.That(WaitFor(() => editor.IsVisible && editor.IsFocused)).IsTrue();

                editor.Text = "После";
                outside.Focus();

                await Assert.That(WaitFor(() => captured is not null)).IsTrue();
                await Assert.That(captured!.ExpectedRevisionHash).IsEqualTo("revision-1");
                await Assert.That(viewModel.Snapshot!.Raw).IsEqualTo("После\n");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task RecoveryBannerRequiresExplicitRestoreOrDiscard()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var drafts = new MemoryFeedDraftStore();
            var draft = new FeedDraft(
                1,
                "vault1",
                "note.md",
                0,
                "revision-1",
                "Восстановленный блок",
                DateTimeOffset.UtcNow,
                "Восстановленный блок\n",
                false);
            await drafts.SaveAsync(draft);
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            viewModel.ConfigureDraftPersistence("vault1", drafts);
            viewModel.CommitBlockAsync = (_, _) => throw new InvalidOperationException("Commit is not expected.");
            viewModel.Load(new MarkdownLiveDocumentSnapshot("Исходный блок\n", "revision-1", false, "note.md"));
            viewModel.OfferRecoveryDraft(draft);
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 720, Height = 420, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                var recovery = FindControlByAutomationId<Border>(view, "MarkdownLivePreview-DraftRecovery");
                var recoveryContent = FindControlByAutomationId<TextBox>(view, "MarkdownLivePreview-DraftContent");
                var restore = FindControlByAutomationId<Button>(view, "MarkdownLivePreview-DraftRestore");
                var discard = FindControlByAutomationId<Button>(view, "MarkdownLivePreview-DraftDiscard");

                await Assert.That(recovery.IsVisible).IsTrue();
                await Assert.That(recoveryContent.Text).IsEqualTo("Восстановленный блок");
                await Assert.That(restore.IsEnabled).IsTrue();
                await Assert.That(viewModel.ActiveBlock).IsNull();

                restore.Command!.Execute(restore.CommandParameter);
                RunLayoutJobs();
                await viewModel.FlushDraftPersistenceAsync();

                await Assert.That(viewModel.HasRecoveryDraft).IsFalse();
                await Assert.That(viewModel.ActiveBlock!.EditorText).IsEqualTo("Восстановленный блок");
                await Assert.That((await drafts.ListAsync("vault1")).Single().RawMarkdown)
                    .IsEqualTo("Восстановленный блок");

                viewModel.CancelActiveEdit();
                await viewModel.FlushDraftPersistenceAsync();
                await drafts.SaveAsync(draft);
                viewModel.OfferRecoveryDraft(draft);
                RunLayoutJobs();
                discard = FindControlByAutomationId<Button>(view, "MarkdownLivePreview-DraftDiscard");
                discard.Command!.Execute(discard.CommandParameter);

                await Assert.That(WaitFor(() => !viewModel.HasRecoveryDraft)).IsTrue();
                await Assert.That(await drafts.ListAsync("vault1")).IsEmpty();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static T FindControlByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        return root.GetVisualDescendants()
                   .OfType<T>()
                   .FirstOrDefault(control => string.Equals(
                       AutomationProperties.GetAutomationId(control),
                       automationId,
                       StringComparison.Ordinal))
               ?? throw new InvalidOperationException($"Control with AutomationId '{automationId}' was not found.");
    }

    private static void PressKey(
        Window window,
        Key key,
        PhysicalKey physicalKey,
        RawInputModifiers modifiers)
    {
        window.KeyPress(key, modifiers, physicalKey, null);
        window.KeyRelease(key, modifiers, physicalKey, null);
        RunLayoutJobs();
    }

    private static bool WaitFor(Func<bool> predicate, int timeoutMilliseconds = 4000)
    {
        return SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return predicate();
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));
    }

    private static void RunLayoutJobs()
    {
        for (var index = 0; index < 20; index++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }
}
