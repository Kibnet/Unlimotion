using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unlimotion.Notes.Recovery;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.Test;

public class MarkdownLivePreviewEditorTests
{
    [Test]
    public async Task Autosave_DebouncesInput_KeepsEditing_AndAdvancesExpectedRevision()
    {
        var delay = new ManualAutosaveDelay();
        var source = new MarkdownLiveDocumentSnapshot("Исходный\n", "revision-1", false, "note.md");
        using var editor = new MarkdownLivePreviewEditorViewModel(
            autosaveDelay: TimeSpan.FromSeconds(2),
            autosaveDelayAsync: delay.DelayAsync);
        var patches = new List<MarkdownBlockPatch>();
        editor.CommitBlockAsync = (patch, _) =>
        {
            patches.Add(patch);
            return Task.FromResult(MarkdownBlockCommitResult.Accepted(source with
            {
                Raw = patch.PatchedDocumentRaw,
                ExpectedRevisionHash = $"revision-{patches.Count + 1}"
            }));
        };
        editor.Load(source);
        var block = editor.Blocks.Single();

        editor.BeginEdit(block);
        block.EditorText = "Промежуточный";
        block.EditorText = "Первый autosave";

        await Assert.That(delay.Count).IsEqualTo(2);
        await Assert.That(delay.IsCanceled(0)).IsTrue();
        delay.Release(1);
        await editor.FlushAutosaveAsync();

        await Assert.That(patches.Count).IsEqualTo(1);
        await Assert.That(patches[0].ExpectedRevisionHash).IsEqualTo("revision-1");
        await Assert.That(patches[0].ReplacementRaw).IsEqualTo("Первый autosave\n");
        await Assert.That(editor.Snapshot!.ExpectedRevisionHash).IsEqualTo("revision-2");
        await Assert.That(editor.ActiveBlock).IsSameReferenceAs(block);
        await Assert.That(block.IsEditing).IsTrue();
        await Assert.That(block.IsDirty).IsFalse();

        block.EditorText = "Второй autosave";
        delay.Release(2);
        await editor.FlushAutosaveAsync();

        await Assert.That(patches.Count).IsEqualTo(2);
        await Assert.That(patches[1].ExpectedRevisionHash).IsEqualTo("revision-2");
        await Assert.That(editor.Snapshot!.ExpectedRevisionHash).IsEqualTo("revision-3");
        await Assert.That(block.IsEditing).IsTrue();
    }

    [Test]
    public async Task Autosave_CancelsStaleDelayWhenBlockChangesOrEditorIsDisposed()
    {
        var delay = new ManualAutosaveDelay();
        var commitCount = 0;
        var editor = new MarkdownLivePreviewEditorViewModel(
            autosaveDelayAsync: delay.DelayAsync);
        editor.CommitBlockAsync = (_, _) =>
        {
            commitCount++;
            throw new InvalidOperationException("A canceled autosave must not commit.");
        };
        editor.Load(new MarkdownLiveDocumentSnapshot("Первый\n\nВторой\n", "revision-1", false, "note.md"));
        var first = editor.Blocks.Single(candidate => candidate.PreviewText == "Первый");
        var second = editor.Blocks.Single(candidate => candidate.PreviewText == "Второй");

        editor.BeginEdit(first);
        first.EditorText = "Изменённый первый";
        await Assert.That(delay.Count).IsEqualTo(1);

        editor.BeginEdit(second);
        await editor.FlushAutosaveAsync();
        await Assert.That(delay.IsCanceled(0)).IsTrue();
        await Assert.That(commitCount).IsEqualTo(0);

        second.EditorText = "Изменённый второй";
        await Assert.That(delay.Count).IsEqualTo(2);
        editor.Dispose();
        await Assert.That(delay.IsCanceled(1)).IsTrue();
        await Assert.That(commitCount).IsEqualTo(0);
    }

