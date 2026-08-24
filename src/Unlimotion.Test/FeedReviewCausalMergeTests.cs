using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Review;

namespace Unlimotion.Test;

public class FeedReviewCausalMergeTests
{
    [Test]
    public async Task ExplicitDecisionAlwaysBeatsBaseline()
    {
        var locator = Locator();
        var state = new ReviewStateStore();
        state.Add(Event("baseline", "a", 4, locator, ReviewDecision.BaselineKept));
        state.Add(Event("keep", "b", 1, locator, ReviewDecision.Kept));

        var result = state.Resolve(locator);

        await Assert.That(result.HasConflict).IsFalse();
        await Assert.That(result.Event!.Decision).IsEqualTo(ReviewDecision.Kept);
    }

    [Test]
    public async Task CausallyNewerExplicitDecisionWins()
    {
        var locator = Locator();
        var state = new ReviewStateStore();
        state.Add(Event("defer", "a", 1, locator, ReviewDecision.Deferred));
        state.Add(new ReviewDecisionEvent(
            "vault", "keep", new CausalEnvelope("b", 1, new Dictionary<string, long> { ["a"] = 1 }),
            DateTimeOffset.UtcNow, locator, ReviewDecision.Kept));

        var result = state.Resolve(locator);

        await Assert.That(result.HasConflict).IsFalse();
        await Assert.That(result.Event!.Decision).IsEqualTo(ReviewDecision.Kept);
    }

    [Test]
    public async Task ConcurrentConflictingExplicitDecisionsStayPendingConflict()
    {
        var locator = Locator();
        var state = new ReviewStateStore();
        state.Add(Event("keep", "a", 1, locator, ReviewDecision.Kept));
        state.Add(Event("move", "b", 1, locator, ReviewDecision.Moved));

        var result = state.Resolve(locator);

        await Assert.That(result.HasConflict).IsTrue();
        await Assert.That(result.ConflictingEvents.Count).IsEqualTo(2);
        await Assert.That(result.IsTerminal).IsFalse();
    }

    [Test]
    public async Task SameOperationAndLocatorIsIdempotent()
    {
        var locator = Locator();
        var state = new ReviewStateStore();
        var first = Event("first", "a", 1, locator, ReviewDecision.Converted) with { OperationId = "op1" };
        var retry = Event("retry", "a", 2, locator, ReviewDecision.Converted) with { OperationId = "op1" };

        var addedFirst = state.Add(first);
        var addedRetry = state.Add(retry);

        await Assert.That(addedFirst).IsTrue();
        await Assert.That(addedRetry).IsFalse();
        await Assert.That(state.DecisionEvents.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SameEventIdWithDifferentOutputsRemainsPendingConflict()
    {
        var locator = Locator();
        var firstOutput = locator with { RelativePath = "Темы/Первая.md" };
        var secondOutput = locator with { RelativePath = "Темы/Вторая.md" };
        var causality = new CausalEnvelope("a", 1, new Dictionary<string, long>());
        var first = new ReviewDecisionEvent(
            "vault", "same-id", causality, DateTimeOffset.UtcNow, locator,
            ReviewDecision.Converted, "session", [firstOutput], "operation", "note-1");
        var replaced = new ReviewDecisionEvent(
            "vault", "same-id", causality, DateTimeOffset.UtcNow, locator,
            ReviewDecision.Converted, "session", [secondOutput], "operation", "note-2");
        var state = new ReviewStateStore();

        state.Add(first);
        var addedReplacement = state.Add(replaced);
        var result = state.Resolve(locator);

        await Assert.That(addedReplacement).IsTrue();
        await Assert.That(state.DecisionEvents.Count).IsEqualTo(2);
        await Assert.That(result.HasConflict).IsTrue();
        await Assert.That(result.IsTerminal).IsFalse();
    }

    [Test]
    public async Task ReplacingConcurrentBootstrapBaselineKeepsExplicitEventsAndMakesRemovedBaselinePending()
    {
        var oldBaseline = Locator();
        var safeBaseline = oldBaseline with { ContentHash = "safe", Occurrence = 1 };
        var explicitLocator = oldBaseline with { ContentHash = "explicit", Occurrence = 2 };
        var state = new ReviewStateStore();
        state.Add(Event("old", "bootstrap", 1, oldBaseline, ReviewDecision.BaselineKept));
        state.Add(Event("explicit", "device", 1, explicitLocator, ReviewDecision.Kept));

        state.ReplaceBootstrapBaseline(
        [
            Event("safe", "bootstrap", 2, safeBaseline, ReviewDecision.BaselineKept)
        ]);

        await Assert.That(state.Resolve(oldBaseline).IsTerminal).IsFalse();
        await Assert.That(state.Resolve(safeBaseline).IsTerminal).IsTrue();
        await Assert.That(state.Resolve(explicitLocator).IsTerminal).IsTrue();
    }

    private static BlockLocator Locator() => new("Ежедневные/2026-08-23.md", null, MarkdownBlockKind.Paragraph, "hash", 0);

    private static ReviewDecisionEvent Event(string id, string device, long sequence, BlockLocator locator, ReviewDecision decision) =>
        new("vault", id, new CausalEnvelope(device, sequence, new Dictionary<string, long>()), DateTimeOffset.UtcNow, locator, decision);
}
