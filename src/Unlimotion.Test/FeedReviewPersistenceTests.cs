using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Review;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Test;

public class FeedReviewPersistenceTests
{
    [Test]
    public async Task PortableEventsAreAppendOnlyReloadableAndIdempotent()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var store = new PortableReviewEventStore(vault);
        var causality = new CausalEnvelope("device1", 1, new Dictionary<string, long>());
        var decision = new ReviewDecisionEvent(
            "vault1", "event1", causality, DateTimeOffset.UtcNow,
            new BlockLocator("Ежедневные/2026-08-24.md", "work", MarkdownBlockKind.Paragraph, "hash", 0),
            ReviewDecision.Kept, "session1");
        var session = new ReviewSessionEvent(
            "vault1", "event2", "session1", ReviewSessionEventKind.Opened,
            new CausalEnvelope("device1", 2, new Dictionary<string, long> { ["device1"] = 1 }), DateTimeOffset.UtcNow);

        await store.AppendAsync(decision);
        await store.AppendAsync(decision);
        await store.AppendAsync(session);
        var loaded = await store.LoadAllAsync();

        await Assert.That(loaded.Decisions).HasSingleItem();
        await Assert.That(loaded.Sessions).HasSingleItem();
        await Assert.That(loaded.Decisions[0].Decision).IsEqualTo(ReviewDecision.Kept);
        await Assert.That(loaded.Sessions[0].Kind).IsEqualTo(ReviewSessionEventKind.Opened);
    }

    [Test]
    public async Task SameEventIdentityWithDifferentPayloadIsRejected()
    {
        using var directory = new TempNotesDirectory();
        var store = new PortableReviewEventStore(new FileNoteVault(directory.Path));
        var locator = new BlockLocator("Ежедневные/2026-08-24.md", null, MarkdownBlockKind.Paragraph, "hash", 0);
        var first = new ReviewDecisionEvent(
            "vault1", "event1", new CausalEnvelope("device1", 1, new Dictionary<string, long>()),
            DateTimeOffset.UnixEpoch, locator, ReviewDecision.Kept);
        var collision = first with { Decision = ReviewDecision.Moved };
        await store.AppendAsync(first);

        var failure = await NotesTestSupport.CaptureAsync<InvalidDataException>(() => store.AppendAsync(collision));

        await Assert.That(failure.Message.Contains("collision", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task CorruptAndPartialArtifactsAreIgnoredWithoutHidingValidEvents()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var store = new PortableReviewEventStore(vault);
        var valid = new ReviewDecisionEvent(
            "vault1",
            "event1",
            new CausalEnvelope("device1", 1, new Dictionary<string, long>()),
            DateTimeOffset.UtcNow,
            new BlockLocator("Ежедневные/2026-08-24.md", null, MarkdownBlockKind.Paragraph, "hash", 0),
            ReviewDecision.Kept);
        await store.AppendAsync(valid);
        await vault.CreateAsync(
            ".unlimotion/review/events/bad/00000000000000000001-truncated.decision.json",
            "{\"vaultId\":");
        await vault.CreateAsync(
            ".unlimotion/review/events/bad/00000000000000000002-partial.session.json",
            "{\"vaultId\":\"vault1\",\"eventId\":\"partial\"}\n");

        var loaded = await store.LoadAllAsync();
        var reloaded = await store.LoadAllAsync();

        await Assert.That(loaded.Decisions).HasSingleItem();
        await Assert.That(loaded.Decisions[0].EventId).IsEqualTo("event1");
        await Assert.That(loaded.Sessions).IsEmpty();
        await Assert.That(reloaded.Decisions).HasSingleItem();
        await Assert.That(reloaded.Sessions).IsEmpty();
    }
}
