using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Notes.Daily;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Review;

namespace Unlimotion.Test;

public class FeedReviewQueueTests
{
    [Test]
    [Arguments("[Задача](unlimotion://task/task-1)", "task-1")]
    [Arguments("[[Работа/Заметка|Заметка]] <!-- unlimotion-note:note-1 -->", "note-1")]
    [Arguments("[[Ежедневные/2026-09-04#^unlimotion-move-1|Перенесено]]", "unlimotion-move-1")]
    public async Task ConfirmedOutput_RemainsResolvedWhenBothNeighboursChange(string link, string entityId)
    {
        var (queue, _, _) = ConfirmedOutputQueue(link, entityId);
        var result = queue.Build([("Ежедневные/2026-09-04.md", $"Новый верх\n\n{link}\n\nНовый низ\n")], Envelope("device", 4));
        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result.Any(candidate => candidate.Block.Raw.Trim() == link)).IsFalse();
    }

    [Test]
    [Arguments("duplicate")]
    [Arguments("area")]
    [Arguments("title")]
    [Arguments("path")]
    [Arguments("conflict")]
    [Arguments("missing-provenance")]
    [Arguments("mention")]
    public async Task ConfirmedOutput_DoesNotHideAmbiguousOrChangedContent(string change)
    {
        var link = change == "mention" ? "См. [Задача](unlimotion://task/task-1) и обсудить" : "[Задача](unlimotion://task/task-1)";
        var (queue, state, locator) = ConfirmedOutputQueue(link, "task-1", change != "missing-provenance");
        if (change == "conflict")
            state.Add(new ReviewDecisionEvent("vault", "concurrent", Envelope("other", 1), DateTimeOffset.UtcNow,
                locator, ReviewDecision.Deferred, "other-session"));
        var currentLink = change == "title" ? link.Replace("Задача", "Изменено") : link;
        var raw = $"Новый верх\n\n{currentLink}\n\nНовый низ\n";
        if (change == "area") raw = "## Работа <!-- unlimotion-area:work -->\n" + raw;
        if (change == "duplicate") raw += $"\n{link}\n\nПоследний\n";
        var path = change == "path" ? "Ежедневные/2026-09-03.md" : "Ежедневные/2026-09-04.md";
        var result = queue.Build([(path, raw)], Envelope("device", 4));
        await Assert.That(result.Count(candidate => candidate.Block.Raw.Contains("unlimotion://task/task-1")))
            .IsEqualTo(change == "duplicate" ? 2 : 1);
    }

    private static (FeedReviewQueue Queue, ReviewStateStore State, BlockLocator Output) ConfirmedOutputQueue(
        string link, string entityId, bool provenance = true)
    {
        const string path = "Ежедневные/2026-09-04.md";
        var state = new ReviewStateStore();
        var queue = new FeedReviewQueue(new MarkdownDocumentParser(), state);
        var locator = queue.Build([(path, $"До\n\n{link}\n\nПосле\n")], Envelope("device", 1))
            .Single(candidate => candidate.Block.Raw.Trim() == link).Locator;
        if (provenance)
            state.Add(new ReviewDecisionEvent("vault", "source", Envelope("device", 2), DateTimeOffset.UtcNow,
                locator with { ContentHash = "original-checkbox" }, ReviewDecision.Converted,
                Outputs: [locator], OperationId: "operation", ResultEntityId: entityId));
        state.Add(new ReviewDecisionEvent("vault", "output", Envelope("device", 3), DateTimeOffset.UtcNow,
            locator, ReviewDecision.Converted, OperationId: "operation", ResultEntityId: entityId));
        return (queue, state, locator);
    }

    [Test]
    public async Task GeneratedMoveAnchor_IsNotAReviewItem_ButCodeExampleRemainsContent()
    {
        const string raw = "- [ ] Купить книгу\n^unlimotion-move-anchor\n\n```text\n^unlimotion-move-example\n```\n";
        var queue = new FeedReviewQueue(new MarkdownDocumentParser(), new ReviewStateStore());
        var candidates = queue.Build([("Ежедневные/2026-09-04.md", raw)], Envelope("device", 1));
        await Assert.That(candidates.Count).IsEqualTo(2);
        await Assert.That(candidates.Any(candidate => candidate.Block.IsTechnicalMoveAnchor)).IsFalse();
        await Assert.That(candidates.Any(candidate => candidate.Block.Kind == MarkdownBlockKind.FencedCode)).IsTrue();
    }

    [Test]
    public async Task QueueUsesActiveDottedNamingAndSkipsOtherLayouts()
    {
        var queue = new FeedReviewQueue(
            new MarkdownDocumentParser(),
            new ReviewStateStore(),
            DailyNoteNaming.Create("yyyy.MM.dd"));

        var candidates = queue.Build(
        [
            ("Ежедневные/2026.08.23.md", "Текущая дневная запись\n"),
            ("Ежедневные/2026-08-24.md", "Неподходящий формат\n")
        ],
        Envelope("device", 1));

        await Assert.That(candidates).HasSingleItem();
        await Assert.That(candidates[0].Day).IsEqualTo(new DateOnly(2026, 8, 23));
        await Assert.That(candidates[0].Block.Raw).Contains("Текущая");
    }

    [Test]
    public async Task Queue_IncludesNestedUnfinishedItemsButExcludesCompletedItems()
    {
        const string raw = "## Работа <!-- unlimotion-area:a1 -->\n- [x] Родитель\n  - [ ] Ребёнок\n  - [x] Готово\n- [ ] Сосед\nОбычная мысль\n";
        var state = new ReviewStateStore();
        var queue = new FeedReviewQueue(new MarkdownDocumentParser(), state);

        var candidates = queue.Build(
            [("Ежедневные/2026-08-23.md", raw)],
            Envelope("device", 1));

        await Assert.That(candidates.Count).IsEqualTo(3);
        await Assert.That(candidates.Count(candidate => candidate.Priority == FeedReviewPriority.IncompleteCheckbox)).IsEqualTo(2);
        await Assert.That(candidates.Any(candidate => candidate.Block.Raw.Contains("Готово", StringComparison.Ordinal))).IsFalse();
        await Assert.That(candidates.Any(candidate => candidate.Block.Raw.Contains("Ребёнок", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Queue_TerminalBaselineHidesOrdinaryBlockButNotUnfinishedCheckbox()
    {
        const string path = "Ежедневные/2026-08-23.md";
        const string raw = "Старая мысль\n\n- [ ] Старое дело\n";
        var parser = new MarkdownDocumentParser();
        var document = parser.Parse(raw);
        var ordinary = document.Blocks.Single(block => block.Kind == MarkdownBlockKind.Paragraph);
        var state = new ReviewStateStore();
        var ordinaryLocator = new FeedReviewQueue(parser, state)
            .Build([(path, raw)], Envelope("device", 0))
            .Single(candidate => candidate.Block == ordinary)
            .Locator;
        state.Add(new ReviewDecisionEvent(
            "vault",
            "event1",
            Envelope("device", 1),
            DateTimeOffset.UtcNow,
            ordinaryLocator,
            ReviewDecision.BaselineKept));

        var candidates = new FeedReviewQueue(parser, state).Build([(path, raw)], Envelope("device", 2));

        await Assert.That(candidates).HasSingleItem();
        await Assert.That(candidates[0].Block.Kind).IsEqualTo(MarkdownBlockKind.TaskListItem);
    }

    [Test]
    public async Task Queue_DeferredReturnsOnlyAfterCausallyObservedClose()
    {
        const string path = "Ежедневные/2026-08-23.md";
        const string raw = "Отложенная мысль\n";
        var parser = new MarkdownDocumentParser();
        var block = parser.Parse(raw).Blocks.Single();
        var state = new ReviewStateStore();
        state.Add(new ReviewSessionEvent(
            "vault", "open", "session1", ReviewSessionEventKind.Opened,
            Envelope("first", 1), DateTimeOffset.UtcNow));
        var deferredCause = Envelope("first", 2);
        state.Add(new ReviewDecisionEvent(
            "vault", "defer", deferredCause, DateTimeOffset.UtcNow,
            new BlockLocator(path, null, block.Kind, block.ContentHash, 0),
            ReviewDecision.Deferred, "session1"));
        var queue = new FeedReviewQueue(parser, state);

        var beforeClose = queue.Build([(path, raw)], Envelope("second", 1));
        state.Add(new ReviewSessionEvent(
            "vault", "close", "session1", ReviewSessionEventKind.Closed,
            Envelope("first", 3, new Dictionary<string, long> { ["first"] = 2 }), DateTimeOffset.UtcNow));
        var withoutObservation = queue.Build([(path, raw)], Envelope("second", 2));
        var afterObservation = queue.Build(
            [(path, raw)],
            Envelope("second", 3, new Dictionary<string, long> { ["first"] = 3 }));

        await Assert.That(beforeClose).IsEmpty();
        await Assert.That(withoutObservation).IsEmpty();
        await Assert.That(afterObservation).HasSingleItem();
        await Assert.That(afterObservation[0].Priority).IsEqualTo(FeedReviewPriority.Deferred);
    }

    [Test]
    public async Task CoveredLocators_ContainsEveryNestedCandidateInsideSelection()
    {
        const string raw = "- [ ] Parent\n  - [ ] Child\n- [ ] Outside\n";
        var parser = new MarkdownDocumentParser();
        var document = parser.Parse(raw);

        var covered = FeedReviewQueue.CoveredLocators(
            "Ежедневные/2026-08-23.md",
            document,
            new MarkdownBlockSelection(0, 2));

        await Assert.That(covered.Count).IsEqualTo(2);
        await Assert.That(covered.Any(locator => locator.ContentHash == document.Blocks[2].ContentHash)).IsFalse();
    }

    [Test]
    public async Task RemovingFirstDuplicateDoesNotTransferItsDecisionToTheSecondDuplicate()
    {
        const string path = "Ежедневные/2026-08-23.md";
        const string original = "До\n\nОдинаково\n\nМежду\n\nОдинаково\n\nПосле\n";
        const string afterDeletion = "До\n\nМежду\n\nОдинаково\n\nПосле\n";
        var parser = new MarkdownDocumentParser();
        var state = new ReviewStateStore();
        var originalQueue = new FeedReviewQueue(parser, state).Build([(path, original)], Envelope("device", 1));
        var decided = originalQueue.Single(candidate =>
            candidate.Block.Raw.Trim() == "Одинаково" && candidate.Locator.Occurrence == 0);
        state.Add(new ReviewDecisionEvent(
            "vault",
            "keep-first",
            Envelope("device", 2),
            DateTimeOffset.UtcNow,
            decided.Locator,
            ReviewDecision.Kept));

        var rebuilt = new FeedReviewQueue(parser, state).Build([(path, afterDeletion)], Envelope("device", 3));

        await Assert.That(rebuilt.Any(candidate => candidate.Block.Raw.Trim() == "Одинаково")).IsTrue();
    }

    [Test]
    public async Task UnchangedBlockKeepsDecisionWhenOneNeighborChanges()
    {
        const string path = "Ежедневные/2026-08-23.md";
        const string original = "До\n\nЦелевая мысль\n\nПосле\n";
        const string changed = "До изменено\n\nЦелевая мысль\n\nПосле\n";
        var parser = new MarkdownDocumentParser();
        var state = new ReviewStateStore();
        var originalQueue = new FeedReviewQueue(parser, state).Build([(path, original)], Envelope("device", 1));
        var target = originalQueue.Single(candidate => candidate.Block.Raw.Trim() == "Целевая мысль");
        state.Add(new ReviewDecisionEvent(
            "vault", "keep-target", Envelope("device", 2), DateTimeOffset.UtcNow,
            target.Locator, ReviewDecision.Kept));

        var rebuilt = new FeedReviewQueue(parser, state).Build([(path, changed)], Envelope("device", 3));

        await Assert.That(rebuilt.Any(candidate => candidate.Block.Raw.Trim() == "Целевая мысль")).IsFalse();
        await Assert.That(rebuilt.Any(candidate => candidate.Block.Raw.Trim() == "До изменено")).IsTrue();
    }

    [Test]
    public async Task UnchangedBlockKeepsDecisionAfterDailyFileRename()
    {
        const string originalPath = "Ежедневные/2026-08-23.md";
        const string renamedPath = "Ежедневные/2026-08-24.md";
        const string raw = "До\n\nЦелевая мысль\n\nПосле\n";
        var parser = new MarkdownDocumentParser();
        var state = new ReviewStateStore();
        var originalQueue = new FeedReviewQueue(parser, state).Build([(originalPath, raw)], Envelope("device", 1));
        var target = originalQueue.Single(candidate => candidate.Block.Raw.Trim() == "Целевая мысль");
        state.Add(new ReviewDecisionEvent(
            "vault", "keep-before-rename", Envelope("device", 2), DateTimeOffset.UtcNow,
            target.Locator, ReviewDecision.Kept));

        var rebuilt = new FeedReviewQueue(parser, state).Build([(renamedPath, raw)], Envelope("device", 3));

        await Assert.That(rebuilt.Any(candidate => candidate.Block.Raw.Trim() == "Целевая мысль")).IsFalse();
    }

    [Test]
    public async Task RenameWithMultipleCurrentSemanticMatchesStaysPending()
    {
        const string originalPath = "Ежедневные/2026-08-22.md";
        const string firstCurrentPath = "Ежедневные/2026-08-23.md";
        const string secondCurrentPath = "Ежедневные/2026-08-24.md";
        const string raw = "До\n\nОдинаковая мысль\n\nПосле\n";
        var parser = new MarkdownDocumentParser();
        var state = new ReviewStateStore();
        var original = new FeedReviewQueue(parser, state)
            .Build([(originalPath, raw)], Envelope("device", 1))
            .Single(candidate => candidate.Block.Raw.Trim() == "Одинаковая мысль");
        state.Add(new ReviewDecisionEvent(
            "vault", "keep-ambiguous", Envelope("device", 2), DateTimeOffset.UtcNow,
            original.Locator, ReviewDecision.Kept));

        var rebuilt = new FeedReviewQueue(parser, state).Build(
            [(firstCurrentPath, raw), (secondCurrentPath, raw)],
            Envelope("device", 3));

        await Assert.That(rebuilt.Count(candidate => candidate.Block.Raw.Trim() == "Одинаковая мысль")).IsEqualTo(2);
    }

    private static CausalEnvelope Envelope(string device, long sequence, IReadOnlyDictionary<string, long>? observed = null) =>
        new(device, sequence, observed ?? new Dictionary<string, long>());
}