    [Test]
    public async Task DebouncedConflict_UsesRevisionCheckedCommitAndPreservesDurableDraft()
    {
        var delay = new ManualAutosaveDelay();
        var drafts = new MemoryFeedDraftStore();
        using var editor = new MarkdownLivePreviewEditorViewModel(
            autosaveDelayAsync: delay.DelayAsync);
        editor.ConfigureDraftPersistence("vault1", drafts);
        var commitStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rejectCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MarkdownBlockPatch? rejectedPatch = null;
        var commitCount = 0;
        editor.CommitBlockAsync = async (patch, _) =>
        {
            commitCount++;
            rejectedPatch = patch;
            commitStarted.TrySetResult();
            await rejectCommit.Task;
            return MarkdownBlockCommitResult.Rejected("Файл изменён снаружи.");
        };
        editor.Load(new MarkdownLiveDocumentSnapshot("Версия диска\n", "revision-1", false, "note.md"));
        var block = editor.Blocks.Single();

        editor.BeginEdit(block);
        block.EditorText = "Версия редактора";
        await editor.FlushDraftPersistenceAsync();
        delay.Release(0);
        var rejectedAutosave = editor.FlushAutosaveAsync();
        await commitStarted.Task;
        block.EditorText = "Версия редактора после конфликта";
        await editor.FlushDraftPersistenceAsync();
        await Assert.That(delay.Count).IsEqualTo(2);
        rejectCommit.TrySetResult();
        await rejectedAutosave;
        await editor.FlushAutosaveAsync();
        await editor.FlushDraftPersistenceAsync();

        await Assert.That(rejectedPatch).IsNotNull();
        await Assert.That(rejectedPatch!.ExpectedRevisionHash).IsEqualTo("revision-1");
        await Assert.That(commitCount).IsEqualTo(1);
        await Assert.That(delay.IsCanceled(1)).IsTrue();
        await Assert.That(editor.ActiveBlock).IsSameReferenceAs(block);
        await Assert.That(block.IsEditing).IsTrue();
        await Assert.That(block.IsDirty).IsTrue();
        await Assert.That(block.ErrorMessage).IsEqualTo("Файл изменён снаружи.");
        var draft = (await drafts.ListAsync("vault1")).Single();
        await Assert.That(draft.BaseRevision).IsEqualTo("revision-1");
        await Assert.That(draft.RawMarkdown).IsEqualTo("Версия редактора после конфликта");
        await Assert.That(draft.EditorDocumentText).IsEqualTo("Версия редактора после конфликта\n");
    }

    [Test]
    public async Task ExplicitCommitWaitsForInFlightAutosave_ThenClosesWithoutDuplicateWrite()
    {
        var delay = new ManualAutosaveDelay();
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new MarkdownLiveDocumentSnapshot("До\n", "revision-1", false, "note.md");
        using var editor = new MarkdownLivePreviewEditorViewModel(
            autosaveDelayAsync: delay.DelayAsync);
        var commitCount = 0;
        var notificationCount = 0;
        editor.CommitAccepted = _ => notificationCount++;
        editor.CommitBlockAsync = async (patch, _) =>
        {
            commitCount++;
            writeStarted.TrySetResult();
            await finishWrite.Task;
            return MarkdownBlockCommitResult.Accepted(source with
            {
                Raw = patch.PatchedDocumentRaw,
                ExpectedRevisionHash = "revision-2"
            });
        };
        editor.Load(source);
        var block = editor.Blocks.Single();

        editor.BeginEdit(block);
        block.EditorText = "После";
        delay.Release(0);
        await writeStarted.Task;

        var explicitCommit = editor.CommitActiveAsync();
        finishWrite.TrySetResult();
        await editor.FlushAutosaveAsync();

        await Assert.That(await explicitCommit).IsTrue();
        await Assert.That(commitCount).IsEqualTo(1);
        await Assert.That(notificationCount).IsEqualTo(1);
        await Assert.That(editor.ActiveBlock).IsNull();
        await Assert.That(editor.Snapshot!.ExpectedRevisionHash).IsEqualTo("revision-2");
    }

