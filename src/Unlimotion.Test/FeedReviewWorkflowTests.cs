using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Operations;
using Unlimotion.Notes.Review;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Test;

public sealed class FeedReviewWorkflowTests
{
    [Test]
    public async Task LeaveDecisionIsDurableAndUnchangedBlockDoesNotReturnAfterRestart()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string path = "Ежедневные/2026-08-23.md";
        const string raw = "Мысль для разбора\n";
        await vault.CreateAsync(path, raw);
        var parser = new MarkdownDocumentParser();
        var document = parser.Parse(raw);
        var locator = FeedReviewQueue.CoveredLocators(path, document, new MarkdownBlockSelection(0, 1)).Single();
        var firstState = new ReviewStateStore();
        var first = new FeedReviewSessionCoordinator(
            "vault1", "device1", new PortableReviewEventStore(vault), firstState);
        await first.InitializeAsync();
        await first.OpenOrResumeAsync();

        await first.ApplyDecisionAsync([locator], ReviewDecision.Kept);
        await first.CloseAsync();

        var reloadedState = new ReviewStateStore();
        var reloaded = new FeedReviewSessionCoordinator(
            "vault1", "device1", new PortableReviewEventStore(vault), reloadedState);
        await reloaded.InitializeAsync();
        await reloaded.OpenOrResumeAsync();
        var queue = new FeedReviewQueue(parser, reloadedState).Build([(path, raw)], reloaded.CurrentObserver);

