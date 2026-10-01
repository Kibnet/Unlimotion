using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Vault;
using Unlimotion.ViewModel.Feed;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class FeedEditorInteractionPerformanceUiTests
{
    [Test]
    [Arguments("paragraph", "Обычный текст для проверки\n")]
    [Arguments("heading", "# Заголовок для проверки\n")]
    [Arguments("blockquote", "> Цитата для проверки\n")]
    [Arguments("list", "- Пункт списка для проверки\n")]
    [Arguments("checklist", "- [ ] Пункт с флажком\n")]
    [Arguments("link", "Текст со [ссылкой](https://example.com) после слова\n")]
    public async Task EditingBlockPreservesFirstGlyphBaseline(string kind, string raw)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var model = new MarkdownLivePreviewEditorViewModel();
            model.CommitBlockAsync = (_, _) => throw new InvalidOperationException("Unchanged focus must not write a note.");
            model.Load(new MarkdownLiveDocumentSnapshot(raw, "revision", false, "note.md"));
            var block = model.Blocks.First(block => block.IsEditable);
            var view = new MarkdownBlockLivePreviewEditor { DataContext = model };
            var window = new Window { Width = 900, Height = 400, Content = view };
            try
            {
                window.Show();
                Layout();
                var preview = Find<MarkdownBlockPreviewControl>(view, block.PreviewAutomationId);
                // List markers are separate text controls: measure the content's glyph baseline, not the bullet.
                var text = preview.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.TextLayout.TextLines.Count > 0)
                    .OrderByDescending(text => text.Bounds.Width).First();
                var before = text.TranslatePoint(default, view)!.Value.Y + text.TextLayout.Baseline;
                model.BeginEdit(block);
                Layout();
                var editor = Find<TextBox>(view, block.EditorAutomationId);
                var presenter = editor.GetVisualDescendants().OfType<TextPresenter>().Single();
                var after = presenter.TranslatePoint(default, view)!.Value.Y + presenter.TextLayout.Baseline;
                Console.WriteLine($"BASELINE kind={kind}; preview={before:F3} DIP; editing={after:F3} DIP; delta={after - before:F3} DIP");
                await Assert.That(Math.Abs(after - before)).IsLessThanOrEqualTo(1d);
                await Assert.That(model.Snapshot!.Raw).IsEqualTo(raw);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task EightHundredWeekdayNotes_RecordFirstAndThirtyWarmClickToCaretSamples()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var today = new DateOnly(2026, 9, 9);
        var date = today;
        var sizes = new List<int>();
        for (var note = 0; note < 800; note++)
        {
            while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) date = date.AddDays(-1);
            var lines = note == 0 ? 200 : 1 + note % 200;
            sizes.Add(lines);
            var text = new StringBuilder();
            for (var line = 0; line < lines; line++)
                text.AppendLine($"День {note}, строка {line}: мысль и контекст для проверки редактора.");
            await vault.CreateAsync($"Ежедневные/{date:yyyy-MM-dd}.md", text.ToString());
            date = date.AddDays(-1);
        }
        await Assert.That(sizes.Count).IsEqualTo(800);
        await Assert.That(sizes.Min()).IsEqualTo(1);
        await Assert.That(sizes.Max()).IsEqualTo(200);
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var feed = new FeedViewModel(() => today);
            var initialization = Stopwatch.StartNew();
            await feed.InitializeVaultAsync(directory.Path);
            initialization.Stop();
            var day = feed.Days.First(day => day.Date == today);
            var model = day.MarkdownEditor;
            var view = new FeedControl { DataContext = feed };
            var window = new Window { Width = 1200, Height = 800, Content = view };
            try
            {
                window.Show();
                Layout();
                var block = model.Blocks.First(block => block.IsEditable);
                var preview = Find<MarkdownBlockPreviewControl>(view, block.PreviewAutomationId);
                var chronology = view.GetVisualDescendants().OfType<ScrollViewer>()
                    .Single(control => control.Name == "ChronologyScroller");
                chronology.Offset = default;
                Layout();
                var initialOffset = chronology.Offset.Y;
                var first = ClickToCaret();
                await Assert.That(Math.Abs(chronology.Offset.Y - initialOffset)).IsLessThanOrEqualTo(1d);
                var samples = new List<double>();
                for (var sample = 0; sample < 30; sample++)
                {
                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    Layout();
                    await Assert.That(model.ActiveBlock).IsNull();
                    await Assert.That(Math.Abs(chronology.Offset.Y - initialOffset)).IsLessThanOrEqualTo(1d);
                    samples.Add(ClickToCaret());
                    await Assert.That(Math.Abs(chronology.Offset.Y - initialOffset)).IsLessThanOrEqualTo(1d);
                }
                var ordered = samples.Order().ToArray();
                Console.WriteLine($"EDITOR_TIMING headless=true; runtime={RuntimeInformation.FrameworkDescription}; os={RuntimeInformation.OSDescription}; cpuCount={Environment.ProcessorCount}; scale={window.RenderScaling}; viewport=1200x800; notes=800; lines=1..200; loadedDays={feed.Days.Count}; initializationMs={initialization.Elapsed.TotalMilliseconds:F2}");
                Console.WriteLine($"EDITOR_TIMING firstInteractionMs={first:F2}; warmSamples=30; p50Ms={ordered[14]:F2}; p95Ms={ordered[28]:F2}; maxMs={ordered[^1]:F2}; allMs=[{string.Join(",", samples.Select(value => value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))}]");
                Console.WriteLine("TIMING_BOUNDARY: first interaction is fixture-cold, not process-cold. Headless dispatcher instrumentation is not native frame/input latency or proof of the production p95 budget.");
                await Assert.That(samples.Count).IsEqualTo(30);
                await Assert.That(model.ActiveBlock).IsNotNull();

                double ClickToCaret()
                {
                    var location = preview.TranslatePoint(new Point(40, Math.Min(10, preview.Bounds.Height / 2)), window)
                        ?? throw new InvalidOperationException("Preview is not attached to the window.");
                    if (!new Rect(window.ClientSize).Contains(location))
                        throw new InvalidOperationException("The timed block left the visible viewport.");
                    var watch = Stopwatch.StartNew();
                    window.MouseDown(location, MouseButton.Left);
                    window.MouseUp(location, MouseButton.Left);
                    Layout();
                    var editor = Find<TextBox>(view, block.EditorAutomationId);
                    if (!editor.IsEffectivelyVisible || !editor.IsFocused)
                        throw new InvalidOperationException("The pointer gesture did not reach an editable focused text caret.");
                    watch.Stop();
                    return watch.Elapsed.TotalMilliseconds;
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static T Find<T>(Control root, string id) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => AutomationProperties.GetAutomationId(control) == id);
    private static void Layout()
    {
        for (var turn = 0; turn < 20; turn++) Dispatcher.UIThread.RunJobs();
    }
}
