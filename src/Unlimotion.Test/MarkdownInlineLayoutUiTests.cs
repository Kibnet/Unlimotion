using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel.Feed;
using Unlimotion.Views;
using Unlimotion.Domain;
using Unlimotion.TaskTree;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class MarkdownInlineLayoutUiTests
{
    [Test]
    [Arguments("\n")]
    [Arguments("\r\n")]
    public async Task Links_OnSeparateLinesKeepTheirLineAndSource(string newline)
    {
        var raw = string.Join(newline, "До ссылки", "[Первая](https://example.org)",
            "[[Тема|Вторая]]", "После ссылки", "");
        await WithPreview(raw, 500, async (model, preview, window) =>
        {
            var first = Find<Button>(preview, "MarkdownLivePreview-Link-0-0");
            var second = Find<Button>(preview, "MarkdownLivePreview-Link-0-1");
            var firstPosition = first.TranslatePoint(default, preview)!.Value;
            var secondPosition = second.TranslatePoint(default, preview)!.Value;
            Capture(window, $"separate-lines-{(newline.Length == 1 ? "lf" : "crlf")}");

            // Both standalone links start their own line, not to the right of the previous token box.
            await Assert.That(Math.Abs(firstPosition.X)).IsLessThanOrEqualTo(1);
            await Assert.That(Math.Abs(secondPosition.X)).IsLessThanOrEqualTo(1);
            await Assert.That(firstPosition.Y).IsGreaterThan(5);
            await Assert.That(secondPosition.Y).IsGreaterThan(firstPosition.Y + 5);
            await Assert.That(preview.Bounds.Height).IsGreaterThan(secondPosition.Y + second.Bounds.Height + 5);
            await Assert.That(model.Snapshot!.Raw).IsEqualTo(raw);

            MarkdownLinkInvokedEventArgs? invoked = null;
            preview.LinkInvoked += (_, args) => invoked = args;
            first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Assert.That(invoked!.Target).IsEqualTo("https://example.org");
            second.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Assert.That(invoked!.Kind).IsEqualTo(MarkdownInlineTokenKind.WikiLink);
            await Assert.That(invoked.Target).IsEqualTo("Тема");
        });
    }

    [Test]
    public async Task MixedInlineFormatting_DoesNotIntroduceAdditionalLineHeight()
    {
        await WithPreview("До *курсив* и **жирный** [ссылка](https://example.org) после\n", 750,
            async (_, preview, window) =>
            {
                var link = Find<Button>(preview, "MarkdownLivePreview-Link-0-0");
                var linkLabel = (TextBlock)link.Content!;
                var reference = new TextBlock
                {
                    Text = "До курсив и жирный ссылка после",
                    FontFamily = linkLabel.FontFamily,
                    FontSize = linkLabel.FontSize
                };
                reference.Measure(new Size(750, double.PositiveInfinity));
                Capture(window, "mixed-inline");
                await Assert.That(Math.Abs(preview.Bounds.Height - reference.DesiredSize.Height))
                    .IsLessThanOrEqualTo(1);
            });
    }

    [Test]
    public async Task LongLinkAndUnsafeLink_StayInsideNarrowViewport()
    {
        const string label = "Очень длинная подпись ссылки с несколькими словами для проверки переноса";
        var raw = $"[{label}](https://example.org)\n[небезопасная](javascript:run())\n";
        await WithPreview(raw, 180, async (model, preview, window) =>
        {
            var link = Find<Button>(preview, "MarkdownLivePreview-Link-0-0");
            var blocked = Find<TextBlock>(preview, "MarkdownLivePreview-BlockedLink-0-1");
            var linkPosition = link.TranslatePoint(default, preview)!.Value;
            Capture(window, "narrow-links");
            await Assert.That(linkPosition.X + link.Bounds.Width).IsLessThanOrEqualTo(preview.Bounds.Width + 1);
            await Assert.That(((TextBlock)link.Content!).TextLayout.TextLines.Count).IsGreaterThan(1);
            await Assert.That(blocked.Text).IsEqualTo("небезопасная");
            await Assert.That(preview.GetVisualDescendants().OfType<Button>().Count()).IsEqualTo(1);
            await Assert.That(model.Snapshot!.Raw).IsEqualTo(raw);
        });
    }

    [Test]
    public async Task FencedCodeLinkSyntax_RemainsLiteralAndInactive()
    {
        const string raw = "```text\n[ссылка](https://example.org)\n[[Тема]]\n```\n";
        await WithPreview(raw, 250, async (model, preview, _) =>
        {
            await Assert.That(preview.GetVisualDescendants().OfType<Button>().Any()).IsFalse();
            var code = preview.GetVisualDescendants().OfType<SelectableTextBlock>().Single();
            await Assert.That(code.Text).Contains("[ссылка](https://example.org)\n[[Тема]]");
            await Assert.That(model.Snapshot!.Raw).IsEqualTo(raw);
        });
    }

    [Test]
    public async Task FencedCode_LongLineUsesLocalHorizontalScroll()
    {
        var raw = $"```text\n{new string('x', 180)}\n```\n";
        await WithPreview(raw, 250, async (_, preview, window) =>
        {
            var code = preview.GetVisualDescendants().OfType<SelectableTextBlock>().Single();
            Capture(window, "code-long-line");
            await Assert.That(code.TextLayout.TextLines.Count).IsEqualTo(1);
            var scroll = preview.GetVisualDescendants().OfType<ScrollViewer>().Single();
            await Assert.That(scroll.Extent.Width).IsGreaterThan(scroll.Viewport.Width);
            scroll.Offset = new Vector(scroll.Extent.Width, 0);
            Layout();
            await Assert.That(scroll.Offset.X).IsGreaterThan(0);
            await Assert.That(preview.Bounds.Width).IsLessThanOrEqualTo(window.ClientSize.Width);
        });
    }

    [Test]
    public async Task TaskLink_KeepsStatusOnLeftAndRefreshesItsTitle()
    {
        const string id = "inline-task";
        await WithPreview($"До\n[Задача](unlimotion://task/{id})\nПосле\n", 500,
            async (model, preview, window) =>
            {
                var memory = new InMemoryStorage();
                await memory.Save(new TaskItem { Id = id, Title = "Текущая задача" });
                using var storage = new UnifiedTaskStorage(new TaskTreeManager(memory));
                await storage.Init();
                using var reference = new FeedTaskReferenceViewModel(id, "Задача", storage.Tasks.Items.Single());
                model.SetTaskReferences([reference]);
                Layout();
                var status = Find<TaskStatusPicker>(preview, reference.StatusAutomationId);
                var title = Find<Button>(preview, reference.TitleAutomationId);
                await Assert.That(status.TranslatePoint(default, preview)!.Value.X + status.Bounds.Width)
                    .IsLessThan(title.TranslatePoint(default, preview)!.Value.X);
                reference.Task!.Title = "Новое название";
                Layout();
                await Assert.That(((TextBlock)title.Content!).Text).IsEqualTo("Новое название");
                Capture(window, "task-inline-status");
            });
    }

    [Test]
    public async Task BeginEditingPlainParagraph_KeepsFirstGlyphBaseline()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var model = new MarkdownLivePreviewEditorViewModel();
            model.CommitBlockAsync = (_, _) => throw new InvalidOperationException("No commit expected.");
            model.Load(new MarkdownLiveDocumentSnapshot("Обычный текст для проверки строки\n", "r", false, "note.md"));
            var view = new MarkdownBlockLivePreviewEditor { DataContext = model };
            var window = new Window { Width = 700, Height = 300, Content = view };
            try
            {
                window.Show();
                Layout();
                var preview = Find<MarkdownBlockPreviewControl>(view, model.Blocks[0].PreviewAutomationId);
                var text = preview.GetVisualDescendants().OfType<TextBlock>().Single();
                var previewBaseline = text.TranslatePoint(default, view)!.Value.Y + text.TextLayout.Baseline;
                Capture(window, "editing-preview");
                model.BeginEdit(model.Blocks[0]);
                Layout();
                var editor = Find<TextBox>(view, model.Blocks[0].EditorAutomationId);
                var presenter = editor.GetVisualDescendants().OfType<TextPresenter>().Single();
                var editingBaseline = presenter.TranslatePoint(default, view)!.Value.Y + presenter.TextLayout.Baseline;
                Capture(window, "editing-active");
                Console.WriteLine($"First glyph baseline: preview={previewBaseline:F3} DIP, editor={editingBaseline:F3} DIP");
                await Assert.That(Math.Abs(previewBaseline - editingBaseline)).IsLessThanOrEqualTo(1);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static async Task WithPreview(string raw, int width,
        Func<MarkdownLivePreviewEditorViewModel, MarkdownBlockPreviewControl, Window, Task> assertion)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var model = new MarkdownLivePreviewEditorViewModel();
            model.Load(new MarkdownLiveDocumentSnapshot(raw, "revision-1", false, "note.md"));
            var preview = new MarkdownBlockPreviewControl { Block = model.Blocks[0] };
            var window = new Window
            {
                Width = width, Height = 400,
                Content = new StackPanel { Children = { preview } }
            };
            try
            {
                window.Show();
                Layout();
                await assertion(model, preview, window);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static T Find<T>(Control root, string id) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => AutomationProperties.GetAutomationId(control) == id);

    private static void Layout()
    {
        for (var i = 0; i < 20; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("UNLIMOTION_FEED_INLINE_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        if (frame is null) throw new InvalidOperationException("Headless renderer returned no frame.");
        frame.Save(Path.Combine(directory, $"{name}.png"));
    }
}