        await Assert.That(queue).IsEmpty();
    }

    [Test]
    public async Task SkipReturnsOnlyInCausallyNextSession()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string path = "Ежедневные/2026-08-23.md";
        const string raw = "Отложить\n";
        await vault.CreateAsync(path, raw);
        var parser = new MarkdownDocumentParser();
        var locator = FeedReviewQueue.CoveredLocators(
            path,
            parser.Parse(raw),
            new MarkdownBlockSelection(0, 1)).Single();
        var state = new ReviewStateStore();
        var coordinator = new FeedReviewSessionCoordinator(
            "vault1", "device1", new PortableReviewEventStore(vault), state);
        await coordinator.InitializeAsync();
        await coordinator.OpenOrResumeAsync();
        await coordinator.ApplyDecisionAsync([locator], ReviewDecision.Deferred);

        var currentSessionQueue = new FeedReviewQueue(parser, state).Build([(path, raw)], coordinator.CurrentObserver);
        await coordinator.CloseAsync();
        await coordinator.OpenOrResumeAsync();
        var nextSessionQueue = new FeedReviewQueue(parser, state).Build([(path, raw)], coordinator.CurrentObserver);

        await Assert.That(currentSessionQueue).IsEmpty();
        await Assert.That(nextSessionQueue).HasSingleItem();
        await Assert.That(nextSessionQueue[0].Priority).IsEqualTo(FeedReviewPriority.Deferred);
    }

    [Test]
    public async Task ConversionDecisionCoversEveryInputAndMarksOutputTerminal()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var store = new ReviewStateStore();
        var coordinator = new FeedReviewSessionCoordinator(
            "vault1", "device1", new PortableReviewEventStore(vault), store);
        await coordinator.InitializeAsync();
        await coordinator.OpenOrResumeAsync();
        var firstInput = new BlockLocator(
            "Ежедневные/2026-08-23.md", "work", MarkdownBlockKind.Paragraph, "input-a", 0);
        var secondInput = new BlockLocator(
            "Ежедневные/2026-08-23.md", "work", MarkdownBlockKind.Paragraph, "input-b", 0);
        var output = new BlockLocator(
            "Ежедневные/2026-08-23.md", "work", MarkdownBlockKind.Paragraph, "task-link", 0);

        await coordinator.ApplyDecisionAsync(
            [firstInput, secondInput],
            ReviewDecision.Converted,
            [output],
            operationId: "conversion1",
            resultEntityId: "feed-conversion1");
        await coordinator.ApplyDecisionAsync(
            [firstInput, secondInput],
            ReviewDecision.Converted,
            [output],
            operationId: "conversion1",
            resultEntityId: "feed-conversion1");

        await Assert.That(store.DecisionEvents.Count).IsEqualTo(3);
        await Assert.That(store.DecisionEvents.Count(value =>
            value.Input.SemanticKey == firstInput.SemanticKey ||
            value.Input.SemanticKey == secondInput.SemanticKey)).IsEqualTo(2);
        await Assert.That(store.DecisionEvents.Single(value => value.Input.SemanticKey == output.SemanticKey).Decision)
            .IsEqualTo(ReviewDecision.Converted);
    }

    [Test]
    public async Task SameBlockCanBeDeferredAgainInANewSession()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var state = new ReviewStateStore();
        var coordinator = new FeedReviewSessionCoordinator(
            "vault1", "device1", new PortableReviewEventStore(vault), state);
        var locator = new BlockLocator(
            "Ежедневные/2026-08-23.md", null, MarkdownBlockKind.Paragraph, "same-block", 0);
        await coordinator.InitializeAsync();
        await coordinator.OpenOrResumeAsync();
        await coordinator.ApplyDecisionAsync([locator], ReviewDecision.Deferred);
        await coordinator.CloseAsync();
        await coordinator.OpenOrResumeAsync();

        await coordinator.ApplyDecisionAsync([locator], ReviewDecision.Deferred);

        await Assert.That(state.DecisionEvents.Count(value => value.Decision == ReviewDecision.Deferred))
            .IsEqualTo(2);
    }

    [Test]
    public async Task AssignAreaMovesWholeSelectionAndReturnsRemappedActiveLocators()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string path = "Ежедневные/2026-08-23.md";
        const string raw = "Первая мысль\n\n## Дом <!-- unlimotion-area:home -->\nДомашняя запись\n";
        var source = await vault.CreateAsync(path, raw);
        var parser = new MarkdownDocumentParser();
        var document = parser.Parse(raw);
        var selectedIndex = document.Blocks.Single(block => block.Raw.Contains("Первая мысль", StringComparison.Ordinal)).Index;
        var service = new FeedAreaAssignmentService(vault, parser, new MarkdownMutationService(parser));

        var result = await service.AssignAsync(new FeedAreaAssignmentRequest(
            path,
            source.Revision,
            new MarkdownBlockSelection(selectedIndex, 1),
            new AreaReference("work", "Работа")));

        var updated = await vault.ReadAsync(path);
        await Assert.That(updated!.Text).Contains("## Работа <!-- unlimotion-area:work -->");
        await Assert.That(updated.Text.IndexOf("Первая мысль", StringComparison.Ordinal))
            .IsGreaterThan(updated.Text.IndexOf("## Работа", StringComparison.Ordinal));
        await Assert.That(result.InputLocators).HasSingleItem();
        await Assert.That(result.OutputLocators).HasSingleItem();
        await Assert.That(result.OutputLocators[0].AreaIdentity).IsEqualTo("work");
        await Assert.That(result.OutputSelection.BlockCount).IsEqualTo(1);
    }

    [Test]
    public async Task AssignAreaRevisionConflictDoesNotMutateMarkdown()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string path = "Ежедневные/2026-08-23.md";
        var source = await vault.CreateAsync(path, "Мысль\n");
        await vault.WriteAsync(path, "Внешняя правка\n", source.Revision);
        var parser = new MarkdownDocumentParser();
        var service = new FeedAreaAssignmentService(vault, parser, new MarkdownMutationService(parser));

        _ = await NotesTestSupport.CaptureAsync<VaultRevisionConflictException>(() => service.AssignAsync(
            new FeedAreaAssignmentRequest(
                path,
                source.Revision,
                new MarkdownBlockSelection(0, 1),
                new AreaReference("work", "Работа"))));

        await Assert.That((await vault.ReadAsync(path))!.Text).IsEqualTo("Внешняя правка\n");
    }

    [Test]
    public async Task AssignAreaRemapIgnoresIdenticalBlockInLaterArea()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string path = "Ежедневные/2026-08-23.md";
        const string raw = "Одинаковая мысль\n\n## Работа <!-- unlimotion-area:work -->\nДругое\n\n## Дом <!-- unlimotion-area:home -->\nОдинаковая мысль\n";
        var source = await vault.CreateAsync(path, raw);
        var parser = new MarkdownDocumentParser();
        var document = parser.Parse(raw);
        var rootBlock = document.Blocks.First(block => block.IsContent && string.IsNullOrWhiteSpace(block.AreaId));
        var service = new FeedAreaAssignmentService(vault, parser, new MarkdownMutationService(parser));

        var result = await service.AssignAsync(new FeedAreaAssignmentRequest(
            path,
            source.Revision,
            new MarkdownBlockSelection(rootBlock.Index, 1),
            new AreaReference("work", "Работа")));

        await Assert.That(result.OutputLocators).HasSingleItem();
        await Assert.That(result.OutputLocators[0].AreaIdentity).IsEqualTo("work");
        var updated = parser.Parse((await vault.ReadAsync(path))!.Text);
        var selected = result.OutputSelection.Resolve(updated).Single();
        await Assert.That(selected.AreaId).IsEqualTo("work");
    }

    [Test]
    public async Task ForeignOrphanRequiresExplicitTakeoverAndKeepsSameSessionId()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var first = new FeedReviewSessionCoordinator(
            "vault1", "device-a", new PortableReviewEventStore(vault), new ReviewStateStore());
        await first.InitializeAsync();
        var orphanedSessionId = await first.OpenOrResumeAsync();

        var secondState = new ReviewStateStore();
        var second = new FeedReviewSessionCoordinator(
            "vault1", "device-b", new PortableReviewEventStore(vault), secondState);
        await second.InitializeAsync();

        var blocked = await NotesTestSupport.CaptureAsync<ForeignReviewSessionRequiresResolutionException>(
            () => second.OpenOrResumeAsync());
        await Assert.That(blocked.Sessions).HasSingleItem();
        await Assert.That(blocked.Sessions[0].ReviewSessionId).IsEqualTo(orphanedSessionId);

        await second.TakeOverAsync(orphanedSessionId);
        var locator = new BlockLocator(
            "Ежедневные/2026-08-24.md", null, MarkdownBlockKind.Paragraph, "takeover", 0);
        await second.ApplyDecisionAsync([locator], ReviewDecision.Kept);

        await Assert.That(second.CurrentSessionId).IsEqualTo(orphanedSessionId);
        await Assert.That(secondState.DecisionEvents).HasSingleItem();
    }

    [Test]
    public async Task ExplicitAbandonClosesForeignOrphanBeforeNewSession()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var first = new FeedReviewSessionCoordinator(
            "vault1", "device-a", new PortableReviewEventStore(vault), new ReviewStateStore());
        await first.InitializeAsync();
        var orphanedSessionId = await first.OpenOrResumeAsync();

        var second = new FeedReviewSessionCoordinator(
            "vault1", "device-b", new PortableReviewEventStore(vault), new ReviewStateStore());
        await second.InitializeAsync();
        await second.AbandonAsync(orphanedSessionId);
        var nextSessionId = await second.OpenOrResumeAsync();

        await Assert.That(nextSessionId).IsNotEqualTo(orphanedSessionId);
    }

    [Test]
    public async Task PreviousOwnerCannotWriteAfterObservingTakeover()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var firstState = new ReviewStateStore();
        var first = new FeedReviewSessionCoordinator(
            "vault1", "device-a", new PortableReviewEventStore(vault), firstState);
        await first.InitializeAsync();
        var sessionId = await first.OpenOrResumeAsync();

        var second = new FeedReviewSessionCoordinator(
            "vault1", "device-b", new PortableReviewEventStore(vault), new ReviewStateStore());
        await second.InitializeAsync();
        await second.TakeOverAsync(sessionId);
        await first.InitializeAsync();
        var locator = new BlockLocator(
            "Ежедневные/2026-08-24.md", null, MarkdownBlockKind.Paragraph, "stale-owner", 0);

        _ = await NotesTestSupport.CaptureAsync<ReviewSessionOwnershipConflictException>(
            () => first.ApplyDecisionAsync([locator], ReviewDecision.Kept));
        await Assert.That(firstState.DecisionEvents).IsEmpty();
    }

    [Test]
    public async Task ConcurrentStaleCloseCannotTerminateTakenOverSession()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var first = new FeedReviewSessionCoordinator(
            "vault1", "device-a", new PortableReviewEventStore(vault), new ReviewStateStore());
        await first.InitializeAsync();
        var sessionId = await first.OpenOrResumeAsync();

        var second = new FeedReviewSessionCoordinator(
            "vault1", "device-b", new PortableReviewEventStore(vault), new ReviewStateStore());
        await second.InitializeAsync();
        await second.TakeOverAsync(sessionId);

        // device-a has not observed the takeover, so this close is concurrent with it.
        await first.CloseAsync();

        var merged = new FeedReviewSessionCoordinator(
            "vault1", "device-c", new PortableReviewEventStore(vault), new ReviewStateStore());
        await merged.InitializeAsync();
        var blocked = await NotesTestSupport.CaptureAsync<ForeignReviewSessionRequiresResolutionException>(
            () => merged.OpenOrResumeAsync());

        await Assert.That(blocked.Sessions).HasSingleItem();
        await Assert.That(blocked.Sessions[0].ReviewSessionId).IsEqualTo(sessionId);
        await Assert.That(blocked.Sessions[0].OwnerDeviceId).IsEqualTo("device-b");
        await Assert.That(merged.State.SessionEvents.Any(value =>
            value.ReviewSessionId == sessionId && value.Kind == ReviewSessionEventKind.Closed)).IsTrue();
        await Assert.That(merged.State.SessionIsTerminal(sessionId)).IsFalse();
    }

    [Test]
    public async Task ObservedTakeoverMakesStaleCloseFailWithoutAppendingTerminalEvent()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var firstState = new ReviewStateStore();
        var first = new FeedReviewSessionCoordinator(
            "vault1", "device-a", new PortableReviewEventStore(vault), firstState);
        await first.InitializeAsync();
        var sessionId = await first.OpenOrResumeAsync();

        var second = new FeedReviewSessionCoordinator(
            "vault1", "device-b", new PortableReviewEventStore(vault), new ReviewStateStore());
        await second.InitializeAsync();
        await second.TakeOverAsync(sessionId);
        await first.InitializeAsync();
        var terminalCount = firstState.SessionEvents.Count(value =>
            value.Kind is ReviewSessionEventKind.Closed or ReviewSessionEventKind.Abandoned);

        _ = await NotesTestSupport.CaptureAsync<ReviewSessionOwnershipConflictException>(
            () => first.CloseAsync());

        await Assert.That(firstState.SessionEvents.Count(value =>
            value.Kind is ReviewSessionEventKind.Closed or ReviewSessionEventKind.Abandoned)).IsEqualTo(terminalCount);
    }

    [Test]
    public async Task PreviousOwnerDoesNotResumeSessionAfterObservingTakeover()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var first = new FeedReviewSessionCoordinator(
            "vault1", "device-a", new PortableReviewEventStore(vault), new ReviewStateStore());
        await first.InitializeAsync();
        var sessionId = await first.OpenOrResumeAsync();

        var second = new FeedReviewSessionCoordinator(
            "vault1", "device-b", new PortableReviewEventStore(vault), new ReviewStateStore());
        await second.InitializeAsync();
        await second.TakeOverAsync(sessionId);

        await first.InitializeAsync();
        var blocked = await NotesTestSupport.CaptureAsync<ForeignReviewSessionRequiresResolutionException>(
            () => first.OpenOrResumeAsync());

        await Assert.That(first.CurrentSessionId).IsNull();
        await Assert.That(blocked.Sessions).HasSingleItem();
        await Assert.That(blocked.Sessions[0].ReviewSessionId).IsEqualTo(sessionId);
        await Assert.That(blocked.Sessions[0].OwnerDeviceId).IsEqualTo("device-b");
    }

    [Test]
    public async Task RecoveredDecisionCompletesPersistedPrefixAfterOriginalSessionWasAbandoned()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var store = new PortableReviewEventStore(vault);
        var recoveringState = new ReviewStateStore();
        var recovering = new FeedReviewSessionCoordinator(
            "vault1", "device-a", store, recoveringState);
        await recovering.InitializeAsync();
        var sessionId = await recovering.OpenOrResumeAsync();
        var input = new BlockLocator(
            "Ежедневные/2026-08-24.md", null, MarkdownBlockKind.Paragraph, "input-prefix", 0);
        var output = new BlockLocator(
            "Ежедневные/2026-08-24.md", null, MarkdownBlockKind.Paragraph, "output-link", 0);
        await store.AppendAsync(new ReviewDecisionEvent(
            "vault1",
            "device-a-00000000000000000002",
            new CausalEnvelope("device-a", 2, new Dictionary<string, long> { ["device-a"] = 1 }),
            DateTimeOffset.UtcNow,
            input,
            ReviewDecision.Converted,
            sessionId,
            [output],
            "operation-prefix",
            "task-1"));

        var abandoning = new FeedReviewSessionCoordinator(
            "vault1", "device-b", store, new ReviewStateStore());
        await abandoning.InitializeAsync();
        await abandoning.AbandonAsync(sessionId);
        await recovering.InitializeAsync();

        await recovering.ApplyRecoveredDecisionAsync(
            sessionId,
            [input],
            ReviewDecision.Converted,
            [output],
            "operation-prefix",
            "task-1");

        var operationEvents = recoveringState.DecisionEvents
            .Where(value => value.OperationId == "operation-prefix")
            .ToArray();
        await Assert.That(operationEvents).Count().IsEqualTo(2);
        await Assert.That(operationEvents.Count(value => value.Input.SemanticKey == input.SemanticKey)).IsEqualTo(1);
        await Assert.That(operationEvents.Count(value => value.Input.SemanticKey == output.SemanticKey)).IsEqualTo(1);
        await Assert.That(operationEvents).All(value => value.ReviewSessionId == sessionId);
    }

    [Test]
    public async Task IdentitySafePendingMarkerDurablySupersedesLosingTerminalDecision()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var parser = new MarkdownDocumentParser();
        var relativePath = "Ежедневные/2026-08-24.md";
        var raw = "# Работа\n\nВажная веточная запись\n";
        var document = parser.Parse(raw);
        var locator = FeedReviewQueue.CoveredLocators(
            relativePath,
            document,
            new MarkdownBlockSelection(document.Blocks.Single(value => value.IsContent).Index, 1)).Single();
        var first = new FeedReviewSessionCoordinator(
            "vault1", "device-a", new PortableReviewEventStore(vault), new ReviewStateStore());
        await first.InitializeAsync();
        await first.OpenOrResumeAsync();
        await first.ApplyDecisionAsync([locator], ReviewDecision.Kept);
        await first.CloseAsync();

        await first.MarkSafePendingAsync([locator], "identity-conflict-deadbeef");

        var reloaded = new FeedReviewSessionCoordinator(
            "vault1", "device-b", new PortableReviewEventStore(vault), new ReviewStateStore());
        await reloaded.InitializeAsync();
        var effective = reloaded.State.Resolve(locator);
        var queue = new FeedReviewQueue(parser, reloaded.State).Build(
            [(relativePath, raw)],
            reloaded.CurrentObserver);
        await Assert.That(effective.Event!.Decision).IsEqualTo(ReviewDecision.Deferred);
        await Assert.That(effective.Event.OperationId).IsEqualTo("identity-conflict-deadbeef");
        await Assert.That(effective.IsTerminal).IsFalse();
        await Assert.That(queue).HasSingleItem();
        await Assert.That(queue[0].Priority).IsEqualTo(FeedReviewPriority.Deferred);
    }
}