    [Test]
    public async Task CancelDuringInFlightAutosave_ReconcilesAlreadyCommittedSnapshot()
    {
        var delay = new ManualAutosaveDelay();
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new MarkdownLiveDocumentSnapshot("До\n", "revision-1", false, "note.md");
        using var editor = new MarkdownLivePreviewEditorViewModel(
            autosaveDelayAsync: delay.DelayAsync);
        var notificationCount = 0;
        editor.CommitAccepted = _ => notificationCount++;
        editor.CommitBlockAsync = async (patch, _) =>
        {
            writeStarted.TrySetResult();
            await finishWrite.Task;
            return MarkdownBlockCommitResult.Accepted(source with
            {
                Raw = patch.PatchedDocumentRaw,
                ExpectedRevisionHash = "revision-2"
            });
        };
        editor.Load(source);
        var block = editor.Blocks.Single();

        editor.BeginEdit(block);
        block.EditorText = "После";
        delay.Release(0);
        await writeStarted.Task;

        editor.CancelActiveEdit();
        await Assert.That(editor.ActiveBlock).IsNull();
        finishWrite.TrySetResult();
        await editor.FlushAutosaveAsync();

        await Assert.That(notificationCount).IsEqualTo(1);
        await Assert.That(editor.Snapshot!.Raw).IsEqualTo("После\n");
        await Assert.That(editor.Snapshot.ExpectedRevisionHash).IsEqualTo("revision-2");
        await Assert.That(editor.Blocks.Single().PreviewText).IsEqualTo("После");
    }

