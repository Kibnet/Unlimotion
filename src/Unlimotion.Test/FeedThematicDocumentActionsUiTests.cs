using System;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI;
using Unlimotion.Notes.Areas;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Operations;
using Unlimotion.Notes.Vault;
using Unlimotion.ViewModel.Feed;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class FeedThematicDocumentActionsUiTests
{
    [Test]
    public async Task TwoHundredLineThematicTabSupportsTaskNoteAndAreaWithoutDailyProjection()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;
            FeedViewModel? feed = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                await owner.Connect();
                var root = TestHelpers.GetTask(owner, MainWindowViewModelFixture.RootTask2Id)
                    ?? throw new InvalidOperationException("Missing parent task fixture.");
                var identity = new FeedTaskSourceIdentity(root.SourceId, "thematic-ui-binding");
                const string relativePath = "Темы/Длинная заметка.md";
                var fullPath = Path.Combine(directory.Path, "Темы", "Длинная заметка.md");
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                var lines = Enumerable.Range(1, 200)
                    .Select(index => index % 2 == 0 ? string.Empty : $"Строка {index:000}").ToArray();
                lines[0] = "## Работа <!-- unlimotion-area:work -->";
                await File.WriteAllTextAsync(fullPath, string.Join("\n", lines));
                await new AreaCatalogStore(new FileNoteVault(directory.Path)).SaveAsync(new AreaCatalog
                {
                    Areas = [new AreaDefinition { Id = "work", Name = "Работа" },
                        new AreaDefinition { Id = "study", Name = "Учёба" }]
                }, expectedRevision: null);
                feed = new FeedViewModel(() => new DateOnly(2026, 9, 9))
                {
                    TaskOwner = owner,
                    TaskResolver = id => owner.taskRepository!.Tasks.Items.FirstOrDefault(task => task.Id == id),
                    TaskCreationTarget = new TaskStorageFeedTaskCreationTarget(() => owner.taskRepository, () => identity)
                };
                feed.ConfigureTaskSourceParents(() => identity,
                    (_, _, areaId) => areaId == "work" ? root.Id : null,
                    (_, _, _, _) => Task.CompletedTask);
                await feed.InitializeVaultAsync(directory.Path);
                var view = new FeedControl { DataContext = feed };
                window = new Window { Content = view, Width = 900, Height = 650 };
                window.Show();
                await feed.OpenVaultLinkAsync(relativePath, null);
                Dispatcher.UIThread.RunJobs();
                var tab = feed.OpenedThematicFile!;
                await Assert.That(tab).IsNotNull();
                await Assert.That(feed.Days.Count).IsEqualTo(0);
                var scroller = view.GetVisualDescendants().OfType<ScrollViewer>().Single(control => control.Name == "DocumentScroller");
                await Assert.That(scroller.Extent.Height).IsGreaterThan(scroller.Viewport.Height);
                scroller.Offset = new Vector(0, 300);
                var sourceOffset = await OpenAction(feed, "Строка 041", MarkdownSelectionSemanticAction.Task, relativePath, scroller);
                await Assert.That(feed.ReviewParents!.Parents.Select(parent => parent.Id)).IsEquivalentTo([root.Id]);
                await Run(feed.CreateTaskCommand);
                await Assert.That(feed.HasError).IsFalse();
                var createdId = feed.CreatedTaskReference!.TaskId;
                var storedTask = await owner.taskRepository!.TaskTreeManager.Storage.Load(createdId);
                await Assert.That(storedTask!.ParentTasks).Contains(root.Id);
                await Assert.That(storedTask.AreaIds).Contains("work");
                await Assert.That(storedTask.Title).Contains("Строка 041");
                await Assert.That((await owner.taskRepository.TaskTreeManager.Storage.Load(root.Id))!.ContainsTasks).Contains(createdId);
                await Assert.That(await File.ReadAllTextAsync(fullPath)).Contains("unlimotion://task/" + createdId);
                await AssertSameSourceTab(feed, tab, relativePath);
                await AssertSourceViewport(scroller, sourceOffset);

                sourceOffset = await OpenAction(feed, "Строка 061", MarkdownSelectionSemanticAction.Note, relativePath, scroller);
                feed.ReviewNoteTitle = "Выделенная информация";
                feed.ReviewNoteFolder = "Знания";
                await Run(feed.CreateNoteCommand);
                await Assert.That(feed.HasError).IsFalse();
                await Assert.That(await File.ReadAllTextAsync(Path.Combine(directory.Path, "Знания", "Выделенная информация.md")))
                    .Contains("Строка 061");
                var afterNote = await File.ReadAllTextAsync(fullPath);
                await Assert.That(afterNote).Contains("[[Знания/Выделенная информация");
                await Assert.That(afterNote).DoesNotContain("Строка 061");
                await AssertSameSourceTab(feed, tab, relativePath);
                await AssertSourceViewport(scroller, sourceOffset);

                sourceOffset = await OpenAction(feed, "Строка 081", MarkdownSelectionSemanticAction.Area, relativePath, scroller);
                feed.ReviewDestinationArea = feed.Areas.Single(area => area.Identity == "study");
                await Run(feed.AssignReviewAreaCommand);
                await Assert.That(feed.HasError).IsFalse();
                var parsed = new MarkdownDocumentParser().Parse(await File.ReadAllTextAsync(fullPath));
                await Assert.That(parsed.Blocks.Single(block => block.IsContent && block.Raw.Trim() == "Строка 081").AreaId)
                    .IsEqualTo("study");
                await Assert.That(parsed.Blocks.Single(block => block.IsContent && block.Raw.Trim() == "Строка 083").AreaId)
                    .IsEqualTo("work");
                await AssertSameSourceTab(feed, tab, relativePath);
                await AssertSourceViewport(scroller, sourceOffset);
                await Assert.That(Directory.Exists(Path.Combine(directory.Path, "Ежедневные"))
                    && Directory.EnumerateFiles(Path.Combine(directory.Path, "Ежедневные"), "*.md").Any()).IsFalse();
            }
            finally
            {
                window?.Close();
                feed?.Dispose();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }

    private static async Task<double> OpenAction(FeedViewModel feed, string text,
        MarkdownSelectionSemanticAction action, string sourcePath, ScrollViewer scroller)
    {
        var editor = feed.OpenedThematicFile!.MarkdownEditor;
        var block = editor.Blocks.Single(candidate => candidate.Block.IsContent && candidate.Block.Raw.Trim() == text);
        var preview = scroller.GetVisualDescendants().OfType<MarkdownBlockPreviewControl>()
            .Single(control => ReferenceEquals(control.DataContext, block));
        preview.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        await Assert.That(preview.Focus()).IsTrue();
        Dispatcher.UIThread.RunJobs();
        await Assert.That(preview.TranslatePoint(default, scroller)!.Value.Y).IsGreaterThanOrEqualTo(0d);
        await Assert.That(preview.TranslatePoint(default, scroller)!.Value.Y).IsLessThan(scroller.Viewport.Height);
        var offset = scroller.Offset.Y;
        editor.SelectMoveBlock(block, toggle: false, extendRange: false);
        await editor.InvokeSelectionActionAsync(action);
        Dispatcher.UIThread.RunJobs();
        await Assert.That(feed.CurrentReview).IsNotNull();
        await Assert.That(feed.CurrentReview!.RelativePath).IsEqualTo(sourcePath);
        await Assert.That(feed.CurrentReview.SelectedMarkdown.Trim()).IsEqualTo(text);
        await Assert.That(feed.IsReviewSelectionVisible).IsTrue();
        return offset;
    }

    private static Task Run(System.Windows.Input.ICommand command) =>
        ((ReactiveCommand<Unit, Unit>)command).Execute().ToTask();

    private static async Task AssertSameSourceTab(FeedViewModel feed,
        FeedThematicDocumentViewModel tab, string sourcePath)
    {
        Dispatcher.UIThread.RunJobs();
        await Assert.That(feed.Days.Count).IsEqualTo(0);
        await Assert.That(feed.OpenedThematicFile).IsSameReferenceAs(tab);
        await Assert.That(feed.DocumentWorkspace.Documents.Count).IsEqualTo(1);
        await Assert.That(tab.MarkdownEditor.Snapshot!.RelativePath).IsEqualTo(sourcePath);
    }

    private static async Task AssertSourceViewport(ScrollViewer scroller, double offset)
    {
        Dispatcher.UIThread.RunJobs();
        await Assert.That(Math.Abs(scroller.Offset.Y - offset)).IsLessThanOrEqualTo(1d);
    }
}
