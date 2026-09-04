using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Vault;
using Unlimotion.ViewModel.Feed;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class FeedBrokenTaskReferenceUiTests
{
    private const string MissingTaskId = "missing-task";
    private const string BrokenLink = "[Исходная задача](unlimotion://task/missing-task)\n";

    [Test]
    public async Task BrokenReferenceShowsExplicitRecoveryActionsInsteadOfStatusPicker()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var editor = CreateEditor();
            var view = new MarkdownBlockLivePreviewEditor { DataContext = editor };
            var window = new Window { Width = 900, Height = 300, Content = view };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                await Assert.That(Find<Grid>(view, $"FeedTask-{MissingTaskId}-BrokenReference")).IsNotNull();
                await Assert.That(Find<Button>(view, $"FeedTask-{MissingTaskId}-BrokenFindButton")).IsNotNull();
                await Assert.That(Find<Button>(view, $"FeedTask-{MissingTaskId}-BrokenUnlinkButton")).IsNotNull();
                await Assert.That(Find<Button>(view, $"FeedTask-{MissingTaskId}-BrokenRestoreRevisionButton")).IsNotNull();
                await Assert.That(Find<TaskStatusPicker>(view, $"FeedTask-{MissingTaskId}-StatusPicker")).IsNull();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task UnlinkReplacesOnlyBrokenLiveLinkWithFallbackText()
    {
        using var editor = CreateEditor();
        using var feed = new FeedViewModel();
        editor.CommitBlockAsync = (patch, _) => Task.FromResult(
            MarkdownBlockCommitResult.Accepted(new MarkdownLiveDocumentSnapshot(
                patch.PatchedDocumentRaw,
                "accepted-revision",
                patch.HasUtf8Bom,
                patch.RelativePath)));

        await feed.HandleBrokenTaskReferenceAsync(
            editor,
            blockIndex: 0,
            MissingTaskId,
            FeedBrokenTaskReferenceAction.Unlink);

        await Assert.That(editor.Snapshot!.Raw).IsEqualTo("Исходная задача\n");
        await Assert.That(editor.ActiveBlock).IsNull();
    }

    [Test]
    public async Task RestoreRevisionRecoversOriginalBlockWithoutRecreatingMissingTask()
    {
        using var vaultDirectory = new TempNotesDirectory();
        using var revisionDirectory = new TempNotesDirectory();
        const string relativePath = "Ежедневные/2026-08-24.md";
        var vault = new FileNoteVault(vaultDirectory.Path);
        await vault.CreateAsync(relativePath, BrokenLink);
        var revisionPath = Path.Combine(revisionDirectory.Path, "before.md");
        await File.WriteAllTextAsync(revisionPath, "- [ ] Исходная задача\n");
        var revisions = new FixedRevisionStore(revisionPath);
        using var feed = new FeedViewModel(
            () => new DateOnly(2026, 8, 24),
            revisionStoreFactory: _ => revisions);
        feed.SetNotificationDispatcher(static action => action());
        await feed.InitializeVaultAsync(vaultDirectory.Path);
        var editor = feed.Days.Single().MarkdownEditor;
        var block = editor.Blocks.Single(candidate => candidate.ResolveTaskReference(
            $"unlimotion://task/{MissingTaskId}") is not null);

        await feed.HandleBrokenTaskReferenceAsync(
            editor,
            block.Index,
            MissingTaskId,
            FeedBrokenTaskReferenceAction.RestoreRevision);

        var restored = await vault.ReadAsync(relativePath);
        await Assert.That(restored!.Text).IsEqualTo("- [ ] Исходная задача\n");
        await Assert.That(feed.ErrorMessage).IsNull();
    }

    private static MarkdownLivePreviewEditorViewModel CreateEditor()
    {
        var editor = new MarkdownLivePreviewEditorViewModel(automationIdPrefix: "BrokenReference");
        editor.SetTaskReferences([
            new FeedTaskReferenceViewModel(MissingTaskId, "Исходная задача", task: null)
        ]);
        editor.Load(new MarkdownLiveDocumentSnapshot(
            BrokenLink,
            "initial-revision",
            false,
            "Ежедневные/2026-08-24.md"));
        return editor;
    }

    private static T? Find<T>(Control root, string automationId)
        where T : Control => root.GetVisualDescendants()
        .OfType<T>()
        .FirstOrDefault(control => string.Equals(
            AutomationProperties.GetAutomationId(control),
            automationId,
            StringComparison.Ordinal));

    private sealed class FixedRevisionStore(string revisionPath) : IRevisionStore
    {
        public Task SaveAsync(
            string vaultId,
            VaultDocument document,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListAsync(
            string vaultId,
            string relativePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([revisionPath]);
    }
}