    [Test]
    public async Task Commit_EmitsExactBlockPatchWithRevisionAndBom_WithoutChangingNeighbors()
    {
        const string raw = "---\r\ncustom:  keep\r\n---\r\n\r\nПервый абзац\r\n\r\nВторой  абзац\r\n";
        var source = new MarkdownLiveDocumentSnapshot(raw, "revision-1", true, "Ежедневные/2026-08-24.md");
        using var editor = new MarkdownLivePreviewEditorViewModel();
        MarkdownBlockPatch? captured = null;
        editor.CommitBlockAsync = (patch, _) =>
        {
            captured = patch;
            return Task.FromResult(MarkdownBlockCommitResult.Accepted(source with
            {
                Raw = patch.PatchedDocumentRaw,
                ExpectedRevisionHash = "revision-2"
            }));
        };
        editor.Load(source);
        var block = editor.Blocks.Single(candidate => candidate.PreviewText == "Первый абзац");

        editor.BeginEdit(block);
        block.EditorText = "Изменённый\nабзац";
        var accepted = await editor.CommitActiveAsync();

        await Assert.That(accepted).IsTrue();
        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.ExpectedRevisionHash).IsEqualTo("revision-1");
        await Assert.That(captured.HasUtf8Bom).IsTrue();
        await Assert.That(captured.RelativePath).IsEqualTo("Ежедневные/2026-08-24.md");
        await Assert.That(captured.ReplacementRaw).IsEqualTo("Изменённый\r\nабзац\r\n");
        await Assert.That(captured.PatchedDocumentRaw)
            .IsEqualTo("---\r\ncustom:  keep\r\n---\r\n\r\nИзменённый\r\nабзац\r\n\r\nВторой  абзац\r\n");
        await Assert.That(editor.Snapshot!.ExpectedRevisionHash).IsEqualTo("revision-2");
    }

    [Test]
    public async Task RejectedCommit_KeepsDirtyEditorAndOriginalSnapshotForConflictRecovery()
    {
        const string raw = "Исходный блок\n";
        using var editor = new MarkdownLivePreviewEditorViewModel();
        editor.CommitBlockAsync = (_, _) => Task.FromResult(MarkdownBlockCommitResult.Rejected("Файл изменён снаружи."));
        editor.Load(new MarkdownLiveDocumentSnapshot(raw, "revision-1", false, "note.md"));
        var block = editor.Blocks.Single();

        editor.BeginEdit(block);
        block.EditorText = "Версия редактора";
        var accepted = await editor.CommitActiveAsync();

        await Assert.That(accepted).IsFalse();
        await Assert.That(block.IsEditing).IsTrue();
        await Assert.That(block.IsDirty).IsTrue();
        await Assert.That(block.ErrorMessage).IsEqualTo("Файл изменён снаружи.");
        await Assert.That(editor.Snapshot!.Raw).IsEqualTo(raw);
    }

    [Test]
    public async Task PreviewTokens_KeepUnsafeUriVisibleButInactive()
    {
        const string raw = "[опасная](javascript:alert(1)) и [безопасная](https://example.org) и [[Тематика/Заметка|wiki]]\n";
        using var editor = new MarkdownLivePreviewEditorViewModel();
        editor.Load(new MarkdownLiveDocumentSnapshot(raw, "revision-1", false, "note.md"));
        var tokens = editor.Blocks.Single().InlineTokens;

        var unsafeLink = tokens.Single(token => token.Text == "опасная");
        var safeLink = tokens.Single(token => token.Text == "безопасная");
        var wikiLink = tokens.Single(token => token.Text == "wiki");

        await Assert.That(unsafeLink.Kind).IsEqualTo(MarkdownInlineTokenKind.Link);
        await Assert.That(unsafeLink.IsSafeLink).IsFalse();
        await Assert.That(safeLink.IsSafeLink).IsTrue();
        await Assert.That(wikiLink.Kind).IsEqualTo(MarkdownInlineTokenKind.WikiLink);
        await Assert.That(wikiLink.IsSafeLink).IsTrue();
    }

    [Test]
    public async Task UnsupportedHtmlAndObsidianPluginSyntax_UseRawFallback()
    {
        const string raw = "<div onclick=\"run()\">raw</div>\n\nproperty:: value\n\n> [!NOTE]\n> callout\n";
        using var editor = new MarkdownLivePreviewEditorViewModel();
        editor.Load(new MarkdownLiveDocumentSnapshot(raw, "revision-1", false, "note.md"));

        var content = editor.Blocks.Where(static block => block.IsEditable).ToArray();

        await Assert.That(content.Length).IsEqualTo(3);
        await Assert.That(content.All(static block => block.RenderKind == MarkdownLiveBlockRenderKind.RawFallback)).IsTrue();
        await Assert.That(content.Select(static block => block.PreviewText))
            .IsEquivalentTo(new[]
            {
                "<div onclick=\"run()\">raw</div>",
                "property:: value",
                "> [!NOTE]\n> callout"
            });
    }

    [Test]
    public async Task Cancel_RestoresOriginalRawEditorText()
    {
        using var editor = new MarkdownLivePreviewEditorViewModel();
        editor.CommitBlockAsync = (_, _) => throw new InvalidOperationException("Commit must not run.");
        editor.Load(new MarkdownLiveDocumentSnapshot("**Исходный**\n", "revision-1", false, "note.md"));
        var block = editor.Blocks.Single();

        editor.BeginEdit(block);
        block.EditorText = "Изменённый";
        editor.CancelActiveEdit();

        await Assert.That(block.IsEditing).IsFalse();
        await Assert.That(block.EditorText).IsEqualTo("**Исходный**");
        await Assert.That(editor.Snapshot!.Raw).IsEqualTo("**Исходный**\n");
    }

    [Test]
    public async Task DirtyBlockIsPersistedWithFullEditorDocumentBeforeEditorDisposal()
    {
        var drafts = new MemoryFeedDraftStore();
        using var editor = new MarkdownLivePreviewEditorViewModel();
        editor.ConfigureDraftPersistence("vault1", drafts);
        editor.CommitBlockAsync = (_, _) => throw new InvalidOperationException("Commit is not expected.");
        editor.Load(new MarkdownLiveDocumentSnapshot("Первый\n\nВторой\n", "revision-1", false, "note.md"));
        var block = editor.Blocks.Single(candidate => candidate.PreviewText == "Первый");

        editor.BeginEdit(block);
        block.EditorText = "Несохранённый";
        editor.Dispose();

        var draft = (await drafts.ListAsync("vault1")).Single();
        await Assert.That(draft.RelativePath).IsEqualTo("note.md");
        await Assert.That(draft.BlockIndex).IsEqualTo(block.Index);
        await Assert.That(draft.BaseRevision).IsEqualTo("revision-1");
        await Assert.That(draft.RawMarkdown).IsEqualTo("Несохранённый");
        await Assert.That(draft.EditorDocumentText).IsEqualTo("Несохранённый\n\nВторой\n");
    }

    [Test]
    public async Task RestartRecoveryRequiresExplicitRestoreAndCommitRemovesDraft()
    {
        var drafts = new MemoryFeedDraftStore();
        var draft = new FeedDraft(
            1,
            "vault1",
            "note.md",
            0,
            "revision-1",
            "Версия из recovery",
            DateTimeOffset.UtcNow,
            "Версия из recovery\n",
            false);
        await drafts.SaveAsync(draft);
        var source = new MarkdownLiveDocumentSnapshot("Исходная\n", "revision-1", false, "note.md");
        using var editor = new MarkdownLivePreviewEditorViewModel();
        editor.ConfigureDraftPersistence("vault1", drafts);
        editor.CommitBlockAsync = (patch, _) => Task.FromResult(MarkdownBlockCommitResult.Accepted(source with
        {
            Raw = patch.PatchedDocumentRaw,
            ExpectedRevisionHash = "revision-2"
        }));
        editor.Load(source);

        editor.OfferRecoveryDraft((await drafts.ListAsync("vault1")).Single());

        await Assert.That(editor.HasRecoveryDraft).IsTrue();
        await Assert.That(editor.ActiveBlock).IsNull();
        await Assert.That(editor.RestoreRecoveryDraft()).IsTrue();
        await Assert.That(editor.ActiveBlock!.EditorText).IsEqualTo("Версия из recovery");
        await Assert.That(editor.ActiveBlock.IsDirty).IsTrue();

        await Assert.That(await editor.CommitActiveAsync()).IsTrue();
        await Assert.That(await drafts.ListAsync("vault1")).IsEmpty();
    }

    [Test]
    public async Task StaleRecoveryCannotOverwriteNewDiskRevisionAndExplicitDiscardDeletesIt()
    {
        var drafts = new MemoryFeedDraftStore();
        var draft = new FeedDraft(
            1,
            "vault1",
            "note.md",
            0,
            "revision-1",
            "Старый recovery",
            DateTimeOffset.UtcNow,
            "Старый recovery\n",
            false);
        await drafts.SaveAsync(draft);
        using var editor = new MarkdownLivePreviewEditorViewModel();
        editor.ConfigureDraftPersistence("vault1", drafts);
        editor.CommitBlockAsync = (_, _) => throw new InvalidOperationException("Commit is not expected.");
        editor.Load(new MarkdownLiveDocumentSnapshot("Новая версия\n", "revision-2", false, "note.md"));
        editor.OfferRecoveryDraft(draft);

        await Assert.That(editor.IsRecoveryDraftStale).IsTrue();
        await Assert.That(editor.RestoreRecoveryDraft()).IsFalse();
        await Assert.That(editor.Snapshot!.Raw).IsEqualTo("Новая версия\n");
        await Assert.That(await drafts.ListAsync("vault1")).IsEquivalentTo(new[] { draft });

        await editor.DiscardRecoveryDraftAsync();

        await Assert.That(editor.HasRecoveryDraft).IsFalse();
        await Assert.That(await drafts.ListAsync("vault1")).IsEmpty();
    }
}

