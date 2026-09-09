using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.Notes.Conflicts;
using Unlimotion.Notes.Vault;
using Unlimotion.Notes.Watching;
using Unlimotion.ViewModel.Feed;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class FeedDocumentExternalChangesUiTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RenameKeepsTabIdentityAndUsesOneDailyEditor(bool daily)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            Directory.CreateDirectory(Path.Combine(directory.Path, "Ежедневные"));
            var oldPath = daily ? "Ежедневные/2026-09-08.md" : "Old.md";
            var newPath = daily ? "Ежедневные/2026-09-09.md" : "New.md";
            await File.WriteAllTextAsync(Path.Combine(directory.Path, oldPath), "Исходный текст\n");
            using var feed = new FeedViewModel(() => new DateOnly(2026, 9, 9));
            await feed.InitializeVaultAsync(directory.Path);
            await feed.OpenVaultLinkAsync(oldPath, null);
            var tab = feed.OpenedThematicFile!;
            tab.ScrollOffset = 25;
            File.Move(Path.Combine(directory.Path, oldPath), Path.Combine(directory.Path, newPath));
            var disk = await new FileNoteVault(directory.Path).ReadAsync(newPath);
            await Reload(feed, new(new(VaultWatchScope.Markdown, VaultWatchChangeKind.Renamed, newPath, oldPath, disk!.Revision), disk));
            await feed.OpenVaultLinkAsync(newPath, null);
            await Assert.That(feed.OpenedThematicFile).IsSameReferenceAs(tab);
            await Assert.That(feed.DocumentWorkspace.Documents.Count).IsEqualTo(1);
            await Assert.That(tab.RelativePath).IsEqualTo(newPath);
            await Assert.That(tab.MarkdownEditor.Snapshot!.RelativePath).IsEqualTo(newPath);
            if (daily)
                await Assert.That(tab.MarkdownEditor).IsSameReferenceAs(feed.Days.Single(day => day.RelativePath == newPath).MarkdownEditor);
        }, CancellationToken.None);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task DirtyExternalChangeRetainsDraftAndRecoveryUntilExplicitResolution(bool deleted, bool daily)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            Directory.CreateDirectory(Path.Combine(directory.Path, "Ежедневные"));
            var oldPath = daily ? "Ежедневные/2026-09-08.md" : "Old.md";
            var newPath = daily ? "Ежедневные/2026-09-09.md" : "New.md";
            await File.WriteAllTextAsync(Path.Combine(directory.Path, oldPath), "Исходный текст\n");
            using var feed = new FeedViewModel(() => new DateOnly(2026, 9, 9));
            await feed.InitializeVaultAsync(directory.Path);
            await feed.OpenVaultLinkAsync(oldPath, null);
            var tab = feed.OpenedThematicFile!;
            var window = new Window { Width = 800, Height = 550, Content = new FeedControl { DataContext = feed } };
            try
            {
                window.Show();
                tab.MarkdownEditor.BeginEdit(tab.MarkdownEditor.Blocks.First(block => block.Block.IsContent));
                tab.MarkdownEditor.ActiveBlock!.EditorText = "Не потерять черновик";
                await tab.MarkdownEditor.FlushDraftPersistenceAsync();
                if (deleted) File.Delete(Path.Combine(directory.Path, oldPath));
                else File.Move(Path.Combine(directory.Path, oldPath), Path.Combine(directory.Path, newPath));
                var runtime = (FeedVaultWatchRuntime)typeof(FeedViewModel).GetField("watchRuntime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(feed)!;
                await runtime.ConflictCoordinator.HandleAsync(new(VaultWatchScope.Markdown,
                    deleted ? VaultWatchChangeKind.Deleted : VaultWatchChangeKind.Renamed,
                    deleted ? oldPath : newPath, deleted ? null : oldPath, null), CancellationToken.None);
                for (var i = 0; i < 100 && feed.DocumentConflict is null; i++)
                { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
                await Assert.That(feed.DocumentConflict).IsNotNull();
                await Assert.That(tab.MarkdownEditor.ActiveBlock!.EditorText).IsEqualTo("Не потерять черновик");
                await Assert.That(File.Exists(feed.DocumentConflict!.Conflict.RecoveryBundlePath)).IsTrue();
                if (deleted) await Assert.That(tab.IsMissing).IsTrue();
                else
                {
                    await feed.RefreshAsync();
                    if (daily) await Assert.That(feed.Days.Any(day => day.RelativePath == newPath)).IsFalse();
                    await Assert.That(tab.MarkdownEditor.ActiveBlock!.EditorText).IsEqualTo("Не потерять черновик");
                    await feed.OpenVaultLinkAsync(newPath, null);
                    await Assert.That(feed.DocumentWorkspace.Documents.Count).IsEqualTo(1);
                    await Assert.That(feed.DocumentWorkspace.Find(newPath)).IsSameReferenceAs(tab);
                    await feed.DocumentConflict.ResolveAsync(DocumentConflictResolution.UseEditor);
                    await Assert.That(tab.RelativePath).IsEqualTo(newPath);
                    await Assert.That(await File.ReadAllTextAsync(Path.Combine(directory.Path, newPath))).Contains("Не потерять черновик");
                    await Assert.That(File.Exists(Path.Combine(directory.Path, oldPath))).IsFalse();
                    if (daily) await Assert.That(tab.MarkdownEditor).IsSameReferenceAs(feed.Days.Single(day => day.RelativePath == newPath).MarkdownEditor);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task CleanDeletionPreservesRecoveryEvenAfterClosingTab()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            await File.WriteAllTextAsync(Path.Combine(directory.Path, "Old.md"), "Сохранить удалённый текст\n");
            using var feed = new FeedViewModel(() => new DateOnly(2026, 9, 9));
            await feed.InitializeVaultAsync(directory.Path);
            await feed.OpenVaultLinkAsync("Old.md", null);
            var tab = feed.OpenedThematicFile!;
            var view = new FeedControl { DataContext = feed };
            var window = new Window { Width = 800, Height = 550, Content = view };
            try
            {
                window.Show();
                File.Delete(Path.Combine(directory.Path, "Old.md"));
                await Reload(feed, new(new(VaultWatchScope.Markdown, VaultWatchChangeKind.Deleted, "Old.md", null, null), null));
                Dispatcher.UIThread.RunJobs();
                var status = view.GetVisualDescendants().OfType<TextBlock>().Single(control =>
                    AutomationProperties.GetAutomationId(control) == "FeedDocumentExternalChangeStatus");
                await Assert.That(status.IsEffectivelyVisible).IsTrue();
                await Assert.That(status.Text).IsEqualTo(tab.ExternalChangeMessage);
                await feed.CloseDocumentAsync(tab);
                var runtime = (FeedVaultWatchRuntime)typeof(FeedViewModel).GetField("watchRuntime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(feed)!;
                var drafts = await runtime.Drafts.ListAsync(runtime.VaultId);
                await Assert.That(drafts.Any(draft => draft.RelativePath == "Old.md"
                    && draft.EditorDocumentText!.Contains("Сохранить удалённый текст"))).IsTrue();
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ScheduledCleanSignalAcknowledgesMatchingDirtyBytesWithoutLoop()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            await File.WriteAllTextAsync(Path.Combine(directory.Path, "Old.md"), "Было\n");
            using var feed = new FeedViewModel(() => new DateOnly(2026, 9, 9));
            await feed.InitializeVaultAsync(directory.Path);
            await feed.OpenVaultLinkAsync("Old.md", null);
            var tab = feed.OpenedThematicFile!;
            tab.MarkdownEditor.BeginEdit(tab.MarkdownEditor.Blocks.First(block => block.Block.IsContent));
            tab.MarkdownEditor.ActiveBlock!.EditorText = "Стало";
            var raw = tab.MarkdownEditor.GetSnapshotWithActiveDraft()!.Raw;
            await File.WriteAllTextAsync(Path.Combine(directory.Path, "Old.md"), raw);
            var disk = await new FileNoteVault(directory.Path).ReadAsync("Old.md");
            await Reload(feed, new(new(VaultWatchScope.Markdown, VaultWatchChangeKind.Changed, "Old.md", null, disk!.Revision), disk))
                .WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(tab.MarkdownEditor.Snapshot!.Raw).IsEqualTo(raw);
            await Assert.That(tab.MarkdownEditor.ActiveBlock?.IsDirty == true).IsFalse();
            await Assert.That(feed.DocumentWorkspace.Documents.Count).IsEqualTo(1);
        }, CancellationToken.None);
    }

    private static Task Reload(FeedViewModel feed, DocumentReloadSignal signal) =>
        (Task)typeof(FeedViewModel).GetMethod("HandleDocumentReloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(feed, [signal, CancellationToken.None])!;

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task RenameOverOpenTargetPreservesBothVersionsAndOneFinalEditor(bool sourceDirty, bool targetDirty)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            await File.WriteAllTextAsync(Path.Combine(directory.Path, "A.md"), "Версия A\n");
            await File.WriteAllTextAsync(Path.Combine(directory.Path, "B.md"), "Версия B\n");
            using var feed = new FeedViewModel(() => new DateOnly(2026, 9, 9));
            await feed.InitializeVaultAsync(directory.Path);
            await feed.OpenVaultLinkAsync("A.md", null);
            var first = feed.OpenedThematicFile!;
            await feed.OpenVaultLinkAsync("B.md", null);
            var second = feed.OpenedThematicFile!;
            await feed.ActivateDocumentAsync(first);
            if (sourceDirty)
            {
                first.MarkdownEditor.BeginEdit(first.MarkdownEditor.Blocks.First(block => block.Block.IsContent));
                first.MarkdownEditor.ActiveBlock!.EditorText = "Черновик A";
            }
            if (targetDirty)
            {
                second.MarkdownEditor.BeginEdit(second.MarkdownEditor.Blocks.First(block => block.Block.IsContent));
                second.MarkdownEditor.ActiveBlock!.EditorText = "Черновик B";
            }
            File.Move(Path.Combine(directory.Path, "A.md"), Path.Combine(directory.Path, "B.md"), overwrite: true);
            var runtime = (FeedVaultWatchRuntime)typeof(FeedViewModel).GetField("watchRuntime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(feed)!;
            var change = new VaultWatchChange(VaultWatchScope.Markdown, VaultWatchChangeKind.Renamed, "B.md", "A.md", null);
            if (sourceDirty)
                await runtime.ConflictCoordinator.HandleAsync(change, CancellationToken.None);
            else
                await Reload(feed, new(change, await new FileNoteVault(directory.Path).ReadAsync("B.md")));
            if (targetDirty)
            {
                for (var i = 0; i < 100 && feed.DocumentConflict is null; i++)
                { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
                await Assert.That(second.MarkdownEditor.ActiveBlock!.EditorText).IsEqualTo("Черновик B");
                if (sourceDirty) await Assert.That(first.MarkdownEditor.ActiveBlock!.EditorText).IsEqualTo("Черновик A");
                await Assert.That(feed.DocumentWorkspace.Documents.Count(tab => tab.RelativePath == "B.md")).IsEqualTo(1);
                for (var step = 0; step < 3 && feed.DocumentConflict is { IsOpen: true } conflict; step++)
                {
                    await conflict.ResolveAsync(DocumentConflictResolution.UseEditor);
                    await Assert.That(conflict.ErrorMessage).IsNull();
                    Dispatcher.UIThread.RunJobs();
                }
                await Assert.That(feed.DocumentConflict?.IsOpen == true).IsFalse();
                var preserved = await runtime.ConflictBundles.ListAsync(runtime.VaultId);
                await Assert.That(preserved.Any(bundle => bundle.EditorMarkdown.Contains("Черновик B"))).IsTrue();
                if (sourceDirty) await Assert.That(preserved.Any(bundle => bundle.EditorMarkdown.Contains("Черновик A"))).IsTrue();
            }
            await Assert.That(feed.DocumentWorkspace.Documents.Count).IsEqualTo(1);
            await Assert.That(feed.OpenedThematicFile).IsSameReferenceAs(first);
            await Assert.That(first.RelativePath).IsEqualTo("B.md");
            await Assert.That(first.MarkdownEditor.Snapshot!.Raw).IsEqualTo(await File.ReadAllTextAsync(Path.Combine(directory.Path, "B.md")));
            await Assert.That(File.Exists(Path.Combine(directory.Path, "A.md"))).IsFalse();
        }, CancellationToken.None);
    }

    [Test]
    public async Task RetainedConflictCallbackCannotTouchSamePathInAnotherVault()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var first = new TempNotesDirectory();
            using var second = new TempNotesDirectory();
            await File.WriteAllTextAsync(Path.Combine(first.Path, "Note.md"), "Пространство A\n");
            await File.WriteAllTextAsync(Path.Combine(second.Path, "Note.md"), "Пространство B\n");
            using var feed = new FeedViewModel(() => new DateOnly(2026, 9, 9));
            await feed.InitializeVaultAsync(first.Path);
            await feed.OpenVaultLinkAsync("Note.md", null);
            var oldEditor = feed.OpenedThematicFile!.MarkdownEditor;
            oldEditor.BeginEdit(oldEditor.Blocks.First(block => block.Block.IsContent));
            oldEditor.ActiveBlock!.EditorText = "Черновик A";
            await File.WriteAllTextAsync(Path.Combine(first.Path, "Note.md"), "Внешняя версия A\n");
            var runtime = (FeedVaultWatchRuntime)typeof(FeedViewModel).GetField("watchRuntime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(feed)!;
            await runtime.ConflictCoordinator.HandleAsync(new(VaultWatchScope.Markdown, VaultWatchChangeKind.Changed,
                "Note.md", null, null), CancellationToken.None);
            for (var i = 0; i < 100 && feed.DocumentConflict is null; i++)
            { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
            var oldConflict = feed.DocumentConflict!;
            var callback = oldConflict.ResolvedCallbackAsync!;
            oldEditor.CancelActiveEdit();
            await feed.InitializeVaultAsync(second.Path);
            await feed.OpenVaultLinkAsync("Note.md", null);
            var tab = feed.OpenedThematicFile!;
            var editor = tab.MarkdownEditor;
            editor.BeginEdit(editor.Blocks.First(block => block.Block.IsContent));
            var active = editor.ActiveBlock!;
            active.EditorText = "Не трогать черновик B";
            await callback(new(oldConflict.Conflict.ConflictId, DocumentConflictResolution.UseDisk,
                "Note.md", null, null, oldConflict.Conflict.RecoveryBundlePath));
            await Assert.That(feed.VaultRootPath).IsEqualTo(second.Path);
            await Assert.That(feed.OpenedThematicFile).IsSameReferenceAs(tab);
            await Assert.That(tab.MarkdownEditor).IsSameReferenceAs(editor);
            await Assert.That(editor.ActiveBlock).IsSameReferenceAs(active);
            await Assert.That(active.EditorText).IsEqualTo("Не трогать черновик B");
            await Assert.That(await File.ReadAllTextAsync(Path.Combine(second.Path, "Note.md"))).IsEqualTo("Пространство B\n");
        }, CancellationToken.None);
    }
}
