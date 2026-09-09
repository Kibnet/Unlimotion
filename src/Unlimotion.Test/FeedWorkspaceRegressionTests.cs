using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.Notes.Markdown;
using Unlimotion.ViewModel.Feed;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class FeedWorkspaceRegressionTests
{
    [Test]
    public async Task AreaFilterAndDayCollapseClearHiddenGlobalSelection()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var daily = System.IO.Path.Combine(directory.Path, "Ежедневные");
            System.IO.Directory.CreateDirectory(daily);
            await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(daily, "2026-09-09.md"),
                "## Работа <!-- unlimotion-area:work -->\nПервый\n");
            await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(daily, "2026-09-08.md"),
                "## Личное <!-- unlimotion-area:personal -->\nВторой\n");
            using var feed = new FeedViewModel(() => new DateOnly(2026, 9, 9));
            await feed.InitializeVaultAsync(directory.Path);
            var view = new FeedControl { DataContext = feed };
            var window = new Window { Width = 1000, Height = 800, Content = view };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var first = feed.Days.Single(day => day.Date.Day == 9);
                var second = feed.Days.Single(day => day.Date.Day == 8);
                void SelectBoth()
                {
                    first.MarkdownEditor.SelectMoveBlock(first.MarkdownEditor.Blocks.Last(block => block.Kind == MarkdownBlockKind.Paragraph), false, false);
                    second.MarkdownEditor.SelectMoveBlock(second.MarkdownEditor.Blocks.Last(block => block.Kind == MarkdownBlockKind.Paragraph), true, false);
                }
                SelectBoth();
                await Assert.That(first.MarkdownEditor.SelectedMoveBlockCount).IsEqualTo(1);
                await Assert.That(second.MarkdownEditor.SelectedMoveBlockCount).IsEqualTo(1);
                var collapse = view.GetVisualDescendants().OfType<ToggleButton>().First(control =>
                    AutomationProperties.GetAutomationId(control) == first.CollapseAutomationId);
                collapse.IsChecked = true;
                Dispatcher.UIThread.RunJobs();
                await Assert.That(first.IsCollapsed).IsTrue();
                await Assert.That(first.MarkdownEditor.SelectedMoveBlockCount).IsEqualTo(0);
                collapse.IsChecked = false;
                SelectBoth();
                feed.FeedAreaFilterOptions.Single(option => option.IsAll).IsSelected = false;
                feed.FeedAreaFilterOptions.Single(option => option.Identity == "work").IsSelected = true;
                Dispatcher.UIThread.RunJobs();
                await Assert.That(feed.VisibleDays.Select(day => day.Date)).IsEquivalentTo([first.Date]);
                await Assert.That(first.MarkdownEditor.SelectedMoveBlockCount).IsEqualTo(0);
                await Assert.That(second.MarkdownEditor.SelectedMoveBlockCount).IsEqualTo(0);
                feed.FeedAreaFilterOptions.Single(option => option.IsAll).IsSelected = true;
                Dispatcher.UIThread.RunJobs();
                await Assert.That(feed.VisibleDays.Count).IsEqualTo(2);
                await Assert.That(second.MarkdownEditor.SelectedMoveBlockCount).IsEqualTo(0);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task DayDisclosure_IsBeforeDate()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(directory.Path, "Ежедневные"));
            await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(directory.Path, "Ежедневные", "2026-09-09.md"), "Проверка дня\n");
            using var feed = new FeedViewModel(() => new DateOnly(2026, 9, 9));
            await feed.InitializeVaultAsync(directory.Path);
            var view = new FeedControl { DataContext = feed };
            var window = new Window { Width = 1000, Height = 700, Content = view };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var date = view.GetVisualDescendants().OfType<TextBlock>().First(x =>
                    AutomationProperties.GetAutomationId(x) == "FeedDay-20260909-DateText");
                var toggle = view.GetVisualDescendants().OfType<ToggleButton>().First(x =>
                    AutomationProperties.GetAutomationId(x) == "FeedDay-20260909-CollapseToggle");
                await Assert.That(toggle.TranslatePoint(default, view)!.Value.X)
                    .IsLessThan(date.TranslatePoint(default, view)!.Value.X);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task NonContiguousSelection_CannotExpandSemanticConversionRange()
    {
        using var editor = new MarkdownLivePreviewEditorViewModel();
        editor.CommitBlockAsync = (_, _) => throw new InvalidOperationException();
        editor.SelectionActionAsync = (_, _, _, _) => Task.CompletedTask;
        editor.Load(new MarkdownLiveDocumentSnapshot("Первый\n\nВторой\n\nТретий\n", "r", false, "note.md"));
        var content = editor.Blocks.Where(x => x.Block.IsContent).ToArray();
        editor.SelectMoveBlock(content[0], false, false);
        editor.SelectMoveBlock(content[2], true, false);
        await Assert.That(editor.CanOpenSelectionActions).IsFalse();
    }
}
