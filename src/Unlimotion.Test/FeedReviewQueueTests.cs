using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Review;

namespace Unlimotion.Test;

public class FeedReviewQueueTests
{
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
