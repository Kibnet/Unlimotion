using System;
using System.Collections.Generic;
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
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.Notes.Markdown;
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
    public async Task Keyboard_F2Edits_EscapeCancels_CtrlEnterAddsLine_AndCtrlSCommits()
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
                editor.Text = "Сохранённое изменение";
                editor.SelectionStart = "Сохранённое".Length;
                editor.SelectionEnd = editor.SelectionStart;
                PressKey(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.Control);

                await Assert.That(captured).IsNull();
                await Assert.That(viewModel.ActiveBlock).IsNotNull();
                await Assert.That(viewModel.ActiveBlock!.EditorText).IsEqualTo("Сохранённое\r\n изменение");

                PressKey(window, Key.S, PhysicalKey.S, RawInputModifiers.Control);
                await Assert.That(WaitFor(() => captured is not null)).IsTrue();
                await Assert.That(captured!.ReplacementRaw).IsEqualTo("Сохранённое\r\n изменение\r\n");
                await Assert.That(viewModel.Snapshot!.Raw).IsEqualTo("Сохранённое\r\n изменение\r\n\r\nВторой\r\n");
                await Assert.That(viewModel.ActiveBlock).IsNotNull();
                var reopenedEditor = FindControlByAutomationId<TextBox>(view, viewModel.ActiveBlock!.EditorAutomationId);
                await Assert.That(WaitFor(() => reopenedEditor.IsFocused)).IsTrue();
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
    public async Task EnterSplitsParagraphAndFocusesRightBlock()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            const string raw = "Левая правая\r\n\r\nСледующий\r\n";
            var source = new MarkdownLiveDocumentSnapshot(raw, "revision-1", true, "note.md");
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            viewModel.CommitBlockAsync = (patch, _) => Task.FromResult(MarkdownBlockCommitResult.Accepted(source with
            {
                Raw = patch.PatchedDocumentRaw,
                ExpectedRevisionHash = "revision-2"
            }));
            viewModel.Load(source);
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 720, Height = 420, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                var first = viewModel.Blocks.First(static block => block.Kind == Unlimotion.Notes.Markdown.MarkdownBlockKind.Paragraph);
                viewModel.BeginEdit(first);
                var editor = FindControlByAutomationId<TextBox>(view, first.EditorAutomationId);
                editor.Focus();
                editor.SelectionStart = "Левая ".Length;
                editor.SelectionEnd = editor.SelectionStart;

                PressKey(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.None);

                await Assert.That(WaitFor(() => viewModel.Snapshot?.ExpectedRevisionHash == "revision-2")).IsTrue();
                await Assert.That(viewModel.Snapshot!.Raw).IsEqualTo("Левая \r\n\r\nправая\r\n\r\nСледующий\r\n");
                await Assert.That(viewModel.ActiveBlock?.PreviewText).IsEqualTo("правая");
                await Assert.That(FindControlByAutomationId<TextBox>(view, viewModel.ActiveBlock!.EditorAutomationId).IsFocused).IsTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task EnterAtBlockEndCreatesSessionBlockAndWritesOnlyAfterTyping()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var persisted = new MarkdownLiveDocumentSnapshot("Конец\r\n", "revision-1", true, "note.md");
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            MarkdownBlockPatch? captured = null;
            viewModel.CommitBlockAsync = (patch, _) =>
            {
                captured = patch;
                persisted = persisted with
                {
                    Raw = patch.PatchedDocumentRaw,
                    ExpectedRevisionHash = "revision-2"
                };
                return Task.FromResult(MarkdownBlockCommitResult.Accepted(persisted));
            };
            viewModel.Load(persisted);
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 720, Height = 420, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                var block = viewModel.Blocks.Single(static candidate => candidate.Kind == MarkdownBlockKind.Paragraph);
                viewModel.BeginEdit(block);
                var editor = FindControlByAutomationId<TextBox>(view, block.EditorAutomationId);
                editor.Focus();
                editor.SelectionStart = editor.Text!.Length;
                editor.SelectionEnd = editor.SelectionStart;

                PressKey(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.None);

                await Assert.That(WaitFor(() => viewModel.ActiveBlock?.IsSessionBlock == true)).IsTrue();
                await Assert.That(captured).IsNull();
                await Assert.That(viewModel.Snapshot!.Raw).IsEqualTo("Конец\r\n");

                var sessionEditor = FindControlByAutomationId<TextBox>(
                    view,
                    viewModel.ActiveBlock!.EditorAutomationId);
                sessionEditor.Text = "Новый блок";
                PressKey(window, Key.S, PhysicalKey.S, RawInputModifiers.Control);

                await Assert.That(WaitFor(() => captured is not null)).IsTrue();
                await Assert.That(viewModel.Snapshot!.Raw).IsEqualTo("Конец\r\n\r\nНовый блок\r\n");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task EnterSplitsCompletedTaskIntoUncheckedSibling()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var source = new MarkdownLiveDocumentSnapshot("- [x] Левая правая\r\n", "revision-1", true, "note.md");
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            viewModel.CommitBlockAsync = (patch, _) => Task.FromResult(MarkdownBlockCommitResult.Accepted(source with
            {
                Raw = patch.PatchedDocumentRaw,
                ExpectedRevisionHash = "revision-2"
            }));
            viewModel.Load(source);
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 720, Height = 420, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                var task = viewModel.Blocks.Single(static block => block.Kind == MarkdownBlockKind.TaskListItem);
                viewModel.BeginEdit(task);
                var editor = FindControlByAutomationId<TextBox>(view, task.EditorAutomationId);
                editor.Focus();
                editor.SelectionStart = "- [x] Левая ".Length;
                editor.SelectionEnd = editor.SelectionStart;

                PressKey(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.None);

                await Assert.That(WaitFor(() => viewModel.Snapshot?.ExpectedRevisionHash == "revision-2")).IsTrue();
                await Assert.That(viewModel.Snapshot!.Raw)
                    .IsEqualTo("- [x] Левая \r\n\r\n- [ ] правая\r\n");
                await Assert.That(viewModel.ActiveBlock?.Kind).IsEqualTo(MarkdownBlockKind.TaskListItem);
                await Assert.That(viewModel.ActiveBlock?.PreviewText).IsEqualTo("правая");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task HeadingRejectsCtrlEnterAndEnterCreatesParagraphSessionBlock()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var persisted = new MarkdownLiveDocumentSnapshot("### Заголовок\r\n", "revision-1", true, "note.md");
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            viewModel.CommitBlockAsync = (patch, _) =>
            {
                persisted = persisted with
                {
                    Raw = patch.PatchedDocumentRaw,
                    ExpectedRevisionHash = "revision-2"
                };
                return Task.FromResult(MarkdownBlockCommitResult.Accepted(persisted));
            };
            viewModel.Load(persisted);
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 720, Height = 420, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                var heading = viewModel.Blocks.Single(static block => block.Kind == MarkdownBlockKind.Heading);
                viewModel.BeginEdit(heading);
                var editor = FindControlByAutomationId<TextBox>(view, heading.EditorAutomationId);
                editor.Focus();
                editor.SelectionStart = editor.Text!.Length;
                editor.SelectionEnd = editor.SelectionStart;

                PressKey(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.Control);
                await Assert.That(heading.EditorText).IsEqualTo("### Заголовок");
                await Assert.That(viewModel.Snapshot!.Raw).IsEqualTo("### Заголовок\r\n");

                PressKey(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.None);
                await Assert.That(WaitFor(() => viewModel.ActiveBlock?.IsSessionBlock == true)).IsTrue();
                var paragraphEditor = FindControlByAutomationId<TextBox>(
                    view,
                    viewModel.ActiveBlock!.EditorAutomationId);
                paragraphEditor.Text = "Текст";
                PressKey(window, Key.S, PhysicalKey.S, RawInputModifiers.Control);

                await Assert.That(WaitFor(() => viewModel.Snapshot?.ExpectedRevisionHash == "revision-2")).IsTrue();
                await Assert.That(viewModel.Snapshot!.Raw)
                    .IsEqualTo("### Заголовок\r\n\r\nТекст\r\n");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CtrlEnterKeepsTaskContinuationInsideOneBlock()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var source = new MarkdownLiveDocumentSnapshot("  - [ ] Левая правая\n", "revision-1", false, "note.md");
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            viewModel.CommitBlockAsync = (patch, _) => Task.FromResult(MarkdownBlockCommitResult.Accepted(source with
            {
                Raw = patch.PatchedDocumentRaw,
                ExpectedRevisionHash = "revision-2"
            }));
            viewModel.Load(source);
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 720, Height = 420, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                var task = viewModel.Blocks.Single(static block => block.Kind == MarkdownBlockKind.TaskListItem);
                viewModel.BeginEdit(task);
                var editor = FindControlByAutomationId<TextBox>(view, task.EditorAutomationId);
                editor.Focus();
                editor.SelectionStart = "  - [ ] Левая ".Length;
                editor.SelectionEnd = editor.SelectionStart;

                PressKey(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.Control);

                await Assert.That(task.EditorText).IsEqualTo("  - [ ] Левая \n    правая");
                await Assert.That(viewModel.Blocks.Count(static block => block.Kind == MarkdownBlockKind.TaskListItem))
                    .IsEqualTo(1);
                await Assert.That(viewModel.Snapshot!.Raw).IsEqualTo("  - [ ] Левая правая\n");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task SelectedBlocksShowToolbarAndTransformAtomicallyToChecklist()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var source = new MarkdownLiveDocumentSnapshot("Альфа\n\nБета\n", "revision-1", false, "note.md");
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
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
            var selected = viewModel.Blocks.Where(static block => block.IsMovable).ToArray();
            viewModel.SelectMoveBlock(selected[0], toggle: false, extendRange: false);
            viewModel.SelectMoveBlock(selected[1], toggle: true, extendRange: false);
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 620, Height = 420, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                var toolbar = FindControlByAutomationId<Border>(view, selected[0].ContextToolbarAutomationId);
                var checklist = toolbar.GetVisualDescendants()
                    .OfType<Button>()
                    .Single(button => string.Equals(button.Content as string, "☑", StringComparison.Ordinal));
                using (Assert.Multiple())
                {
                    await Assert.That(toolbar.IsEffectivelyVisible).IsTrue();
                    await Assert.That(checklist.IsEnabled).IsTrue();
                    await Assert.That(selected.All(static block => block.IsMoveSelected)).IsTrue();
                }

                checklist.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Assert.That(WaitFor(() => commitCount == 1)).IsTrue();
                await Assert.That(viewModel.Snapshot!.Raw).IsEqualTo("- [ ] Альфа\n\n- [ ] Бета\n");
                await Assert.That(viewModel.SelectedMoveBlockCount).IsEqualTo(2);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task PreviewTaskCheckboxTogglesMarkerWithoutEnteringEditMode()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var source = new MarkdownLiveDocumentSnapshot("- [ ] Сделать\n", "revision-1", false, "note.md");
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            viewModel.CommitBlockAsync = (patch, _) => Task.FromResult(MarkdownBlockCommitResult.Accepted(source with
            {
                Raw = patch.PatchedDocumentRaw,
                ExpectedRevisionHash = "revision-2"
            }));
            viewModel.Load(source);
            var task = viewModel.Blocks.Single(static block => block.Kind == Unlimotion.Notes.Markdown.MarkdownBlockKind.TaskListItem);
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 620, Height = 320, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                var checkbox = FindControlByAutomationId<CheckBox>(view, task.TaskCheckboxAutomationId);
                checkbox.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                await Assert.That(WaitFor(() => viewModel.Snapshot?.ExpectedRevisionHash == "revision-2")).IsTrue();
                await Assert.That(viewModel.Snapshot!.Raw).IsEqualTo("- [x] Сделать\n");
                await Assert.That(viewModel.ActiveBlock).IsNull();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task EnteringEditMode_KeepsExactBlockHeightWithoutFocusChrome()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            const string raw = "Длинный абзац, который занимает несколько строк и позволяет проверить стабильность геометрии блока при переключении из режима чтения в редактирование без визуального скачка.\n";
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            viewModel.CommitBlockAsync = (_, _) => throw new InvalidOperationException("Commit is not expected.");
            viewModel.Load(new MarkdownLiveDocumentSnapshot(raw, "revision-1", false, "note.md"));
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 420, Height = 360, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                var block = FindControlByAutomationId<Grid>(view, "MarkdownLivePreview-Block-0");
                var preview = FindControlByAutomationId<MarkdownBlockPreviewControl>(view, "MarkdownLivePreview-BlockPreview-0");
                var editor = FindControlByAutomationId<TextBox>(view, "MarkdownLivePreview-BlockEditor-0");
                var previewHeight = preview.Bounds.Height;
                var blockHeight = block.Bounds.Height;

                preview.Focus();
                PressKey(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.None);
                await Assert.That(WaitFor(() => editor.IsVisible && editor.IsFocused)).IsTrue();
                RunLayoutJobs();

                using (Assert.Multiple())
                {
                    await Assert.That(previewHeight).IsGreaterThan(0);
                    await Assert.That(editor.BorderThickness).IsEqualTo(new Thickness(0));
                    await Assert.That(editor.MinHeight).IsLessThan(42);
                    await Assert.That(Math.Abs(block.Bounds.Height - blockHeight)).IsLessThanOrEqualTo(1.0);
                    await Assert.That(view.GetVisualDescendants().OfType<Border>().Any(candidate =>
                            string.Equals(
                                AutomationProperties.GetAutomationId(candidate),
                                "MarkdownLivePreview-BlockFocusAccent-0",
                                StringComparison.Ordinal)))
                        .IsFalse();
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task MoveHandles_SupportMultiSelectionAndKeyboardMove()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            const string raw = "Альфа\n\nБета\n\nГамма\n\nДельта\n";
            var source = new MarkdownLiveDocumentSnapshot(raw, "revision-1", false, "note.md");
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            MarkdownBlocksMoveRequest? captured = null;
            viewModel.MoveBlocksAsync = (request, _) =>
            {
                captured = request;
                return Task.FromResult(MarkdownBlocksMoveResult.Accepted(
                    source with { ExpectedRevisionHash = "revision-2" },
                    request.SelectedBlockIndices));
            };
            viewModel.Load(source);
            var movable = viewModel.Blocks.Where(static block => block.IsMovable).ToArray();
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 520, Height = 420, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                var firstHandle = FindControlByAutomationId<ToggleButton>(view, movable[0].MoveHandleAutomationId);
                var secondHandle = FindControlByAutomationId<ToggleButton>(view, movable[1].MoveHandleAutomationId);

                viewModel.SelectMoveBlock(movable[0], toggle: false, extendRange: false);
                viewModel.SelectMoveBlock(movable[1], toggle: true, extendRange: false);
                RunLayoutJobs();

                using (Assert.Multiple())
                {
                    await Assert.That(viewModel.SelectedMoveBlockCount).IsEqualTo(2);
                    await Assert.That(firstHandle.IsChecked).IsTrue();
                    await Assert.That(secondHandle.IsChecked).IsTrue();
                    await Assert.That(viewModel.CanMoveSelectionDown).IsTrue();
                }

                var moveDown = new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Down,
                    PhysicalKey = PhysicalKey.ArrowDown,
                    KeyModifiers = KeyModifiers.Alt,
                    Source = secondHandle
                };
                secondHandle.RaiseEvent(moveDown);
                await Assert.That(moveDown.Handled).IsTrue();
                await Assert.That(WaitFor(() => captured is not null)).IsTrue();
                using (Assert.Multiple())
                {
                    await Assert.That(captured!.SelectedBlockIndices).IsEquivalentTo(
                        new[] { movable[0].Index, movable[1].Index });
                    await Assert.That(captured.InsertBeforeBlockIndex).IsEqualTo(movable[3].Index);
                    await Assert.That(viewModel.Snapshot!.ExpectedRevisionHash).IsEqualTo("revision-2");
                    await Assert.That(viewModel.SelectedMoveBlockCount).IsEqualTo(2);
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task MoveHandlePointerDrag_MovesSelectedBlockToDropTarget()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            const string raw = "Альфа\n\nБета\n\nГамма\n\nДельта\n";
            var source = new MarkdownLiveDocumentSnapshot(raw, "revision-1", false, "note.md");
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            MarkdownBlocksMoveRequest? captured = null;
            viewModel.MoveBlocksAsync = (request, _) =>
            {
                captured = request;
                return Task.FromResult(MarkdownBlocksMoveResult.Accepted(
                    source with { ExpectedRevisionHash = "revision-2" },
                    request.SelectedBlockIndices));
            };
            viewModel.Load(source);
            var movable = viewModel.Blocks.Where(static block => block.IsMovable).ToArray();
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 520, Height = 420, Content = view };
            var mouseIsDown = false;
            try
            {
                window.Show();
                RunLayoutJobs();
                var sourceHandle = FindControlByAutomationId<ToggleButton>(view, movable[0].MoveHandleAutomationId);
                var targetBlock = FindControlByAutomationId<Grid>(view, movable[2].BlockAutomationId);
                var start = sourceHandle.TranslatePoint(
                    new Point(sourceHandle.Bounds.Width / 2, sourceHandle.Bounds.Height / 2),
                    window) ?? throw new InvalidOperationException("The move handle is not attached to the test window.");
                var drop = targetBlock.TranslatePoint(
                    new Point(targetBlock.Bounds.Width / 2, targetBlock.Bounds.Height * 0.75),
                    window) ?? throw new InvalidOperationException("The target block is not attached to the test window.");
                var hit = window.InputHitTest(start);
                var hitBelongsToHandle = ReferenceEquals(hit, sourceHandle)
                    || hit is Visual visualHit && visualHit.GetVisualAncestors().Contains(sourceHandle);
                if (!hitBelongsToHandle)
                {
                    throw new InvalidOperationException(
                        $"The move handle hit test resolved to {hit?.GetType().FullName ?? "<null>"} instead of the handle.");
                }

                viewModel.SelectMoveBlock(movable[0], toggle: false, extendRange: false);
                viewModel.SelectMoveBlock(movable[1], toggle: true, extendRange: false);
                RunLayoutJobs();

                window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
                mouseIsDown = true;
                RunLayoutJobs();
                await Assert.That(viewModel.SelectedMoveBlockCount).IsEqualTo(2);
                window.MouseMove(drop, RawInputModifiers.LeftMouseButton);
                RunLayoutJobs();
                await Assert.That(movable[2].IsMoveDropAfter).IsTrue();
                window.MouseUp(drop, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                mouseIsDown = false;

                await Assert.That(WaitFor(() => captured is not null)).IsTrue();
                using (Assert.Multiple())
                {
                    await Assert.That(captured!.SelectedBlockIndices).IsEquivalentTo(
                        new[] { movable[0].Index, movable[1].Index });
                    await Assert.That(captured.InsertBeforeBlockIndex).IsEqualTo(movable[3].Index);
                    await Assert.That(viewModel.Snapshot!.ExpectedRevisionHash).IsEqualTo("revision-2");
                }
            }
            finally
            {
                if (mouseIsDown)
                {
                    window.MouseUp(default, MouseButton.Left, RawInputModifiers.None);
                }

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ArrowDownAcrossBlocks_PreservesPreferredCaretColumn()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            const string raw = "0123456789\n\nxy\n\nabcdefghij\n";
            var source = new MarkdownLiveDocumentSnapshot(raw, "revision-1", false, "note.md");
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            viewModel.CommitBlockAsync = (patch, _) => Task.FromResult(MarkdownBlockCommitResult.Accepted(source with
            {
                Raw = patch.PatchedDocumentRaw,
                ExpectedRevisionHash = "revision-1"
            }));
            viewModel.Load(source);
            var paragraphs = viewModel.Blocks.Where(static block => block.Kind == MarkdownBlockKind.Paragraph).ToArray();
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 720, Height = 420, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                viewModel.BeginEdit(paragraphs[0]);
                var editor = FindControlByAutomationId<TextBox>(view, paragraphs[0].EditorAutomationId);
                editor.Focus();
                editor.SelectionStart = 7;
                editor.SelectionEnd = 7;

                PressKey(window, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.None);

                await Assert.That(WaitFor(() => viewModel.ActiveBlock?.PreviewText == "xy")).IsTrue();
                RunLayoutJobs();
                editor = FindVisibleControlByAutomationId<TextBox>(view, viewModel.ActiveBlock!.EditorAutomationId);
                await Assert.That(WaitFor(() => editor.SelectionStart == 2)).IsTrue();

                PressKey(window, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.None);

                await Assert.That(WaitFor(() => viewModel.ActiveBlock?.PreviewText == "abcdefghij")).IsTrue();
                RunLayoutJobs();
                editor = FindVisibleControlByAutomationId<TextBox>(view, viewModel.ActiveBlock!.EditorAutomationId);
                await Assert.That(WaitFor(() => editor.SelectionStart == 7)).IsTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ArrowKeysAtDocumentEdges_KeepTheActiveEditorOpen()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var snapshot = new MarkdownLiveDocumentSnapshot("Альфа\n\nОмега\n", "revision-1", false, "note.md");
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            viewModel.CommitBlockAsync = (patch, _) => Task.FromResult(MarkdownBlockCommitResult.Accepted(snapshot with
            {
                Raw = patch.PatchedDocumentRaw,
                ExpectedRevisionHash = "revision-2"
            }));
            viewModel.Load(snapshot);
            var paragraphs = viewModel.Blocks.Where(static block => block.Kind == MarkdownBlockKind.Paragraph).ToArray();
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 420, Height = 260, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                viewModel.BeginEdit(paragraphs[0]);
                RunLayoutJobs();
                var editor = FindControlByAutomationId<TextBox>(view, paragraphs[0].EditorAutomationId);
                editor.Focus();
                editor.SelectionStart = 0;
                editor.SelectionEnd = 0;

                PressKey(window, Key.Up, PhysicalKey.ArrowUp, RawInputModifiers.None);
                PressKey(window, Key.Left, PhysicalKey.ArrowLeft, RawInputModifiers.None);

                using (Assert.Multiple())
                {
                    await Assert.That(viewModel.ActiveBlock).IsSameReferenceAs(paragraphs[0]);
                    await Assert.That(editor.IsFocused).IsTrue();
                    await Assert.That(editor.SelectionStart).IsEqualTo(0);
                }

                editor.SelectionStart = editor.Text!.Length;
                editor.SelectionEnd = editor.SelectionStart;
                PressKey(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.None);
                await Assert.That(WaitFor(() => viewModel.ActiveBlock?.PreviewText == "Омега")).IsTrue();
                editor = FindVisibleControlByAutomationId<TextBox>(view, viewModel.ActiveBlock!.EditorAutomationId);
                editor.SelectionStart = 0;
                editor.SelectionEnd = 0;
                PressKey(window, Key.Left, PhysicalKey.ArrowLeft, RawInputModifiers.None);
                await Assert.That(WaitFor(() => viewModel.ActiveBlock?.PreviewText == "Альфа")).IsTrue();

                editor = FindVisibleControlByAutomationId<TextBox>(view, viewModel.ActiveBlock!.EditorAutomationId);
                editor.SelectionStart = editor.Text!.Length;
                editor.SelectionEnd = editor.SelectionStart;
                PressKey(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.None);
                await Assert.That(WaitFor(() => viewModel.ActiveBlock?.PreviewText == "Омега")).IsTrue();
                editor = FindVisibleControlByAutomationId<TextBox>(view, viewModel.ActiveBlock!.EditorAutomationId);
                editor.SelectionStart = editor.Text!.Length;
                editor.SelectionEnd = editor.SelectionStart;

                PressKey(window, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.None);
                PressKey(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.None);

                using (Assert.Multiple())
                {
                    await Assert.That(viewModel.ActiveBlock?.PreviewText).IsEqualTo("Омега");
                    await Assert.That(editor.IsFocused).IsTrue();
                    await Assert.That(editor.SelectionStart).IsEqualTo(editor.Text.Length);
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ArrowUpAcrossBlocks_PreservesPreferredCaretColumn()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            const string raw = "0123456789\n\nxy\n\nabcdefghij\n";
            var source = new MarkdownLiveDocumentSnapshot(raw, "revision-1", false, "note.md");
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            viewModel.CommitBlockAsync = (patch, _) => Task.FromResult(MarkdownBlockCommitResult.Accepted(source with
            {
                Raw = patch.PatchedDocumentRaw,
                ExpectedRevisionHash = "revision-2"
            }));
            viewModel.Load(source);
            var paragraphs = viewModel.Blocks.Where(static block => block.Kind == MarkdownBlockKind.Paragraph).ToArray();
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 720, Height = 420, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                viewModel.BeginEdit(paragraphs[2]);
                RunLayoutJobs();
                var editor = FindControlByAutomationId<TextBox>(view, paragraphs[2].EditorAutomationId);
                editor.Focus();
                editor.SelectionStart = 7;
                editor.SelectionEnd = 7;

                PressKey(window, Key.Up, PhysicalKey.ArrowUp, RawInputModifiers.None);
                await Assert.That(WaitFor(() => viewModel.ActiveBlock?.PreviewText == "xy")).IsTrue();
                editor = FindVisibleControlByAutomationId<TextBox>(view, viewModel.ActiveBlock!.EditorAutomationId);
                await Assert.That(WaitFor(() => editor.SelectionStart == 2)).IsTrue();

                PressKey(window, Key.Up, PhysicalKey.ArrowUp, RawInputModifiers.None);
                await Assert.That(WaitFor(() => viewModel.ActiveBlock?.PreviewText == "0123456789")).IsTrue();
                editor = FindVisibleControlByAutomationId<TextBox>(view, viewModel.ActiveBlock!.EditorAutomationId);
                await Assert.That(WaitFor(() => editor.SelectionStart == 7)).IsTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ArrowDownWithinWrappedBlock_DoesNotSwitchBeforeTheLastVisualLine()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var source = new MarkdownLiveDocumentSnapshot(
                "Очень длинная строка которая обязательно переносится внутри узкого редактора\n\nСледующий блок\n",
                "revision-1",
                false,
                "note.md");
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            viewModel.CommitBlockAsync = (patch, _) => Task.FromResult(MarkdownBlockCommitResult.Accepted(source with
            {
                Raw = patch.PatchedDocumentRaw,
                ExpectedRevisionHash = "revision-2"
            }));
            viewModel.Load(source);
            var first = viewModel.Blocks.First(static block => block.Kind == MarkdownBlockKind.Paragraph);
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 220, Height = 320, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                viewModel.BeginEdit(first);
                RunLayoutJobs();
                var editor = FindControlByAutomationId<TextBox>(view, first.EditorAutomationId);
                editor.Focus();
                editor.SelectionStart = 3;
                editor.SelectionEnd = 3;

                PressKey(window, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.None);

                await Assert.That(viewModel.ActiveBlock?.PreviewText).IsEqualTo(first.PreviewText);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TypingResetsPreferredCaretColumnBeforeTheNextBlockTransition()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var source = new MarkdownLiveDocumentSnapshot("0123456789\n\nxy\n\nabcdefghij\n", "revision-1", false, "note.md");
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            viewModel.CommitBlockAsync = (patch, _) => Task.FromResult(MarkdownBlockCommitResult.Accepted(source with
            {
                Raw = patch.PatchedDocumentRaw,
                ExpectedRevisionHash = "revision-2"
            }));
            viewModel.Load(source);
            var first = viewModel.Blocks.First(static block => block.Kind == MarkdownBlockKind.Paragraph);
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 720, Height = 420, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                viewModel.BeginEdit(first);
                RunLayoutJobs();
                var editor = FindVisibleControlByAutomationId<TextBox>(view, first.EditorAutomationId);
                editor.Focus();
                editor.SelectionStart = 7;
                editor.SelectionEnd = 7;
                PressKey(window, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.None);
                await Assert.That(WaitFor(() => viewModel.ActiveBlock?.PreviewText == "xy")).IsTrue();

                editor = FindVisibleControlByAutomationId<TextBox>(view, viewModel.ActiveBlock!.EditorAutomationId);
                editor.SelectionStart = 1;
                editor.SelectionEnd = 1;
                window.KeyTextInput("z");
                RunLayoutJobs();
                PressKey(window, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.None);

                await Assert.That(WaitFor(() => viewModel.ActiveBlock?.PreviewText == "abcdefghij")).IsTrue();
                editor = FindVisibleControlByAutomationId<TextBox>(view, viewModel.ActiveBlock!.EditorAutomationId);
                await Assert.That(WaitFor(() => editor.SelectionStart == 2)).IsTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ExtendingSelectionResetsPreferredCaretColumnBeforeTheNextBlockTransition()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var source = new MarkdownLiveDocumentSnapshot("0123456789\n\nxy\n\nabcdefghij\n", "revision-1", false, "note.md");
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            viewModel.CommitBlockAsync = (patch, _) => Task.FromResult(MarkdownBlockCommitResult.Accepted(source with
            {
                Raw = patch.PatchedDocumentRaw,
                ExpectedRevisionHash = "revision-2"
            }));
            viewModel.Load(source);
            var first = viewModel.Blocks.First(static block => block.Kind == MarkdownBlockKind.Paragraph);
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 720, Height = 420, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                viewModel.BeginEdit(first);
                RunLayoutJobs();
                var editor = FindVisibleControlByAutomationId<TextBox>(view, first.EditorAutomationId);
                editor.Focus();
                editor.SelectionStart = 7;
                editor.SelectionEnd = 7;
                PressKey(window, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.None);
                await Assert.That(WaitFor(() => viewModel.ActiveBlock?.PreviewText == "xy")).IsTrue();

                editor = FindVisibleControlByAutomationId<TextBox>(view, viewModel.ActiveBlock!.EditorAutomationId);
                await Assert.That(WaitFor(() => editor.SelectionStart == 2)).IsTrue();
                PressKey(window, Key.Left, PhysicalKey.ArrowLeft, RawInputModifiers.Shift);
                using (Assert.Multiple())
                {
                    await Assert.That(viewModel.ActiveBlock?.PreviewText).IsEqualTo("xy");
                    await Assert.That(Math.Min(editor.SelectionStart, editor.SelectionEnd)).IsEqualTo(1);
                    await Assert.That(Math.Max(editor.SelectionStart, editor.SelectionEnd)).IsEqualTo(2);
                }

                editor.SelectionStart = 1;
                editor.SelectionEnd = 1;
                PressKey(window, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.None);

                await Assert.That(WaitFor(() => viewModel.ActiveBlock?.PreviewText == "abcdefghij")).IsTrue();
                editor = FindVisibleControlByAutomationId<TextBox>(view, viewModel.ActiveBlock!.EditorAutomationId);
                await Assert.That(WaitFor(() => editor.SelectionStart == 1)).IsTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task DelayedMoveCallback_UpdatesBoundBlocksOnTheCapturedUiContext()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var uiThreadId = Environment.CurrentManagedThreadId;
            var callbackStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCallback = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var source = new MarkdownLiveDocumentSnapshot("Первый\n\nВторой\n", "revision-1", false, "note.md");
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            viewModel.MoveBlocksAsync = async (_, cancellationToken) =>
            {
                callbackStarted.TrySetResult(true);
                await releaseCallback.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return MarkdownBlocksMoveResult.Accepted(
                    source with { ExpectedRevisionHash = "revision-2" },
                    [1]);
            };
            viewModel.Load(source);
            var first = viewModel.Blocks.First(static block => block.Kind == MarkdownBlockKind.Paragraph);
            viewModel.SelectMoveBlock(first, toggle: false, extendRange: false);
            var collectionChangedThreadId = 0;
            viewModel.Blocks.CollectionChanged += (_, _) => collectionChangedThreadId = Environment.CurrentManagedThreadId;

            var moveTask = viewModel.MoveSelectionByOffsetAsync(1);
            await callbackStarted.Task;
            await Task.Run(() => releaseCallback.TrySetResult(true));
            await moveTask;

            await Assert.That(collectionChangedThreadId).IsEqualTo(uiThreadId);
        }, CancellationToken.None);
    }

    [Test]
    [Arguments(320)]
    [Arguments(500)]
    public async Task HoverToolbar_RemainsInsideNarrowViewport(int width)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            viewModel.MoveBlocksAsync = (_, _) => Task.FromResult(MarkdownBlocksMoveResult.Rejected("not invoked"));
            viewModel.Load(new MarkdownLiveDocumentSnapshot("Первый блок\n\nВторой блок\n", "revision-1", false, "note.md"));
            var blockModel = viewModel.Blocks.First(static block => block.Kind == MarkdownBlockKind.Paragraph);
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = width, Height = 280, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                viewModel.SetPointerOverBlock(blockModel, true);
                RunLayoutJobs();
                var toolbar = FindControlByAutomationId<Border>(view, blockModel.ContextToolbarAutomationId);
                var handle = FindControlByAutomationId<ToggleButton>(view, blockModel.MoveHandleAutomationId);
                var toolbarOrigin = toolbar.TranslatePoint(default, view)
                    ?? throw new InvalidOperationException("Toolbar position could not be resolved.");

                using (Assert.Multiple())
                {
                    await Assert.That(toolbarOrigin.X).IsGreaterThanOrEqualTo(0);
                    await Assert.That(toolbarOrigin.X + toolbar.Bounds.Width).IsLessThanOrEqualTo(view.Bounds.Width + 0.5);
                    await Assert.That(handle.Bounds.Width).IsGreaterThanOrEqualTo(24);
                    await Assert.That(toolbar.GetVisualDescendants().OfType<Button>().Count()).IsEqualTo(6);
                    await Assert.That(blockModel.CanMoveUpFromToolbar).IsFalse();
                    await Assert.That(blockModel.CanMoveDownFromToolbar).IsTrue();
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TechnicalBlocks_DisableSemanticToolbarActions()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            viewModel.SelectionActionAsync = (_, _, _, _) => Task.CompletedTask;
            viewModel.Load(new MarkdownLiveDocumentSnapshot("---\ntitle: demo\n---\n\nText\n", "revision-1", false, "note.md"));
            var rawIndex = viewModel.Blocks.Count;
            viewModel.Blocks.Add(new MarkdownLiveBlockViewModel(
                viewModel,
                new MarkdownBlock(rawIndex, MarkdownBlockKind.Raw, "<raw>\n", 0, 6, 1),
                "\n"));
            var technicalBlocks = viewModel.Blocks
                .Where(static block => block.Kind is MarkdownBlockKind.FrontMatter
                    or MarkdownBlockKind.Blank
                    or MarkdownBlockKind.Raw)
                .ToArray();
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 720, Height = 420, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                await Assert.That(technicalBlocks.Select(static block => block.Kind))
                    .IsEquivalentTo([
                        MarkdownBlockKind.FrontMatter,
                        MarkdownBlockKind.Blank,
                        MarkdownBlockKind.Raw
                    ]);

                foreach (var block in technicalBlocks)
                {
                    viewModel.SetPointerOverBlock(block, true);
                    RunLayoutJobs();
                    var toolbar = FindControlByAutomationId<Border>(view, block.ContextToolbarAutomationId);
                    var more = toolbar.GetVisualDescendants()
                        .OfType<Button>()
                        .Single(button => string.Equals(button.Content as string, "⋯", StringComparison.Ordinal));
                    var toolbarButtons = toolbar.GetVisualDescendants().OfType<Button>().ToArray();

                    using (Assert.Multiple())
                    {
                        await Assert.That(block.IsMovable).IsFalse();
                        await Assert.That(block.CanMoveUpFromToolbar).IsFalse();
                        await Assert.That(block.CanMoveDownFromToolbar).IsFalse();
                        await Assert.That(block.CanTransformFromToolbar).IsFalse();
                        await Assert.That(block.CanOpenActionsFromToolbar).IsFalse();
                        await Assert.That(more.IsEnabled).IsFalse();
                        await Assert.That(toolbarButtons.Length).IsEqualTo(6);
                        await Assert.That(toolbarButtons.All(static button => !button.IsEnabled)).IsTrue();
                    }

                    viewModel.SetPointerOverBlock(block, false);
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task MoreActionsFlyout_KeepsToolbarVisibleUntilActionAndClose()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            MarkdownSelectionSemanticAction? invokedAction = null;
            IReadOnlyList<int>? invokedIndices = null;
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            viewModel.SelectionActionAsync = (_, indices, action, _) =>
            {
                invokedIndices = indices;
                invokedAction = action;
                return Task.CompletedTask;
            };
            viewModel.Load(new MarkdownLiveDocumentSnapshot("Полезный блок\n", "revision-1", false, "note.md"));
            var block = viewModel.Blocks.Single(static candidate => candidate.Kind == MarkdownBlockKind.Paragraph);
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 720, Height = 320, Content = view };
            MenuFlyout? flyout = null;
            try
            {
                window.Show();
                RunLayoutJobs();
                viewModel.SetPointerOverBlock(block, true);
                RunLayoutJobs();
                var toolbar = FindControlByAutomationId<Border>(view, block.ContextToolbarAutomationId);
                var more = toolbar.GetVisualDescendants()
                    .OfType<Button>()
                    .Single(button => string.Equals(button.Content as string, "⋯", StringComparison.Ordinal));
                flyout = more.Flyout as MenuFlyout
                    ?? throw new InvalidOperationException("More actions button should use a MenuFlyout.");

                more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, more));
                flyout.ShowAt(more);
                RunLayoutJobs();
                viewModel.SetPointerOverBlock(block, false);
                RunLayoutJobs();

                using (Assert.Multiple())
                {
                    await Assert.That(block.IsToolbarFlyoutOpen).IsTrue();
                    await Assert.That(toolbar.IsEffectivelyVisible).IsTrue();
                    await Assert.That(block.MoveHandleIdleOpacity).IsEqualTo(1);
                }

                var createNote = flyout.Items.OfType<MenuItem>().ElementAt(1);
                createNote.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, createNote));
                await Assert.That(WaitFor(() => invokedAction == MarkdownSelectionSemanticAction.Note)).IsTrue();
                await Assert.That(invokedIndices).IsNotNull();
                await Assert.That(invokedIndices!).IsEquivalentTo([block.Index]);

                flyout.Hide();
                RunLayoutJobs();
                using (Assert.Multiple())
                {
                    await Assert.That(block.IsToolbarFlyoutOpen).IsFalse();
                    await Assert.That(block.IsMoveSelected).IsTrue();
                    await Assert.That(toolbar.IsEffectivelyVisible).IsTrue();
                }

                viewModel.ClearMoveSelection();
                RunLayoutJobs();
                await Assert.That(toolbar.IsEffectivelyVisible).IsFalse();
            }
            finally
            {
                flyout?.Hide();
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task BackspaceAtStart_MergesCurrentBlockIntoPreviousAndKeepsCaretAtJoin()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var snapshot = new MarkdownLiveDocumentSnapshot("Альфа\n\n- [ ] Бета\n", "revision-1", false, "note.md");
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            viewModel.CommitBlockAsync = (patch, _) =>
            {
                snapshot = snapshot with
                {
                    Raw = patch.PatchedDocumentRaw,
                    ExpectedRevisionHash = "revision-2"
                };
                return Task.FromResult(MarkdownBlockCommitResult.Accepted(snapshot));
            };
            viewModel.Load(snapshot);
            var current = viewModel.Blocks.Single(static block => block.Kind == MarkdownBlockKind.TaskListItem);
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 720, Height = 420, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                viewModel.BeginEdit(current);
                var editor = FindControlByAutomationId<TextBox>(view, current.EditorAutomationId);
                editor.Focus();
                editor.SelectionStart = 0;
                editor.SelectionEnd = 0;

                PressKey(window, Key.Back, PhysicalKey.Backspace, RawInputModifiers.None);

                await Assert.That(WaitFor(() => snapshot.ExpectedRevisionHash == "revision-2")).IsTrue();
                await Assert.That(snapshot.Raw).IsEqualTo("АльфаБета\n");
                await Assert.That(viewModel.ActiveBlock?.Kind).IsEqualTo(MarkdownBlockKind.Paragraph);
                editor = FindControlByAutomationId<TextBox>(view, viewModel.ActiveBlock!.EditorAutomationId);
                using (Assert.Multiple())
                {
                    await Assert.That(editor.IsFocused).IsTrue();
                    await Assert.That(editor.SelectionStart).IsEqualTo("Альфа".Length);
                    await Assert.That(editor.SelectionEnd).IsEqualTo("Альфа".Length);
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task HoveringBlock_ShowsMoveHandleAndInlineToolbarWithoutChangingGeometry()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var viewModel = new MarkdownLivePreviewEditorViewModel();
            viewModel.CommitBlockAsync = (_, _) => throw new InvalidOperationException("Commit is not expected.");
            viewModel.Load(new MarkdownLiveDocumentSnapshot("Текст блока\n", "revision-1", false, "note.md"));
            var blockModel = viewModel.Blocks.Single(static block => block.Kind == MarkdownBlockKind.Paragraph);
            var view = new MarkdownBlockLivePreviewEditor { DataContext = viewModel };
            var window = new Window { Width = 720, Height = 320, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                var block = FindControlByAutomationId<Grid>(view, blockModel.BlockAutomationId);
                var handle = FindControlByAutomationId<ToggleButton>(view, blockModel.MoveHandleAutomationId);
                var before = block.Bounds.Height;
                await Assert.That(handle.Opacity).IsEqualTo(0);
                viewModel.SetPointerOverBlock(blockModel, true);
                RunLayoutJobs();

                var toolbar = FindControlByAutomationId<Border>(
                    view,
                    $"MarkdownLivePreview-BlockToolbar-{blockModel.Index}");
                using (Assert.Multiple())
                {
                    await Assert.That(handle.Opacity).IsEqualTo(1);
                    await Assert.That(toolbar.IsEffectivelyVisible).IsTrue();
                    await Assert.That(Math.Abs(block.Bounds.Height - before)).IsLessThanOrEqualTo(0.5);
                    await Assert.That(viewModel.SelectedMoveBlockCount).IsEqualTo(0);
                }
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

    private static T FindVisibleControlByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        return root.GetVisualDescendants()
                   .OfType<T>()
                   .FirstOrDefault(control => control.IsEffectivelyVisible
                       && string.Equals(
                           AutomationProperties.GetAutomationId(control),
                           automationId,
                           StringComparison.Ordinal))
               ?? throw new InvalidOperationException($"Visible control with AutomationId '{automationId}' was not found.");
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