internal sealed class ManualAutosaveDelay
{
    private readonly object sync = new();
    private readonly List<DelayRequest> requests = [];

    public int Count
    {
        get
        {
            lock (sync)
            {
                return requests.Count;
            }
        }
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(
            static state =>
            {
                var request = (DelayRequest)state!;
                request.Completion.TrySetCanceled(request.CancellationToken);
            },
            new DelayRequest(completion, cancellationToken));
        var request = new DelayRequest(completion, cancellationToken, registration);
        lock (sync)
        {
            requests.Add(request);
        }

        return AwaitAndDisposeAsync(request);
    }

    public bool IsCanceled(int index)
    {
        lock (sync)
        {
            return requests[index].Completion.Task.IsCanceled;
        }
    }

    public void Release(int index)
    {
        DelayRequest request;
        lock (sync)
        {
            request = requests[index];
        }

        request.Completion.TrySetResult();
    }

    private static async Task AwaitAndDisposeAsync(DelayRequest request)
    {
        try
        {
            await request.Completion.Task.ConfigureAwait(false);
        }
        finally
        {
            request.Registration.Dispose();
        }
    }

    private sealed record DelayRequest(
        TaskCompletionSource Completion,
        CancellationToken CancellationToken,
        CancellationTokenRegistration Registration = default);
}
